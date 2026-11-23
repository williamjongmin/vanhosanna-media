using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Source-aware multi-output reveal preparation from one captured set of
/// original OutData revisions. Each screen can use different style.lines,
/// but all share getFewestOutputLines()/getHighestOutputLinePos() values
/// derived from the SAME batch. No future shortcut states are predicted.
/// </summary>
public static class FreeShowOutputRevealBatchPlanner
{
    public static FreeShowOutputRevealBatchPlan Prepare(
        FreeShowOutputConfiguration configuration,
        FreeShowVisualSettings originalStyles,
        FreeShowCapturedOutputBatch capture,
        Func<string, string, string, int, NativeFramePlan?> preparedSourceFrame)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(originalStyles);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(preparedSourceFrame);

        var active = configuration.GetActiveNormalOutputs();
        var results = new Dictionary<string, FreeShowOutputRevealTarget>(
            StringComparer.Ordinal);
        var problems = capture.Problems.ToList();

        if (active.Count == 0)
            problems.Add("No active original FreeShow normal outputs.");
        if (!capture.IsCoherent)
            return new(capture, results, problems.AsReadOnly());

        var captured = capture.OutDataById;

        foreach (var output in active)
        {
            if (!capture.Outputs.TryGetValue(output.Id, out var outData))
            {
                problems.Add("Original output is missing from captured batch: " + output.Id);
                continue;
            }

            var slide = outData.OutSlide;
            string showId = ShowDocument.String(slide, "id");
            string layoutId = ShowDocument.String(slide, "layout");
            var rawIndex = ShowDocument.Property(slide, "index");
            if (showId.Length == 0 || layoutId.Length == 0 ||
                rawIndex.ValueKind != JsonValueKind.Number ||
                !rawIndex.TryGetInt32(out int index) || index < 0)
            {
                problems.Add("Original OutSlide is incomplete for " + output.Id);
                continue;
            }

            var style = FreeShowRevealStyleResolver.ResolveCaptured(
                output.Id, configuration, originalStyles, captured);
            if (!style.CanPrepare)
            {
                problems.Add(output.Id + ": " + style.Warning);
                continue;
            }

            NativeFramePlan? sourceFrame;
            try { sourceFrame = preparedSourceFrame(output.Id, showId, layoutId, index); }
            catch (Exception exception)
            {
                problems.Add("Original render plan lookup failed for " +
                             output.Id + ": " + exception.Message);
                continue;
            }

            if (sourceFrame == null ||
                sourceFrame.Source.LayoutIndex != index)
            {
                problems.Add("Prepared native source frame is missing for " + output.Id);
                continue;
            }

            var reveal = FreeShowRevealRenderProjection.Prepare(
                sourceFrame, slide, style.Context!);
            if (!reveal.IsSupported)
            {
                problems.Add(output.Id + ": " + string.Join("; ", reveal.Unported));
                continue;
            }

            results[output.Id] = new(
                output.Id, outData.Revision, showId, layoutId,
                index, slide.Clone(), style.Context!, reveal.Visible);
        }

        if (results.Count != active.Count)
            problems.Add("Not all active original output layers can render this state.");

        return new(capture, results, problems.AsReadOnly());
    }
}

public sealed record FreeShowOutputRevealTarget(
    string OutputId,
    long SourceRevision,
    string ShowId,
    string LayoutId,
    int OriginalLayoutIndex,
    JsonElement OutSlide,
    FreeShowRevealStyleContext Style,
    NativeFramePlan VisibleFrame);

public sealed record FreeShowOutputRevealBatchPlan(
    FreeShowCapturedOutputBatch Captured,
    IReadOnlyDictionary<string, FreeShowOutputRevealTarget> Targets,
    IReadOnlyList<string> Problems)
{
    public bool CanPrewarmAll => Captured.IsCoherent &&
        Targets.Count > 0 && Problems.Count == 0;

    public bool CanDispatchLive => false;
}
