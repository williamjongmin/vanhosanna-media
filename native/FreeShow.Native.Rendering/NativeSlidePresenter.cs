using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Text.Json;
using FreeShow.Native.Core;
using System.Globalization;

namespace FreeShow.Native.Rendering;

/// <summary>
/// No Chromium, no HTML, no WebView. Native visual output prototype only.
/// Uses cached Avalonia controls to avoid rebuilding text during cue changes.
/// Does not yet implement exact FreeShow style cascading, full media, or effects.
/// </summary>
public sealed class NativeSlidePresenter : ContentControl
{
    private readonly List<(NativeFramePlan Frame, Viewbox Visual)> prepared = new();
    private readonly NativeStillImageCache images = new();
    private readonly Dictionary<NativeRevealCacheKey, Viewbox> revealVariants = new();
    private readonly List<FreeShowOutputLayerPlan?> outputLayerPlans = new();
    private Func<string, string?>? imageResolver;
    private bool overlayOnly;
    private NativeRevealCacheKey? activeRevealVariant;
    private const int MaximumPreparedRevealVariants = 384;
    public int CurrentIndex { get; private set; } = -1;
    public IReadOnlyList<NativeFramePlan> Frames => prepared.Select(x => x.Frame).ToArray();

    public void Prepare(IReadOnlyList<NativeFramePlan> frames,
        IReadOnlyList<FreeShowOutputLayerPlan>? outputPlans = null,
        Func<string, string?>? localStillImagePath = null,
        bool lyricsOnlyOverlay = false)
    {
        if (outputPlans != null && outputPlans.Count != frames.Count)
            throw new ArgumentException("Output plan and renderer frame counts differ.", nameof(outputPlans));

        Content = null;
        prepared.Clear();
        revealVariants.Clear();
        outputLayerPlans.Clear();
        images.Dispose();
        imageResolver = localStillImagePath;
        overlayOnly = lyricsOnlyOverlay;
        CurrentIndex = -1;
        activeRevealVariant = null;

        for (var i = 0; i < frames.Count; i++)
        {
            var output = outputPlans?[i];
            if (output != null && output.Frame.SlideId != frames[i].Source.SlideId)
                throw new ArgumentException("Output plan order does not match source frames.", nameof(outputPlans));
            prepared.Add((frames[i], Build(frames[i], output, localStillImagePath, lyricsOnlyOverlay)));
            outputLayerPlans.Add(output);
        }
    }

    /// <summary>
    /// F2: clear the currently displayed lyrics without disposing the next
    /// prepared slides or touching the independently playing background.
    /// </summary>
    public void ClearVisible()
    {
        Content = null;
        CurrentIndex = -1;
        activeRevealVariant = null;
    }

    public void ClearPrepared()
    {
        Content = null;
        prepared.Clear();
        revealVariants.Clear();
        outputLayerPlans.Clear();
        CurrentIndex = -1;
        activeRevealVariant = null;
        images.Dispose();
    }

    public bool Show(int index)
    {
        if (index < 0 || index >= prepared.Count) return false;
        if (index == CurrentIndex && activeRevealVariant == null) return true;
        // Only swap a prebuilt visual; no parsing, text layout object creation
        // or synchronous file I/O is performed on this path.
        Content = prepared[index].Visual;
        CurrentIndex = index;
        activeRevealVariant = null;
        return true;
    }

    /// <summary>
    /// Precompute a source reveal state while preparing a show, never in the
    /// cue hot path. Only fully supported text states are cached; original
    /// display/playlist/actions are not silently executed by this visual.
    /// The native persistent video decoder is not accessed here.
    /// </summary>
    public bool PrewarmReveal(
        int nativeCueOrdinal, JsonElement outSlide,
        FreeShowRevealStyleContext outputStyle)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Reveal visual preparation belongs to the UI thread.");
        if (nativeCueOrdinal < 0 || nativeCueOrdinal >= prepared.Count)
            return false;

        var key = NativeRevealCacheKey.From(nativeCueOrdinal, outSlide, outputStyle);
        if (key == null) return false;
        if (revealVariants.ContainsKey(key.Value)) return true;
        if (revealVariants.Count >= MaximumPreparedRevealVariants) return false;

        var projection = FreeShowRevealRenderProjection.Prepare(
            prepared[nativeCueOrdinal].Frame, outSlide, outputStyle);
        if (!projection.IsSupported) return false;

        // This allocates Avalonia controls only during prewarm. A subsequent
        // ShowPrewarmedReveal does exactly one cached Content swap.
        var view = Build(projection.Visible, outputLayerPlans[nativeCueOrdinal],
            imageResolver, overlayOnly);
        revealVariants.Add(key.Value, view);
        return true;
    }

    /// <summary>
    /// Live cue-time primitive: no source parsing, text control creation,
    /// disk access or decoder interaction. Returns false for any reveal
    /// state not explicitly prepared in advance.
    /// </summary>
    public bool ShowPrewarmedReveal(
        int nativeCueOrdinal, JsonElement outSlide,
        FreeShowRevealStyleContext outputStyle)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Reveal selection belongs to the UI thread.");
        var key = NativeRevealCacheKey.From(nativeCueOrdinal, outSlide, outputStyle);
        if (key == null || !revealVariants.TryGetValue(key.Value, out var view))
            return false;
        if (CurrentIndex == nativeCueOrdinal && activeRevealVariant == key.Value)
            return true;
        Content = view;
        CurrentIndex = nativeCueOrdinal;
        activeRevealVariant = key.Value;
        return true;
    }

    /// <summary>
    /// Pure cache lookup for the exact original slide and reveal style.
    /// It never makes controls, moves the visual, or invokes video media.
    /// </summary>
    public bool HasPrewarmedReveal(
        int nativeCueOrdinal, JsonElement outSlide,
        FreeShowRevealStyleContext outputStyle)
    {
        if (nativeCueOrdinal < 0 || nativeCueOrdinal >= prepared.Count)
            return false;
        var key = NativeRevealCacheKey.From(
            nativeCueOrdinal, outSlide, outputStyle);
        return key.HasValue &&
            prepared[nativeCueOrdinal].Frame.Source.LayoutIndex ==
                key.Value.LayoutIndex &&
            revealVariants.ContainsKey(key.Value);
    }

    public int PreparedRevealVariantCount => revealVariants.Count;

    private Viewbox Build(NativeFramePlan frame, FreeShowOutputLayerPlan? output,
        Func<string, string?>? localStillImagePath, bool lyricsOnlyOverlay)
    {
        var canvas = new Canvas
        {
            Width = 1920, Height = 1080,
            Background = output == null || lyricsOnlyOverlay
                ? Brushes.Transparent : Brush(output.BackgroundColor, Brushes.Black),
            ClipToBounds = true
        };

        // Draw FreeShow's independent style background before output background.
        // This prototype can display local still images only. Video, filters,
        // animations, cropping and transitions are not yet implemented.
        void AddStillImage(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference) || localStillImagePath == null)
                return;
            string? fullPath = localStillImagePath(reference);
            if (!images.TryGet(fullPath, out var bitmap) || bitmap == null)
                return;
            canvas.Children.Add(new Image
            {
                Source = bitmap,
                Width = 1920,
                Height = 1080,
                Stretch = Stretch.UniformToFill
            });
        }

        if (output != null && !lyricsOnlyOverlay)
        {
            AddStillImage(output.StyleBackgroundId);
            if (output.BackgroundEnabled)
                AddStillImage(output.OutputBackgroundId);
        }

        if (output == null || output.SlideEnabled)
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

            if (item.Shadow is { } shadow && Color.TryParse(shadow.Color, out var shadowColor))
                text.Effect = new DropShadowEffect
                {
                    OffsetX = shadow.OffsetX,
                    OffsetY = shadow.OffsetY,
                    BlurRadius = shadow.BlurRadius,
                    Color = shadowColor,
                    Opacity = 1
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


/// <summary>
/// Stable cache key for a prepared output state. An invalid or missing
/// source index is never accidentally aliased to the first slide.
/// </summary>
internal readonly record struct NativeRevealCacheKey(
    int NativeCueOrdinal, int LayoutIndex,
    int Line, int RevealCount, bool ItemClickReveal,
    FreeShowRevealStyleContext Style)
{
    public static NativeRevealCacheKey? From(
        int cueOrdinal, JsonElement sourceOutSlide,
        FreeShowRevealStyleContext outputStyle)
    {
        if (outputStyle == null || cueOrdinal < 0 ||
            sourceOutSlide.ValueKind != JsonValueKind.Object)
            return null;
        var index = ShowDocument.Property(sourceOutSlide, "index");
        if (index.ValueKind != JsonValueKind.Number ||
            !index.TryGetInt32(out var layoutIndex) || layoutIndex < 0)
            return null;

        static int? Position(JsonElement source, string name)
        {
            var raw = ShowDocument.Property(source, name);
            if (raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return 0;
            return raw.ValueKind == JsonValueKind.Number &&
                   raw.TryGetInt32(out var position) && position >= 0
                ? position : null;
        }

        var line = Position(sourceOutSlide, "line");
        var reveal = Position(sourceOutSlide, "revealCount");
        if (line == null || reveal == null) return null;

        return new NativeRevealCacheKey(
            cueOrdinal, layoutIndex, line.Value, reveal.Value,
            ShowDocument.IsTrue(sourceOutSlide, "itemClickReveal"),
            outputStyle);
    }
}
