namespace FreeShow.Native.Core;

/// <summary>
/// In-memory record of original FreeShow slideTimers and overlayTimers
/// (src/frontend/components/output/clear.ts). This does NOT create or
/// schedule a timer; a real timer engine must register its native handle.
/// Original clearTimers(outputId, false) cancels slide timers only. Full
/// clearTimers(outputId, true) also clears overlay/effect timers.
/// </summary>
public sealed class FreeShowOutputTimerRegistry
{
    private readonly Dictionary<string, IFreeShowTimerCancellation> slideTimers =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, IFreeShowTimerCancellation> overlayTimers =
        new(StringComparer.Ordinal);

    public int SlideTimerCount => slideTimers.Count;
    public int OverlayTimerCount => overlayTimers.Count;

    public void RegisterSlide(string outputId, IFreeShowTimerCancellation timer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputId);
        slideTimers[outputId] = timer ?? throw new ArgumentNullException(nameof(timer));
    }

    public void RegisterOverlay(string outputId, string overlayId,
        IFreeShowTimerCancellation timer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputId);
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayId);
        overlayTimers[Key(outputId, overlayId)] = timer ??
            throw new ArgumentNullException(nameof(timer));
    }

    /// <summary>
    /// A failing backend timer remains registered for explicit reconciliation;
    /// never silently mark it cancelled. Only registered handles are touched.
    /// The caller must separately clear OutData.transition and propagate
    /// output_changed as the source setOutput() implementation requires.
    /// </summary>
    public async Task<FreeShowTimerCancellationReport> CancelAsync(
        IReadOnlyList<string> originalOutputIds,
        bool cancelOverlayTimers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalOutputIds);
        var selected = originalOutputIds.ToHashSet(StringComparer.Ordinal);
        var errors = new List<string>();
        int slidesCancelled = 0, overlaysCancelled = 0;

        foreach (var item in slideTimers.ToArray())
        {
            if (!selected.Contains(item.Key)) continue;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await item.Value.CancelAsync(cancellationToken);
                slideTimers.Remove(item.Key);
                slidesCancelled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add("Slide timer " + item.Key + " failed: " + ex.Message);
            }
        }

        if (cancelOverlayTimers)
        {
            foreach (var item in overlayTimers.ToArray())
            {
                var outputId = item.Key[..item.Key.IndexOf('\0')];
                if (!selected.Contains(outputId)) continue;
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await item.Value.CancelAsync(cancellationToken);
                    overlayTimers.Remove(item.Key);
                    overlaysCancelled++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add("Overlay timer " + item.Key.Replace('\0', ':') +
                               " failed: " + ex.Message);
                }
            }
        }

        return new(slidesCancelled, overlaysCancelled, errors.AsReadOnly());
    }

    private static string Key(string outputId, string overlayId) =>
        outputId + "\0" + overlayId;
}

public interface IFreeShowTimerCancellation
{
    Task CancelAsync(CancellationToken cancellationToken = default);
}

public sealed record FreeShowTimerCancellationReport(
    int CancelledSlideTimers, int CancelledOverlayTimers,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}
