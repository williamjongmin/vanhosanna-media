using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Preparation-only enumeration of source OutputHelper next/reveal states.
/// It is intentionally limited to one active output: getOutputLines() also
/// depends on the highest line position of OTHER active outputs. Prewarming
/// a multi-screen timeline without those changing states would be unsound.
/// </summary>
public static class FreeShowRevealPrewarmSequence
{
    public static FreeShowRevealPrewarmPlan PrepareSingleOutput(
        ShowDocument source,
        NativeFramePlan frame,
        JsonElement originalOutSlide,
        string originalOutputId,
        FreeShowRevealStyleContext initialStyle,
        int activeNormalOutputCount,
        int maximumStates = 64)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(initialStyle);

        if (activeNormalOutputCount != 1)
            return Refuse("Prewarm sequencing needs an authoritative multi-output state timeline.");
        if (maximumStates < 1 || maximumStates > 384)
            return Refuse("Reveal prewarm state limit must be between 1 and 384.");
        if (ShowDocument.String(originalOutSlide, "id") != source.ShowId ||
            ShowDocument.String(originalOutSlide, "layout") != source.ActiveLayoutId)
            return Refuse("Original Show or layout does not match the prepared native source.");

        var initialIndex = ShowDocument.Property(originalOutSlide, "index");
        if (initialIndex.ValueKind != JsonValueKind.Number ||
            !initialIndex.TryGetInt32(out int index) ||
            index != frame.Source.LayoutIndex)
            return Refuse("Original source slide index differs from prewarmed frame.");

        var states = new List<FreeShowRevealPrewarmState>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = originalOutSlide.Clone();
        string? stopReason = null;
        bool complete = false;

        for (int step = 0; step < maximumStates; step++)
        {
            var lineRaw = ShowDocument.Property(current, "line");
            int currentLine = lineRaw.ValueKind == JsonValueKind.Number &&
                              lineRaw.TryGetInt32(out int n) && n >= 0 ? n : 0;
            var style = initialStyle with
            {
                HighestActiveLinePosition = currentLine
            };

            // The renderer will never show a state that its source projection
            // could not reproduce. Check before queueing any prewarm item.
            var prepared = FreeShowRevealRenderProjection.Prepare(frame, current, style);
            if (!prepared.IsSupported)
            {
                stopReason = "Original reveal contains unported renderer features: " +
                             string.Join("; ", prepared.Unported);
                break;
            }

            string fingerprint = current.GetRawText() + "|" + currentLine;
            if (!seen.Add(fingerprint))
            {
                stopReason = "Original reveal navigation repeated a previous state.";
                break;
            }

            states.Add(new(current.Clone(), style, frame.Source.LayoutIndex));
            var next = FreeShowSlideAdvancePlanner.Prepare(
                source, current, originalOutputId,
                FreeShowSlideAdvanceDirection.Next,
                initialStyle.FewestActiveStyleLines);

            if (next.Kind != FreeShowSlideAdvanceKind.RevealWithinSlide)
            {
                // Includes a normal slide change or a show/project boundary;
                // no other source slide is included in the current cache.
                complete = next.Kind is
                    (FreeShowSlideAdvanceKind.ChangeSlide or
                     FreeShowSlideAdvanceKind.ProjectBoundary);
                if (!complete) stopReason = next.Reason;
                break;
            }

            if (!next.HasTarget || next.LayoutIndex != index ||
                next.NextOutSlide.ValueKind != JsonValueKind.Object)
            {
                stopReason = "Unexpected source reveal transition data.";
                break;
            }
            current = next.NextOutSlide.Clone();
        }

        if (!complete && stopReason == null)
            stopReason = "Reveal sequence exceeded bounded prewarm limit.";

        return new(states.AsReadOnly(), complete, stopReason);
    }

    private static FreeShowRevealPrewarmPlan Refuse(string reason) =>
        new([], false, reason);
}

public sealed record FreeShowRevealPrewarmState(
    JsonElement OutSlide,
    FreeShowRevealStyleContext Style,
    int OriginalLayoutIndex);

public sealed record FreeShowRevealPrewarmPlan(
    IReadOnlyList<FreeShowRevealPrewarmState> States,
    bool CompletedCurrentSlide,
    string? Omission)
{
    public bool CanPrewarmCompleteSlide =>
        CompletedCurrentSlide && Omission == null && States.Count > 0;
}
