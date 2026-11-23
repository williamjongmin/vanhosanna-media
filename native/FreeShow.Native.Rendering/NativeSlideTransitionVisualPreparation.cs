using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using FreeShow.Native.Core;

namespace FreeShow.Native.Rendering;

/// <summary>
/// Constructs expected next-slide-only output snapshots for prewarming
/// independent Native lyric views. Physical source effects are NOT executed:
/// background, timers, audio and the actual acknowledged output snapshots
/// must come from the complete FreeShow slide transition runtime.
/// </summary>
public static class NativeSlideTransitionVisualPreparation
{
    public static NativeSlideTransitionVisualResult Prepare(
        FreeShowSlideTransitionPlan plan,
        FreeShowOutputConfiguration settings,
        FreeShowVisualSettings styles,
        IReadOnlyDictionary<string, FreeShowOutputState> acknowledgedStates,
        IReadOnlyDictionary<string, NativeFreeShowOutputSession> sourceSessions,
        Func<string, string, string, int, NativeFramePlan?> sourceFrame)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(acknowledgedStates);
        ArgumentNullException.ThrowIfNull(sourceSessions);
        ArgumentNullException.ThrowIfNull(sourceFrame);

        if (!Dispatcher.UIThread.CheckAccess())
            return Refuse("Native lyric prewarming requires the Avalonia UI thread.");
        if (!plan.IsSourcePrepared || !plan.SourceCapture.StillMatches(acknowledgedStates))
            return Refuse("FreeShow source slide plan is incomplete or already stale.");

        // Speculative text visibility preparation is separate from applying
        // the original setOutput()/updateOut() lifecycle. Only clone the
        // expected OutSlide field, keeping all previous source fields intact.
        var frozen = plan.SourceCapture.Outputs.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var target in plan.Targets)
        {
            if (!frozen.TryGetValue(target.OutputId, out var original) ||
                original.Revision != target.ExpectedRevision)
                return Refuse("Source output changed since slide request: " +
                              target.OutputId);

            JsonObject output;
            try
            {
                output = JsonNode.Parse(original.OutData.GetRawText()) as JsonObject ??
                    throw new InvalidDataException("Original OutData is not an object.");
                output["slide"] = JsonNode.Parse(target.NextOutSlide.GetRawText());
            }
            catch (Exception error)
            {
                return Refuse("Cannot safely prepare source OutSlide " +
                              target.OutputId + ": " + error.Message);
            }

            frozen[target.OutputId] = new(
                target.OutputId, original.Revision,
                JsonSerializer.SerializeToElement(output));
        }

        // All physical outputs must agree on the exact original output batch
        // before a single lyric control is changed. Unknown/currently
        // cleared or unrenderable outputs block the full-batch prototype.
        var projected = FreeShowOutputRevealBatchPlanner.Prepare(
            settings, styles,
            new FreeShowCapturedOutputBatch(frozen, Array.Empty<string>()),
            sourceFrame);
        if (!projected.CanPrewarmAll)
            return Refuse(string.Join("; ", projected.Problems));

        var warmed = NativeMultiOutputRevealPrewarmer.Prewarm(
            projected, sourceSessions, acknowledgedStates);
        if (!warmed.PreparedAllOutputs)
            return Refuse(warmed.Problem ??
                "Native source lyric prewarming was incomplete.");

        return new(warmed.PreparedOutputCount, true, null)
        {
            PreparedStyles = new System.Collections.ObjectModel.ReadOnlyDictionary<
                string, FreeShowRevealStyleContext>(projected.Targets.ToDictionary(
                    pair => pair.Key, pair => pair.Value.Style, StringComparer.Ordinal))
        };
    }

    private static NativeSlideTransitionVisualResult Refuse(string reason) =>
        new(0, false, reason);
}

public sealed record NativeSlideTransitionVisualResult(
    int PrewarmedOutputCount, bool ReadyForSourcePhaseIntegration,
    string? Refusal)
{
    /// <summary>
    /// Exact source-batch styles used by the cached controls, for constructing
    /// a single-command FreeShowPreparedSlideTransitionRuntime per target.
    /// A production source backend must independently capture current style
    /// at application/acknowledgment time; this prediction is not an ack.
    /// </summary>
    public IReadOnlyDictionary<string, FreeShowRevealStyleContext> PreparedStyles { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, FreeShowRevealStyleContext>(
            new Dictionary<string, FreeShowRevealStyleContext>());

    public bool CanDispatchLive => false;
}
