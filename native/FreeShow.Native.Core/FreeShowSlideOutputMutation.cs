using System.Text.Json;
using System.Text.Json.Nodes;

namespace FreeShow.Native.Core;

/// <summary>
/// The data assignment inside output.ts setOutput("slide", data, false, id).
/// This is only the assignment step: the caller still owes the original
/// break-recording, attribution, audio, category, foreground and event effects.
/// Neither the source JSON nor a coordinator's acknowledged state is changed.
/// </summary>
public static class FreeShowSlideOutputMutation
{
    public static JsonElement Prepare(
        JsonElement previousOutData, JsonElement requestedSlide,
        JsonElement activeProjectItem = default)
    {
        if (previousOutData.ValueKind != JsonValueKind.Object ||
            requestedSlide.ValueKind != JsonValueKind.Object ||
            ShowDocument.String(requestedSlide, "id").Length == 0)
            throw new ArgumentException("Original OutData and named OutSlide are required.");

        var output = (JsonObject)JsonNode.Parse(previousOutData.GetRawText())!;
        var slide = (JsonObject)JsonNode.Parse(requestedSlide.GetRawText())!;

        // setOutput carries the selected project occurrence, even when the
        // same Show occurs more than once in a project. Never infer it from
        // the Show ID alone or retain the previous slide's occurrence.
        if (ShowDocument.String(activeProjectItem, "id") ==
            ShowDocument.String(requestedSlide, "id"))
        {
            var index = ShowDocument.Property(activeProjectItem, "index");
            if (index.ValueKind != JsonValueKind.Undefined)
                slide["projectIndex"] = JsonNode.Parse(index.GetRawText());
        }

        // Binding selection is done before assignment in the original.
        slide.Remove("bindings");

        var previous = ShowDocument.Property(previousOutData, "slide");
        bool temporary = ShowDocument.String(requestedSlide, "id") is "temp" or "tempText";
        bool sameSlide = previous.ValueKind == JsonValueKind.Object && !temporary &&
            new[] { "id", "layout", "index", "page", "type" }.All(
                key => SameProperty(previous, requestedSlide, key));

        // Same index and line can still be a different click/line reveal.
        // FreeShow bumps refresh for those requests too. Missing and null
        // remain distinct, like the source's === comparison.
        if (sameSlide && SameProperty(previous, requestedSlide, "line"))
        {
            var refresh = ShowDocument.Property(previousOutData, "refresh");
            double value = 0;
            if (refresh.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) &&
                (refresh.ValueKind != JsonValueKind.Number ||
                 !refresh.TryGetDouble(out value) || !double.IsFinite(value)))
                throw new NotSupportedException("Original OutData.refresh must be a finite number.");
            output["refresh"] = value + 1;
        }

        // Replacement, not merging: omitted reveal flags reset on a new
        // cue. All unrelated/unknown original OutData fields are retained.
        output["slide"] = slide;
        return JsonSerializer.SerializeToElement(output);
    }

    internal static bool SameProperty(JsonElement first, JsonElement second, string key)
    {
        var a = ShowDocument.Property(first, key);
        var b = ShowDocument.Property(second, key);
        if (a.ValueKind == JsonValueKind.Undefined || b.ValueKind == JsonValueKind.Undefined)
            return a.ValueKind == b.ValueKind;
        return JsonElement.DeepEquals(a, b);
    }
}

/// <summary>
/// Checks the entire requested slide shape, including the absence of reveal
/// fields. Source setOutput may add projectIndex and attributionString and
/// removes bindings; it does not retain stale reveal/page/temp-item fields.
/// Full OutData effects outside slide are owned by the source backend.
/// </summary>
public static class FreeShowSlideAcknowledgement
{
    public static bool Matches(
        FreeShowSlideTransitionTarget target, JsonElement originalOutData)
    {
        ArgumentNullException.ThrowIfNull(target);
        var expected = target.NextOutSlide;
        var actual = ShowDocument.Property(originalOutData, "slide");
        if (originalOutData.ValueKind != JsonValueKind.Object ||
            expected.ValueKind != JsonValueKind.Object ||
            actual.ValueKind != JsonValueKind.Object ||
            ShowDocument.String(expected, "id") != target.ShowId ||
            ShowDocument.String(expected, "layout") != target.LayoutId)
            return false;

        var index = ShowDocument.Property(expected, "index");
        if (index.ValueKind != JsonValueKind.Number ||
            !index.TryGetInt32(out int value) || value != target.LayoutIndex)
            return false;

        foreach (var property in expected.EnumerateObject())
        {
            if (property.Name == "bindings" || IsSourceMetadata(property.Name)) continue;
            if (!FreeShowSlideOutputMutation.SameProperty(expected, actual, property.Name))
                return false;
        }
        foreach (var property in actual.EnumerateObject())
        {
            if (IsSourceMetadata(property.Name)) continue;
            if (property.Name == "bindings" ||
                !expected.TryGetProperty(property.Name, out _))
                return false;
        }
        return true;
    }

    private static bool IsSourceMetadata(string key) =>
        key is "projectIndex" or "attributionString";
}
