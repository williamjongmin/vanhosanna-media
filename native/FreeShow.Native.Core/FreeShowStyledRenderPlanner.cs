namespace FreeShow.Native.Core;

/// <summary>
/// Native output style adapter. Resolution and mutation happen entirely in
/// memory before a slide is selected: the original .show file and its
/// user-authored item JSON stay untouched.
/// </summary>
public static class FreeShowStyledRenderPlanner
{
    public static IReadOnlyList<NativeFramePlan> Prepare(
        ShowDocument source, FreeShowVisualSettings visualSettings, string outputStyleId,
        string? layoutId = null)
    {
        var sourceFrames = SlideProjection.ReadFrames(source, layoutId);
        var result = new List<NativeFramePlan>(sourceFrames.Count);
        foreach (var original in sourceFrames)
        {
            var template = visualSettings.SelectTemplate(source, original, outputStyleId);
            var merge = FreeShowTemplateMerger.Apply(original, template);
            var outputFrame = original with { Items = merge.Items };
            var rendered = NativeRenderPlanner.BuildFrame(outputFrame);

            var warnings = rendered.Unsupported.Concat(merge.Unsupported);
            if (template.TemplateId.Length > 0 && !template.TemplateExists)
                warnings = warnings.Append("Original FreeShow output template is unavailable: " +
                    template.TemplateId);

            result.Add(rendered with { Unsupported = warnings.Distinct().ToArray() });
        }
        return result.AsReadOnly();
    }
}
