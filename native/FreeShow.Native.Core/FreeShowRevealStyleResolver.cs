using System.Globalization;
using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Source Output.svelte getOutputLines(outSlide, currentStyle.lines)
/// and helpers/output.ts getHighestOutputLinePos(). A caller must provide
/// all original active output states, not just one selected song.
/// </summary>
public static class FreeShowRevealStyleResolver
{
    public static FreeShowRevealStyleResolution Resolve(
        string originalOutputId,
        FreeShowOutputConfiguration configuration,
        FreeShowVisualSettings visuals,
        IReadOnlyDictionary<string, FreeShowOutputState> outputStates,
        bool sourceHasUnportedVirtualBreaks = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalOutputId);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(visuals);
        ArgumentNullException.ThrowIfNull(outputStates);

        var snapshots = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var state in outputStates)
            snapshots[state.Key] = state.Value.Snapshot();
        return ResolveCaptured(originalOutputId, configuration, visuals, snapshots,
            sourceHasUnportedVirtualBreaks);
    }

    /// <summary>
    /// Compute source-style line state entirely from one already-captured
    /// OutData batch. Preview must not read a different slide revision
    /// separately from the text that it draws.
    /// </summary>
    public static FreeShowRevealStyleResolution ResolveCaptured(
        string originalOutputId,
        FreeShowOutputConfiguration configuration,
        FreeShowVisualSettings visuals,
        IReadOnlyDictionary<string, JsonElement> capturedOutData,
        bool sourceHasUnportedVirtualBreaks = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalOutputId);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(visuals);
        ArgumentNullException.ThrowIfNull(capturedOutData);

        var output = configuration.Find(originalOutputId);
        if (output == null || !output.Enabled || output.StageSourceId != null)
            return Refused("Original FreeShow normal output is absent or disabled.");
        if (!configuration.GetActiveNormalOutputs().Any(x => x.Id == originalOutputId))
            return Refused("Original FreeShow output is not active.");
        if (!visuals.HasSettingsFile)
            return Refused("Original FreeShow synced styles are not loaded.");

        var style = visuals.GetStyle(output.StyleId);
        if (style.ValueKind != JsonValueKind.Object)
            return Refused("Original FreeShow output style is not available.");

        var limits = new FreeShowNavigationSourceContext(
            configuration, visuals).GetStyleLimits();
        if (!limits.IsAuthoritative)
            return Refused(limits.Warning ?? "Minimum active output line count unknown.");

        if (!TryReadLines(style, out int styleLines))
            return Refused("Original output style.lines has unsupported number format.");

        int highest = 0;
        foreach (var active in configuration.GetActiveNormalOutputs())
        {
            if (!capturedOutData.TryGetValue(active.Id, out var data))
                return Refused("Missing captured OutData for " + active.Id);
            var slide = ShowDocument.Property(data, "slide");
            var line = ShowDocument.Property(slide, "line");
            if (line.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            if (line.ValueKind != JsonValueKind.Number ||
                !line.TryGetInt32(out int position) || position < 0)
                return Refused("Invalid original output line position: " + active.Id);
            highest = Math.Max(highest, position);
        }

        var resolved = new FreeShowRevealStyleContext(
            styleLines, limits.FewestStyleLines ?? 0, highest,
            ShowDocument.IsTrue(style, "showAsFaded"),
            sourceHasUnportedVirtualBreaks);

        return new(resolved, null);
    }

    private static bool TryReadLines(JsonElement style, out int lines)
    {
        lines = 0;
        var raw = ShowDocument.Property(style, "lines");
        if (raw.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return true;

        double n;
        if (raw.ValueKind == JsonValueKind.Number)
        {
            if (!raw.TryGetDouble(out n)) return false;
        }
        else if (raw.ValueKind == JsonValueKind.String &&
                 double.TryParse(raw.GetString(), NumberStyles.Any,
                     CultureInfo.InvariantCulture, out var parsed))
            n = parsed;
        else return false;

        if (!double.IsFinite(n) || n < 0 || n > 100000 ||
            n != Math.Truncate(n))
            return false;
        lines = (int)n;
        return true;
    }

    private static FreeShowRevealStyleResolution Refused(string warning) =>
        new(null, warning);
}

public sealed record FreeShowRevealStyleResolution(
    FreeShowRevealStyleContext? Context, string? Warning)
{
    public bool CanPrepare => Context != null && Warning == null;
}
