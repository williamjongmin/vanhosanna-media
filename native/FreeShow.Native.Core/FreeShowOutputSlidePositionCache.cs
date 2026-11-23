using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// A read-only-data, in-memory equivalent of source clear.ts
/// outputSlideCache for F2. The original FreeShow source preserves a slide
/// for restoration when its index is known and it is not the final
/// layout entry, unless focus mode allows caching the final slide.
/// Does not persist, send output, or guess unknown show/layout metadata.
/// </summary>
public sealed class FreeShowOutputSlidePositionCache
{
    private readonly Dictionary<string, JsonElement> snapshots =
        new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, JsonElement> Snapshot() =>
        new Dictionary<string, JsonElement>(snapshots, StringComparer.Ordinal);

    public void Clear() => snapshots.Clear();

    public bool TryCapture(
        FreeShowOutputState source,
        ShowDocument knownShow,
        bool focusMode)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(knownShow);

        var slide = ShowDocument.Property(source.Snapshot(), "slide");
        if (slide.ValueKind != JsonValueKind.Object ||
            ShowDocument.String(slide, "id") != knownShow.ShowId)
            return false;

        var indexValue = ShowDocument.Property(slide, "index");
        if (indexValue.ValueKind != JsonValueKind.Number ||
            !indexValue.TryGetInt32(out var index) || index < 0)
            return false;

        var layout = ShowDocument.String(slide, "layout");
        if (layout.Length == 0) layout = knownShow.ActiveLayoutId;
        if (ShowDocument.Property(knownShow.Layouts, layout).ValueKind != JsonValueKind.Object)
            return false;

        IReadOnlyList<LayoutEntry> entries;
        try { entries = knownShow.ReadLayoutEntries(layout); }
        catch (InvalidDataException) { return false; }

        if (!focusMode && index >= entries.Count - 1) return false;
        if (index >= entries.Count) return false;

        // The JSON clone also keeps unknown original OutSlide fields such as
        // line, tempItems, layout, special media and future schema additions.
        snapshots[source.OutputId] = slide.Clone();
        return true;
    }

    public JsonElement? Get(string outputId) =>
        snapshots.TryGetValue(outputId, out var slide) ? slide.Clone() : null;
}
