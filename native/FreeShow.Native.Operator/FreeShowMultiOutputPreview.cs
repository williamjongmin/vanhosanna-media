using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeShow.Native.Core;
using FreeShow.Native.Rendering;

namespace FreeShow.Native.Operator;

/// <summary>
/// Read-only subset of original Preview.svelte -> MultiOutputs.svelte:
/// use original enabled output IDs, source order, names, active markers,
/// hideFromPreview flags and source OutData.slide references.
/// All preview rendering uses the same NativeRenderPlanner as output content.
/// Never mirrors a different song or stage renderer as if it were live.
/// </summary>
public sealed class FreeShowMultiOutputPreview : UserControl
{
    private readonly FreeShowOutputConfiguration configuration;
    private readonly ShowDocument? selectedShow;
    private readonly WrapPanel previews = new()
    {
        Orientation = Orientation.Horizontal
    };
    private int gridColumns = 2;

    public FreeShowMultiOutputPreview(
        FreeShowOutputConfiguration source,
        ShowDocument? currentShow = null)
    {
        configuration = source ?? throw new ArgumentNullException(nameof(source));
        selectedShow = currentShow;
        Content = previews;
        Refresh();
    }

    public int GridColumns => gridColumns;

    public void SetGridColumns(int columns)
    {
        if (columns is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(columns));
        gridColumns = columns;
        Refresh();
    }

    public void Refresh()
    {
        previews.Children.Clear();
        // MultiOutputs.svelte uses all enabled outputs, not just active ones;
        // it orders stage outputs after normal outputs and hides those
        // explicitly marked hideFromPreview when more than one is enabled.
        var enabled = configuration.All.Where(x => x.Enabled)
            .OrderBy(x => x.StageSourceId != null)
            .ThenBy(x => x.Name, FreeShowOutputNameComparer.Instance)
            .ToArray();
        var visible = enabled.Length > 1
            ? enabled.Where(o => !ShowDocument.IsTrue(o.Raw, "hideFromPreview")).ToArray()
            : enabled;
        gridColumns = Math.Clamp(gridColumns, 1, Math.Max(1, visible.Length));

        foreach (var output in visible)
            previews.Children.Add(CreatePreview(output));
        ResizePreviewCells();

        void ResizePreviewCells()
        {
            // The original MultiOutputs component takes 100 / gridColumns %.
            var previewWidth = Bounds.Width <= 0 ? 290 : Bounds.Width;
            var width = Math.Max(100, (previewWidth - 4) / gridColumns);
            foreach (var control in previews.Children)
                control.Width = width;
        }
    }

    private Control CreatePreview(FreeShowConfiguredOutput output)
    {
        var color = Color.TryParse(output.Color, out var parsed)
            ? new SolidColorBrush(parsed) : Brushes.Gray;
        var stack = new StackPanel { Spacing = 0 };
        var label = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(output.Name) ? output.Id : output.Name,
            Foreground = color,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 3),
            Opacity = output.Active ? 1 : 0.65
        };
        stack.Children.Add(label);

        var surface = new Border
        {
            Height = 150,
            Background = Brushes.Black,
            BorderBrush = color,
            BorderThickness = new Thickness(output.Active ? 2 : 1),
            ClipToBounds = true
        };

        // Stage screens use a different source component and should not be
        // falsely rendered with standard show lyric output.
        if (output.StageSourceId == null && !output.NeedsUnportedStreamingBackend)
        {
            var outData = ShowDocument.Property(output.Raw, "out");
            var liveSlide = ShowDocument.Property(outData, "slide");
            string showId = ShowDocument.String(liveSlide, "id");
            string layoutId = ShowDocument.String(liveSlide, "layout");
            int? index = ReadIndex(ShowDocument.Property(liveSlide, "index"));
            if (selectedShow != null && showId == selectedShow.ShowId && index != null)
            {
                if (layoutId.Length == 0) layoutId = selectedShow.ActiveLayoutId;
                if (ShowDocument.Property(selectedShow.Layouts, layoutId).ValueKind == JsonValueKind.Object)
                {
                    var prepared = NativeRenderPlanner.Prepare(
                        selectedShow, layoutId, includeDisabled: true);
                    // Original output "index" uses the source ref index, not
                    // ordinal among visible slides after disabled filtering.
                    var frame = prepared.FirstOrDefault(p => p.Source.LayoutIndex == index.Value);
                    if (frame != null)
                    {
                        var renderer = new NativeSlidePresenter();
                        renderer.Prepare([frame]);
                        renderer.Show(0);
                        surface.Child = renderer;
                    }
                }
            }
        }

        stack.Children.Add(surface);
        stack.Children.Add(new TextBlock
        {
            Text = output.Bounds.Width.ToString("0") + " × " +
                   output.Bounds.Height.ToString("0"),
            Margin = new Thickness(4, 2),
            FontSize = 10,
            Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        return new Border { Child = stack, Margin = new Thickness(2) };
    }

    private static int? ReadIndex(JsonElement number) =>
        number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out var value)
        && value >= 0 ? value : null;
}
