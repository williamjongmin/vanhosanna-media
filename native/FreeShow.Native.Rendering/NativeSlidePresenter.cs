using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using FreeShow.Native.Core;
using System.Globalization;

namespace FreeShow.Native.Rendering;

/// <summary>
/// No Chromium, no HTML, no WebView. Native visual output prototype only.
/// Uses cached Avalonia controls to avoid rebuilding text during cue changes.
/// Does not yet implement exact FreeShow style cascading, shadows, media or effects.
/// </summary>
public sealed class NativeSlidePresenter : ContentControl
{
    private readonly List<(NativeFramePlan Frame, Viewbox Visual)> prepared = new();
    public int CurrentIndex { get; private set; } = -1;
    public IReadOnlyList<NativeFramePlan> Frames => prepared.Select(x => x.Frame).ToArray();

    public void Prepare(IReadOnlyList<NativeFramePlan> frames)
    {
        Content = null;
        prepared.Clear();
        CurrentIndex = -1;
        foreach (var frame in frames)
            prepared.Add((frame, Build(frame)));
    }

    public bool Show(int index)
    {
        if (index < 0 || index >= prepared.Count) return false;
        if (index == CurrentIndex) return true;
        // Only swap a prebuilt visual; no parsing, text layout object creation
        // or synchronous file I/O is performed on this path.
        Content = prepared[index].Visual;
        CurrentIndex = index;
        return true;
    }

    private static Viewbox Build(NativeFramePlan frame)
    {
        var canvas = new Canvas
        {
            Width = 1920, Height = 1080,
            Background = Brushes.Transparent,
            ClipToBounds = true
        };

        foreach (var item in frame.TextItems)
        {
            var wrapper = new Grid
            {
                Width = item.Bounds.Width,
                Height = item.Bounds.Height,
                ClipToBounds = true
            };
            var text = new TextBlock
            {
                FontSize = item.FontSize,
                LineHeight = LineHeight(item.LineHeight,item.FontSize),
                Foreground = Brush(item.Foreground, Brushes.White),
                FontFamily = new FontFamily(item.FontFamily.Trim('"', '\'')),
                FontWeight = Weight(item.FontWeight),
                FontStyle = item.FontStyle.Equals("italic", StringComparison.OrdinalIgnoreCase)
                    ? FontStyle.Italic : FontStyle.Normal,
                TextAlignment = Alignment(item.Alignment),
                VerticalAlignment = item.VerticalAlignment == "flex-start" ? VerticalAlignment.Top :
                    item.VerticalAlignment == "flex-end" ? VerticalAlignment.Bottom : VerticalAlignment.Center,
                Inlines = new InlineCollection(),
                Opacity = item.Opacity,
                TextWrapping = TextWrapping.Wrap,
                ClipToBounds = true
            };

            // Text runs remain individual Avalonia inlines, so original
            // bold/italic/foreground differences survive conversion.
            bool firstLine = true;
            foreach (var line in item.Source.Lines)
            {
                if (!firstLine) text.Inlines?.Add(new LineBreak());
                firstLine = false;
                foreach (var run in line.Runs)
                {
                    var runStyle = new CssDeclarations(run.Style);
                    var inline = new Run { Text = run.Text };
                    if (runStyle.Has("color"))
                        inline.Foreground = Brush(runStyle.Get("color"), text.Foreground);
                    if (runStyle.Has("font-weight"))
                        inline.FontWeight = Weight(runStyle.Get("font-weight"));
                    if (runStyle.Has("font-style"))
                        inline.FontStyle = runStyle.Get("font-style").Equals("italic", StringComparison.OrdinalIgnoreCase)
                            ? FontStyle.Italic : FontStyle.Normal;
                    if (runStyle.Has("font-family"))
                        inline.FontFamily = new FontFamily(runStyle.Get("font-family").Trim('"', '\''));
                    if (runStyle.Length("font-size") is { Unit: CssUnit.Pixels } px)
                        inline.FontSize = px.Value;
                    text.Inlines?.Add(inline);
                }
            }
            wrapper.Children.Add(text);
            Canvas.SetLeft(wrapper, item.Bounds.X);
            Canvas.SetTop(wrapper, item.Bounds.Y);
            canvas.Children.Add(wrapper);
        }

        return new Viewbox
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = canvas
        };
    }

    private static double LineHeight(string css, double fontSize)
    {
        // FreeShow Zoomed.svelte sets 1.1 as the default line-height.
        if (CssDeclarations.ParseLength(css) is { Unit: CssUnit.Pixels } pixels)
            return pixels.Value;
        if (double.TryParse(css, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
            && scale > 0 && double.IsFinite(scale))
            return scale * fontSize;
        return 1.1 * fontSize;
    }

    private static IBrush Brush(string? color, IBrush fallback)
    {
        return color != null && Color.TryParse(color, out var parsed)
            ? new SolidColorBrush(parsed) : fallback;
    }

    private static FontWeight Weight(string value)
    {
        if (value.Equals("bold", StringComparison.OrdinalIgnoreCase)) return FontWeight.Bold;
        if (int.TryParse(value, out var number))
        {
            if (number >= 700) return FontWeight.Bold;
            if (number <= 300) return FontWeight.Light;
            if (number >= 600) return FontWeight.SemiBold;
        }
        return FontWeight.Normal;
    }

    private static TextAlignment Alignment(string value) =>
        value.ToLowerInvariant() switch
        {
            "center" => TextAlignment.Center,
            "right" or "end" => TextAlignment.Right,
            "justify" => TextAlignment.Justify,
            _ => TextAlignment.Left
        };
}
