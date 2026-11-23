using System.Globalization;
using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Input context for OutputHelper.ts: its style line limit is the minimum
/// nonzero "style.lines" among active outputs that render the slide layer.
/// Output-bound refs can be original IDs, registered synced IDs or names.
/// This helper NEVER invents a missing style or hidden synced mapping.
/// </summary>
public sealed class FreeShowNavigationSourceContext
{
    private readonly FreeShowOutputConfiguration outputs;
    private readonly FreeShowVisualSettings? visuals;
    private readonly JsonElement syncedNames;

    public FreeShowNavigationSourceContext(
        FreeShowOutputConfiguration sourceOutputs,
        FreeShowVisualSettings? sourceVisuals,
        JsonElement originalSyncedOutputNames = default)
    {
        outputs = sourceOutputs ?? throw new ArgumentNullException(nameof(sourceOutputs));
        visuals = sourceVisuals;
        syncedNames = originalSyncedOutputNames.ValueKind == JsonValueKind.Object
            ? originalSyncedOutputNames.Clone() : default;
    }

    public FreeShowNavigationStyleLimits GetStyleLimits()
    {
        var active = outputs.GetActiveNormalOutputs();
        if (visuals == null || !visuals.HasSettingsFile)
            return new(null,
                "Original Config/settings_synced.json must be loaded before line-split navigation.");

        int minimum = 0;
        foreach (var output in active)
        {
            if (output.StyleId.Length == 0) continue;
            var style = visuals.GetStyle(output.StyleId);
            if (style.ValueKind != JsonValueKind.Object)
                return new(null, "Missing original FreeShow output style " + output.StyleId);
            var layers = ShowDocument.Property(style, "layers");
            if (layers.ValueKind == JsonValueKind.Array &&
                !layers.EnumerateArray().Any(x =>
                    x.ValueKind == JsonValueKind.String && x.GetString() == "slide"))
                continue;

            var raw = ShowDocument.Property(style, "lines");
            if (raw.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                continue;

            double parsed;
            if (raw.ValueKind == JsonValueKind.Number)
            {
                if (!raw.TryGetDouble(out parsed)) return new(null, "Invalid output style lines.");
            }
            else if (raw.ValueKind == JsonValueKind.String &&
                     double.TryParse(raw.GetString(), NumberStyles.Any,
                         CultureInfo.InvariantCulture, out var numeric))
                parsed = numeric;
            else
                return new(null, "Original output style lines cannot be read safely.");

            if (!double.IsFinite(parsed) || parsed < 0 || parsed > 100000 ||
                Math.Abs(parsed - Math.Round(parsed)) > 0.00001)
                return new(null, "Unsupported original output style lines setting.");
            int lines = (int)parsed;
            if (lines > 0 && (minimum == 0 || lines < minimum))
                minimum = lines;
        }
        return new(minimum, null);
    }

    /// <summary>
    /// resolveOutputId(binding) accepts a direct local output ID, or maps
    /// syncedOutputs[binding] to a normalized local output name. If the
    /// synced registry was not supplied and a binding is not locally known,
    /// returns null (unknown), never guesses whether to skip a slide.
    /// </summary>
    public bool? MatchesOriginalOutput(string bindingId, string currentOutputId)
    {
        var current = outputs.Find(currentOutputId);
        if (current == null) return null;
        if (bindingId == currentOutputId) return true;
        if (outputs.Find(bindingId) != null) return false;

        string value = bindingId;
        var registered = ShowDocument.Property(syncedNames, bindingId);
        if (registered.ValueKind == JsonValueKind.String)
            value = registered.GetString() ?? bindingId;
        else if (syncedNames.ValueKind != JsonValueKind.Object)
        {
            var localNameMatch = outputs.All.Any(x => string.Equals(
                x.Name.Trim(), bindingId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!localNameMatch) return null;
        }

        string normalized = value.Trim();
        return string.Equals(
            current.Name.Trim(), normalized, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record FreeShowNavigationStyleLimits(
    int? FewestStyleLines, string? Warning)
{
    public bool IsAuthoritative => Warning == null && FewestStyleLines.HasValue;
}
