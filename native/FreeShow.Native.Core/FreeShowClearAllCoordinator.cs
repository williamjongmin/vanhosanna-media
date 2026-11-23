using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Source FreeShow clear.ts clearAll(button): its complete ordered lifecycle,
/// NOT a shortcut combining partly implemented Native F1-F4 effects.
/// The only executor is a separately registered source-complete runtime.
/// All original church files and physical outputs remain untouched unless
/// a caller explicitly installs and invokes that runtime.
/// </summary>
public sealed partial class FreeShowOutputCommandCoordinator
{
    public FreeShowClearAllPlan PlanClearAll(
        FreeShowClearAllContext context,
        bool hasOriginalShowLookup = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        var problems = new List<string>();
        if (ShowDocument.IsTrue(configuration.RawSettings, "outLocked"))
            problems.Add("The original FreeShow output is locked.");

        if (!context.FromButton && (context.PopupActive ||
            context.SelectedNonScripture || context.EditingSelectionActive ||
            context.StageSelectionActive || context.ContextMenuActive ||
            context.TopContextMenuActive))
            problems.Add("Original clearAll(false) is blocked by operator UI context.");

        if (context.PlayingAudioData.ValueKind != JsonValueKind.Array ||
            !context.PlayingMetronome.HasValue)
            problems.Add("Authoritative global playingAudioData/metronome snapshot is missing.");

        var selected = configuration.GetActiveNormalOutputs();
        if (selected.Count == 0)
            problems.Add("No enabled FreeShow normal outputs are active.");

        var revisions = new Dictionary<string, long>(StringComparer.Ordinal);
        var locked = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        bool anyVisibleOutputContent = false;

        foreach (var output in selected)
        {
            if (!states.TryGetValue(output.Id, out var state))
            {
                problems.Add("Original output state missing: " + output.Id);
                continue;
            }

            revisions[output.Id] = state.Revision;
            var lockedIds = new HashSet<string>(StringComparer.Ordinal);
            var outData = state.Snapshot();

            var overlaysValue = ShowDocument.Property(outData, "overlays");
            if (overlaysValue.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in overlaysValue.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        problems.Add("Non-string overlay ID in original output " + output.Id);
                        continue;
                    }

                    var id = item.GetString() ?? "";
                    var definition = ShowDocument.Property(overlays, id);
                    if (definition.ValueKind != JsonValueKind.Object)
                    {
                        problems.Add("Cannot safely check original overlay lock: " + id);
                        continue;
                    }
                    if (ShowDocument.IsTrue(definition, "locked"))
                        lockedIds.Add(id);
                    else
                        anyVisibleOutputContent = true;
                }
            }
            else if (overlaysValue.ValueKind is not
                (JsonValueKind.Null or JsonValueKind.Undefined))
                problems.Add("Output overlays must be an original string array.");

            locked[output.Id] = lockedIds;

            foreach (var property in outData.EnumerateObject())
            {
                if (property.Name is "scene" or "refresh" or "overlays")
                    continue; // Original isOutCleared(null) deliberately ignores scene/refresh.
                var value = property.Value;
                if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined or
                    JsonValueKind.False)
                    continue;
                if (value.ValueKind == JsonValueKind.Array &&
                    property.Name == "effects" && value.GetArrayLength() == 0)
                    continue;
                if (value.ValueKind == JsonValueKind.Number &&
                    value.TryGetDouble(out var zero) && zero == 0)
                    continue;
                if (value.ValueKind == JsonValueKind.String &&
                    value.GetString() == "")
                    continue;
                anyVisibleOutputContent = true;
            }
        }

        bool audioPlaying = context.PlayingAudioData.ValueKind == JsonValueKind.Array &&
                            context.PlayingAudioData.GetArrayLength() > 0 ||
                            context.PlayingMetronome == true;
        bool alreadyCleared = !anyVisibleOutputContent && !audioPlaying;
        if (!alreadyCleared && context.FocusMode && !hasOriginalShowLookup &&
            selected.Any(x => states.TryGetValue(x.Id, out var st) && st.HasSlide))
            problems.Add("Focus-mode clearSlide requires original Show for source slide cache.");

        return new(
            selected.Select(x => x.Id).ToArray(),
            revisions,
            locked,
            context,
            alreadyCleared,
            problems.AsReadOnly());
    }

    public IReadOnlyList<string> CheckClearAllReadiness(
        FreeShowClearAllPlan plan, IFreeShowClearAllRuntime? runtime)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var issues = plan.Problems.ToList();

        foreach (var outputId in plan.ActiveOutputIds)
        {
            if (!states.TryGetValue(outputId, out var current) ||
                !plan.ExpectedRevisions.TryGetValue(outputId, out var revision) ||
                current.Revision != revision)
                issues.Add("Original FreeShow output changed since Clear All planning: " + outputId);
        }

        if (plan.AlreadyCleared && issues.Count == 0)
            return issues; // Only local slide-position cache reset is needed.

        if (runtime == null)
            issues.Add("Source-complete Clear All backend not installed.");
        else
            foreach (var step in FreeShowClearAllSteps.ExecuteInOriginalOrder)
                if (!runtime.SupportsCompleteSourceStep(step))
                    issues.Add("Missing original Clear All source step: " + step);

        return issues.AsReadOnly();
    }

    /// <summary>
    /// Entire batch shares the same executeGate as the existing native
    /// F1/F2/F3/F4 command coordinator and RestoreAsync.
    /// The snapshot is captured BEFORE timeline stop / output changes.
    /// On failure, cache survives and the result states partial execution.
    /// No physical rollback is falsely implied.
    /// </summary>
    public async Task<FreeShowClearAllResult> ClearAllAsync(
        FreeShowClearAllContext context,
        FreeShowOutputRestoreCache cache,
        FreeShowOutputSlidePositionCache slidePositionCache,
        IFreeShowClearAllRuntime? runtime,
        Func<string, ShowDocument?>? knownShowLookup = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(slidePositionCache);

        await executeGate.WaitAsync(cancellationToken);
        try
        {
            var plan = PlanClearAll(context, knownShowLookup != null);
            var issues = CheckClearAllReadiness(plan, runtime);
            if (issues.Count != 0)
                return new(FreeShowClearAllStatus.Refused, 0, issues);

            // Validate every original Show lookup needed for focus-mode F2
            // before touching cached state or external output devices.
            var verifiedShows = new Dictionary<string, ShowDocument>(
                StringComparer.Ordinal);
            if (!plan.AlreadyCleared && context.FocusMode)
            {
                foreach (var id in plan.ActiveOutputIds)
                {
                    var slide = ShowDocument.Property(states[id].Snapshot(), "slide");
                    var showId = ShowDocument.String(slide, "id");
                    if (showId.Length == 0 || verifiedShows.ContainsKey(showId)) continue;
                    ShowDocument? sourceShow;
                    try { sourceShow = knownShowLookup?.Invoke(showId); }
                    catch (Exception ex)
                    {
                        return new(FreeShowClearAllStatus.Refused, 0,
                            ["Original Show lookup failed: " + ex.Message]);
                    }
                    if (sourceShow == null || sourceShow.ShowId != showId)
                        return new(FreeShowClearAllStatus.Refused, 0,
                            ["Original Show is missing or mismatched: " + showId]);
                    verifiedShows.Add(showId, sourceShow);
                }
            }

            // Source clearAll first clears the slide-position cache, even
            // if everything is already cleared and the operation returns.
            slidePositionCache.Clear();
            if (plan.AlreadyCleared)
                return new(FreeShowClearAllStatus.AlreadyCleared, 0,
                    Array.Empty<string>());

            // Once a previous Clear All partially failed, keep the original
            // recovery snapshot rather than overwriting it with a half-cleared
            // state. This conservative policy is stricter than source JS.
            if (cache.HasSnapshot)
                return new(FreeShowClearAllStatus.Refused, 0,
                    ["An earlier Clear All restore cache still exists. Restore or reconcile it first."]);

            // This preflight requires authoritative audio state. Audio metadata
            // is not inferred from a playback button or empty native registry.
            cache.Capture(plan.ActiveOutputIds, states,
                context.PlayingAudioData, context.PlayingMetronome);

            int completed = 0;
            try
            {
                foreach (var step in FreeShowClearAllSteps.ExecuteInOriginalOrder)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (step == FreeShowClearAllStep.ClearSlides && context.FocusMode)
                    {
                        // clearAll() calls clearSlide(false) in focus mode,
                        // re-caching eligible current positions after its
                        // initial reset. Refuse guessed Show/layout metadata.
                        foreach (var id in plan.ActiveOutputIds)
                        {
                            var state = states[id];
                            var slide = ShowDocument.Property(state.Snapshot(), "slide");
                            var showId = ShowDocument.String(slide, "id");
                            if (showId.Length == 0) continue;
                            // All Shows were validated before the first
                            // external step; no unverified callback runs here.
                            slidePositionCache.TryCapture(
                                state, verifiedShows[showId], true);
                        }
                    }

                    await runtime!.ExecuteCompleteSourceStepAsync(step, plan, cancellationToken);
                    completed++;
                    CommitClearAllStepAfterAcknowledgement(step, plan);
                }

                return new(FreeShowClearAllStatus.Completed, completed,
                    Array.Empty<string>());
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // Preserve cache and current state; a physical action may have
                // partially executed even if that individual step threw.
                return new(completed > 0 ? FreeShowClearAllStatus.PartialExecution
                        : FreeShowClearAllStatus.Failed,
                    completed, [error.GetType().Name + ": " + error.Message +
                        " — physical output state requires reconciliation."]);
            }
        }
        finally { executeGate.Release(); }
    }

    private void CommitClearAllStepAfterAcknowledgement(
        FreeShowClearAllStep step, FreeShowClearAllPlan plan)
    {
        foreach (var id in plan.ActiveOutputIds)
        {
            if (!states.TryGetValue(id, out var state)) continue;
            switch (step)
            {
                case FreeShowClearAllStep.ClearBackgrounds:
                    state.TryApply(new FreeShowShortcutAction(
                        NativeShortcutCommand.ClearBackground, id));
                    break;
                case FreeShowClearAllStep.ClearNextSlideTimers:
                case FreeShowClearAllStep.ClearAllRemainingTimers:
                    state.ClearTransition();
                    break;
                case FreeShowClearAllStep.ClearSlides:
                    state.TryApply(new FreeShowShortcutAction(
                        NativeShortcutCommand.ClearSlide, id));
                    break;
                case FreeShowClearAllStep.ClearOverlaysAndEffects:
                    state.ClearOverlaysKeepingLocked(plan.LockedOverlayIds[id]);
                    break;
            }
        }
    }
}

public sealed record FreeShowClearAllContext(
    bool FromButton, bool FocusMode,
    JsonElement PlayingAudioData, bool? PlayingMetronome,
    bool PopupActive = false, bool SelectedNonScripture = false,
    bool EditingSelectionActive = false, bool StageSelectionActive = false,
    bool ContextMenuActive = false, bool TopContextMenuActive = false);

public enum FreeShowClearAllStep
{
    StopTimelinePlayback,
    ClearBackgrounds,
    ClearNextSlideTimers,
    ClearSlides,
    ClearOverlaysAndEffects,
    ClearGlobalAudio,
    ClearAllRemainingTimers
}

public static class FreeShowClearAllSteps
{
    public static IReadOnlyList<FreeShowClearAllStep> ExecuteInOriginalOrder { get; } =
    [
        FreeShowClearAllStep.StopTimelinePlayback,
        FreeShowClearAllStep.ClearBackgrounds,
        FreeShowClearAllStep.ClearNextSlideTimers,
        FreeShowClearAllStep.ClearSlides,
        FreeShowClearAllStep.ClearOverlaysAndEffects,
        FreeShowClearAllStep.ClearGlobalAudio,
        FreeShowClearAllStep.ClearAllRemainingTimers
    ];
}

public sealed record FreeShowClearAllPlan(
    IReadOnlyList<string> ActiveOutputIds,
    IReadOnlyDictionary<string, long> ExpectedRevisions,
    IReadOnlyDictionary<string, IReadOnlySet<string>> LockedOverlayIds,
    FreeShowClearAllContext Context,
    bool AlreadyCleared,
    IReadOnlyList<string> Problems)
{
    public bool IsReady => Problems.Count == 0;
}

/// <summary>
/// Each registered step must reproduce the *full* original source action,
/// including setOutput events, custom action notifications, lyric transition,
/// background media continuity, overlay timers, global audio and timeline
/// effects. A JSON-only state write is not a valid implementation.
/// </summary>
public interface IFreeShowClearAllRuntime
{
    bool SupportsCompleteSourceStep(FreeShowClearAllStep step);
    Task ExecuteCompleteSourceStepAsync(
        FreeShowClearAllStep step, FreeShowClearAllPlan plan,
        CancellationToken cancellationToken = default);
}

public enum FreeShowClearAllStatus
{
    Completed, AlreadyCleared, Refused, Failed, PartialExecution
}

public sealed record FreeShowClearAllResult(
    FreeShowClearAllStatus Status, int CompletedSteps,
    IReadOnlyList<string> Problems)
{
    public bool Succeeded =>
        Status is FreeShowClearAllStatus.Completed or FreeShowClearAllStatus.AlreadyCleared;
}
