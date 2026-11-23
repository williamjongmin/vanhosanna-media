namespace FreeShow.Native.Core;

/// <summary>
/// Original project.ts openProject/openProjectItem navigation, but local-only:
/// selecting a project selects its first source item by index unless the
/// previously selected item already belongs to that project. The original
/// side effects (last-used timestamps, editor changes, media playback) are
/// intentionally not performed in this read-only development preview.
/// </summary>
public sealed class FreeShowProjectNavigation
{
    private readonly FreeShowProjectLibrary library;
    public string? ActiveProjectId { get; private set; }
    public FreeShowProjectContentItem? ActiveItem { get; private set; }

    public FreeShowProjectNavigation(FreeShowProjectLibrary library) =>
        this.library = library ?? throw new ArgumentNullException(nameof(library));

    public FreeShowProjectContent OpenProject(string projectId, bool openFirstItem = true)
    {
        var project = library.Find(projectId);
        if (project == null || project.IsFolder)
            throw new ArgumentException("Original FreeShow project ID not found.", nameof(projectId));

        var content = FreeShowProjectContentProjection.Prepare(project);
        bool sameProject = ActiveProjectId == projectId;
        if (!sameProject)
        {
            ActiveProjectId = projectId;
            if (!content.Items.Any(x => x.Id == ActiveItem?.Id))
                ActiveItem = openFirstItem ? content.FirstSourceItem() : null;
        }
        return content;
    }

    public FreeShowProjectContentItem OpenItem(int originalIndex)
    {
        if (ActiveProjectId == null)
            throw new InvalidOperationException("Open a source project before selecting an item.");
        var project = library.Find(ActiveProjectId)!;
        var content = FreeShowProjectContentProjection.Prepare(project);
        var item = content.ItemAtOriginalIndex(originalIndex);
        if (item == null)
            throw new ArgumentOutOfRangeException(nameof(originalIndex), "Source project item missing.");
        ActiveItem = item;
        return item;
    }

    public void BackToProjects()
    {
        ActiveProjectId = null;
        ActiveItem = null;
    }
}
