using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// An explicit source-lifecycle contract for original
/// OutputHelper.playSlide(): checkActionTrigger(layout.data), then (after
/// the pending slide callback) setOutput("slide", ...), then updateOut(...).
/// It is NOT permission to invoke a Native lyric frame by itself.
/// </summary>
public static class FreeShowSlideTransitionPlanner
{
    public static FreeShowSlideTransitionPlan Prepare(
        FreeShowOutputConfiguration configuration,
        FreeShowCapturedOutputBatch captured,
        FreeShowMultiOutputAdvanceResult inspected,
        Func<string, ShowDocument?> sourceShows)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(inspected);
        ArgumentNullException.ThrowIfNull(sourceShows);

        var problems = captured.Problems.ToList();
        var targets = new List<FreeShowSlideTransitionTarget>();
        var active = configuration.GetActiveNormalOutputs();
        if (ShowDocument.IsTrue(configuration.RawSettings, "outLocked"))
            problems.Add("Original FreeShow output is locked.");
        if (!captured.IsCoherent)
            return new(captured, targets, problems.AsReadOnly());

        if (inspected.Status != FreeShowMultiOutputAdvanceStatus.SlideOrRevealTargets)
            problems.Add("Original navigation is not a physical lyric-slide action.");
        if (inspected.Warnings.Count > 0)
            problems.AddRange(inspected.Warnings);

        // A multi-screen project boundary is a selection operation. It must
        // not be mistaken for an empty lyric frame on that output.
        foreach (var output in active)
        {
            if (!captured.Outputs.TryGetValue(output.Id, out var previous))
            {
                problems.Add("Current source OutData snapshot missing for " + output.Id);
                continue;
            }
            if (!inspected.OutputPlans.TryGetValue(output.Id, out var next))
            {
                problems.Add("Source navigation target missing for " + output.Id);
                continue;
            }
            if (next.Kind == FreeShowSlideAdvanceKind.ProjectBoundary)
                continue;
            if (next.Kind is not
                (FreeShowSlideAdvanceKind.ChangeSlide or
                 FreeShowSlideAdvanceKind.RevealWithinSlide) ||
                next.NextOutSlide.ValueKind != JsonValueKind.Object)
            {
                problems.Add("Original slide navigation cannot be executed for " + output.Id);
                continue;
            }

            string sourceId = ShowDocument.String(next.NextOutSlide, "id");
            string layoutId = ShowDocument.String(next.NextOutSlide, "layout");
            var rawIndex = ShowDocument.Property(next.NextOutSlide, "index");
            if (sourceId.Length == 0 || layoutId.Length == 0 ||
                rawIndex.ValueKind != JsonValueKind.Number ||
                !rawIndex.TryGetInt32(out int index) || index < 0)
            {
                problems.Add("Original target has no valid Show/layout/index: " + output.Id);
                continue;
            }

            var original = previous.OutSlide;
            string previousId = ShowDocument.String(original, "id");
            if (previousId.Length == 0 || previousId != sourceId)
            {
                // ActiveShow/activeFocus, projectIndex and a cleared slide
                // cache are source-level selection operations not ported to
                // this output-only transition path.
                problems.Add("Original Show selection changes require active project controller.");
                continue;
            }

            ShowDocument? show;
            try { show = sourceShows(sourceId); }
            catch (Exception error)
            {
                problems.Add("Original Show lookup failed: " + error.Message);
                continue;
            }
            if (show == null || show.ShowId != sourceId)
            {
                problems.Add("Original Show ID not available: " + sourceId);
                continue;
            }

            IReadOnlyList<LayoutEntry> layout;
            try { layout = show.ReadLayoutEntries(layoutId); }
            catch (Exception error)
            {
                problems.Add("Original source layout could not be inspected: " +
                    output.Id + ": " + error.Message);
                continue;
            }
            if (index >= layout.Count || layout[index].Index != index ||
                layout[index].EffectiveDisabled)
            {
                problems.Add("Original target layout index not outputtable: " + output.Id);
                continue;
            }

            // Preserve the source layout's exact action metadata for the
            // eventual complete runtime. Never perform it implicitly.
            targets.Add(new(
                output.Id, previous.Revision, previous.OutData.Clone(),
                original.Clone(), next.NextOutSlide.Clone(), sourceId, layoutId,
                index, next.Kind, layout[index].RawEntry.Clone(),
                next.RequiresUnportedSourceEffects));
        }

        if (targets.Count == 0)
            problems.Add("No active output has a supported slide/reveal transition.");

        if (inspected.OutputPlans.Count != active.Count)
            problems.Add("Navigation did not inspect every active FreeShow normal output.");

        return new(captured, targets.AsReadOnly(), problems.AsReadOnly());
    }
}

public enum FreeShowSlideTransitionPhase
{
    CheckOriginalBeforeSlideActions,
    SetOriginalSlideWithFullOutputLifecycle,
    RunOriginalUpdateOutAndAfterSlideEffects
}

public static class FreeShowSlideTransitionOrder
{
    public static IReadOnlyList<FreeShowSlideTransitionPhase> Phases { get; } =
    [
        FreeShowSlideTransitionPhase.CheckOriginalBeforeSlideActions,
        FreeShowSlideTransitionPhase.SetOriginalSlideWithFullOutputLifecycle,
        FreeShowSlideTransitionPhase.RunOriginalUpdateOutAndAfterSlideEffects
    ];
}

public sealed record FreeShowSlideTransitionTarget(
    string OutputId, long ExpectedRevision, JsonElement PreviousOutData,
    JsonElement PreviousOutSlide, JsonElement NextOutSlide,
    string ShowId, string LayoutId, int LayoutIndex,
    FreeShowSlideAdvanceKind NavigationKind,
    JsonElement OriginalLayoutEntry,
    bool RequiresUnportedSourceEffects);

public sealed record FreeShowSlideTransitionPlan(
    FreeShowCapturedOutputBatch SourceCapture,
    IReadOnlyList<FreeShowSlideTransitionTarget> Targets,
    IReadOnlyList<string> Problems)
{
    public bool IsSourcePrepared => Problems.Count == 0 &&
        SourceCapture.IsCoherent && Targets.Count > 0;
    public bool CanDispatchLive => false; // Requires an actual complete runtime.
}

/// <summary>
/// The runtime must handle each entire upstream source phase, including
/// source actions, project selection, output_changed, timers, media, audio,
/// attribution, category actions and slide usage. It must report the full
/// acknowledged original OutData, not a synthetic slide index. Merely
/// calling ShowPrewarmedReveal() does not satisfy this contract.
/// </summary>
public interface IFreeShowSlideTransitionRuntime
{
    string OutputId { get; }
    bool SupportsCompletePhase(
        FreeShowSlideTransitionPhase phase,
        FreeShowSlideTransitionTarget target);
    Task ExecuteCompletePhaseAsync(
        FreeShowSlideTransitionPhase phase,
        FreeShowSlideTransitionTarget target,
        CancellationToken cancellationToken = default);
    Task<JsonElement> CaptureAcknowledgedOutDataAsync(
        FreeShowSlideTransitionTarget target,
        CancellationToken cancellationToken = default);
}

public enum FreeShowSlideTransitionStatus
{
    Completed,
    Refused,
    Failed,
    PartialExecution
}

public sealed record FreeShowSlideTransitionResult(
    FreeShowSlideTransitionStatus Status,
    int OutputsAcknowledged,
    IReadOnlyList<string> Problems)
{
    public bool Succeeded => Status == FreeShowSlideTransitionStatus.Completed;
}
