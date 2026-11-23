using System.Text;
using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Non-rendering output model from native FreeShow data. Preserve all raw JSON for
/// lossless future compatibility with layers, effects, dynamic items, and styling.
/// This is NOT a replacement for FreeShow's full HTML/CSS rendering yet.
/// </summary>
public static class SlideProjection
{
    public static IReadOnlyList<SlideFrame> ReadFrames(ShowDocument show, string? layoutId = null, bool includeDisabled = false)
    {
        var result = new List<SlideFrame>();
        foreach (var slide in show.ReadLayoutEntries(layoutId))
        {
            if (!includeDisabled && slide.EffectiveDisabled) continue;

            var items = new List<SlideItem>();
            if (slide.Items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in slide.Items.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var lines = new List<SlideLine>();
                    var rawLines = ShowDocument.Property(item, "lines");
                    if (rawLines.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var line in rawLines.EnumerateArray())
                        {
                            if (line.ValueKind != JsonValueKind.Object) continue;
                            var runs = new List<TextRun>();
                            var text = ShowDocument.Property(line, "text");
                            if (text.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var run in text.EnumerateArray())
                                {
                                    if (run.ValueKind != JsonValueKind.Object) continue;
                                    runs.Add(new TextRun(
                                        ShowDocument.String(run, "value"),
                                        ShowDocument.String(run, "style"), run));
                                }
                            }
                            lines.Add(new SlideLine(runs.AsReadOnly(), line));
                        }
                    }
                    items.Add(new SlideItem(
                        ShowDocument.String(item, "type"),
                        ShowDocument.String(item, "style"),
                        ShowDocument.String(item, "language"),
                        lines.AsReadOnly(), item));
                }
            }
            result.Add(new SlideFrame(
                slide.Index, slide.SlideId, slide.IsChild, slide.ParentSlideId,
                slide.EffectiveDisabled, slide.Group, ShowDocument.String(slide.Slide, "notes"),
                items.AsReadOnly(), slide.RawEntry, slide.Slide));
        }
        return result.AsReadOnly();
    }
}

public sealed record TextRun(string Text, string Style, JsonElement Raw);
public sealed record SlideLine(IReadOnlyList<TextRun> Runs, JsonElement Raw)
{
    public string Text => string.Concat(Runs.Select(run => run.Text));
}
public sealed record SlideItem(
    string Type, string Style, string Language, IReadOnlyList<SlideLine> Lines, JsonElement Raw)
{
    public string PlainText => string.Join("\n", Lines.Select(line => line.Text));
}
public sealed record SlideFrame(
    int LayoutIndex, string SlideId, bool IsChild, string? ParentSlideId,
    bool Disabled, string Group, string Notes, IReadOnlyList<SlideItem> Items,
    JsonElement RawLayoutData, JsonElement RawSlide)
{
    public string PlainText => string.Join("\n", Items.Where(item => item.Lines.Count > 0)
        .Select(item => item.PlainText));
    public bool HasNonTextItems => Items.Any(item => item.Type.Length > 0 && item.Type != "text");
}
