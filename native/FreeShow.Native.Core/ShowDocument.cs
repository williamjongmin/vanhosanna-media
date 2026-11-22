using System.Text.Json;

namespace FreeShow.Native.Core;

// Read-only compatibility layer for src/types/Show.ts.
// Preserve original bytes, including fields unknown to the native port.
public sealed class ShowDocument
{
    private readonly byte[] original;
    public JsonElement Root { get; }
    public string Name => String(Root, "name");
    public string ActiveLayoutId => String(Property(Root, "settings"), "activeLayout");
    public JsonElement Slides => Property(Root, "slides");
    public JsonElement Layouts => Property(Root, "layouts");
    public JsonElement Media => Property(Root, "media");

    private ShowDocument(byte[] bytes)
    {
        original = bytes;
        using var document = JsonDocument.Parse(bytes);
        Root = document.RootElement.Clone();
        if (Root.ValueKind != JsonValueKind.Object ||
            Slides.ValueKind != JsonValueKind.Object ||
            Layouts.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected a FreeShow Show object (slides and layouts).");
    }
    public static ShowDocument Parse(ReadOnlySpan<byte> bytes) => new(bytes.ToArray());
    public static async Task<ShowDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
        => new(await File.ReadAllBytesAsync(path, cancellationToken));
    public byte[] OriginalBytes() => (byte[])original.Clone();
    public JsonElement GetSlide(string id) => Property(Slides, id);
    public JsonElement GetLayout(string id) => Property(Layouts, id);
    public IReadOnlyList<LayoutEntry> ReadLayoutEntries(string? layoutId = null)
    {
        var layout = GetLayout(layoutId ?? ActiveLayoutId);
        var entries = Property(layout, "slides");
        if (entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Selected layout is missing or invalid.");
        var result = new List<LayoutEntry>();
        foreach (var entry in entries.EnumerateArray())
        {
            var id = String(entry, "id");
            if (id.Length == 0) throw new InvalidDataException("Layout entry missing slide ID.");
            result.Add(new(result.Count, id, Property(entry, "disabled").ValueKind == JsonValueKind.True,
                entry, GetSlide(id)));
        }
        return result.AsReadOnly();
    }

    public static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) ? result : default;
    public static string String(JsonElement value, string name)
    {
        var item = Property(value, name);
        return item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    }
}

public sealed record LayoutEntry(int Index, string SlideId, bool Disabled, JsonElement RawEntry, JsonElement Slide)
{
    public string Group => ShowDocument.String(Slide, "group");
    public JsonElement Items => ShowDocument.Property(Slide, "items");
    public JsonElement Children => ShowDocument.Property(Slide, "children");
    public bool MissingSlide => Slide.ValueKind == JsonValueKind.Undefined;
}
