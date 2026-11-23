using System.Text.Json;
using System.Text.Json.Nodes;

namespace FreeShow.Native.Core;

/// <summary>
/// Non-destructive, output-only subset of FreeShow's mergeWithTemplate() in
/// src/frontend/components/helpers/output.ts. Input Show and template JSON remain
/// untouched. Only the ordinary non-dynamic text-item path is enabled.
/// Unsupported cases explicitly return the original content plus warnings.
/// </summary>
public static class FreeShowTemplateMerger
{
    private static readonly string[] ExtraStyleKeys =
    [
        "chords", "actions", "specialStyle", "scrolling", "bindings", "conditions",
        "clickReveal", "lineReveal", "fit", "filter", "flipped", "flippedY", "blend"
    ];

    public static TemplateMergeResult Apply(SlideFrame frame, TemplateSelection selected)
    {
        var templateItems = selected.Items;
        if (!selected.TemplateExists || templateItems.ValueKind != JsonValueKind.Array
            || templateItems.GetArrayLength() == 0)
            return new(frame.Items, Array.Empty<string>(), false);

        var warnings = new List<string>();
        var templateSettings = selected.Settings;
        string mode = ShowDocument.String(templateSettings, "mode");
        if (mode is "scripture" or "text")
            warnings.Add("Full original template mode not yet ported: " + mode);
        if (ShowDocument.Property(templateSettings, "styleOverrides").ValueKind == JsonValueKind.Array)
            warnings.Add("Template word-style overrides are not yet ported.");

        var rawSlideItems = ShowDocument.Property(frame.RawSlide, "items");
        if (rawSlideItems.ValueKind != JsonValueKind.Array)
            return new(frame.Items, ["Source slide items are not a JSON array."], false);

        var source = rawSlideItems.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => JsonNode.Parse(x.GetRawText()) as JsonObject)
            .Where(x => x != null).Select(x => x!).ToList();
        var templates = templateItems.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => JsonNode.Parse(x.GetRawText()) as JsonObject)
            .Where(x => x != null).Select(x => x!).ToList();

        // Deliberately do not output an incomplete/misleading "styled" frame.
        // Dynamic scripture, bracket variables and non-text templates have
        // additional FreeShow runtime dependencies, not a simple style swap.
        if (source.Concat(templates).Any(x => GetType(x) != "text"))
            warnings.Add("Non-text template/source items require full original merge logic.");
        if (source.Concat(templates).Any(HasDynamicValue))
            warnings.Add("Dynamic template tokens require FreeShow variable/scripture resolution.");
        if (templates.Any(x => GetString(x, "textFit") is not ("" or "none")))
            warnings.Add("Template text auto-sizing requires native text measurement.");
        if (templates.Any(x => GetBool(x, "lineReveal") || GetBool(x, "clickReveal")))
            warnings.Add("Template line/click reveal is not yet implemented.");
        if (templates.Any(x => ExtraStyleKeys.Any(k =>
            k is not ("bindings" or "lineReveal" or "clickReveal") && x[k] != null)))
            warnings.Add("Template defines interactive or animated item settings not yet ported.");
        if (source.Any(x => x["fromTemplate"] != null && GetBool(x,"fromTemplate")))
            warnings.Add("Source contains prior generated template items.");
        if (warnings.Count > 0)
            return new(frame.Items, warnings.AsReadOnly(), false);

        // The original implementation consumes text template items in source
        // order. Excess text template boxes are dropped for normal lyrics.
        var sourceItems = source;
        var textTemplates = templates.Where(x => GetType(x) == "text").ToList();
        if (textTemplates.Count == 0)
            return new(frame.Items, ["No ordinary text template box found."], false);
        var count = Math.Min(sourceItems.Count, textTemplates.Count);
        var merged = new List<JsonElement>(sourceItems.Count);
        for (int i = 0; i < sourceItems.Count; i++)
        {
            var current = sourceItems[i];
            if (i < count)
                ApplySingleTextTemplate(current, textTemplates[i], mode);
            merged.Add(ToElement(current));
        }
        var result = SlideProjection.ReadItems(ToElement(new JsonArray(
            merged.Select(m => JsonNode.Parse(m.GetRawText())).ToArray())));
        return new(result, Array.Empty<string>(), count > 0);
    }

    private static void ApplySingleTextTemplate(JsonObject item, JsonObject template, string mode)
    {
        // Original mergeWithTemplate overwrites item style and align, while
        // keeping the existing lyric text values and original row structure.
        item["style"] = GetString(template, "style");
        item["align"] = GetString(template, "align");
        if (mode == "item") return;

        item.Remove("autoFontSize");
        CopyIfTruthy(template, item, "textFit");
        if (!Truthy(template["textFit"])) item.Remove("textFit");
        CopyIfTruthy(template, item, "list");
        if (!Truthy(template["list"])) item.Remove("list");

        foreach (var key in ExtraStyleKeys)
        {
            // setTemplateStyle() calls mergeWithTemplate with isOutputStyle=true
            // and preserves original output bindings unless template specifies some.
            if (key == "bindings" &&
                (template["bindings"] is not JsonArray bindings || bindings.Count == 0))
                continue;

            item.Remove(key);
            CopyIfTruthy(template, item, key);
        }

        if (item["lines"] is not JsonArray lines) return;
        if (template["lines"] is not JsonArray templLines || templLines.Count == 0) return;
        var distinct = InspectOriginalFormatting(lines);

        for (int li = 0; li < lines.Count; li++)
        {
            if (lines[li] is not JsonObject line) continue;
            JsonObject? templLine = li < templLines.Count ? templLines[li] as JsonObject
                : templLines[0] as JsonObject;
            if (templLine == null) continue;
            line["align"] = GetString(templLine, "align");
            if (line["text"] is not JsonArray runs) continue;
            if (templLine["text"] is not JsonArray tRuns || tRuns.Count == 0) continue;

            for (int ri = 0; ri < runs.Count; ri++)
            {
                if (runs[ri] is not JsonObject run) continue;
                var templateRun = ri < tRuns.Count ? tRuns[ri] as JsonObject
                    : tRuns[0] as JsonObject;
                if (templateRun == null) continue;

                // Source customType=disableTemplate skips template text style.
                if (GetString(run, "customType").Contains("disableTemplate",
                    StringComparison.Ordinal))
                    continue;

                string templateStyle = GetString(templateRun, "style");
                string originalStyle = GetString(run, "style");
                run["style"] = MergeMixedFormatting(templateStyle, originalStyle, distinct);
                // Crucial: do not replace the original lyric text string!
            }
        }
    }

    private static OriginalFormatting InspectOriginalFormatting(JsonArray lines)
    {
        var colors = new HashSet<string>();
        var bold = new HashSet<bool>();
        var italic = new HashSet<bool>();
        var underline = new HashSet<bool>();
        var strike = new HashSet<bool>();
        foreach (var line in lines.OfType<JsonObject>())
        {
            if (line["text"] is not JsonArray runs) continue;
            foreach (var run in runs.OfType<JsonObject>())
            {
                if (GetString(run, "customType").Length != 0) continue;
                var css = new CssDeclarations(GetString(run, "style"));
                colors.Add(css.Get("color") is { Length: > 0 } color ? color : "#FFFFFF");
                bold.Add(IsBold(css.Get("font-weight")));
                italic.Add(css.Get("font-style").Equals("italic", StringComparison.OrdinalIgnoreCase));
                var decorations = css.Get("text-decoration").Split(' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                underline.Add(decorations.Contains("underline", StringComparer.OrdinalIgnoreCase));
                strike.Add(decorations.Contains("line-through", StringComparer.OrdinalIgnoreCase));
            }
        }
        return new(colors.Count > 1, bold.Count > 1, italic.Count > 1,
            underline.Count > 1, strike.Count > 1);
    }

    private static string MergeMixedFormatting(string templateStyle, string sourceStyle,
        OriginalFormatting distinct)
    {
        var css = new CssDeclarations(sourceStyle);
        var style = templateStyle;
        if (distinct.Colors)
            style += "color: " + (css.Get("color") is { Length: > 0 } color ? color : "#FFFFFF") + ";";
        if (distinct.Bold)
            style += "font-weight: " + (IsBold(css.Get("font-weight"))
                ? (css.Get("font-weight") is { Length: > 0 } bold ? bold : "bold") : "normal") + ";";
        if (distinct.Italic)
            style += "font-style: " + (css.Get("font-style").Equals("italic",
                StringComparison.OrdinalIgnoreCase) ? "italic" : "normal") + ";";
        if (distinct.Underline || distinct.Strike)
        {
            var decorations = new HashSet<string>(
                new CssDeclarations(templateStyle).Get("text-decoration")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
            var sourceDecorations = css.Get("text-decoration").Split(' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Preserve("underline", distinct.Underline);
            Preserve("line-through", distinct.Strike);
            style += "text-decoration: " +
                (decorations.Count == 0 ? "none" : string.Join(" ", decorations)) + ";";
            void Preserve(string name, bool enabled)
            {
                if (!enabled) return;
                if (sourceDecorations.Contains(name, StringComparer.OrdinalIgnoreCase))
                    decorations.Add(name);
                else decorations.Remove(name);
            }
        }
        return style;
    }

    private static void CopyIfTruthy(JsonObject src, JsonObject target, string key)
    {
        if (Truthy(src[key])) target[key] = src[key]!.DeepClone();
    }
    private static bool Truthy(JsonNode? node)
    {
        if (node is null) return false;
        if (node is JsonObject or JsonArray) return true;
        if (node is not JsonValue scalar) return false;
        if (scalar.TryGetValue<bool>(out var b)) return b;
        if (scalar.TryGetValue<string>(out var s)) return s.Length > 0;
        if (scalar.TryGetValue<double>(out var n)) return n != 0;
        return true;
    }
    private static bool GetBool(JsonObject obj, string key) =>
        obj[key] is JsonValue j && j.TryGetValue<bool>(out var b) && b;
    private static string GetString(JsonObject obj, string key) =>
        obj[key] is JsonValue j && j.TryGetValue<string>(out var s) ? s : "";
    private static string GetType(JsonObject item) =>
        GetString(item, "type") is { Length: > 0 } type ? type : "text";
    private static bool HasDynamicValue(JsonObject item) =>
        item["lines"] is JsonArray lines &&
        lines.OfType<JsonObject>().Any(line => line["text"] is JsonArray text &&
            text.OfType<JsonObject>().Any(run => GetString(run,"value").Contains('{')));

    private static bool IsBold(string weight)
    {
        if (weight.Equals("bold",StringComparison.OrdinalIgnoreCase)) return true;
        return double.TryParse(weight, out var number) && number >= 600;
    }
    private static JsonElement ToElement(JsonNode node) =>
        JsonSerializer.SerializeToElement(node);

    private sealed record OriginalFormatting(bool Colors,bool Bold,bool Italic,
        bool Underline,bool Strike);
}

public sealed record TemplateMergeResult(
    IReadOnlyList<SlideItem> Items, IReadOnlyList<string> Unsupported,
    bool WasApplied)
{
    public bool IsExactSupportedSubset => Unsupported.Count == 0;
}
