namespace FreeShow.Native.Core;

/// <summary>
/// In-memory handles corresponding to original FreeShow slideTimers and
/// overlayTimers. An actual source-aligned scheduler must register them;
/// this registry does not create/schedule timers or pretend an unknown
/// original timer has been cancelled.
/// clearTimers(outputId, false) cancels slide timers only. The default
/// clearTimers(outputId, true) additionally cancels overlay/effect timers.
/// </summary>
public sealed class FreeShowOutputTimerRegistry
{
    private readonly object gate = new();
    private readonly SemaphoreSlim cancellationGate = new(1, 1);
    private readonly Dictionary<string, IFreeShowTimerCancellation> slideTimers =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, IFreeShowTimerCancellation> overlayTimers =
        new(StringComparer.Ordinal);

    public int SlideTimerCount
    {
        get { lock (gate) return slideTimers.Count; }
    }

    public int OverlayTimerCount
    {
        get { lock (gate) return overlayTimers.Count; }
    }

    public void RegisterSlide(string outputId, IFreeShowTimerCancellation timer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputId);
        ArgumentNullException.ThrowIfNull(timer);
        lock (gate)
        {
            if (!slideTimers.TryAdd(outputId, timer))
                throw new InvalidOperationException(
                    "Existing original output slide timer must be cancelled before replacement.");
        }
    }

    public void RegisterOverlay(string outputId, string overlayId,
        IFreeShowTimerCancellation timer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputId);
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayId);
        ArgumentNullException.ThrowIfNull(timer);
        lock (gate)
        {
            if (!overlayTimers.TryAdd(Key(outputId, overlayId), timer))
                throw new InvalidOperationException(
                    "Existing original overlay timer must be cancelled before replacement.");
        }
    }

    /// <summary>
    /// Failed cancellations remain registered. New timers registered
    /// concurrently with cancellation cannot be accidentally removed; only
    /// the exact handle that acknowledged cancellation is removed.
    /// OutData.transition and output_changed need their own source hooks.
    /// </summary>
    public async Task<FreeShowTimerCancellationReport> CancelAsync(
        IReadOnlyList<string> originalOutputIds, bool cancelOverlayTimers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalOutputIds);
        await cancellationGate.WaitAsync(cancellationToken);
        try
        {
            var selected = originalOutputIds.ToHashSet(StringComparer.Ordinal);
            KeyValuePair<string, IFreeShowTimerCancellation>[] slides;
            KeyValuePair<string, IFreeShowTimerCancellation>[] overlays;
            lock (gate)
            {
                slides = slideTimers.Where(x => selected.Contains(x.Key)).ToArray();
                overlays = !cancelOverlayTimers
                    ? [] : overlayTimers.Where(x =>
                        selected.Contains(x.Key[..x.Key.IndexOf('\0')])).ToArray();
            }

            var errors = new List<string>();
            int slidesCancelled = 0, overlaysCancelled = 0;
            foreach (var item in slides)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await item.Value.CancelAsync(cancellationToken);
                    lock (gate)
                    {
                        if (slideTimers.TryGetValue(item.Key, out var registered) &&
                            ReferenceEquals(registered, item.Value))
                            slideTimers.Remove(item.Key);
                    }
                    slidesCancelled++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add("Slide timer " + item.Key + " failed: " + ex.Message);
                }
            }

            foreach (var item in overlays)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await item.Value.CancelAsync(cancellationToken);
                    lock (gate)
                    {
                        if (overlayTimers.TryGetValue(item.Key, out var registered) &&
                            ReferenceEquals(registered, item.Value))
                            overlayTimers.Remove(item.Key);
                    }
                    overlaysCancelled++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add("Overlay timer " + item.Key.Replace('\0', ':') +
                        " failed: " + ex.Message);
                }
            }
            return new(slidesCancelled, overlaysCancelled, errors.AsReadOnly());
        }
        finally { cancellationGate.Release(); }
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
