using System.Text.Json;
using System.Text.Json.Nodes;
using FreeShow.Native.Core;

// Synthetic data and fake devices only. Invoked with the suite at Beta 2.
internal static class SlideTransitionRuntimeChecks
{
    public static async Task RunAsync()
    {
        CheckMutation();
        await CheckRuntime();
    }

    private static void CheckMutation()
    {
        var previous = Json("""
        {"slide":{"id":"song","layout":"main","index":0,"line":2,
          "revealCount":4,"itemClickReveal":true,"projectIndex":1},
         "background":{"id":"video","time":37},"overlays":["locked"],
         "transition":{"duration":12},"refresh":8,"extension":{"keep":true}}
        """);
        var next = Json("""
        {"id":"song","layout":"main","index":1,"bindings":["screen"],"future":"kept"}
        """);
        var oldText = previous.GetRawText();
        var nextText = next.GetRawText();
        var result = FreeShowSlideOutputMutation.Prepare(previous, next,
            Json("""{"id":"song","index":6}"""));
        var slide = ShowDocument.Property(result, "slide");
        Check(!slide.TryGetProperty("line", out _) &&
              !slide.TryGetProperty("revealCount", out _) &&
              !slide.TryGetProperty("itemClickReveal", out _) &&
              !slide.TryGetProperty("bindings", out _) &&
              slide.GetProperty("projectIndex").GetInt32() == 6 &&
              slide.GetProperty("future").GetString() == "kept",
            "New cue replaces old reveal state and uses the selected project occurrence");
        Check(new[] { "background", "overlays", "transition", "extension", "refresh" }
                  .All(key => JsonElement.DeepEquals(result.GetProperty(key), previous.GetProperty(key))) &&
              previous.GetRawText() == oldText && next.GetRawText() == nextText,
            "Slide assignment retains unrelated output data and leaves both source inputs unchanged");

        var revealed = Json("""
        {"id":"song","layout":"main","index":0,"line":2,"revealCount":5}
        """);
        var refresh = FreeShowSlideOutputMutation.Prepare(previous, revealed);
        Check(refresh.GetProperty("refresh").GetDouble() == 9,
            "Same source slide and line increments refresh when revealCount changes");
        var otherLine = FreeShowSlideOutputMutation.Prepare(previous,
            Json("""{"id":"song","layout":"main","index":0,"line":3}"""));
        Check(otherLine.GetProperty("refresh").GetInt32() == 8,
            "A different source line does not increment refresh");

        var missingLine = Json("""{"slide":{"id":"song","layout":"main","index":0}}""");
        var explicitNull = FreeShowSlideOutputMutation.Prepare(missingLine,
            Json("""{"id":"song","layout":"main","index":0,"line":null}"""));
        Check(!explicitNull.TryGetProperty("refresh", out _),
            "Missing and null line remain different for original strict refresh equality");
        var temporary = FreeShowSlideOutputMutation.Prepare(
            Json("""{"slide":{"id":"tempText","index":0},"refresh":4}"""),
            Json("""{"id":"tempText","index":0}"""));
        Check(temporary.GetProperty("refresh").GetInt32() == 4,
            "Original temporary slides never trigger same-slide refresh");
        var differentActive = FreeShowSlideOutputMutation.Prepare(previous, next,
            Json("""{"id":"other-song","index":9}"""));
        Check(!differentActive.GetProperty("slide").TryGetProperty("projectIndex", out _),
            "Selecting another song cannot insert its project index into the output cue");

        var target = Target(previous, next);
        Check(FreeShowSlideAcknowledgement.Matches(target, result),
            "Acknowledgment accepts source-added project metadata and removed bindings");
        var stale = (JsonObject)JsonNode.Parse(result.GetRawText())!;
        ((JsonObject)stale["slide"]!)["revealCount"] = 4;
        Check(!FreeShowSlideAcknowledgement.Matches(target, JsonSerializer.SerializeToElement(stale)),
            "Acknowledgment refuses a stale revealCount omitted by the new request");
        ((JsonObject)stale["slide"]!).Remove("revealCount");
        ((JsonObject)stale["slide"]!)["itemClickReveal"] = true;
        Check(!FreeShowSlideAcknowledgement.Matches(target, JsonSerializer.SerializeToElement(stale)),
            "Acknowledgment refuses stale click reveal that would expose hidden lyrics");
        ((JsonObject)stale["slide"]!).Remove("itemClickReveal");
        ((JsonObject)stale["slide"]!)["type"] = "pdf";
        Check(!FreeShowSlideAcknowledgement.Matches(target, JsonSerializer.SerializeToElement(stale)),
            "Acknowledgment refuses an unexpected slide renderer type");

        var semantic = Target(missingLine,
            Json("""{"id":"song","layout":"main","index":0,"line":1}"""));
        Check(FreeShowSlideAcknowledgement.Matches(semantic,
            Json("""{"slide":{"line":1.0,"index":0,"layout":"main","id":"song"}}""")),
            "Acknowledgment compares JSON values rather than numeric spelling or property order");
    }

    private static async Task CheckRuntime()
    {
        var happy = CreateScenario();
        var result = await happy.Dispatch();
        Check(result.Succeeded && result.OutputsAcknowledged == 1 &&
              happy.Controller.States["screen"].Revision == 1 &&
              happy.Trace.SequenceEqual(new[] { "before", "set", "capture", "present", "after", "capture", "visible" }),
            "Source phases, Native swap and final selected-control receipt run in original order");
        var state = happy.Controller.States["screen"].Snapshot();
        Check(state.GetProperty("extension").GetString() == "after-effects" &&
              state.GetProperty("background").GetProperty("id").GetString() == "video" &&
              state.GetProperty("slide").GetProperty("projectIndex").GetInt32() == 6,
            "Coordinator commits full post-effect OutData, retaining video and project metadata");
        int completedTraceLength = happy.Trace.Count;
        Check((await happy.Dispatch()).Status == FreeShowSlideTransitionStatus.Refused &&
              happy.Trace.Count == completedTraceLength,
            "Consumed command cannot replay its physical phases");

        var incomplete = CreateScenario();
        incomplete.Source.Complete = false;
        Check((await incomplete.Dispatch()).Status == FreeShowSlideTransitionStatus.Refused &&
              incomplete.Trace.Count == 0,
            "Incomplete source lifecycle prevents any visual or source mutation");

        var unprepared = CreateScenario();
        unprepared.Surface.Prepared = false;
        Check((await unprepared.Dispatch()).Status == FreeShowSlideTransitionStatus.Refused &&
              unprepared.Trace.Count == 0,
            "Missing cached lyrics refuses before original before-slide actions");

        var wrongState = CreateScenario();
        wrongState.Source.StaleRevealAfterSet = true;
        var wrongResult = await wrongState.Dispatch();
        Check(wrongResult.Status == FreeShowSlideTransitionStatus.PartialExecution &&
              !wrongState.Trace.Contains("present") && !wrongState.Trace.Contains("after") &&
              wrongState.Controller.States["screen"].Revision == 0 &&
              wrongState.Controller.UnreconciledSlideOutputs.Contains("screen"),
            "Wrong source state after setOutput refuses the Native swap and quarantines output");

        var wrongStyle = CreateScenario();
        wrongStyle.Source.ChangeStyleAfterSet = true;
        Check((await wrongStyle.Dispatch()).Status == FreeShowSlideTransitionStatus.PartialExecution &&
              !wrongStyle.Trace.Contains("present"),
            "Source action changing style invalidates the old prewarm prediction before presentation");

        var swapFailure = CreateScenario();
        swapFailure.Surface.FailPresentation = true;
        Check((await swapFailure.Dispatch()).Status == FreeShowSlideTransitionStatus.PartialExecution &&
              !swapFailure.Trace.Contains("after") &&
              swapFailure.Controller.States["screen"].Revision == 0,
            "Failed Native swap does not run after-slide effects or acknowledge fictional output");

        var lostVisual = CreateScenario();
        lostVisual.Source.AfterEffects = () => lostVisual.Surface.Presenting = false;
        Check((await lostVisual.Dispatch()).Status == FreeShowSlideTransitionStatus.PartialExecution &&
              lostVisual.Controller.States["screen"].Revision == 0 &&
              lostVisual.Controller.UnreconciledSlideOutputs.Contains("screen"),
            "A cleared or replaced Native control after source effects cannot produce a successful ack");

        var finalSourceChange = CreateScenario();
        finalSourceChange.Source.ChangeSlideAfterEffects = true;
        Check((await finalSourceChange.Dispatch()).Status == FreeShowSlideTransitionStatus.PartialExecution &&
              finalSourceChange.Controller.States["screen"].Revision == 0,
            "Final acknowledgment recaptures actual output rather than reusing the setOutput snapshot");

        var cancelled = CreateScenario();
        using var cancellation = new CancellationTokenSource();
        cancelled.Surface.AfterPresentation = cancellation.Cancel;
        Check((await cancelled.Dispatch(cancellation.Token)).Status == FreeShowSlideTransitionStatus.PartialExecution &&
              cancelled.Controller.UnreconciledSlideOutputs.Contains("screen") &&
              cancelled.Controller.States["screen"].Revision == 0,
            "Cancellation after presentation quarantines output until explicit reconciliation");

        var wrongOrder = CreateScenario();
        bool refusedOrder = false;
        try
        {
            await wrongOrder.Runtime.ExecuteCompletePhaseAsync(
                FreeShowSlideTransitionPhase.SetOriginalSlideWithFullOutputLifecycle,
                wrongOrder.Plan.Targets[0]);
        }
        catch (InvalidOperationException) { refusedOrder = true; }
        Check(refusedOrder && wrongOrder.Trace.Count == 0,
            "Direct runtime callers cannot skip the before-slide phase");

        var changedTarget = CreateScenario();
        var changed = changedTarget.Plan.Targets[0] with
        {
            NextOutSlide = Json("""{"id":"song","layout":"main","index":1,"revealCount":99}""")
        };
        Check(!changedTarget.Runtime.HasPreparedSourceVisual(changed),
            "A prepared runtime cannot be retargeted to another reveal state");
    }

    private static Scenario CreateScenario()
    {
        var config = FreeShowOutputConfiguration.Parse(Json("""
        {"outputs":{"screen":{"name":"Main","enabled":true,"active":true,
          "out":{"slide":{"id":"song","layout":"main","index":0,"revealCount":3},
                 "background":{"id":"video"},"extension":"before-effects"}}}}
        """));
        var controller = new FreeShowOutputCommandCoordinator(config);
        var captured = FreeShowOutputSnapshotBatch.Capture(config, controller.States);
        var target = Target(captured.Outputs["screen"].OutData,
            Json("""{"id":"song","layout":"main","index":1}"""));
        var plan = new FreeShowSlideTransitionPlan(captured, [target], []);
        var trace = new List<string>();
        var style = new FreeShowRevealStyleContext(0, 0, 0);
        var source = new FakeSourceBackend(trace, style);
        var surface = new FakeSurface(trace);
        var runtime = new FreeShowPreparedSlideTransitionRuntime(target, style, source, surface);
        return new(controller, plan, runtime, source, surface, trace);
    }

    private static FreeShowSlideTransitionTarget Target(JsonElement previous, JsonElement next) =>
        new("screen", 0, previous, ShowDocument.Property(previous, "slide"), next,
            "song", "main", next.GetProperty("index").GetInt32(),
            FreeShowSlideAdvanceKind.ChangeSlide, Json("{}"), false);

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
        Console.WriteLine("OK: " + label);
    }

    private sealed record Scenario(
        FreeShowOutputCommandCoordinator Controller, FreeShowSlideTransitionPlan Plan,
        FreeShowPreparedSlideTransitionRuntime Runtime, FakeSourceBackend Source,
        FakeSurface Surface, List<string> Trace)
    {
        public Task<FreeShowSlideTransitionResult> Dispatch(CancellationToken token = default) =>
            Controller.DispatchSlideTransitionAsync(Plan,
                new Dictionary<string, IFreeShowSlideTransitionRuntime> { ["screen"] = Runtime }, token);
    }

    // Deliberately fake source backend; not included in either production assembly.
    private sealed class FakeSourceBackend(List<string> trace, FreeShowRevealStyleContext style)
        : IFreeShowSlideTransitionSourceBackend
    {
        private JsonElement current;
        public string OutputId => "screen";
        public bool Complete { get; set; } = true;
        public bool StaleRevealAfterSet { get; set; }
        public bool ChangeStyleAfterSet { get; set; }
        public bool ChangeSlideAfterEffects { get; set; }
        public Action? AfterEffects { get; set; }

        public bool SupportsCompletePhase(FreeShowSlideTransitionPhase phase,
            FreeShowSlideTransitionTarget target) => Complete;

        public Task ExecuteCompletePhaseAsync(FreeShowSlideTransitionPhase phase,
            FreeShowSlideTransitionTarget target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (phase == FreeShowSlideTransitionPhase.CheckOriginalBeforeSlideActions)
                trace.Add("before");
            else if (phase == FreeShowSlideTransitionPhase.SetOriginalSlideWithFullOutputLifecycle)
            {
                trace.Add("set");
                current = FreeShowSlideOutputMutation.Prepare(target.PreviousOutData,
                    target.NextOutSlide, Json("""{"id":"song","index":6}"""));
                if (StaleRevealAfterSet) SetSlideField("revealCount", 3);
            }
            else
            {
                trace.Add("after");
                var output = (JsonObject)JsonNode.Parse(current.GetRawText())!;
                output["extension"] = "after-effects";
                current = JsonSerializer.SerializeToElement(output);
                if (ChangeSlideAfterEffects) SetSlideField("index", 2);
                AfterEffects?.Invoke();
            }
            return Task.CompletedTask;
        }

        public Task<FreeShowSlideRuntimeSnapshot> CaptureAcknowledgedStateAsync(
            FreeShowSlideTransitionTarget target, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            trace.Add("capture");
            return Task.FromResult(new FreeShowSlideRuntimeSnapshot(OutputId, current,
                ChangeStyleAfterSet ? style with { StyleLines = 2 } : style));
        }

        private void SetSlideField(string key, int value)
        {
            var output = (JsonObject)JsonNode.Parse(current.GetRawText())!;
            ((JsonObject)output["slide"]!)[key] = value;
            current = JsonSerializer.SerializeToElement(output);
        }
    }

    private sealed class FakeSurface(List<string> trace) : IFreeShowPreparedSlideSurface
    {
        public string OutputId => "screen";
        public bool Prepared { get; set; } = true;
        public bool Presenting { get; set; }
        public bool FailPresentation { get; set; }
        public Action? AfterPresentation { get; set; }

        public bool HasPrepared(FreeShowSlideTransitionTarget target,
            FreeShowRevealStyleContext style) => Prepared;

        public Task PresentAsync(FreeShowSlideTransitionTarget target,
            FreeShowRevealStyleContext style, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            trace.Add("present");
            if (FailPresentation) throw new IOException("Synthetic visual failure.");
            Presenting = true;
            AfterPresentation?.Invoke();
            return Task.CompletedTask;
        }

        public Task<bool> IsPresentingAsync(FreeShowSlideTransitionTarget target,
            FreeShowRevealStyleContext style, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            trace.Add("visible");
            return Task.FromResult(Presenting);
        }
    }
}
