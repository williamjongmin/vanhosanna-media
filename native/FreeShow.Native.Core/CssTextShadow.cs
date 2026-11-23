using System.Globalization;
using System.Text.RegularExpressions;

namespace FreeShow.Native.Core;

/// <summary>
/// The FreeShow preview's default text shadow is 2px 2px 10px #000000.
/// Only a single CSS shadow with pixel offsets is supported for now.
/// Complex, multiple or variable shadows remain in original CSS for later parity.
/// </summary>
public static class CssTextShadow
{
    private static readonly Regex SingleShadow = new(
        @"^\s*(?<x>-?\d+(?:\.\d+)?)px\s+(?<y>-?\d+(?:\.\d+)?)px\s+(?<blur>\d+(?:\.\d+)?)px\s+(?<color>#[0-9a-fA-F]{3,8}|[a-zA-Z]+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static CssShadow? Parse(string? cssValue, out bool supported)
    {
        supported = true;
        if (string.IsNullOrWhiteSpace(cssValue)) return null;
        if (cssValue.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        var m = SingleShadow.Match(cssValue);
        if (!m.Success)
        {
            supported = false;
            return null;
        }

        var ci = CultureInfo.InvariantCulture;
        if (!double.TryParse(m.Groups["x"].Value, NumberStyles.Float, ci, out var x) ||
            !double.TryParse(m.Groups["y"].Value, NumberStyles.Float, ci, out var y) ||
            !double.TryParse(m.Groups["blur"].Value, NumberStyles.Float, ci, out var blur))
        {
            supported = false;
            return null;
        }
        return new(x, y, blur, m.Groups["color"].Value);
    }
}

public sealed record CssShadow(double OffsetX, double OffsetY, double BlurRadius, string Color);
