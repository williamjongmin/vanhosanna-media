using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Read-only snapshot of the original local FreeShow SETTINGS store.
/// Original files: src/electron/data/defaults.ts, src/electron/data/store.ts,
/// src/types/Output.ts and src/frontend/components/helpers/output.ts.
/// The caller supplies the *actual* settings.json path (normally Electron's
/// app data folder, NOT the portable Config/settings_synced.json).
/// </summary>
public sealed class FreeShowOutputConfiguration
{
    public JsonElement RawSettings { get; }
    public IReadOnlyList<FreeShowConfiguredOutput> All { get; }
    public IReadOnlyList<string> Warnings { get; }

    private FreeShowOutputConfiguration(JsonElement settings,
        IReadOnlyList<FreeShowConfiguredOutput> outputs,
        IReadOnlyList<string> warnings)
    {
        RawSettings = settings;
        All = outputs;
        Warnings = warnings;
    }

    public static async Task<FreeShowOutputConfiguration> ReadAsync(
        string originalSettingsJsonPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalSettingsJsonPath);
        var info = new FileInfo(originalSettingsJsonPath);
        if (!info.Exists) throw new FileNotFoundException(
            "FreeShow's local settings.json is missing; no output defaults are invented.",
            originalSettingsJsonPath);
        if (info.Length > ShowDocument.MaxFileBytes)
            throw new InvalidDataException("FreeShow settings.json exceeds the safe reader limit.");

        await using var stream = File.OpenRead(originalSettingsJsonPath);
        using var parsed = await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth = 128 }, cancellationToken);
        return Parse(parsed.RootElement);
    }

    public static FreeShowOutputConfiguration Parse(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("FreeShow SETTINGS must be a JSON object.");

        var json = source.Clone();
        var raw = ShowDocument.Property(json, "outputs");
        if (raw.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                "FreeShow settings.json contains no outputs object. Refusing to invent outputs.");

        var outputs = new List<FreeShowConfiguredOutput>();
        var warnings = new List<string>();
        foreach (var entry in raw.EnumerateObject())
        {
            var o = entry.Value;
            if (o.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Output '" + entry.Name + "' is not an object.");
                continue;
            }

            string id = entry.Name;
            if (id.Length == 0)
            {
                warnings.Add("Found an output with an empty ID; ignored.");
                continue;
            }

            var bounds = ShowDocument.Property(o, "bounds");
            double x = ReadNumber(bounds, "x", 0);
            double y = ReadNumber(bounds, "y", 0);
            double width = ReadNumber(bounds, "width", 1920);
            double height = ReadNumber(bounds, "height", 1080);
            if (width <= 0 || height <= 0)
            {
                warnings.Add("Output '" + id + "' has invalid bounds; it is not ready to display.");
                continue;
            }

            // The stageOutput field is the *linked source output ID*, not a
            // new fixed output enum or a Media stage renderer selector.
            var stage = ShowDocument.String(o, "stageOutput");
            bool enabled = ShowDocument.IsTrue(o, "enabled");
            bool active = ShowDocument.IsTrue(o, "active");
            outputs.Add(new FreeShowConfiguredOutput(
                id,
                ShowDocument.String(o, "name"),
                enabled, active, stage.Length > 0 ? stage : null,
                ShowDocument.String(o, "style"),
                ShowDocument.String(o, "color"),
                ShowDocument.String(o, "screen"),
                new FreeShowOutputBounds(x, y, width, height),
                ShowDocument.IsTrue(o, "boundsLocked"),
                ShowDocument.IsTrue(o, "alwaysOnTop"),
                ShowDocument.IsTrue(o, "transparent"),
                ShowDocument.IsTrue(o, "ndi"),
                ShowDocument.IsTrue(o, "omt"),
                ShowDocument.IsTrue(o, "blackmagic"),
                ShowDocument.IsTrue(o, "webrtc"),
                ShowDocument.IsTrue(o, "rtmp"),
                ShowDocument.IsTrue(o, "invisible"),
                o.Clone()));
        }

        if (outputs.Count == 0)
            warnings.Add("No configured native outputs. Keep the original FreeShow application unchanged.");

        return new(json, outputs.AsReadOnly(), warnings.AsReadOnly());
    }

    /// <summary>
    /// Original getAllNormalOutputs() selects enabled non-stage outputs and
    /// getAllActiveOutputs() prefers active outputs. If none are marked active,
    /// original main window activates the first in memory; a read-only native
    /// snapshot returns the first without mutating settings.
    /// </summary>
    public IReadOnlyList<FreeShowConfiguredOutput> GetActiveNormalOutputs()
    {
        var normal = All
            .Where(o => o.Enabled && o.StageSourceId == null)
            .OrderBy(o => o.Name, FreeShowOutputNameComparer.Instance)
            .ToArray();
        var active = normal.Where(o => o.Active).ToArray();
        if (active.Length > 0) return active;
        return normal.Length == 0 ? Array.Empty<FreeShowConfiguredOutput>() : [normal[0]];
    }

    public IReadOnlyList<FreeShowConfiguredOutput> GetEnabledStageOutputs() =>
        All.Where(o => o.Enabled && o.StageSourceId != null).ToArray();

    public FreeShowConfiguredOutput? Find(string id) =>
        All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));

    private static double ReadNumber(JsonElement value, string property, double fallback)
    {
        var element = ShowDocument.Property(value, property);
        return element.ValueKind == JsonValueKind.Number &&
            element.TryGetDouble(out var number) && double.IsFinite(number)
            ? number : fallback;
    }
}

/// <summary>
/// Approximate source sortByName() using case-insensitive culture sort and
/// natural numbers. Browser localeCompare() ordering can differ for unusual
/// Unicode names; this ordering is not declared exact UI parity.
/// </summary>
internal sealed class FreeShowOutputNameComparer : IComparer<string>
{
    public static readonly FreeShowOutputNameComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        x ??= ""; y ??= "";
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int xStart = i, yStart = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x[xStart..i].TrimStart('0');
                var b = y[yStart..j].TrimStart('0');
                int len = a.Length.CompareTo(b.Length);
                if (len != 0) return len;
                int digits = string.CompareOrdinal(a, b);
                if (digits != 0) return digits;
                continue;
            }

            int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (c != 0) return c;
            i++; j++;
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}

public sealed record FreeShowOutputBounds(double X, double Y, double Width, double Height);

public sealed record FreeShowConfiguredOutput(
    string Id, string Name, bool Enabled, bool Active, string? StageSourceId,
    string StyleId, string Color, string ScreenId, FreeShowOutputBounds Bounds,
    bool BoundsLocked, bool AlwaysOnTop, bool Transparent,
    bool Ndi, bool Omt, bool Blackmagic, bool WebRtc, bool Rtmp, bool Invisible,
    JsonElement Raw)
{
    public bool NeedsUnportedStreamingBackend =>
        Ndi || Omt || Blackmagic || WebRtc || Rtmp;
}
