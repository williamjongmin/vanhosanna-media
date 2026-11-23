using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FreeShow.Native.Core;

// Synthetic source stores and runtimes, never church data or output devices.
// Authored for Beta 2; not executed during this checkpoint.
internal static class NativeNavigationCommandChecks
{
    public static async Task RunAsync()
    {
        string root = Directory.CreateTempSubdirectory("freeshow-command-fixture-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Config"));
            await File.WriteAllTextAsync(Path.Combine(root, "Config", "settings_synced.json"), "{}");
            var visuals = await FreeShowVisualSettings.OpenAsync(root);
            await CheckRapidCommands(visuals);
            await CheckTimerAdvance(visuals);
            await CheckSchedulerConnection(visuals);
            await CheckStaleTimerAndSelections(visuals);
            await CheckFailures(visuals);
            await CheckQueueLifetime();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task CheckRapidCommands(FreeShowVisualSettings visuals)
    {
        using var h = new Harness(visuals);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.BeforeFirst = () => blocked.Task;
        var first = h.Commands.AdvanceAsync(FreeShowSlideAdvanceDirection.Next);
        var second = h.Commands.AdvanceAsync(FreeShowSlideAdvanceDirection.Next);
        h.Loop.Pump();
        Check(h.Plans.Count == 1 && !first.IsCompleted && !second.IsCompleted && h.Index("main") == 0,
            "Second rapid cue cannot plan or execute while the first source phase is pending");
        blocked.SetResult();
        await h.Loop.PumpUntilAsync(Task.WhenAll(first, second));
        Check(first.Result.Succeeded && second.Result.Succeeded &&
              h.Index("main") == 2 && h.Index("online") == 2 &&
              h.Plans.Select(plan => plan.Targets[0].LayoutIndex).SequenceEqual(new[] { 1, 2 }),
            "Rapid Next commands consume successive acknowledged states on every active output");
        Check(h.Plans[1].Targets.All(target => target.ExpectedRevision == 1) &&
              h.Plans.All(plan => plan.SourceCapture.Outputs.Count == 3) && h.Queue.OutstandingCount == 0,
            "Planning preserves the entire normal/stage capture and uses the latest revisions");
    }

    private static async Task CheckTimerAdvance(FreeShowVisualSettings visuals)
    {
        using var h = new Harness(visuals);
        var result = h.Commands.SlideTimerElapsedAsync(new("main", ""));
        await h.Loop.PumpUntilAsync(result);
        Check(result.Result.Status == FreeShowSlideTimerExpiryStatus.Completed &&
              result.Result.TransitionClearAcknowledged && h.Index("main") == 1 && h.Index("online") == 0 &&
              h.Controller.States["main"].Revision == 2 && h.Controller.States["online"].Revision == 0,
            "Timer acknowledges transition cleanup and advances only its original output");
        Check(h.Plans.Single().Targets.Single().ExpectedRevision == 1 &&
              h.Trace.Take(2).SequenceEqual(new[] { "clear:main", "plan:main" }) &&
              h.Controller.States["main"].Snapshot().GetProperty("background").GetProperty("id").GetString() == "video",
            "Timer plans after cleanup and retains the original unrelated output layers");

        using var boundary = new Harness(visuals);
        boundary.Controller.States["main"].SetSlide(Json("""{"id":"song","layout":"main","index":3}"""));
        boundary.Physical["main"] = boundary.Controller.States["main"].Snapshot();
        var end = boundary.Commands.SlideTimerElapsedAsync(new("main", ""));
        await boundary.Loop.PumpUntilAsync(end);
        Check(end.Result.Status == FreeShowSlideTimerExpiryStatus.TransitionClearedWithoutAdvance &&
              end.Result.TransitionClearAcknowledged && boundary.Plans.Count == 0 && boundary.Index("online") == 0,
            "A per-output project boundary reports completed cleanup without inventing another project item");

        using var locked = new Harness(visuals, locked: true);
        var lockedResult = locked.Commands.SlideTimerElapsedAsync(new("main", ""));
        await locked.Loop.PumpUntilAsync(lockedResult);
        Check(lockedResult.Result.Status == FreeShowSlideTimerExpiryStatus.TransitionClearedWithoutAdvance &&
              lockedResult.Result.TransitionClearAcknowledged && locked.Index("main") == 0,
            "Original timer transition is cleared even when output lock prevents advancing");
    }

    private static async Task CheckSchedulerConnection(FreeShowVisualSettings visuals)
    {
        using var h = new Harness(visuals);
        var clock = new NativeTimerSchedulerChecks.ManualClock();
        Task<FreeShowSlideTimerExpiryResult>? elapsedCommand = null;
        using var scheduler = new FreeShowNativeTimerScheduler(["main", "online"], h.Loop,
            elapsed => elapsedCommand = h.Commands.SlideTimerElapsedAsync(elapsed),
            _ => throw new InvalidOperationException("No overlay fixture is scheduled."), clock);
        scheduler.StartSlideTimer("main", 1);
        clock.Advance(TimeSpan.FromMilliseconds(10));
        h.Loop.Pump();
        clock.Advance(TimeSpan.FromSeconds(1));
        h.Loop.Pump();
        Check(elapsedCommand != null && scheduler.GetSlideTimer("main") == null,
            "Real Native scheduler removes the expired registration before admitting its output command");
        await h.Loop.PumpUntilAsync(elapsedCommand!);
        Check(elapsedCommand!.Result.Status == FreeShowSlideTimerExpiryStatus.Completed &&
              h.Index("main") == 1 && h.Index("online") == 0 && scheduler.FailureCount == 0,
            "Manual-clock expiry travels through the real FIFO/coordinator path to one acknowledged slide");
        scheduler.StartSlideTimer("main", 1);
        await scheduler.SourceEffects.CancelAsync(["main"], includeOverlayTimers: false);
        clock.Advance(TimeSpan.FromSeconds(2));
        h.Loop.Pump();
        Check(h.Index("main") == 1 && h.Plans.Count == 1,
            "Cancelling the next actual countdown prevents a second queued source command");
    }

    private static async Task CheckStaleTimerAndSelections(FreeShowVisualSettings visuals)
    {
        using var h = new Harness(visuals);
        var key = h.Commands.AdvanceAsync(FreeShowSlideAdvanceDirection.Next);
        var timer = h.Commands.SlideTimerElapsedAsync(new("main", ""));
        await h.Loop.PumpUntilAsync(Task.WhenAll(key, timer));
        Check(key.Result.Succeeded && timer.Result.Status == FreeShowSlideTimerExpiryStatus.Refused &&
              !h.Trace.Any(entry => entry.StartsWith("clear:")) && h.Index("main") == 1,
            "A timer queued behind a newer slide cannot clear or advance that new slide");

        foreach (var selection in new[] { Array.Empty<string>(), new[] { "main", "main" }, new[] { "main", "missing" } })
        {
            var invalid = await h.Controller.DispatchSlideAdvanceAsync(h.Context, h.Lookup,
                FreeShowSlideAdvanceDirection.Next, h.Resolve, selection);
            Check(invalid.Status == FreeShowSlideTransitionStatus.Refused && h.Index("main") == 1,
                "Invalid target selection refuses the whole command before source work");
        }

        foreach (string path in new[] { "folder", "file.pdf", "file.PDF" })
        {
            var unsupported = h.Commands.SlideTimerElapsedAsync(new("main", path));
            await h.Loop.PumpUntilAsync(unsupported);
            Check(unsupported.Result.Status == FreeShowSlideTimerExpiryStatus.Refused &&
                  !unsupported.Result.TransitionClearAcknowledged,
                "Folder/PDF timer paths cannot masquerade as ordinary lyric advances");
        }

        using var changed = new Harness(visuals);
        changed.DuringResolve = () => changed.Controller.States["online"].SetSlide(
            Json("""{"id":"song","layout":"main","index":2}"""));
        var stale = changed.Commands.AdvanceAsync(FreeShowSlideAdvanceDirection.Next);
        await changed.Loop.PumpUntilAsync(stale);
        Check(stale.Result.Status == FreeShowSlideTransitionStatus.Refused &&
              !changed.Trace.Any(entry => entry.StartsWith("phase:")),
            "State mutation during runtime resolution invalidates the full batch before any source phase");
    }

    private static async Task CheckFailures(FreeShowVisualSettings visuals)
    {
        using var badClear = new Harness(visuals) { WrongClearAcknowledgment = true };
        var wrong = badClear.Commands.SlideTimerElapsedAsync(new("main", ""));
        await badClear.Loop.PumpUntilAsync(wrong);
        Check(wrong.Result.Status == FreeShowSlideTimerExpiryStatus.PartialExecution &&
              !wrong.Result.TransitionClearAcknowledged && badClear.Plans.Count == 0 &&
              badClear.Controller.UnreconciledSlideOutputs.Contains("main"),
            "Wrong timer cleanup acknowledgment quarantines the output without advancing");

        using var cancelledClear = new Harness(visuals) { CancelDuringClear = true };
        var cancelled = cancelledClear.Commands.SlideTimerElapsedAsync(new("main", ""));
        await cancelledClear.Loop.PumpUntilAsync(cancelled);
        Check(cancelled.Result.Status == FreeShowSlideTimerExpiryStatus.PartialExecution &&
              cancelledClear.Controller.UnreconciledSlideOutputs.Contains("main"),
            "Cancellation after source cleanup starts cannot be reported as an untouched output");

        using var missingVisual = new Harness(visuals) { VisualPrepared = false };
        var unprepared = missingVisual.Commands.SlideTimerElapsedAsync(new("main", ""));
        await missingVisual.Loop.PumpUntilAsync(unprepared);
        Check(unprepared.Result.Status == FreeShowSlideTimerExpiryStatus.TransitionClearedWithoutAdvance &&
              unprepared.Result.TransitionClearAcknowledged && missingVisual.Index("main") == 0 &&
              !missingVisual.Trace.Any(entry => entry.StartsWith("phase:")),
            "Missing prepared lyrics reports cleanup separately and never invokes source slide phases");

        using var failed = new Harness(visuals) { FailSourcePhase = true };
        var first = failed.Commands.AdvanceAsync(FreeShowSlideAdvanceDirection.Next);
        var second = failed.Commands.AdvanceAsync(FreeShowSlideAdvanceDirection.Next);
        await failed.Loop.PumpUntilAsync(Task.WhenAll(first, second));
        Check(first.Result.Status == FreeShowSlideTransitionStatus.PartialExecution &&
              second.Result.Status == FreeShowSlideTransitionStatus.Refused && failed.Index("main") == 0,
            "Queued inputs cannot bypass quarantine after a partially attempted source command");

        using var failedAfterClear = new Harness(visuals) { FailSourcePhase = true };
        var timerFailure = failedAfterClear.Commands.SlideTimerElapsedAsync(new("main", ""));
        await failedAfterClear.Loop.PumpUntilAsync(timerFailure);
        Check(timerFailure.Result.Status == FreeShowSlideTimerExpiryStatus.PartialExecution &&
              timerFailure.Result.TransitionClearAcknowledged &&
              failedAfterClear.Controller.States["main"].Revision == 1 &&
              failedAfterClear.Controller.UnreconciledSlideOutputs.SequenceEqual(new[] { "main" }),
            "Failed advance retains confirmed timer cleanup and quarantines only the selected output");

        using var missingBackend = new Harness(visuals, timerBackends: false);
        var absent = missingBackend.Commands.SlideTimerElapsedAsync(new("main", ""));
        await missingBackend.Loop.PumpUntilAsync(absent);
        Check(absent.Result.Status == FreeShowSlideTimerExpiryStatus.Refused && missingBackend.Trace.Count == 0,
            "Timer commands without an authoritative source backend perform no fictional cleanup");
    }

    private static async Task CheckQueueLifetime()
    {
        var loop = new ManualLoop();
        using var queue = new FreeShowNativeCommandQueue(loop);
        var trace = new List<int>();
        var blocked = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = queue.Enqueue(_ => { trace.Add(1); return blocked.Task; });
        using var cancellation = new CancellationTokenSource();
        var skipped = queue.Enqueue(_ => { trace.Add(2); return Task.FromResult(2); }, cancellation.Token);
        var throws = queue.Enqueue<int>(_ => throw new IOException("synthetic sync failure"));
        var last = queue.Enqueue(_ => { trace.Add(3); return Task.FromResult(3); });
        loop.Pump();
        cancellation.Cancel();
        Check(trace.SequenceEqual(new[] { 1 }), "FIFO queue waits for an asynchronous active command");
        blocked.SetResult(1);
        await loop.PumpUntilAsync(last);
        Check(await active == 1 && await last == 3 && skipped.IsCanceled && throws.IsFaulted &&
              trace.SequenceEqual(new[] { 1, 3 }),
            "Unstarted cancellation and command failure do not execute stale work or poison later commands");
        await ExpectAsync<IOException>(throws);

        var held = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishing = queue.Enqueue(_ => held.Task);
        var abandoned = queue.Enqueue(_ => Task.FromResult(99));
        loop.Pump();
        queue.Dispose();
        Check(abandoned.IsCanceled && !finishing.IsCompleted,
            "Queue disposal cancels only unstarted requests, preserving an in-flight physical operation");
        held.SetResult(4);
        await loop.PumpUntilAsync(finishing);
        Check(await finishing == 4 && queue.OutstandingCount == 0,
            "Active operation can acknowledge completion after queue admission has closed");

        var brokenLoop = new ManualLoop();
        using var brokenQueue = new FreeShowNativeCommandQueue(brokenLoop);
        var heldFailure = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inflight = brokenQueue.Enqueue(_ => heldFailure.Task);
        var pending = brokenQueue.Enqueue(_ => Task.FromResult(7));
        brokenLoop.Pump();
        brokenLoop.FailPost = true;
        heldFailure.SetException(new IOException("synthetic asynchronous failure"));
        await ExpectAsync<InvalidOperationException>(inflight);
        await ExpectAsync<InvalidOperationException>(pending);
        Check(brokenQueue.OutstandingCount == 0,
            "Lost owner loop settles all request tasks and observes an asynchronous backend fault");
    }

    private sealed class Harness : IDisposable
    {
        public ManualLoop Loop { get; } = new();
        public FreeShowNativeCommandQueue Queue { get; }
        public FreeShowOutputCommandCoordinator Controller { get; }
        public FreeShowNativeNavigationCommands Commands { get; }
        public FreeShowNavigationSourceContext Context { get; }
        public Dictionary<string, JsonElement> Physical { get; } = new(StringComparer.Ordinal);
        public List<FreeShowSlideTransitionPlan> Plans { get; } = new();
        public List<string> Trace { get; } = new();
        public Func<Task>? BeforeFirst { get; set; }
        public Action? DuringResolve { get; set; }
        public bool WrongClearAcknowledgment { get; set; }
        public bool CancelDuringClear { get; set; }
        public bool VisualPrepared { get; set; } = true;
        public bool FailSourcePhase { get; set; }
        private readonly ShowDocument show = ShowDocument.Parse(Encoding.UTF8.GetBytes("""
        ["song",{"settings":{"activeLayout":"main"},
          "slides":{"a":{"items":[]},"b":{"items":[]},"c":{"items":[]},"d":{"items":[]}},
          "layouts":{"main":{"slides":[{"id":"a"},{"id":"b"},{"id":"c"},{"id":"d"}]}}}]
        """));

        public Harness(FreeShowVisualSettings visuals, bool locked = false, bool timerBackends = true)
        {
            var settings = JsonNode.Parse("""
            {"outputs":{
              "main":{"name":"Main","enabled":true,"active":true,"out":{
                "slide":{"id":"song","layout":"main","index":0},"transition":{"duration":1},
                "background":{"id":"video"},"extension":{"keep":true}}},
              "online":{"name":"Online","enabled":true,"active":true,"out":{
                "slide":{"id":"song","layout":"main","index":0},"transition":{"duration":5}}},
              "stage":{"name":"Stage","enabled":true,"stageOutput":"main","out":{}}}}
            """)!.AsObject();
            settings["outLocked"] = locked;
            var config = FreeShowOutputConfiguration.Parse(JsonSerializer.SerializeToElement(settings));
            Controller = new(config);
            Context = new(config, visuals);
            Queue = new(Loop);
            foreach (var pair in Controller.States) Physical.Add(pair.Key, pair.Value.Snapshot());
            var sources = new Dictionary<string, IFreeShowSlideTimerExpirySource>();
            if (timerBackends)
                foreach (string id in new[] { "main", "online" }) sources.Add(id, new FakeTimerSource(this, id));
            Commands = new(Queue, Controller, Context, Lookup, Resolve, sources);
        }

        public ShowDocument? Lookup(string id) => id == show.ShowId ? show : null;
        public int Index(string id) => Controller.States[id].Snapshot().GetProperty("slide").GetProperty("index").GetInt32();
        public IReadOnlyDictionary<string, IFreeShowSlideTransitionRuntime> Resolve(FreeShowSlideTransitionPlan plan)
        {
            Plans.Add(plan);
            Trace.Add("plan:" + string.Join(",", plan.Targets.Select(target => target.OutputId)));
            DuringResolve?.Invoke();
            return plan.Targets.ToDictionary(target => target.OutputId,
                target => (IFreeShowSlideTransitionRuntime)new FakeSlideRuntime(this, target.OutputId));
        }
        public void Dispose() => Queue.Dispose();
    }

    private sealed class FakeTimerSource(Harness h, string id) : IFreeShowSlideTimerExpirySource
    {
        public string OutputId => id;
        public bool CanClearTransition(FreeShowCapturedOutput current) => current.OutputId == id;
        public Task<JsonElement> ClearAndCaptureTransitionAsync(FreeShowCapturedOutput current, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            h.Trace.Add("clear:" + id);
            var data = JsonNode.Parse(h.Physical[id].GetRawText())!.AsObject();
            data["transition"] = null;
            h.Physical[id] = JsonSerializer.SerializeToElement(data);
            if (h.CancelDuringClear) throw new OperationCanceledException("synthetic cleanup interruption");
            if (h.WrongClearAcknowledgment) data["extension"] = "unexpected";
            return Task.FromResult(JsonSerializer.SerializeToElement(data));
        }
    }

    private sealed class FakeSlideRuntime(Harness h, string id) : IFreeShowSlideTransitionRuntime
    {
        public string OutputId => id;
        public bool HasPreparedSourceVisual(FreeShowSlideTransitionTarget target) => h.VisualPrepared;
        public bool SupportsCompletePhase(FreeShowSlideTransitionPhase phase, FreeShowSlideTransitionTarget target) => true;
        public async Task ExecuteCompletePhaseAsync(FreeShowSlideTransitionPhase phase,
            FreeShowSlideTransitionTarget target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            h.Trace.Add("phase:" + id + ":" + target.LayoutIndex + ":" + phase);
            if (h.FailSourcePhase) throw new IOException("synthetic source phase failure");
            if (phase == FreeShowSlideTransitionPhase.CheckOriginalBeforeSlideActions && h.BeforeFirst != null)
            {
                var wait = h.BeforeFirst;
                h.BeforeFirst = null;
                await wait();
            }
            if (phase == FreeShowSlideTransitionPhase.SetOriginalSlideWithFullOutputLifecycle)
                h.Physical[id] = FreeShowSlideOutputMutation.Prepare(h.Physical[id], target.NextOutSlide);
        }
        public Task<JsonElement> CaptureAcknowledgedOutDataAsync(FreeShowSlideTransitionTarget target,
            CancellationToken token) => Task.FromResult(h.Physical[id]);
    }

    private sealed class ManualLoop : IFreeShowTimerEventLoop
    {
        private readonly object sync = new();
        private readonly Queue<Action> callbacks = new();
        private TaskCompletionSource available = NewSignal();
        public bool FailPost { get; set; }
        public bool CheckAccess() => true; // Explicit synthetic event-loop ownership.
        public void Post(Action callback)
        {
            lock (sync)
            {
                if (FailPost) throw new IOException("synthetic stopped dispatcher");
                callbacks.Enqueue(callback);
                available.TrySetResult();
            }
        }
        public void Pump()
        {
            while (true)
            {
                Action callback;
                lock (sync)
                {
                    if (callbacks.Count == 0) { available = NewSignal(); return; }
                    callback = callbacks.Dequeue();
                }
                callback();
            }
        }
        public async Task PumpUntilAsync(Task operation)
        {
            while (!operation.IsCompleted)
            {
                Pump();
                Task notification;
                lock (sync) notification = available.Task;
                if (!operation.IsCompleted) await Task.WhenAny(operation, notification);
            }
            await operation;
        }
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static async Task ExpectAsync<T>(Task task) where T : Exception
    {
        try { await task; }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static JsonElement Json(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine("OK: " + label);
    }
}
