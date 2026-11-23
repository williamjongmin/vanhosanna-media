using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Lossless source projection plus a *partial* native render plan. All item and
/// layout JSON remain available to implement unsupported FreeShow output effects.
/// Never describe this plan as pixel-identical to the Svelte renderer.
/// </summary>
public static class NativeRenderPlanner
{
    public static IReadOnlyList<NativeFramePlan> Prepare(
        ShowDocument document, string? layoutId = null, bool includeDisabled = false)
    {
        var frames = SlideProjection.ReadFrames(document, layoutId, includeDisabled);
        return frames.Select(Build).ToArray();
    }

    private static NativeFramePlan Build(SlideFrame frame)
    {
        var warnings = new List<string>();
        var layers = new List<NativeItemPlan>();
        foreach (var item in frame.Items)
        {
            var type = string.IsNullOrEmpty(item.Type) ? "text" : item.Type;
            if (type != "text")
            {
                warnings.Add("Non-text item requires FreeShow native renderer parity: " + type);
                continue;
            }

            var css = new CssDeclarations(item.Style);
            var alignCss = new CssDeclarations(ShowDocument.String(item.Raw,"align"));
            string[] requiredLengths = ["left", "top", "width", "height", "font-size"];
            foreach (var key in requiredLengths)
                if (css.Has(key) && css.Length(key) is null)
                    warnings.Add("Unsupported CSS length " + key + ": " + css.Get(key));

            if (item.Lines.Count == 0) continue;

            // FreeShow's src/frontend/components/slide/Zoomed.svelte defaults:
            // absolute items, width 400px, height 150px, font size 100px,
            // white foreground, shadow and 1.1 line-height.
            var left = css.Length("left")?.Resolve(1920) ?? 0;
            var top = css.Length("top")?.Resolve(1080) ?? 0;
            var width = css.Length("width")?.Resolve(1920) ?? 400;
            var height = css.Length("height")?.Resolve(1080) ?? 150;
            var size = css.Length("font-size")?.Resolve(1920) ?? 100;
            var opacity = css.Number("opacity") ?? 1;
            // Zoomed.svelte supplies a default text-shadow of 2px 2px 10px black.
            // Explicit `none` must override it. Complex CSS shadows stay diagnostic.
            var shadow = css.Has("text-shadow")
                ? CssTextShadow.Parse(css.Get("text-shadow"), out _) : new CssShadow(2, 2, 10, "#000000");
            if (css.Has("text-shadow"))
            {
                CssTextShadow.Parse(css.Get("text-shadow"), out var shadowSupported);
                if (!shadowSupported) warnings.Add("Unsupported CSS text-shadow: " + css.Get("text-shadow"));
            }
            if (item.Lines.SelectMany(line => line.Runs).Any(run => new CssDeclarations(run.Style).Has("text-shadow")))
                warnings.Add("Per-run text-shadow overrides not yet supported.");

            if (width <= 0 || height <= 0 || size <= 0)
            {
                warnings.Add("Invalid item dimensions/font size; item skipped.");
                continue;
            }

            var right = css.Length("right");
            var bottom = css.Length("bottom");
            if (right.HasValue && !css.Has("left"))
                left = 1920 - right.Value.Resolve(1920) - width;
            if (bottom.HasValue && !css.Has("top"))
                top = 1080 - bottom.Value.Resolve(1080) - height;

            string? unknown = css.Values.Keys.FirstOrDefault(key =>
                key is "transform" or "filter" or "backdrop-filter" or "clip-path" or "shape-outside"
                or "-webkit-text-stroke" or "animation");
            if (unknown != null)
                warnings.Add("Not yet faithfully rendered CSS: " + unknown);

            layers.Add(new NativeItemPlan(
                new NativeRect(left, top, width, height),
                size, Math.Clamp(opacity, 0, 1),
                css.Get("color") is { Length: > 0 } color ? color : "white",
                css.Get("font-family") is { Length: > 0 } font ? font : "CMGSans",
                css.Get("font-weight"), css.Get("font-style"),
                css.Get("text-align") is { Length: > 0 } alignment ? alignment :
                    alignCss.Get("text-align") is { Length: > 0 } lineAlignment ? lineAlignment : "center",
                alignCss.Get("align-items") is { Length: > 0 } vertical ? vertical : "center",
                css.Get("line-height"), shadow,
                css, item, frame));
        }

        var transition = ShowDocument.Property(frame.RawLayoutData, "transition");
        if (transition.ValueKind == JsonValueKind.Object)
        {
            var type = ShowDocument.String(transition, "type");
            if (type != "none")
                warnings.Add("FreeShow slide transition not yet implemented: " + type);
        }

        var background = ShowDocument.String(frame.RawLayoutData, "background");
        if (!string.IsNullOrEmpty(background))
            warnings.Add("FreeShow media background not yet implemented: " + background);

        return new NativeFramePlan(frame, layers.AsReadOnly(), warnings.Distinct().ToArray());
    }
}

public sealed record NativeRect(double X,double Y,double Width,double Height);
public sealed record NativeItemPlan(
    NativeRect Bounds, double FontSize, double Opacity, string Foreground, string FontFamily,
    string FontWeight, string FontStyle, string Alignment, string VerticalAlignment, string LineHeight,
    CssShadow? Shadow, CssDeclarations Style, SlideItem Source, SlideFrame Frame);
public sealed record NativeFramePlan(
    SlideFrame Source, IReadOnlyList<NativeItemPlan> TextItems, IReadOnlyList<string> Unsupported)
{
    public bool CanRenderWithoutKnownOmissions => Unsupported.Count == 0;
}
