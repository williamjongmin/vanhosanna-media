using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeShow.Native.Core;
using FreeShow.Native.Rendering;

namespace FreeShow.Native.Operator;

/// <summary>
/// Read-only FreeShow Preview.svelte/MultiOutputs.svelte native region.
/// The original configured output IDs and real OutData.slide refs determine
/// the contents; missing/unsupported output types are never faked.
/// </summary>
public sealed class FreeShowMultiOutputPreview : UserControl
{
    private readonly FreeShowOutputConfiguration configuration;
    private readonly ShowDocument? sourceShow;
    private readonly FreeShowVisualSettings? sourceStyles;
    private readonly IReadOnlyDictionary<string, FreeShowOutputState>? nativeStates;
    private readonly WrapPanel previews = new() { Orientation = Orientation.Horizontal };
    private int gridColumns = 2;

    public FreeShowMultiOutputPreview(
        FreeShowOutputConfiguration settings, ShowDocument? loadedShow = null,
        FreeShowVisualSettings? originalStyles = null,
        IReadOnlyDictionary<string, FreeShowOutputState>? outputStates = null)
    {
        configuration = settings ?? throw new ArgumentNullException(nameof(settings));
        sourceShow = loadedShow;
        sourceStyles = originalStyles;
        nativeStates = outputStates;
        previews.SizeChanged += (_, _) => ResizeCards();
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
        var active = FreeShowOutputPreviewProjection.Prepare(configuration, sourceShow);
        gridColumns = Math.Clamp(gridColumns, 1, Math.Max(1, active.Count));
        foreach (var entry in active)
            previews.Children.Add(CreatePreview(entry));
        ResizeCards();
    }

    private void ResizeCards()
    {
        var width = Bounds.Width > 0 ? Bounds.Width : 290;
        var cardWidth = Math.Max(80, (width - 4) / gridColumns);
        foreach (var child in previews.Children)
            child.Width = cardWidth;
    }

    private Control CreatePreview(FreeShowOutputPreviewEntry entry)
    {
        var output = entry.Output;
        var color = Color.TryParse(output.Color, out var value)
            ? new SolidColorBrush(value) : Brushes.Gray;

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(output.Name) ? output.Id : output.Name,
            Foreground = color,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 3),
            Opacity = output.Active ? 1 : 0.65
        });

        var surface = new Border
        {
            Height = 150,
            Background = Brushes.Black,
            BorderBrush = color,
            BorderThickness = new Thickness(output.Active ? 2 : 1),
            ClipToBounds = true
        };
        string? omission = entry.Omission;
        if (entry.SourceFrame != null)
        {
            var candidate = NativeRenderPlanner.BuildFrame(entry.SourceFrame);
            bool requiresReveal = entry.SourceFrame.Items.Any(item =>
                ShowDocument.IsTrue(item.Raw, "clickReveal") ||
                ShowDocument.IsTrue(item.Raw, "lineReveal"));
            if (requiresReveal)
            {
                if (sourceStyles == null || nativeStates == null ||
                    !nativeStates.TryGetValue(output.Id, out var native))
                {
                    omission = "Original output style/reveal state unavailable";
                }
                else
                {
                    var style = FreeShowRevealStyleResolver.Resolve(
                        output.Id, configuration, sourceStyles, nativeStates);
                    if (!style.CanPrepare)
                        omission = style.Warning;
                    else
                    {
                        var outSlide = ShowDocument.Property(native.Snapshot(), "slide");
                        var projected = FreeShowRevealRenderProjection.Prepare(
                            candidate, outSlide, style.Context!);
                        if (!projected.IsSupported)
                            omission = string.Join("; ", projected.Unported);
                        else
                            candidate = projected.Visible;
                    }
                }
            }

            // Rendering the full lyric while a click/line reveal is incomplete
            // leaks hidden words to operators. Show a blank unknown preview
            // instead of pretending that everything is already revealed.
            if (omission == null)
            {
                var renderer = new NativeSlidePresenter();
                renderer.Prepare([candidate]);
                renderer.Show(0);
                surface.Child = renderer;
            }
        }
        stack.Children.Add(surface);
        if (omission != null)
            stack.Children.Add(new TextBlock
            {
                Text = "Preview unavailable: " + omission,
                FontSize = 9,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 1)
            });

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
}
