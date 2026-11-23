using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>Read-only source projection for MultiOutputs.svelte. Never invent an
/// output's current lyric from the selected song when OutData points elsewhere.</summary>
public static class FreeShowOutputPreviewProjection
{
    public static IReadOnlyList<FreeShowOutputPreviewEntry> Prepare(
        FreeShowOutputConfiguration configuration, ShowDocument? loadedShow)
    {
        var enabled = configuration.All.Where(o => o.Enabled)
            .OrderBy(o => o.StageSourceId != null)
            .ThenBy(o => o.Name, FreeShowOutputNameComparer.Instance).ToArray();
        var visible = enabled.Length > 1
            ? enabled.Where(o => !ShowDocument.IsTrue(o.Raw, "hideFromPreview"))
            : enabled.AsEnumerable();
        var result = new List<FreeShowOutputPreviewEntry>();
        foreach (var output in visible)
        {
            var active = ShowDocument.Property(
                ShowDocument.Property(output.Raw, "out"), "slide");
            var id = ShowDocument.String(active, "id");
            var layout = ShowDocument.String(active, "layout");
            var element = ShowDocument.Property(active, "index");
            int? index = element.ValueKind == JsonValueKind.Number &&
                         element.TryGetInt32(out var value) && value >= 0 ? value : null;
            SlideFrame? frame = null;
            string? warning = null;
            if (output.StageSourceId != null)
                warning = "Stage output renderer not yet ported";
            else if (output.NeedsUnportedStreamingBackend)
                warning = "Streaming output renderer not yet ported";
            else if (loadedShow == null || id != loadedShow.ShowId)
                warning = "Output does not reference the selected source show";
            else if (index == null)
                warning = "No valid output slide index";
            else
            {
                if (layout.Length == 0) layout = loadedShow.ActiveLayoutId;
                if (ShowDocument.Property(loadedShow.Layouts, layout).ValueKind != JsonValueKind.Object)
                    warning = "Missing original output layout";
                else
                {
                    frame = SlideProjection.ReadFrames(loadedShow, layout, includeDisabled: true)
                        .FirstOrDefault(s => s.LayoutIndex == index);
                    if (frame == null) warning = "Missing original output slide";
                }
            }
            result.Add(new(output, frame, layout, index, warning));
        }
        return result.AsReadOnly();
    }
}
public sealed record FreeShowOutputPreviewEntry(
    FreeShowConfiguredOutput Output, SlideFrame? SourceFrame,
    string LayoutId, int? LayoutIndex, string? Omission);
