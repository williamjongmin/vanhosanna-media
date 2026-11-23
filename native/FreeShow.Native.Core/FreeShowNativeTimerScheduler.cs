using System.Collections.Concurrent;
using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Single source event-loop ownership, like the original main-window JS.
/// Post MUST enqueue, never invoke inline (even from the owning thread).
/// </summary>
public interface IFreeShowTimerEventLoop
{
    bool CheckAccess();
    void Post(Action callback);
}

public enum FreeShowTimedLayer { Overlays, Effects }
public enum FreeShowSlideTimerDestination { NextSlide, Folder, Pdf }

public sealed record FreeShowSlideTimerElapsed(string OutputId, string FolderPath)
{
    // Preserve tick.ts's case-sensitive suffix and distinct PDF branch.
    public FreeShowSlideTimerDestination Destination => FolderPath.Length == 0
        ? FreeShowSlideTimerDestination.NextSlide
        : FolderPath.EndsWith(".pdf", StringComparison.Ordinal)
            ? FreeShowSlideTimerDestination.Pdf : FreeShowSlideTimerDestination.Folder;
}

public sealed record FreeShowOverlayTimerElapsed(
    string OutputId, string OverlayId, FreeShowTimedLayer Layer);

public sealed record FreeShowSlideTimerSnapshot(
    string OutputId, double Time, double Max, bool Paused, bool AutoPlay, string Data);

public sealed record FreeShowTimerFailure(string OutputId, string? OverlayId, Exception Error);

/// <summary>
/// Concrete native countdowns corresponding to helpers/tick.ts and
/// output.ts's overlay timers. Owns a NEW isolated Native timer inventory;
/// it cannot adopt or infer the timers of an existing Electron process.
///
/// Registration, pause/resume/seek, snapshots and disposal belong to the
/// source event loop. Worker timer callbacks only post generation-checked
/// notifications. No output, file, media decoder or source command is
/// mutated by this scheduler itself.
/// </summary>
public sealed class FreeShowNativeTimerScheduler : IFreeShowTimerInventoryAuthority, IDisposable
{
    private readonly HashSet<string> ownedOutputs;
    private readonly IFreeShowTimerEventLoop eventLoop;
    private readonly TimeProvider clock;
    private readonly Action<FreeShowSlideTimerElapsed> slideElapsed;
    private readonly Action<FreeShowOverlayTimerElapsed> overlayElapsed;
    private readonly FreeShowOutputTimerRegistry registry = new();
    private readonly Dictionary<string, Registration> slides = new(StringComparer.Ordinal);
    private readonly Dictionary<(string OutputId, string OverlayId), Registration> overlays = new();
    private readonly ConcurrentQueue<FreeShowTimerFailure> failures = new();
    private long failureCount;
    private bool disposed;

    /// <summary>
    /// Existing F2 and Clear All hooks cancel these real timer handles.
    /// They still need source transition/output_changed hooks separately.
    /// </summary>
    public FreeShowTimerSourceEffects SourceEffects { get; }
    public IReadOnlyList<FreeShowTimerFailure> Failures => failures.ToArray();
    public long FailureCount => Interlocked.Read(ref failureCount);

    /// <param name="onSlideElapsed">
    /// Source event-loop callback. The complete backend must clear the
    /// current transition, then advance with playNext:true, or perform the
    /// original folder/PDF path. Accept into its serialized command queue;
    /// this notification is not an acknowledgment of physical output.
    /// </param>
    /// <param name="onOverlayElapsed">
    /// Source callback must recheck current membership before toggling the
    /// original overlays/effects layer and apply its whole setOutput lifecycle.
    /// </param>
    public FreeShowNativeTimerScheduler(
        IEnumerable<string> originalOutputIds, IFreeShowTimerEventLoop sourceEventLoop,
        Action<FreeShowSlideTimerElapsed> onSlideElapsed,
        Action<FreeShowOverlayTimerElapsed> onOverlayElapsed,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(originalOutputIds);
        eventLoop = sourceEventLoop ?? throw new ArgumentNullException(nameof(sourceEventLoop));
        slideElapsed = onSlideElapsed ?? throw new ArgumentNullException(nameof(onSlideElapsed));
        overlayElapsed = onOverlayElapsed ?? throw new ArgumentNullException(nameof(onOverlayElapsed));
        clock = timeProvider ?? TimeProvider.System;
        RequireAccess();
        ownedOutputs = originalOutputIds.ToHashSet(StringComparer.Ordinal);
        if (ownedOutputs.Count == 0 || ownedOutputs.Any(id =>
                string.IsNullOrWhiteSpace(id) || id.Contains('\0')))
            throw new ArgumentException("Original Native output IDs must be nonempty and unambiguous.");
        SourceEffects = new FreeShowTimerSourceEffects(registry, this);
    }

    public bool HasCompleteInventory(string originalOutputId, bool includeOverlayTimers) =>
        !Volatile.Read(ref disposed) && ownedOutputs.Contains(originalOutputId);

    /// <summary>
    /// Original Preview.svelte creates a timer only if out.transition exists
    /// and there is no registered slide timer. Re-reading it must not restart
    /// the countdown. Source updateOut explicitly clears old timers first.
    /// </summary>
    public bool EnsureSlideTimer(string outputId, JsonElement originalTransition)
    {
        RequireOutput(outputId);
        if (slides.ContainsKey(outputId) ||
            originalTransition.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;
        if (originalTransition.ValueKind != JsonValueKind.Object ||
            !originalTransition.TryGetProperty("duration", out var duration) ||
            duration.ValueKind != JsonValueKind.Number || !duration.TryGetDouble(out double seconds))
            throw new NotSupportedException("Original next-slide duration must be numeric.");
        var path = ShowDocument.Property(originalTransition, "folderPath");
        if (path.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.String))
            throw new NotSupportedException("Original timer folderPath must be a string.");
        return StartSlideTimer(outputId, seconds, path.ValueKind == JsonValueKind.String ? path.GetString()! : "");
    }

    /// <summary>
    /// Replaces one timer, initially paused/autoPlay:true, then resumes after
    /// the source's 10 ms startup delay. Nonpositive durations leave the old
    /// timer alone, matching newSlideTimer().
    /// </summary>
    public bool StartSlideTimer(string outputId, double durationSeconds, string folderPath = "")
    {
        RequireOutput(outputId);
        ArgumentNullException.ThrowIfNull(folderPath);
        ValidateDuration(durationSeconds);
        if (durationSeconds <= 0) return false;
        if (slides.TryGetValue(outputId, out var previous)) Stop(previous);
        var timer = new Registration(this, outputId, durationSeconds, folderPath);
        registry.RegisterSlide(outputId, timer);
        slides.Add(outputId, timer);
        try { Schedule(timer, TimeSpan.FromMilliseconds(10), autoStart: true); }
        catch { Stop(timer); throw; }
        return true;
    }

    public bool PauseSlideTimer(string outputId)
    {
        RequireOutput(outputId);
        if (!slides.TryGetValue(outputId, out var timer)) return false;
        timer.Position = Position(timer);
        InvalidateAlarm(timer);
        timer.Paused = true;
        timer.AutoPlay = false;
        return true;
    }

    public bool ResumeSlideTimer(string outputId)
    {
        RequireOutput(outputId);
        if (!slides.TryGetValue(outputId, out var timer)) return false;
        if (!timer.Paused) return true;
        Resume(timer);
        return true;
    }

    /// <summary>
    /// NextTimer.svelte pauses before setting slider time and resumes on
    /// mouseup if it was running. The UI adapter keeps that gesture state;
    /// a running timer cannot be silently scrubbed by this primitive.
    /// </summary>
    public bool SetSlideTime(string outputId, double elapsedSeconds)
    {
        RequireOutput(outputId);
        if (!slides.TryGetValue(outputId, out var timer)) return false;
        // One Preview slider applies to all active outputs. Their durations
        // can differ, so source time may exceed a shorter output's max;
        // resuming that timer queues an immediate expiry, like setTimeout.
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0 ||
            elapsedSeconds > int.MaxValue / 1000d)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (!timer.Paused)
            throw new InvalidOperationException("Pause the original timer before changing slider time.");
        // Invalidate a still-pending initial auto-start as well.
        InvalidateAlarm(timer);
        timer.Position = elapsedSeconds;
        return true;
    }

    public FreeShowSlideTimerSnapshot? GetSlideTimer(string outputId)
    {
        RequireOutput(outputId);
        return slides.TryGetValue(outputId, out var timer)
            ? new(outputId, Position(timer), timer.Duration, timer.Paused, timer.AutoPlay, timer.FolderPath)
            : null;
    }

    public bool CancelSlideTimer(string outputId)
    {
        RequireOutput(outputId);
        return slides.TryGetValue(outputId, out var timer) && Stop(timer);
    }

    /// <summary>
    /// Called only after the source has confirmed membership and displayDuration.
    /// Overlay/effect IDs share a timer slot, as in output.ts. The tuple key
    /// preserves output ownership without concatenation collisions.
    /// </summary>
    public bool StartOverlayTimer(string outputId, string overlayId,
        FreeShowTimedLayer layer, double durationSeconds)
    {
        RequireOutput(outputId);
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayId);
        if (overlayId.Contains('\0')) throw new ArgumentException("Invalid original overlay ID.");
        if (layer is not (FreeShowTimedLayer.Overlays or FreeShowTimedLayer.Effects))
            throw new ArgumentOutOfRangeException(nameof(layer));
        ValidateDuration(durationSeconds);
        if (durationSeconds < 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (durationSeconds == 0) return false;
        var key = (outputId, overlayId);
        if (overlays.TryGetValue(key, out var previous)) Stop(previous);
        var timer = new Registration(this, outputId, durationSeconds, "", overlayId, layer);
        registry.RegisterOverlay(outputId, overlayId, timer);
        overlays.Add(key, timer);
        try { Schedule(timer, TimeSpan.FromSeconds(durationSeconds), autoStart: false); }
        catch { Stop(timer); throw; }
        return true;
    }

    public bool CancelOverlayTimer(string outputId, string overlayId)
    {
        RequireOutput(outputId);
        return overlays.TryGetValue((outputId, overlayId), out var timer) && Stop(timer);
    }

    public void CancelOverlayTimers(string outputId)
    {
        RequireOutput(outputId);
        foreach (var timer in overlays.Values.Where(t => t.OutputId == outputId).ToArray()) Stop(timer);
    }

    public int OverlayTimerCount(string outputId)
    {
        RequireOutput(outputId);
        return overlays.Values.Count(t => t.OutputId == outputId);
    }

    private void Resume(Registration timer)
    {
        timer.Started = clock.GetTimestamp();
        timer.Paused = false;
        timer.AutoPlay = true;
        try { Schedule(timer, TimeSpan.FromSeconds(Math.Max(0, timer.Duration - timer.Position)), autoStart: false); }
        catch { Stop(timer); throw; }
    }

    private double Position(Registration timer) => timer.Paused ? timer.Position :
        Math.Clamp(timer.Position + clock.GetElapsedTime(timer.Started).TotalSeconds, 0, timer.Duration);

    private void Schedule(Registration timer, TimeSpan delay, bool autoStart)
    {
        InvalidateAlarm(timer);
        long generation = timer.Generation;
        timer.Alarm = clock.CreateTimer(_ =>
        {
            try { eventLoop.Post(() => Fire(timer, generation, autoStart)); }
            catch (Exception error) { RecordFailure(timer, error); }
        }, null, delay, Timeout.InfiniteTimeSpan);
    }

    private void Fire(Registration timer, long generation, bool autoStart)
    {
        RequireAccess();
        if (disposed || generation != timer.Generation || !IsCurrent(timer)) return;
        try
        {
            if (autoStart) { Resume(timer); return; }
            // Remove before invoking the source action. A callback may create
            // a new timer for this output; the old one must not erase it.
            Stop(timer);
            if (timer.OverlayId == null)
                slideElapsed(new(timer.OutputId, timer.FolderPath));
            else
                overlayElapsed(new(timer.OutputId, timer.OverlayId, timer.Layer));
        }
        catch (Exception error) { RecordFailure(timer, error); }
    }

    private void RecordFailure(Registration timer, Exception error)
    {
        Interlocked.Increment(ref failureCount);
        failures.Enqueue(new(timer.OutputId, timer.OverlayId, error));
        // Keep diagnostics bounded during long operator sessions.
        while (failures.Count > 128) failures.TryDequeue(out _);
    }

    private bool IsCurrent(Registration timer) => timer.OverlayId == null
        ? slides.TryGetValue(timer.OutputId, out var current) && ReferenceEquals(current, timer)
        : overlays.TryGetValue((timer.OutputId, timer.OverlayId), out var overlay) && ReferenceEquals(overlay, timer);

    private bool Stop(Registration timer)
    {
        if (!IsCurrent(timer)) return false;
        InvalidateAlarm(timer);
        if (timer.OverlayId == null)
        {
            slides.Remove(timer.OutputId);
            registry.UnregisterSlide(timer.OutputId, timer);
        }
        else
        {
            overlays.Remove((timer.OutputId, timer.OverlayId));
            registry.UnregisterOverlay(timer.OutputId, timer.OverlayId, timer);
        }
        return true;
    }

    private static void InvalidateAlarm(Registration timer)
    {
        timer.Generation++;
        var old = timer.Alarm;
        timer.Alarm = null;
        old?.Dispose();
    }

    private Task CancelRegistrationAsync(Registration timer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (eventLoop.CheckAccess())
        {
            Stop(timer);
            return Task.CompletedTask;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            eventLoop.Post(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    Stop(timer);
                    completion.TrySetResult();
                }
                catch (OperationCanceledException) { completion.TrySetCanceled(token); }
                catch (Exception error) { completion.TrySetException(error); }
            });
        }
        catch (Exception error) { completion.TrySetException(error); }
        return completion.Task;
    }

    private void RequireAccess()
    {
        if (!eventLoop.CheckAccess())
            throw new InvalidOperationException("FreeShow timer state belongs to its source event loop.");
    }

    private void RequireOutput(string outputId)
    {
        RequireAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ownedOutputs.Contains(outputId))
            throw new ArgumentException("Timer scheduler does not own this original output ID.");
    }

    private static void ValidateDuration(double seconds)
    {
        // Source setTimeout's supported positive range. Reject invalid or
        // overflowing input rather than turning it into an immediate cue.
        if (!double.IsFinite(seconds) || seconds > int.MaxValue / 1000d)
            throw new ArgumentOutOfRangeException(nameof(seconds));
    }

    public void Dispose()
    {
        RequireAccess();
        if (disposed) return;
        Volatile.Write(ref disposed, true);
        foreach (var timer in slides.Values.Concat(overlays.Values).ToArray()) Stop(timer);
    }

    private sealed class Registration(
        FreeShowNativeTimerScheduler owner, string outputId, double duration,
        string folderPath, string? overlayId = null, FreeShowTimedLayer layer = FreeShowTimedLayer.Overlays)
        : IFreeShowTimerCancellation
    {
        public string OutputId { get; } = outputId;
        public string? OverlayId { get; } = overlayId;
        public FreeShowTimedLayer Layer { get; } = layer;
        public string FolderPath { get; } = folderPath;
        public double Duration { get; } = duration;
        public double Position { get; set; }
        public bool Paused { get; set; } = true;
        public bool AutoPlay { get; set; } = true;
        public long Started { get; set; }
        public long Generation { get; set; }
        public ITimer? Alarm { get; set; }
        public Task CancelAsync(CancellationToken cancellationToken = default) =>
            owner.CancelRegistrationAsync(this, cancellationToken);
    }
}
