using System.Collections.Concurrent;

namespace FreeShow.Native.Core;

public sealed record FreeShowSlideTimerCommandReceipt(
    string OutputId, FreeShowSlideTimerExpiryResult? Result, Exception? Error);

/// <summary>
/// Connects input intent and the scheduler's onSlideElapsed callback to
/// one FIFO queue and the actual coordinator dispatch path. Call keyboard
/// AdvanceAsync only AFTER original shortcut filtering/debounce. Timers
/// bypass key filtering and retain their single-output source identity.
/// This is not a substitute for the unported complete source backend.
/// </summary>
public sealed class FreeShowNativeNavigationCommands
{
    private readonly FreeShowNativeCommandQueue queue;
    private readonly FreeShowOutputCommandCoordinator coordinator;
    private readonly FreeShowNavigationSourceContext sourceContext;
    private readonly Func<string, ShowDocument?> sourceShows;
    private readonly FreeShowPreparedSlideRuntimeResolver preparedRuntimes;
    private readonly IReadOnlyDictionary<string, IFreeShowSlideTimerExpirySource> timerSources;
    private readonly ConcurrentQueue<FreeShowSlideTimerCommandReceipt> timerReceipts = new();
    private long timerReceiptCount;

    public IReadOnlyList<FreeShowSlideTimerCommandReceipt> TimerReceipts => timerReceipts.ToArray();
    public long TimerReceiptCount => Interlocked.Read(ref timerReceiptCount);

    public FreeShowNativeNavigationCommands(FreeShowNativeCommandQueue commandQueue,
        FreeShowOutputCommandCoordinator outputCoordinator,
        FreeShowNavigationSourceContext context, Func<string, ShowDocument?> cachedSourceShows,
        FreeShowPreparedSlideRuntimeResolver preparedRuntimeResolver,
        IReadOnlyDictionary<string, IFreeShowSlideTimerExpirySource> sourceTimerBackends)
    {
        queue = commandQueue ?? throw new ArgumentNullException(nameof(commandQueue));
        coordinator = outputCoordinator ?? throw new ArgumentNullException(nameof(outputCoordinator));
        sourceContext = context ?? throw new ArgumentNullException(nameof(context));
        sourceShows = cachedSourceShows ?? throw new ArgumentNullException(nameof(cachedSourceShows));
        preparedRuntimes = preparedRuntimeResolver ?? throw new ArgumentNullException(nameof(preparedRuntimeResolver));
        ArgumentNullException.ThrowIfNull(sourceTimerBackends);
        timerSources = new Dictionary<string, IFreeShowSlideTimerExpirySource>(
            sourceTimerBackends, StringComparer.Ordinal);
    }

    public Task<FreeShowSlideTransitionResult> AdvanceAsync(
        FreeShowSlideAdvanceDirection direction, CancellationToken cancellationToken = default) =>
        queue.Enqueue(token => coordinator.DispatchSlideAdvanceAsync(sourceContext,
            sourceShows, direction, preparedRuntimes, cancellationToken: token), cancellationToken);

    public Task<FreeShowSlideTimerExpiryResult> SlideTimerElapsedAsync(
        FreeShowSlideTimerElapsed elapsed, CancellationToken cancellationToken = default)
    {
        queue.RequireAccess();
        // Capture before enqueue: later commands cannot silently redirect
        // this expired timer to whatever slide is current when it executes.
        var expiry = coordinator.CaptureSlideTimerExpiry(elapsed);
        return queue.Enqueue(token =>
        {
            if (!timerSources.TryGetValue(elapsed.OutputId, out var source))
                return Task.FromResult(new FreeShowSlideTimerExpiryResult(
                    FreeShowSlideTimerExpiryStatus.Refused, false, null,
                    ["No authoritative Native timer source backend is installed for this output."]));
            return coordinator.DispatchSlideTimerExpiryAsync(expiry, source, sourceContext,
                sourceShows, preparedRuntimes, token);
        }, cancellationToken);
    }

    /// <summary>
    /// Pass this method directly as FreeShowNativeTimerScheduler's
    /// onSlideElapsed callback. Asynchronous failures and refusals remain
    /// observable, rather than disappearing in a fire-and-forget handler.
    /// </summary>
    public void OnSlideElapsed(FreeShowSlideTimerElapsed elapsed)
    {
        ArgumentNullException.ThrowIfNull(elapsed);
        try { _ = RecordTimerCompletionAsync(elapsed.OutputId, SlideTimerElapsedAsync(elapsed)); }
        catch (Exception error) { Record(new(elapsed.OutputId, null, error)); }
    }

    private async Task RecordTimerCompletionAsync(string outputId, Task<FreeShowSlideTimerExpiryResult> operation)
    {
        try { Record(new(outputId, await operation.ConfigureAwait(false), null)); }
        catch (Exception error) { Record(new(outputId, null, error)); }
    }

    private void Record(FreeShowSlideTimerCommandReceipt receipt)
    {
        Interlocked.Increment(ref timerReceiptCount);
        timerReceipts.Enqueue(receipt);
        while (timerReceipts.Count > 128) timerReceipts.TryDequeue(out _);
    }
}
