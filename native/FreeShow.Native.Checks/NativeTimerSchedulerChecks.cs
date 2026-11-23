using System.Text.Json;
using FreeShow.Native.Core;

// Deterministic synthetic fixtures, to be executed with the suite at Beta 2.
// No sleeps, wall-clock timers, output devices or church files are used here.
internal static class NativeTimerSchedulerChecks
{
    public static async Task RunAsync()
    {
        CheckCountdownAndControls();
        CheckStaleNotifications();
        await CheckSourceCancellation();
        CheckOverlayOwnership();
        CheckCallbackLifetime();
        await CheckEventLoopCancellation();
        CheckInputAndDisposal();
    }

    private static void CheckCountdownAndControls()
    {
        using var h = new Harness();
        h.Scheduler.StartSlideTimer("a", 10);
        Check(h.Scheduler.GetSlideTimer("a") is { Paused: true, AutoPlay: true, Time: 0 },
            "Source timer starts paused with autoPlay before the 10 ms startup task");
        h.Advance(.009);
        Check(h.Scheduler.GetSlideTimer("a")!.Paused, "Startup delay does not count toward slide time");
        h.Advance(.001);
        Check(!h.Scheduler.GetSlideTimer("a")!.Paused, "Source startup resumes the timer after 10 ms");
        h.Advance(2.5);
        h.Scheduler.PauseSlideTimer("a");
        h.Advance(100);
        Check(Near(h.Scheduler.GetSlideTimer("a")!.Time, 2.5) && h.Slides.Count == 0,
            "Paused countdown keeps its actual elapsed time and cannot advance lyrics");
        h.Scheduler.SetSlideTime("a", 7);
        h.Scheduler.ResumeSlideTimer("a");
        h.Advance(2.9);
        h.Clock.UtcOffset = TimeSpan.FromHours(6);
        Check(Near(h.Scheduler.GetSlideTimer("a")!.Time, 9.9) && h.Slides.Count == 0,
            "Resumed countdown uses the chosen position and is unaffected by wall-clock changes");
        h.Advance(.1);
        Check(h.Slides.Count == 1 && h.Scheduler.GetSlideTimer("a") == null,
            "Elapsed countdown is removed before exactly one next-slide notification");

        h.Scheduler.StartSlideTimer("a", 10);
        h.Scheduler.StartSlideTimer("b", 5);
        h.Advance(.01);
        h.Scheduler.PauseSlideTimer("b");
        var controls = new FreeShowNextTimerControls(h.Scheduler, () => ["a", "b"]);
        controls.ChangeSlider(8);
        Check(h.Scheduler.GetSlideTimer("a") is { Paused: true, Time: 8 } &&
              h.Scheduler.GetSlideTimer("b") is { Paused: true, Time: 8 },
            "Source slider pauses running outputs and shares the chosen time across different durations");
        controls.ReleaseSlider();
        h.Advance(0);
        Check(h.Slides.Last().OutputId == "b" && h.Scheduler.GetSlideTimer("a") is { Paused: false },
            "Mouseup resumes all active timers; a shorter output already past its max expires next turn");
        h.Advance(2);
        Check(h.Slides.Last().OutputId == "a", "Longer output retains its remaining time after the same gesture");

        h.Scheduler.StartSlideTimer("a", 10);
        h.Scheduler.StartSlideTimer("b", 10);
        controls.TogglePlayPause(currentPreviewIsPaused: false);
        controls.ChangeSlider(3);
        controls.ReleaseSlider();
        h.Advance(20);
        Check(h.Scheduler.GetSlideTimer("a") is { Paused: true, Time: 3 } &&
              h.Scheduler.GetSlideTimer("b") is { Paused: true, Time: 3 },
            "Scrubbing entirely paused timers does not start them on mouseup");
        controls.TogglePlayPause(currentPreviewIsPaused: true);
        var invalidSelection = new FreeShowNextTimerControls(h.Scheduler, () => ["a", "unknown"]);
        Expect<ArgumentException>(() => invalidSelection.ChangeSlider(4),
            "Unknown selected output is rejected before pausing any valid output");
        Check(!h.Scheduler.GetSlideTimer("a")!.Paused,
            "Failed selection preflight leaves an already running timer unchanged");
    }

    private static void CheckStaleNotifications()
    {
        using var h = new Harness();
        h.Scheduler.StartSlideTimer("a", 1);
        h.Clock.Advance(TimeSpan.FromMilliseconds(10)); // old startup is queued, not delivered
        h.Scheduler.StartSlideTimer("a", 20);
        h.Loop.Pump();
        Check(h.Scheduler.GetSlideTimer("a") is { Paused: true, Max: 20 },
            "An old queued startup cannot resume a replacement timer early");
        h.Advance(.01);
        h.Clock.Advance(TimeSpan.FromSeconds(20)); // expiry queued
        h.Scheduler.CancelSlideTimer("a");
        h.Loop.Pump();
        Check(h.Slides.Count == 0, "Cancellation invalidates an already queued countdown expiry");

        h.Scheduler.StartSlideTimer("a", 1);
        h.Advance(.01);
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        h.Scheduler.StartSlideTimer("a", 5);
        h.Loop.Pump();
        Check(h.Slides.Count == 0 && h.Scheduler.GetSlideTimer("a") is { Paused: true, Max: 5 },
            "A replaced timer cannot advance or erase the new output timer");
        h.Advance(.01);
        h.Clock.Advance(TimeSpan.FromSeconds(5));
        h.Scheduler.PauseSlideTimer("a");
        h.Loop.Pump();
        Check(h.Slides.Count == 0, "Pause invalidates a queued expiry at the end of a countdown");
        h.Scheduler.ResumeSlideTimer("a");
        h.Advance(0);
        Check(h.Slides.Count == 1, "Resuming at the end queues exactly one fresh expiry");
    }

    private static async Task CheckSourceCancellation()
    {
        using var h = new Harness();
        h.Scheduler.StartSlideTimer("a", 1);
        h.Scheduler.StartSlideTimer("b", 1);
        h.Scheduler.StartOverlayTimer("a", "caption", FreeShowTimedLayer.Overlays, 2);
        h.Advance(.01);
        var f2 = await h.Scheduler.SourceEffects.CancelAsync(["a"], includeOverlayTimers: false);
        Check(f2.CancelledSlideTimers == 1 && f2.CancelledOverlayTimers == 0 &&
              h.Scheduler.OverlayTimerCount("a") == 1,
            "Original F2 timer hook cancels real slide handles while preserving overlays");
        h.Advance(1);
        Check(h.Slides.Count == 1 && h.Slides[0].OutputId == "b",
            "Cancelling one output does not cancel another output's slide timer");
        h.Advance(1);
        Check(h.Overlays.Single().OverlayId == "caption", "Overlay expiry survives slide-only timer clear");
        var completed = await h.Scheduler.SourceEffects.CancelAsync(["b"], true);
        Check(completed.CancelledSlideTimers == 0,
            "Expired handles are removed from the authoritative cancellation registry");

        h.Scheduler.StartSlideTimer("a", 1);
        h.Scheduler.StartOverlayTimer("a", "effect", FreeShowTimedLayer.Effects, 1);
        h.Scheduler.StartOverlayTimer("b", "caption", FreeShowTimedLayer.Overlays, 1);
        var all = await h.Scheduler.SourceEffects.CancelAsync(["a", "b"], true);
        Check(all.CancelledSlideTimers == 1 && all.CancelledOverlayTimers == 2,
            "Clear All timer hooks cancel both actual slide and overlay/effect inventories");
        int count = h.Slides.Count + h.Overlays.Count;
        h.Advance(100);
        Check(h.Slides.Count + h.Overlays.Count == count, "Cleared timers cannot produce later output actions");
    }

    private static void CheckOverlayOwnership()
    {
        using var h = new Harness(["a", "ab"]);
        h.Scheduler.StartOverlayTimer("a", "bc", FreeShowTimedLayer.Overlays, 1);
        h.Scheduler.StartOverlayTimer("ab", "c", FreeShowTimedLayer.Effects, 1);
        h.Scheduler.CancelOverlayTimers("a");
        h.Advance(1);
        Check(h.Overlays.Count == 1 && h.Overlays[0] is { OutputId: "ab", OverlayId: "c", Layer: FreeShowTimedLayer.Effects },
            "Overlapping output/overlay names cannot collide or cancel another output");

        h.Scheduler.StartOverlayTimer("a", "same-id", FreeShowTimedLayer.Overlays, 1);
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        h.Scheduler.StartOverlayTimer("a", "same-id", FreeShowTimedLayer.Effects, 2);
        h.Loop.Pump();
        Check(h.Overlays.Count == 1 && h.Scheduler.OverlayTimerCount("a") == 1,
            "Late overlay expiry does not toggle a replacement using the same shared effect slot");
        h.Advance(2);
        Check(h.Overlays.Last().Layer == FreeShowTimedLayer.Effects,
            "Replacement timer retains the original target layer for its elapsed action");
    }

    private static void CheckCallbackLifetime()
    {
        using var h = new Harness();
        bool removedBeforeCallback = false;
        h.OnSlide = e =>
        {
            removedBeforeCallback = h.Scheduler.GetSlideTimer(e.OutputId) == null;
            h.Scheduler.StartSlideTimer(e.OutputId, 3, "C:/folder");
        };
        h.Scheduler.StartSlideTimer("a", 1, "C:/book.pdf");
        h.Advance(.01);
        h.Advance(1);
        Check(removedBeforeCallback && h.Scheduler.GetSlideTimer("a") is { Max: 3, Data: "C:/folder" } &&
              h.Slides[0].Destination == FreeShowSlideTimerDestination.Pdf,
            "Expiry removes its own handle before a PDF/folder action can install the next timer");
        h.Scheduler.CancelSlideTimer("a");
        h.OnSlide = e => { if (e.OutputId == "a") throw new IOException("Synthetic source queue failure."); };
        h.Scheduler.StartSlideTimer("a", 1);
        h.Scheduler.StartSlideTimer("b", 1);
        h.Advance(.01);
        h.Advance(1);
        Check(h.Scheduler.Failures is [{ OutputId: "a", Error: IOException }] &&
              h.Slides.Last().OutputId == "b" && h.Scheduler.GetSlideTimer("a") == null,
            "Failed source callback is recorded without replaying it or preventing another output's timer");
        Check(new FreeShowSlideTimerElapsed("a", "BOOK.PDF").Destination == FreeShowSlideTimerDestination.Folder &&
              new FreeShowSlideTimerElapsed("a", "").Destination == FreeShowSlideTimerDestination.NextSlide,
            "Timer destinations preserve the source's folder, case-sensitive PDF and next-slide branches");
    }

    private static async Task CheckEventLoopCancellation()
    {
        using var h = new Harness();
        h.Scheduler.StartSlideTimer("a", 1);
        h.Loop.HasAccess = false;
        Expect<InvalidOperationException>(() => h.Scheduler.PauseSlideTimer("a"),
            "Worker-thread callers cannot mutate event-loop timer state");
        var cancelling = h.Scheduler.SourceEffects.CancelAsync(["a"], false);
        Check(!cancelling.IsCompleted, "Off-thread source cancellation waits for owner-loop acknowledgment");
        h.Loop.HasAccess = true;
        h.Loop.Pump();
        Check((await cancelling).CancelledSlideTimers == 1 && h.Scheduler.GetSlideTimer("a") == null,
            "Registry acknowledges cancellation only after the owner loop removed the exact handle");
        h.Advance(10);
        Check(h.Slides.Count == 0, "Cross-thread cancelled startup cannot later resume itself");
    }

    private static void CheckInputAndDisposal()
    {
        using var h = new Harness();
        using var transition = JsonDocument.Parse("""{"duration":5,"folderPath":"C:/folder"}""");
        h.Scheduler.EnsureSlideTimer("a", transition.RootElement);
        h.Advance(.01);
        h.Advance(2);
        Check(!h.Scheduler.EnsureSlideTimer("a", transition.RootElement) &&
              Near(h.Scheduler.GetSlideTimer("a")!.Time, 2),
            "Preview refresh cannot restart an existing source transition timer");
        Check(!h.Scheduler.StartSlideTimer("a", 0) && h.Scheduler.GetSlideTimer("a")!.Max == 5,
            "Nonpositive newSlideTimer request leaves its existing timer intact");
        Expect<ArgumentOutOfRangeException>(() => h.Scheduler.StartSlideTimer("a", double.PositiveInfinity),
            "Invalid duration is rejected before replacing the existing timer");
        Expect<ArgumentException>(() => h.Scheduler.StartSlideTimer("unknown", 1),
            "Scheduler does not invent ownership of an external output");
        Check(!h.Scheduler.HasCompleteInventory("unknown", true),
            "An unknown output never acquires authoritative inventory from an empty registry");
        h.Clock.Advance(TimeSpan.FromSeconds(3)); // expiry is queued
        h.Scheduler.StartSlideTimer("b", 1);      // startup will be pending on disposal
        h.Scheduler.Dispose();
        h.Loop.Pump();
        h.Advance(10);
        Check(h.Slides.Count == 0 && !h.Scheduler.HasCompleteInventory("a", false),
            "Disposal invalidates queued expiry/startup and relinquishes inventory authority");
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < .000001;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("OK: " + message);
    }
    private static void Expect<T>(Action action, string message) where T : Exception
    {
        bool threw = false;
        try { action(); }
        catch (T) { threw = true; }
        Check(threw, message);
    }

    private sealed class Harness : IDisposable
    {
        public ManualClock Clock { get; } = new();
        public ManualLoop Loop { get; } = new();
        public FreeShowNativeTimerScheduler Scheduler { get; }
        public List<FreeShowSlideTimerElapsed> Slides { get; } = new();
        public List<FreeShowOverlayTimerElapsed> Overlays { get; } = new();
        public Action<FreeShowSlideTimerElapsed>? OnSlide { get; set; }
        public Harness(string[]? ids = null)
        {
            Scheduler = new(ids ?? ["a", "b"], Loop,
                e => { Slides.Add(e); OnSlide?.Invoke(e); }, Overlays.Add, Clock);
        }
        public void Advance(double seconds)
        {
            Clock.Advance(TimeSpan.FromSeconds(seconds));
            Loop.Pump();
        }
        public void Dispose() => Scheduler.Dispose();
    }

    private sealed class ManualLoop : IFreeShowTimerEventLoop
    {
        private readonly Queue<Action> pending = new();
        public bool HasAccess { get; set; } = true;
        public bool CheckAccess() => HasAccess;
        public void Post(Action callback) => pending.Enqueue(callback);
        public void Pump()
        {
            if (!HasAccess) throw new InvalidOperationException("Synthetic owner loop not selected.");
            while (pending.TryDequeue(out var action)) action();
        }
    }

    internal sealed class ManualClock : TimeProvider
    {
        private readonly List<Alarm> alarms = new();
        private long timestamp;
        public TimeSpan UtcOffset { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(timestamp) + UtcOffset;
        public override ITimer CreateTimer(TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Alarm(this, callback, state);
            alarms.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
            long target = checked(timestamp + elapsed.Ticks);
            while (alarms.Where(a => a.Due.HasValue && a.Due.Value <= target)
                       .OrderBy(a => a.Due).FirstOrDefault() is { } alarm)
            {
                timestamp = alarm.Due!.Value;
                alarm.Fire();
            }
            timestamp = target;
        }
        private sealed class Alarm(ManualClock owner, TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;
            private TimeSpan period;
            public long? Due { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan repeat)
            {
                if (disposed) return false;
                period = repeat;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.timestamp + dueTime.Ticks;
                return true;
            }
            public void Fire()
            {
                Due = period > TimeSpan.Zero ? owner.timestamp + period.Ticks : null;
                callback(state);
            }
            public void Dispose() { disposed = true; Due = null; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
