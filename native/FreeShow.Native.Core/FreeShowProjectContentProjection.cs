using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Read-only source projection of ProjectContentList.svelte's
/// splitProjectItemsToSections() and openProjectItem() ordering.
/// Original project shows indices are authoritative; synthetic dividers
/// do not consume an index. No original JSON is mutated.
/// </summary>
public static class FreeShowProjectContentProjection
{
    public static FreeShowProjectContent Prepare(FreeShowProjectNode project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.IsFolder)
            throw new ArgumentException("ProjectContentList requires a project, not a folder.", nameof(project));

        var source = ShowDocument.Property(project.Raw, "shows");
        var warnings = new List<string>();
        var rows = new List<FreeShowProjectContentItem>();
        if (source.ValueKind != JsonValueKind.Array)
            return new(project.Id, project.Name, [], ["Project contains no valid shows array."]);

        for (int index = 0; index < source.GetArrayLength(); index++)
        {
            var raw = source[index];
            if (raw.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Non-object project item at index " + index + " retained in source only.");
                continue;
            }

            string type = ShowDocument.String(raw, "type");
            if (type.Length == 0) type = "show";
            string id = ShowDocument.String(raw, "id");
            string name = ShowDocument.String(raw, "name");
            string color = ShowDocument.String(raw, "color");
            string notes = ShowDocument.String(raw, "notes");
            string layout = ShowDocument.String(raw, "layout");
            rows.Add(new(index, id, type, name, color, notes, layout, raw.Clone()));
        }

        var groups = new List<FreeShowProjectSectionGroup>();
        var items = new List<FreeShowProjectContentRow>();
        string currentColor = "";
        for (int pos = 0; pos < rows.Count; pos++)
        {
            var entry = rows[pos];
            string prev = pos > 0 ? NormalizeType(rows[pos - 1].Type) : "";
            string current = NormalizeType(entry.Type);
            string next = pos + 1 < rows.Count ? NormalizeType(rows[pos + 1].Type) : "";

            bool split = current == "section" &&
                (entry.Color.Length > 0 || prev == "section" || next == "section");
            bool typeChanged = current != "section" && prev != "section" &&
                (pos < 2 || rows[pos - 2].Type != "section") && current != prev;
            bool newSection = groups.Count == 0 && items.Count == 0 ||
                split || (typeChanged && currentColor.Length == 0);

            if (newSection)
            {
                if (items.Count != 0)
                    groups.Add(new(groups.Count, currentColor, items.ToArray()));
                currentColor = entry.Color;
                items.Clear();
            }
            else if (typeChanged && currentColor.Length != 0)
            {
                // Original synthetic DIVIDER inserted only in the UI.
                items.Add(new(-1, null, true));
            }
            items.Add(new(entry.SourceIndex, entry, false));
        }
        if (items.Count > 0) groups.Add(new(groups.Count, currentColor, items.ToArray()));

        return new(project.Id, project.Name, groups.AsReadOnly(), warnings.AsReadOnly());
    }

    private static string NormalizeType(string type) =>
        type is "image" or "video" ? "image" : type;
}

public sealed record FreeShowProjectContentItem(
    int SourceIndex, string Id, string Type, string Name,
    string Color, string Notes, string LayoutId, JsonElement Raw)
{
    public bool IsSection => Type == "section";
    public bool IsMedia => Type is "image" or "video" or "audio";
    public bool IsShow => Type == "show";
}

public sealed record FreeShowProjectContentRow(
    int SourceIndex, FreeShowProjectContentItem? Item, bool SyntheticDivider);

public sealed record FreeShowProjectSectionGroup(
    int GroupIndex, string Color, IReadOnlyList<FreeShowProjectContentRow> Rows)
{
    public bool BeginsWithSection => Rows.Count > 0 && Rows[0].Item?.IsSection == true;
}

public sealed record FreeShowProjectContent(
    string ProjectId, string ProjectName,
    IReadOnlyList<FreeShowProjectSectionGroup> Groups,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<FreeShowProjectContentItem> Items =>
        Groups.SelectMany(g => g.Rows).Where(r => !r.SyntheticDivider && r.Item != null)
            .Select(r => r.Item!).ToArray();

    public FreeShowProjectContentItem? ItemAtOriginalIndex(int index) =>
        Items.FirstOrDefault(item => item.SourceIndex == index);

    /// <summary>
    /// Original openProjectItem(id,index=0) chooses the first source item,
    /// even if it is a section/media/placeholder. We retain that selection
    /// without treating it as a live-playback command.
    /// </summary>
    public FreeShowProjectContentItem? FirstSourceItem() => ItemAtOriginalIndex(0);
}
