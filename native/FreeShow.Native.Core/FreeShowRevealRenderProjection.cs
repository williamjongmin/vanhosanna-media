using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Non-mutating subset of original output.ts getOutputLines() plus
/// SlideContent.svelte/TextboxLines.svelte visibility rules.
/// This is a *render preparation* calculation, not a replacement for
/// OutputHelper's source effects or the original Svelte compositor.
/// </summary>
public static class FreeShowRevealRenderProjection
{
    public static FreeShowRevealFrame Prepare(
        NativeFramePlan source, JsonElement outSlide,
        FreeShowRevealStyleContext style)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(style);

        if (style.StyleLines < 0 || style.FewestActiveStyleLines < 0 ||
            style.HighestActiveLinePosition < 0)
            return Unsupported(source, "FreeShow output line settings must be nonnegative.");
        if (outSlide.ValueKind != JsonValueKind.Object)
            return Unsupported(source, "Original OutSlide is missing.");
        var rawIndex = ShowDocument.Property(outSlide, "index");
        if (rawIndex.ValueKind != JsonValueKind.Number ||
            !rawIndex.TryGetInt32(out int index) || index != source.Source.LayoutIndex)
            return Unsupported(source, "Original OutSlide layout index does not match prepared slide.");
        if (style.ShowAsFaded)
            return Unsupported(source, "Original output style.showAsFaded requires alpha compositor.");
        if (style.RequiresVirtualBreaks)
            return Unsupported(source, "FreeShow virtual-break layout is not ported.");

        var sourceItems = source.TextItems;
        int maxLines = sourceItems.Count == 0 ? 0 :
            sourceItems.Max(x => x.Source.Lines.Count(l => HasSourceText(l)));
        int? line = ReadNonNegativeInt(outSlide, "line");
        int? reveal = ReadNonNegativeInt(outSlide, "revealCount");
        if (line is null || reveal is null)
            return Unsupported(source, "Original line/revealCount must be known nonnegative integers.");

        bool hasClick = sourceItems.Any(x => ShowDocument.IsTrue(x.Source.Raw, "clickReveal"));
        bool hasLineReveal = sourceItems.Any(x => ShowDocument.IsTrue(x.Source.Raw, "lineReveal"));
        bool clickRevealed = !hasClick || ShowDocument.IsTrue(outSlide, "itemClickReveal");

        // Match getOutputLines(): the original output uses the current active
        // style's own lines count, while the ending condition uses the
        // minimum and highest position from ALL active output styles.
        int? start = null, end = null;
        int? revealStart = null, revealEnd = null;
        if (maxLines > 0 && style.StyleLines > 0)
        {
            double progress = ((double)line.Value + 1) / maxLines;
            if (line.Value + style.FewestActiveStyleLines > maxLines)
                progress = 1;
            int lineIndex = (int)Math.Ceiling(maxLines * progress) - 1;
            int first = style.StyleLines * (int)Math.Floor((double)lineIndex / style.StyleLines);
            bool ending = style.HighestActiveLinePosition +
                          style.FewestActiveStyleLines >= maxLines;
            int overflow = maxLines % style.StyleLines;
            if (ending && overflow > 0)
                first = maxLines - overflow;
            start = Math.Max(0, first);
            end = start + style.StyleLines;
        }
        if (hasLineReveal)
        {
            revealStart = style.StyleLines > 0
                ? Math.Max(0, reveal.Value - style.StyleLines)
                : 0;
            revealEnd = reveal.Value;
        }

        var selected = new List<NativeItemPlan>(sourceItems.Count);
        foreach (var item in sourceItems)
        {
            bool itemClick = ShowDocument.IsTrue(item.Source.Raw, "clickReveal");
            if (itemClick && !clickRevealed) continue;

            bool itemLineReveal = ShowDocument.IsTrue(item.Source.Raw, "lineReveal");
            int? itemStart = itemLineReveal ? revealStart : start;
            int? itemEnd = itemLineReveal ? revealEnd : end;

            // TextboxLines.svelte filters out textless entries only when a
            // start and end are both present. Full FreeShow chord/virtual
            // break features remain unsupported by NativeRenderPlanner.
            var lines = item.Source.Lines;
            if (itemStart.HasValue && itemEnd.HasValue)
                lines = lines.Where(HasSourceText).ToArray();

            // Original TextboxLines.svelte falls back to the final
            // possible window if this particular item is shorter than the
            // output-wide maximum text item. It does not simply go blank.
            if (itemStart.HasValue && itemEnd.HasValue &&
                lines.Count > 0 && itemStart.Value >= lines.Count)
            {
                int windowSize = itemEnd.Value - itemStart.Value;
                if (windowSize > 0)
                {
                    int last = lines.Count - 1;
                    int first = last - last % windowSize;
                    itemStart = first;
                    itemEnd = first + windowSize;
                }
            }

            IReadOnlyList<SlideLine> visible = itemStart.HasValue && itemEnd.HasValue
                ? lines.Where((_, i) => i >= itemStart.Value && i < itemEnd.Value).ToArray()
                : lines;

            // Keep all run CSS and original raw data intact. Only the
            // visible line list changes in an output-only record clone.
            if (visible.Count > 0)
                selected.Add(item with
                {
                    Source = item.Source with { Lines = visible }
                });
        }

        var omissions = source.Unsupported
            .Where(x => x != "Text item reveal and click reveal not implemented.")
            .ToList();
        if (sourceItems.Any(x => ShowDocument.Property(x.Source.Raw, "actions").ValueKind ==
                JsonValueKind.Object))
            omissions.Add("Original Textbox item actions/displayDuration not ported.");
        if (style.StyleLines > 0 && style.FewestActiveStyleLines == 0)
            omissions.Add("Original minimum active style line count cannot be established.");

        return new(
            source with { TextItems = selected.AsReadOnly(),
                          Unsupported = omissions.Distinct().ToArray() },
            new(start, end, revealStart, revealEnd, clickRevealed),
            omissions.Distinct().ToArray());
    }

    private static bool HasSourceText(SlideLine line) =>
        line.Runs.Any(run => ShowDocument.Property(run.Raw, "value").ValueKind !=
            JsonValueKind.Undefined);

    private static int? ReadNonNegativeInt(JsonElement outSlide, string name)
    {
        var value = ShowDocument.Property(outSlide, name);
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return 0;
        return value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out int number) && number >= 0 ? number : null;
    }

    private static FreeShowRevealFrame Unsupported(NativeFramePlan source, string warning) =>
        new(source,
            new(null, null, null, null, false), [warning]);
}

public sealed record FreeShowRevealStyleContext(
    int StyleLines,
    int FewestActiveStyleLines,
    int HighestActiveLinePosition,
    bool ShowAsFaded = false,
    bool RequiresVirtualBreaks = false);

public sealed record FreeShowVisibleLineWindow(
    int? Start, int? End, int? RevealStart, int? RevealEnd, bool ClickRevealed);

public sealed record FreeShowRevealFrame(
    NativeFramePlan Visible,
    FreeShowVisibleLineWindow Window,
    IReadOnlyList<string> Unported)
{
    public bool IsSupported => Unported.Count == 0 &&
        Visible.CanRenderWithoutKnownOmissions;
}
