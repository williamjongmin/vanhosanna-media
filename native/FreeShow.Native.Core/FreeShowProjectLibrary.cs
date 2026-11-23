using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Source-based read-only Config/projects.json model. The original FreeShow
/// PROJECTS store contains {projects, folders, projectTemplates}; see
/// src/electron/data/store.ts and src/types/Projects.ts.
/// No project record or user data is rewritten or inferred from .show files.
/// </summary>
public sealed class FreeShowProjectLibrary
{
    public JsonElement Raw { get; }
    public IReadOnlyList<FreeShowProjectNode> Tree { get; }
    public IReadOnlyList<string> Warnings { get; }

    private FreeShowProjectLibrary(JsonElement raw, IReadOnlyList<FreeShowProjectNode> tree,
        IReadOnlyList<string> warnings)
    {
        Raw = raw;
        Tree = tree;
        Warnings = warnings;
    }

    public static async Task<FreeShowProjectLibrary> OpenAsync(
        string selectedFreeShowDataRoot, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(selectedFreeShowDataRoot, "Config", "projects.json");
        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException("Original FreeShow Config/projects.json not found.", path);
        if (file.Length > ShowDocument.MaxFileBytes)
            throw new InvalidDataException("Project library exceeds safe file read limit.");
        await using var input = File.OpenRead(path);
        using var parsed = await JsonDocument.ParseAsync(input,
            new JsonDocumentOptions { MaxDepth = 128 }, cancellationToken);
        return Parse(parsed.RootElement);
    }

    public static FreeShowProjectLibrary Parse(JsonElement original)
    {
        if (original.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("FreeShow projects.json root must be an object.");

        var folders = ShowDocument.Property(original, "folders");
        var projects = ShowDocument.Property(original, "projects");
        if (folders.ValueKind != JsonValueKind.Object || projects.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected original PROJECTS store's folders and projects objects.");

        var warnings = new List<string>();
        var all = new Dictionary<string, FreeShowProjectNode>(StringComparer.Ordinal);
        foreach (var entry in folders.EnumerateObject())
        {
            var data = entry.Value;
            if (data.ValueKind != JsonValueKind.Object || ShowDocument.IsTrue(data, "deleted"))
                continue;
            all[entry.Name] = Create(entry.Name, data, isFolder: true);
        }

        foreach (var entry in projects.EnumerateObject())
        {
            var data = entry.Value;
            if (data.ValueKind != JsonValueKind.Object || ShowDocument.IsTrue(data, "deleted"))
                continue;
            if (all.ContainsKey(entry.Name))
            {
                warnings.Add("Folder and project share an ID: " + entry.Name);
                continue;
            }
            all[entry.Name] = Create(entry.Name, data, isFolder: false);
        }

        // Projects.svelte places orphan items under root if their parent
        // folder has disappeared or is marked deleted; data is not edited.
        var normalized = all.Values.Select(n =>
        {
            bool validParent = n.ParentId == "/" ||
                all.TryGetValue(n.ParentId, out var parent) && parent.IsFolder;
            if (validParent) return n;
            warnings.Add("Missing parent folder for " + n.Id + "; displayed at root.");
            return n with { ParentId = "/" };
        }).ToArray();

        var folderMap = normalized.Where(n => n.IsFolder)
            .ToDictionary(n => n.Id, n => n, StringComparer.Ordinal);

        // Protect the native reader against malformed cyclic folder graphs.
        var cleaned = new List<FreeShowProjectNode>();
        foreach (var node in normalized)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            var ancestor = node.ParentId;
            bool cyclic = false;
            while (folderMap.TryGetValue(ancestor, out var folder))
            {
                if (!seen.Add(ancestor)) { cyclic = true; break; }
                ancestor = folder.ParentId;
            }
            if (cyclic)
            {
                warnings.Add("Project folder cycle detected at " + node.Id + "; displayed at root.");
                cleaned.Add(node with { ParentId = "/" });
            }
            else cleaned.Add(node);
        }

        // Original Projects.svelte sorts folders and projects separately,
        // groups archived projects last, then flattens children recursively.
        // JS localeCompare numeric sort may differ for uncommon locales.
        var foldersSorted = cleaned.Where(x => x.IsFolder)
            .OrderBy(x => x.Name, FreeShowOutputNameComparer.Instance).ToArray();
        var projectsSorted = cleaned.Where(x => !x.IsFolder)
            .OrderBy(x => x.Archived)
            .ThenBy(x => x.Name, FreeShowOutputNameComparer.Instance).ToArray();

        var tree = new List<FreeShowProjectNode>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Walk(string parentId, int depth)
        {
            if (depth > 64)
            {
                warnings.Add("Project tree depth exceeded 64.");
                return;
            }
            foreach (var item in foldersSorted.Concat(projectsSorted)
                         .Where(x => x.ParentId == parentId))
            {
                if (!visited.Add(item.Id)) continue;
                tree.Add(item with { Depth = depth });
                if (item.IsFolder) Walk(item.Id, depth + 1);
            }
        }
        Walk("/", 0);

        foreach (var node in cleaned)
        {
            if (visited.Contains(node.Id)) continue;
            warnings.Add("Unreachable project node displayed at root: " + node.Id);
            tree.Add(node with { ParentId = "/", Depth = 0 });
        }

        return new(original.Clone(), tree.AsReadOnly(), warnings.AsReadOnly());
    }

    public FreeShowProjectNode? Find(string id) =>
        Tree.FirstOrDefault(x => x.Id == id);

    private static FreeShowProjectNode Create(string id, JsonElement data, bool isFolder)
    {
        var parent = ShowDocument.String(data, "parent");
        var children = new List<FreeShowProjectShowRef>();
        if (!isFolder)
        {
            var shows = ShowDocument.Property(data, "shows");
            if (shows.ValueKind == JsonValueKind.Array)
                foreach (var show in shows.EnumerateArray())
                {
                    if (show.ValueKind != JsonValueKind.Object) continue;
                    var showId = ShowDocument.String(show, "id");
                    if (showId.Length == 0) continue;
                    children.Add(new(showId, ShowDocument.String(show, "name"),
                        ShowDocument.String(show, "type"), ShowDocument.String(show, "layout"),
                        show.Clone()));
                }
        }
        return new(id, ShowDocument.String(data, "name"),
            parent.Length == 0 ? "/" : parent, isFolder,
            ShowDocument.IsTrue(data, "archived"), children.AsReadOnly(), data.Clone(), 0);
    }
}

public sealed record FreeShowProjectNode(string Id, string Name, string ParentId,
    bool IsFolder, bool Archived, IReadOnlyList<FreeShowProjectShowRef> Shows,
    JsonElement Raw, int Depth);

public sealed record FreeShowProjectShowRef(string ShowId, string Name, string Type,
    string LayoutId, JsonElement Raw);
