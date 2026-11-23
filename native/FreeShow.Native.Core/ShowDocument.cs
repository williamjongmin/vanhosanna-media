using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Read-only FreeShow show reader. Original source references:
/// src/types/Show.ts, src/electron/utils/files.ts (parseShow),
/// src/frontend/components/helpers/shows.ts (_show().layouts().ref()).
/// </summary>
public sealed class ShowDocument
{
    public const long MaxFileBytes = 64L * 1024 * 1024;
    private readonly byte[] original;
    public string ShowId { get; }
    public bool IsFreeShowFileEnvelope { get; }
    public JsonElement Root { get; }
    public string Name => String(Root, "name");
    public string ActiveLayoutId => String(Property(Root, "settings"), "activeLayout");
    public JsonElement Slides => Property(Root, "slides");
    public JsonElement Layouts => Property(Root, "layouts");
    public JsonElement Media => Property(Root, "media");

    private ShowDocument(byte[] bytes)
    {
        if (bytes.Length > MaxFileBytes) throw new InvalidDataException("Show exceeds the 64 MiB read-only limit.");
        original = bytes;
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 128 });
        var source = document.RootElement;
        if (source.ValueKind == JsonValueKind.Array)
        {
            // FreeShow .show files are JSON tuples [showId, show], not standalone Show objects.
            if (source.GetArrayLength() != 2 || source[0].ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Invalid FreeShow .show envelope; expected [id, show].");
            ShowId = source[0].GetString() ?? "";
            if (ShowId.Length == 0) throw new InvalidDataException("FreeShow show ID is empty.");
            Root = source[1].Clone();
            IsFreeShowFileEnvelope = true;
        }
        else
        {
            // Also accept an extracted Show object for code compatibility and fixtures.
            Root = source.Clone();
            ShowId = String(Root, "id");
        }

        if (Root.ValueKind != JsonValueKind.Object ||
            Slides.ValueKind != JsonValueKind.Object ||
            Layouts.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected a Show object with slides and layouts.");
    }

    public static ShowDocument Parse(ReadOnlySpan<byte> bytes) => new(bytes.ToArray());

    public static async Task<ShowDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("FreeShow file not found.", path);
        if (info.Length > MaxFileBytes) throw new InvalidDataException("Show exceeds the 64 MiB read-only limit.");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return new ShowDocument(bytes);
    }

    // Lossless: no normalization, serialization, metadata rewrite or conversion on source bytes.
    public byte[] OriginalBytes() => (byte[])original.Clone();
    public JsonElement GetSlide(string id) => Property(Slides, id);
    public JsonElement GetLayout(string id) => Property(Layouts, id);

    /// <summary>
    /// FreeShow layout order: each parent layout entry followed by children listed in
    /// Show.slides[parentId].children. Child-specific settings come from
    /// Layout.slides[parentIndex].children[childId], not the parent layout entry.
    /// Entries with missing slides are excluded, as FreeShow's ref() does.
    /// </summary>
    public IReadOnlyList<LayoutEntry> ReadLayoutEntries(string? layoutId = null)
    {
        string selected = layoutId ?? ActiveLayoutId;
        var layout = GetLayout(selected);
        var entries = Property(layout, "slides");
        if (entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Selected FreeShow layout is missing or invalid: " + selected);

        var result = new List<LayoutEntry>();
        for (int sourceIndex = 0; sourceIndex < entries.GetArrayLength(); sourceIndex++)
        {
            var rawParent = entries[sourceIndex];
            string parentId = String(rawParent, "id");
            if (parentId.Length == 0) continue;
            var parent = GetSlide(parentId);
            if (parent.ValueKind != JsonValueKind.Object) continue;

            bool parentDisabled = IsTrue(rawParent, "disabled");
            int parentIndex = result.Count;
            result.Add(new LayoutEntry(parentIndex, parentId, parentDisabled, rawParent, parent,
                null, sourceIndex, -1, false));

            var children = Property(parent, "children");
            if (children.ValueKind != JsonValueKind.Array) continue;
            int childIndex = 0;
            foreach (var childId in ReadChildIds(children))
            {
                var child = GetSlide(childId);
                if (child.ValueKind != JsonValueKind.Object) { childIndex++; continue; }
                var childOverrides = Property(Property(rawParent, "children"), childId);
                result.Add(new LayoutEntry(result.Count, childId, IsTrue(childOverrides, "disabled"),
                    childOverrides, child, parentId, sourceIndex, childIndex, parentDisabled));
                childIndex++;
            }
        }
        return result.AsReadOnly();
    }

    public IReadOnlyList<string> ValidateLayout(string? layoutId = null)
    {
        var problems = new List<string>();
        string selected = layoutId ?? ActiveLayoutId;
        var layout = GetLayout(selected);
        var entries = Property(layout, "slides");
        if (entries.ValueKind != JsonValueKind.Array)
        {
            problems.Add("Missing layout: " + selected);
            return problems.AsReadOnly();
        }
        for (int sourceIndex = 0; sourceIndex < entries.GetArrayLength(); sourceIndex++)
        {
            var raw = entries[sourceIndex];
            string id = String(raw, "id");
            if (string.IsNullOrEmpty(id))
            {
                problems.Add("Layout position " + sourceIndex + " has no slide ID.");
                continue;
            }
            var parent = GetSlide(id);
            if (parent.ValueKind != JsonValueKind.Object)
            {
                problems.Add("Missing parent slide: " + id);
                continue;
            }
            var children = Property(parent, "children");
            if (children.ValueKind != JsonValueKind.Array) continue;
            foreach (var childId in ReadChildIds(children))
                if (GetSlide(childId).ValueKind != JsonValueKind.Object)
                    problems.Add("Missing child slide: " + childId + " (parent " + id + ")");
        }
        return problems.AsReadOnly();
    }

    private static IEnumerable<string> ReadChildIds(JsonElement children)
    {
        foreach (var child in children.EnumerateArray())
        {
            if (child.ValueKind == JsonValueKind.String)
            {
                var id = child.GetString();
                if (!string.IsNullOrEmpty(id)) yield return id;
            }
            else if (child.ValueKind == JsonValueKind.Array)
            {
                // FreeShow's ref() accepts an older nested-array child representation.
                foreach (var nested in ReadChildIds(child)) yield return nested;
            }
        }
    }

    public static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result)
            ? result : default;

    public static string String(JsonElement value, string name)
    {
        var item = Property(value, name);
        return item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "";
    }

    public static bool IsTrue(JsonElement value, string name) =>
        Property(value, name).ValueKind == JsonValueKind.True;
}

public sealed record LayoutEntry(
    int Index, string SlideId, bool Disabled, JsonElement RawEntry, JsonElement Slide,
    string? ParentSlideId = null, int SourceIndex = 0, int ChildIndex = -1, bool ParentDisabled = false)
{
    public bool IsChild => ParentSlideId != null;
    public bool EffectiveDisabled => Disabled || ParentDisabled;
    public string Group => ShowDocument.String(Slide, "group");
    public JsonElement Items => ShowDocument.Property(Slide, "items");
    public JsonElement Children => ShowDocument.Property(Slide, "children");
    public bool MissingSlide => Slide.ValueKind != JsonValueKind.Object;
}
