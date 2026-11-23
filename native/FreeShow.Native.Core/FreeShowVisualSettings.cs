using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Read-only access to the same on-disk settings the original FreeShow uses:
/// src/electron/data/store.ts => portable "settings_synced.json" and "templates.json".
/// The caller supplies FreeShow's selected data root, including custom dataPath.
/// </summary>
public sealed class FreeShowVisualSettings
{
    public JsonElement SyncedSettings { get; }
    public JsonElement Templates { get; }
    public JsonElement Overlays { get; }
    public JsonElement Styles => ShowDocument.Property(SyncedSettings, "styles");
    public bool HasSettingsFile { get; }
    public bool HasTemplatesFile { get; }
    public bool HasOverlaysFile { get; }

    private FreeShowVisualSettings(JsonElement syncedSettings, JsonElement templates,
        JsonElement overlays, bool hasSettingsFile, bool hasTemplatesFile,
        bool hasOverlaysFile)
    {
        SyncedSettings = syncedSettings;
        Templates = templates;
        Overlays = overlays;
        HasSettingsFile = hasSettingsFile;
        HasTemplatesFile = hasTemplatesFile;
        HasOverlaysFile = hasOverlaysFile;
    }

    public static async Task<FreeShowVisualSettings> OpenAsync(string freeShowDataRoot,
        CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(freeShowDataRoot, "Config");
        var settingsPath = Path.Combine(folder, "settings_synced.json");
        var templatesPath = Path.Combine(folder, "templates.json");
        var overlaysPath = Path.Combine(folder, "overlays.json");
        var styles = await ReadObjectAsync(settingsPath, cancellationToken);
        var templates = await ReadObjectAsync(templatesPath, cancellationToken);
        var overlays = await ReadObjectAsync(overlaysPath, cancellationToken);
        return new(styles, templates, overlays, File.Exists(settingsPath),
            File.Exists(templatesPath), File.Exists(overlaysPath));
    }

    private static async Task<JsonElement> ReadObjectAsync(string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return default;
        var info = new FileInfo(path);
        if (info.Length > ShowDocument.MaxFileBytes)
            throw new InvalidDataException("FreeShow configuration exceeds reader size limit: " + path);
        await using var stream = File.OpenRead(path);
        using var parsed = await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth = 128 }, cancellationToken);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected FreeShow JSON configuration object: " + path);
        return parsed.RootElement.Clone();
    }

    public JsonElement GetStyle(string styleId) => ShowDocument.Property(Styles, styleId);
    public JsonElement GetTemplate(string templateId) => ShowDocument.Property(Templates, templateId);
    public bool IsOverlayLocked(string overlayId) =>
        ShowDocument.IsTrue(ShowDocument.Property(Overlays, overlayId), "locked");

    /// <summary>
    /// Same ID selection priority as getStyleTemplate() in the original output.ts:
    /// ordinary template vs scripture template(_N), with optional first-slide override.
    /// Does not attempt the complex mergeWithTemplate() logic.
    /// </summary>
    public TemplateSelection SelectTemplate(ShowDocument show, SlideFrame frame, string styleId)
    {
        var style = GetStyle(styleId);
        if (style.ValueKind != JsonValueKind.Object)
            return new("", default, styleId, false, false);

        bool isScripture = ShowDocument.String(
            ShowDocument.Property(show.Root, "reference"), "type") == "scripture";
        var dynamic = ShowDocument.Property(frame.RawSlide, "customDynamicValues");
        if (dynamic.ValueKind == JsonValueKind.Object)
            isScripture |= dynamic.EnumerateObject().Any(p => p.Name.StartsWith("scripture",
                StringComparison.Ordinal));

        string translationSuffix = "";
        if (isScripture)
        {
            var referenceData = ShowDocument.Property(
                ShowDocument.Property(show.Root, "reference"), "data");
            int translations = 1;
            var rawTranslations = ShowDocument.Property(referenceData, "translations");
            if (rawTranslations.ValueKind == JsonValueKind.Number
                && rawTranslations.TryGetInt32(out var count) && count > 0)
                translations = count;
            else
            {
                var version = ShowDocument.String(referenceData, "version");
                if (!string.IsNullOrWhiteSpace(version))
                    translations = version.Split('+').Length;
            }
            if (translations > 1) translationSuffix = "_" + translations;
        }

        var id = isScripture
            ? ShowDocument.String(style, "templateScripture" + translationSuffix)
            : "";
        if (id.Length == 0)
            id = ShowDocument.String(style, isScripture ? "templateScripture" : "template");

        var template = GetTemplate(id);
        bool firstSlideOverride = false;
        if (frame.LayoutIndex == 0 && template.ValueKind == JsonValueKind.Object)
        {
            var first = ShowDocument.String(
                ShowDocument.Property(template, "settings"), "firstSlideTemplate");
            if (first.Length > 0)
            {
                var firstTemplate = GetTemplate(first);
                if (firstTemplate.ValueKind == JsonValueKind.Object)
                {
                    firstSlideOverride = true;
                    id = first;
                    template = firstTemplate;
                }
            }
        }
        return new(id, template, styleId, isScripture, firstSlideOverride);
    }
}

public sealed record TemplateSelection(
    string TemplateId, JsonElement RawTemplate, string OutputStyleId,
    bool Scripture, bool UsedFirstSlideOverride)
{
    public bool TemplateExists => RawTemplate.ValueKind == JsonValueKind.Object;
    public JsonElement Settings => ShowDocument.Property(RawTemplate, "settings");
    public JsonElement Items => ShowDocument.Property(RawTemplate, "items");
}
