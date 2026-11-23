using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FreeShow.Native.Core;

/// <summary>
/// FreeShow's Svelte text output uses item.style and per-run style strings.
/// This parser intentionally preserves unknown CSS instead of replacing them.
/// Based on src/frontend/components/helpers/style.ts (getStyles).
/// A native visual only supports a subset of CSS today.
/// </summary>
public sealed class CssDeclarations
{
    private static readonly Regex Declaration = new(
        @"(?:^|;)\s*(?<key>[-a-zA-Z0-9]+)\s*:\s*(?<value>[^;]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly IReadOnlyDictionary<string, string> values;

    public CssDeclarations(string? source)
    {
        Source = source ?? "";
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Declaration.Matches(Source))
        {
            var name = match.Groups["key"].Value.Trim();
            if (name.Length == 0) continue;
            pairs[name] = match.Groups["value"].Value.Trim();
        }
        values = new ReadOnlyDictionary<string, string>(pairs);
    }

    public string Source { get; }
    public IReadOnlyDictionary<string,string> Values => values;
    public string Get(string property) => values.TryGetValue(property, out var v) ? v : "";
    public bool Has(string property) => values.ContainsKey(property);

    public static CssLength? ParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim();
        if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(s[..^2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var px) &&
            double.IsFinite(px)) return new(px, CssUnit.Pixels);
        if (s.EndsWith("%", StringComparison.Ordinal) &&
            double.TryParse(s[..^1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) &&
            double.IsFinite(pct)) return new(pct, CssUnit.Percent);
        if (s == "0") return new(0, CssUnit.Pixels);
        return null; // CSS expressions, rem, em, vh and calc() are not silently guessed.
    }

    public CssLength? Length(string property) => ParseLength(Get(property));

    public double? Number(string property)
    {
        var s = Get(property);
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n))
            return n;
        return null;
    }
}

public enum CssUnit { Pixels, Percent }
public readonly record struct CssLength(double Value, CssUnit Unit)
{
    public double Resolve(double reference) => Unit == CssUnit.Percent ? reference * Value / 100.0 : Value;
}
