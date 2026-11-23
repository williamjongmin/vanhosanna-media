using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Read-only FreeShow compositing instructions. Output.svelte renders multiple
/// independent layers and can retain an existing background across slide changes.
/// This model deliberately does not confuse a layout "background" action with an
/// already-live background or decode video on the cue-change path.
/// </summary>
public static class FreeShowOutputLayerPlanner
{
    public static IReadOnlyList<FreeShowOutputLayerPlan> Prepare(
        ShowDocument show, FreeShowVisualSettings settings, string outputStyleId,
        string? existingOutputBackgroundId = null)
    {
        var style = settings.GetStyle(outputStyleId);
        var layers = ReadLayers(style);
        var frames = SlideProjection.ReadFrames(show);
        var plans = new List<FreeShowOutputLayerPlan>(frames.Count);

        foreach (var frame in frames)
        {
            var template = settings.SelectTemplate(show, frame, outputStyleId);
            var templateSettings = template.Settings;
            var slideSettings = ShowDocument.Property(frame.RawSlide, "settings");
            var warnings = new List<string>();

            if (!settings.HasSettingsFile)
                warnings.Add("settings_synced.json not loaded; output styles may differ.");
            if (!settings.HasTemplatesFile)
                warnings.Add("templates.json not loaded; template content may differ.");
            if (outputStyleId.Length != 0 && style.ValueKind != JsonValueKind.Object)
                warnings.Add("Unknown FreeShow output style: " + outputStyleId);
            if (template.TemplateId.Length > 0 && !template.TemplateExists)
                warnings.Add("Unknown FreeShow template: " + template.TemplateId);

            // Output.svelte uses the template background first, otherwise the
            // current output background state (which belongs to the output,
            // not to this slide's static layout entry).
            var templateBackground = ShowDocument.String(templateSettings, "backgroundPath");
            var outputBackground = templateBackground.Length > 0
                ? templateBackground : existingOutputBackgroundId ?? "";

            // Style-background is an independent layer beneath output background.
            bool hasActiveOutput = true;
            bool clearStyleBackground = ShowDocument.IsTrue(style, "clearStyleBackgroundOnText");
            var styleBackground = clearStyleBackground && hasActiveOutput
                ? "" : ShowDocument.String(style, "backgroundImage");

            var baseColor = ShowDocument.String(templateSettings, "backgroundColor");
            if (baseColor.Length == 0) baseColor = ShowDocument.String(slideSettings, "color");
            if (baseColor.Length == 0) baseColor = ShowDocument.String(style, "background");
            if (baseColor.Length == 0) baseColor = "black";

            // Layout background is a playback/action request, not necessarily
            // the presently composited background. Do not show it automatically.
            var backgroundAction = ShowDocument.String(frame.RawLayoutData, "background");

            var transition = ResolveTransition(frame.RawLayoutData, style);
            if (template.TemplateExists && template.Items.ValueKind == JsonValueKind.Array
                && template.Items.GetArrayLength() > 0)
                warnings.Add("Template merging (mergeWithTemplate) not implemented; selected template is metadata only.");

            if (!string.IsNullOrWhiteSpace(templateBackground))
                warnings.Add("Template media background not rendered yet: " + templateBackground);
            if (!string.IsNullOrWhiteSpace(styleBackground))
                warnings.Add("Style media background not rendered yet: " + styleBackground);
            if (!string.IsNullOrWhiteSpace(outputBackground))
                warnings.Add("Output media background not rendered yet: " + outputBackground);
            if (!string.IsNullOrWhiteSpace(backgroundAction))
                warnings.Add("Slide media action not executed yet: " + backgroundAction);
            if (!layers.Contains("slide"))
                warnings.Add("Slide layer disabled by original output style; renderer must hide slide.");

            if (transition.Type != "none" && transition.DurationMs > 0)
                warnings.Add("Original text transition not implemented: " + transition.Type);

            // Preserve all source data so future renderers can implement original behavior.
            plans.Add(new FreeShowOutputLayerPlan(
                frame, outputStyleId, template, layers, baseColor,
                styleBackground, outputBackground, backgroundAction,
                transition, style, warnings.Distinct().ToArray()));
        }
        return plans.AsReadOnly();
    }

    public static IReadOnlyList<string> ReadLayers(JsonElement style)
    {
        var raw = ShowDocument.Property(style, "layers");
        if (raw.ValueKind != JsonValueKind.Array)
            return ["background", "slide", "overlays"];
        return raw.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? "")
            .Where(x => x.Length > 0).ToArray();
    }

    private static NativeTransition ResolveTransition(JsonElement layout, JsonElement style)
    {
        var transition = ShowDocument.Property(layout, "transition");
        if (ShowDocument.String(transition, "type").Length == 0)
            transition = ShowDocument.Property(ShowDocument.Property(style, "transition"), "text");
        // Global transitionData is a separate runtime source and cannot be
        // inferred from a .show file alone.
        string type = ShowDocument.String(transition, "type");
        double duration = 0;
        var rawDuration = ShowDocument.Property(transition, "duration");
        if (rawDuration.ValueKind == JsonValueKind.Number &&
            rawDuration.TryGetDouble(out var parsed) && double.IsFinite(parsed))
            duration = Math.Max(parsed, 0);
        return new(type, duration, transition);
    }
}

public sealed record NativeTransition(string Type, double DurationMs, JsonElement Raw);
public sealed record FreeShowOutputLayerPlan(
    SlideFrame Frame, string StyleId, TemplateSelection Template,
    IReadOnlyList<string> EnabledLayers, string BackgroundColor,
    string StyleBackgroundId, string OutputBackgroundId,
    string BackgroundActionId, NativeTransition TextTransition,
    JsonElement RawStyle, IReadOnlyList<string> Unsupported)
{
    public bool SlideEnabled => EnabledLayers.Contains("slide");
    public bool BackgroundEnabled => EnabledLayers.Contains("background");
    public bool OverlayEnabled => EnabledLayers.Contains("overlays");
}
