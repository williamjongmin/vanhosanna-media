using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Read-only coordination subset of OutputHelper.advanceOutputs():
/// inspect ALL active normal outputs before selecting a project boundary.
/// Each physical output retains its original ID and separate OutSlide index,
/// line, click-reveal and reveal-count position. The original 80ms
/// cross-source duplicate suppression remains FreeShowShortcutRouter's job.
/// No outputs, project played flags or church files are written.
/// </summary>
public static class FreeShowMultiOutputAdvanceInspection
{
    public static FreeShowMultiOutputAdvanceResult Prepare(
        FreeShowOutputConfiguration configured,
        IReadOnlyDictionary<string, FreeShowOutputState> currentStates,
        FreeShowNavigationSourceContext sourceSettings,
        Func<string, ShowDocument?> knownSourceShow,
        FreeShowSlideAdvanceDirection direction,
        FreeShowProjectContent? activeProject = null,
        FreeShowProjectContentItem? activeProjectItem = null,
        bool focusMode = false,
        bool nextItemOnLastSlide = true)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(currentStates);
        ArgumentNullException.ThrowIfNull(sourceSettings);
        ArgumentNullException.ThrowIfNull(knownSourceShow);

        var active = configured.GetActiveNormalOutputs();
        if (ShowDocument.IsTrue(configured.RawSettings, "outLocked"))
            return Block("Original FreeShow outputs are locked.");
        if (active.Count == 0)
            return Block("No active FreeShow normal output exists.");

        var limit = sourceSettings.GetStyleLimits();
        if (!limit.IsAuthoritative)
            return Block(limit.Warning ?? "Original output style line settings are unavailable.");

        var plans = new Dictionary<string, FreeShowSlideAdvanceResult>(
            StringComparer.Ordinal);
        var warnings = new List<string>();

        foreach (var output in active)
        {
            if (!currentStates.TryGetValue(output.Id, out var state))
            {
                warnings.Add("Missing original OutData state for " + output.Id);
                continue;
            }
            var slide = ShowDocument.Property(state.Snapshot(), "slide");
            string id = ShowDocument.String(slide, "id");
            if (id.Length == 0)
            {
                // The real OutputHelper may instead choose an active project
                // item, handle a cleared output slide cache, media or focus
                // context. An empty current slide cannot be interpreted as
                // "first slide" without the original active item.
                warnings.Add("Output " + output.Id +
                    " has no current Show; active item / cleared cache resolution is unported.");
                continue;
            }

            ShowDocument? sourceShow;
            try { sourceShow = knownSourceShow(id); }
            catch (Exception error)
            {
                warnings.Add("Source Show lookup failed for " + id + ": " + error.Message);
                continue;
            }
            if (sourceShow == null || sourceShow.ShowId != id)
            {
                warnings.Add("Original Show unavailable or ID mismatched: " + id);
                continue;
            }

            var plan = FreeShowSlideAdvancePlanner.Prepare(
                sourceShow, slide, output.Id, direction, limit.FewestStyleLines,
                sourceSettings.MatchesOriginalOutput);
            plans.Add(output.Id, plan);
            if (plan.Kind == FreeShowSlideAdvanceKind.Refused)
                warnings.Add(output.Id + ": " + plan.Reason);
        }

        if (warnings.Count != 0 || plans.Count != active.Count)
            return new(FreeShowMultiOutputAdvanceStatus.Refused, plans,
                null, warnings.AsReadOnly());

        bool atBoundary = plans.Values.All(p =>
            p.Kind == FreeShowSlideAdvanceKind.ProjectBoundary);

        if (atBoundary &&
            (direction is FreeShowSlideAdvanceDirection.Next or
                FreeShowSlideAdvanceDirection.Previous) &&
            activeProject != null)
        {
            var projectResult = FreeShowProjectAdvancePlanner.Prepare(
                activeProject, ShowDocument.Property(
                    currentStates[active[0].Id].Snapshot(), "slide"),
                activeProjectItem, direction, focusMode, nextItemOnLastSlide);
            return new(FreeShowMultiOutputAdvanceStatus.ProjectSelectionOnly,
                plans, projectResult, []);
        }

        if (atBoundary)
            return new(FreeShowMultiOutputAdvanceStatus.AtShowBoundary, plans,
                null, ["Original project/active-item controller is needed for further navigation."]);

        // Original changeProjectItem must wait until ALL active outputs have
        // reached the end of the active Show; never jump a project merely
        // because one screen reached its final slide.
        if (plans.Values.Any(p => p.HasTarget))
            return new(FreeShowMultiOutputAdvanceStatus.SlideOrRevealTargets,
                plans, null, []);

        return new(FreeShowMultiOutputAdvanceStatus.AtShowBoundary, plans, null,
            ["No source show target available."]);
    }

    private static FreeShowMultiOutputAdvanceResult Block(string reason) =>
        new(FreeShowMultiOutputAdvanceStatus.Refused,
            new Dictionary<string, FreeShowSlideAdvanceResult>(), null, [reason]);
}

public enum FreeShowMultiOutputAdvanceStatus
{
    SlideOrRevealTargets, ProjectSelectionOnly, AtShowBoundary, Refused
}

public sealed record FreeShowMultiOutputAdvanceResult(
    FreeShowMultiOutputAdvanceStatus Status,
    IReadOnlyDictionary<string, FreeShowSlideAdvanceResult> OutputPlans,
    FreeShowProjectAdvanceResult? ProjectPlan,
    IReadOnlyList<string> Warnings)
{
    public bool CanDispatchLive => false; // Full updateOut/actions not yet ported.
}
