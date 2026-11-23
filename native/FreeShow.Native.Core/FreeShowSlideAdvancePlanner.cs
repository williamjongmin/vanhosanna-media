using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Read-only, source-ordered subset of OutputHelper.ts getNextSlide(),
/// getPreviousSlide(), playFirstSlide() and playLastSlide(). The resulting
/// OutSlide is original-shaped JSON; neither outputs nor church files change.
/// A calculated next slide is NOT permission to send it to an output:
/// checkActionTrigger/updateOut, media/timers/reveal compositing are separate.
/// </summary>
public static class FreeShowSlideAdvancePlanner
{
    public static FreeShowSlideAdvanceResult Prepare(
        ShowDocument source, JsonElement currentOutSlide,
        string originalOutputId, FreeShowSlideAdvanceDirection direction,
        int? fewestOutputStyleLines,
        Func<string, string, bool?>? bindingMatchesOutput = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalOutputId);

        string sourceId = ShowDocument.String(currentOutSlide, "id");
        if (sourceId.Length > 0 && sourceId != source.ShowId)
            return Refuse("Output is displaying a different FreeShow Show ID.");
        string type = ShowDocument.String(currentOutSlide, "type");
        if (type.Length > 0 && type != "show")
            return Refuse("PDF/PPT/media navigation requires the original media controller.");

        string layoutId = ShowDocument.String(currentOutSlide, "layout");
        if (layoutId.Length == 0) layoutId = source.ActiveLayoutId;
        if (layoutId.Length == 0 ||
            source.GetLayout(layoutId).ValueKind != JsonValueKind.Object)
            return Refuse("Original FreeShow active/layout override is missing.");

        if (fewestOutputStyleLines is < 0)
            return Refuse("Invalid original getFewestOutputLines style count.");

        IReadOnlyList<LayoutEntry> entries;
        try { entries = source.ReadLayoutEntries(layoutId); }
        catch (InvalidDataException error) { return Refuse(error.Message); }

        // ref() uses a zero-based expanded layout array. Earlier native
        // ShowDocument deliberately preserves gaps for missing child slides.
        // Never silently translate those gaps to a different live index.
        if (entries.Where((entry, pos) => entry.Index != pos).Any())
            return Refuse("Missing source slide produces an ambiguous original ref index.");

        int count = entries.Count;
        if (count == 0)
            return Boundary("Original FreeShow layout has no slides.");

        int? sourceIndex = ReadInt(ShowDocument.Property(currentOutSlide, "index"));
        int index = direction switch
        {
            FreeShowSlideAdvanceDirection.First => -1,
            FreeShowSlideAdvanceDirection.Last => count,
            FreeShowSlideAdvanceDirection.Next => sourceIndex ?? -1,
            FreeShowSlideAdvanceDirection.Previous => sourceIndex ?? count,
            _ => -1
        };

        if (index < -1 || index > count)
            return Refuse("Original output slide index is outside the source layout.");

        bool next = direction is FreeShowSlideAdvanceDirection.Next
            or FreeShowSlideAdvanceDirection.First;

        var current = index >= 0 && index < count ? entries[index] : null;
        var currentPosition = new FreeShowRevealPosition(
            ReadInt(ShowDocument.Property(currentOutSlide, "line")) ?? 0,
            ShowDocument.IsTrue(currentOutSlide, "itemClickReveal"),
            ReadInt(ShowDocument.Property(currentOutSlide, "revealCount")) ?? 0);

        if (direction is FreeShowSlideAdvanceDirection.Next or
            FreeShowSlideAdvanceDirection.Previous && current != null)
        {
            var reveal = AnalyzeReveal(current, currentPosition, fewestOutputStyleLines ?? 0);
            if (next && (!reveal.StyleEnded || !reveal.ClickEnded || !reveal.LinesEnded))
            {
                var updated = new Dictionary<string, object?>
                {
                    ["id"] = source.ShowId,
                    ["layout"] = layoutId,
                    ["index"] = index
                };
                if (reveal.StyleStep != null)
                    updated["line"] = currentPosition.Line + reveal.StyleStep;
                if (reveal.HasClickReveal)
                    updated["itemClickReveal"] = true;
                if (reveal.ClickEnded && reveal.HasLineReveal)
                    updated["revealCount"] = currentPosition.RevealCount + 1;
                return Ready(source, current, layoutId, updated,
                    FreeShowSlideAdvanceKind.RevealWithinSlide);
            }

            if (!next && (!reveal.StyleAtBeginning ||
                          currentPosition.ItemClickReveal || !reveal.LinesAtBeginning))
            {
                var updated = new Dictionary<string, object?>
                {
                    ["id"] = source.ShowId,
                    ["layout"] = layoutId,
                    ["index"] = index
                };
                if (reveal.StyleStep != null)
                    updated["line"] = currentPosition.Line - reveal.StyleStep;
                if (reveal.HasLineReveal)
                    updated["revealCount"] = currentPosition.RevealCount - 1;
                if (currentPosition.RevealCount > 0 && reveal.ClickEnded)
                    updated["itemClickReveal"] = true;
                return Ready(source, current, layoutId, updated,
                    FreeShowSlideAdvanceKind.RevealWithinSlide);
            }
        }

        // Source next-slide behavior: end:true loops the show to index 0.
        if (next && current != null && ShowDocument.IsTrue(
                ShowDocument.Property(current.RawEntry, "data"), "end"))
            index = -1;

        index += next ? 1 : -1;
        while (index >= 0 && index < count)
        {
            var candidate = entries[index];
            if (IsDisabled(candidate))
            {
                index += next ? 1 : -1;
                continue;
            }

            var bindings = ShowDocument.Property(
                ShowDocument.Property(candidate.RawEntry, "data"), "bindings");
            if (bindings.ValueKind == JsonValueKind.Array &&
                bindings.GetArrayLength() > 0)
            {
                var decision = BindingDecision(bindings, originalOutputId,
                    bindingMatchesOutput);
                if (decision == null)
                    return Refuse("Original output binding cannot be resolved authoritatively.");
                if (decision == false)
                {
                    index += next ? 1 : -1;
                    continue;
                }
            }

            var updated = new Dictionary<string, object?>
            {
                ["id"] = source.ShowId,
                ["layout"] = layoutId,
                ["index"] = index
            };

            if (!next)
            {
                var last = AnalyzeReveal(candidate, new(0, false, 0),
                    fewestOutputStyleLines ?? 0);
                if (last.LastLine > 0)
                    updated["line"] = last.LastLine;
                if (last.HasClickReveal)
                    updated["itemClickReveal"] = true;
                if (last.HasLineReveal)
                    updated["revealCount"] = last.MaxReveal;
            }
            return Ready(source, candidate, layoutId, updated,
                FreeShowSlideAdvanceKind.ChangeSlide);
        }

        return Boundary("At the end of the source Show; original OutputHelper may advance the project.");
    }

    private static bool IsDisabled(LayoutEntry entry) =>
        entry.EffectiveDisabled ||
        ShowDocument.IsTrue(ShowDocument.Property(entry.RawEntry, "data"), "disabled");

    private static bool? BindingDecision(JsonElement raw, string outputId,
        Func<string, string, bool?>? matchesOutput)
    {
        bool unknown = false;
        foreach (var binding in raw.EnumerateArray())
        {
            if (binding.ValueKind != JsonValueKind.String) return null;
            var id = binding.GetString() ?? "";
            if (id == outputId) return true;
            var match = matchesOutput?.Invoke(id, outputId);
            if (match == true) return true;
            if (match == null) unknown = true;
        }
        return unknown ? null : false;
    }

    private static FreeShowRevealAnalysis AnalyzeReveal(
        LayoutEntry entry, FreeShowRevealPosition current, int styleLines)
    {
        int maxLines = 0, maxReveal = 0;
        bool click = false, reveal = false;
        if (entry.Items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in entry.Items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                int count = CountSourceLines(ShowDocument.Property(item, "lines"));
                maxLines = Math.Max(maxLines, count);
                if (ShowDocument.IsTrue(item, "clickReveal")) click = true;
                if (ShowDocument.IsTrue(item, "lineReveal"))
                {
                    reveal = true;
                    maxReveal = Math.Max(maxReveal, count);
                }
            }
        }

        bool styleEnded = styleLines <= 0 || current.Line + styleLines >= maxLines;
        bool styleBeginning = styleLines <= 0 || current.Line < 1;
        int lastLine = styleLines <= 0 ? 0 :
            Math.Max(0, styleLines * (int)Math.Ceiling((double)maxLines / styleLines)
                - styleLines);
        return new(
            styleEnded, styleBeginning,
            !click || current.ItemClickReveal,
            !reveal || current.RevealCount >= maxReveal,
            !reveal || current.RevealCount == 0,
            click, reveal, styleLines <= 0 ? null : styleLines,
            lastLine, maxReveal);
    }

    private static int CountSourceLines(JsonElement rawLines)
    {
        if (rawLines.ValueKind != JsonValueKind.Array) return 0;
        int count = 0;
        foreach (var line in rawLines.EnumerateArray())
        {
            var text = ShowDocument.Property(line, "text");
            if (text.ValueKind == JsonValueKind.Array &&
                text.EnumerateArray().Any(run => ShowDocument.Property(run, "value")
                    .ValueKind != JsonValueKind.Undefined))
                count++;
        }
        return count;
    }

    private static FreeShowSlideAdvanceResult Ready(
        ShowDocument source, LayoutEntry entry, string layoutId,
        Dictionary<string, object?> shaped, FreeShowSlideAdvanceKind kind)
    {
        var data = ShowDocument.Property(entry.RawEntry, "data");
        bool action = HasOwnData(data, "actions") ||
                      HasOwnData(data, "background") ||
                      HasOwnData(data, "nextTimer");
        bool unsupported = entry.Items.ValueKind == JsonValueKind.Array &&
            entry.Items.EnumerateArray().Any(x =>
                ShowDocument.IsTrue(x, "clickReveal") ||
                ShowDocument.IsTrue(x, "lineReveal"));
        // The native renderer can display prepared text, but has not ported
        // source per-line/multi-item reveal or full original slide actions.
        return new(kind, source.ShowId, layoutId, entry.Index,
            JsonSerializer.SerializeToElement(shaped), null,
            action || unsupported || kind == FreeShowSlideAdvanceKind.RevealWithinSlide);
    }

    private static bool HasOwnData(JsonElement source, string name)
    {
        var item = ShowDocument.Property(source, name);
        return item.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
    }

    private static int? ReadInt(JsonElement data) =>
        data.ValueKind == JsonValueKind.Number && data.TryGetInt32(out int number)
            ? number : null;

    private static FreeShowSlideAdvanceResult Refuse(string message) =>
        new(FreeShowSlideAdvanceKind.Refused, "", "", null, default, message, true);

    private static FreeShowSlideAdvanceResult Boundary(string message) =>
        new(FreeShowSlideAdvanceKind.ProjectBoundary, "", "", null, default, message, true);
}

public enum FreeShowSlideAdvanceDirection { Next, Previous, First, Last }
public enum FreeShowSlideAdvanceKind
{
    ChangeSlide, RevealWithinSlide, ProjectBoundary, Refused
}

public sealed record FreeShowSlideAdvanceResult(
    FreeShowSlideAdvanceKind Kind,
    string ShowId, string LayoutId, int? LayoutIndex,
    JsonElement NextOutSlide, string? Reason, bool RequiresUnportedSourceEffects)
{
    public bool HasTarget =>
        Kind is FreeShowSlideAdvanceKind.ChangeSlide or
            FreeShowSlideAdvanceKind.RevealWithinSlide;
    public bool CanDispatchLive => false; // See OutputHelper.ts source action lifecycle.
}

internal sealed record FreeShowRevealPosition(
    int Line, bool ItemClickReveal, int RevealCount);
internal sealed record FreeShowRevealAnalysis(
    bool StyleEnded, bool StyleAtBeginning, bool ClickEnded,
    bool LinesEnded, bool LinesAtBeginning,
    bool HasClickReveal, bool HasLineReveal, int? StyleStep,
    int LastLine, int MaxReveal);
