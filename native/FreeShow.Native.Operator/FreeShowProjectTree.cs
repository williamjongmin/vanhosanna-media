using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeShow.Native.Core;

namespace FreeShow.Native.Operator;

/// <summary>
/// Source-derived read-only project explorer for Projects.svelte and
/// ProjectList.svelte, using original projects.json folder IDs and ordering.
/// Selecting a project only selects library context; it never starts an output.
/// Creation, dragging, archive editing and provider sync are not yet ported.
/// </summary>
public sealed class FreeShowProjectTree : UserControl
{
    private readonly FreeShowProjectLibrary library;
    private readonly StackPanel rows = new() { Spacing = 5, Margin = new Thickness(0, 10, 5, 0) };
    private readonly HashSet<string> openedFolders = new(StringComparer.Ordinal);
    private string? selectedProjectId;

    public event EventHandler<FreeShowProjectSelectedEventArgs>? ProjectSelected;

    public FreeShowProjectTree(FreeShowProjectLibrary source)
    {
        library = source ?? throw new ArgumentNullException(nameof(source));
        Content = new ScrollViewer
        {
            Content = rows,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        Redraw();
    }

    public string? SelectedProjectId => selectedProjectId;
    public IReadOnlyCollection<string> OpenedFolderIds => openedFolders;

    public void SetOpenedFolders(IEnumerable<string> folderIds)
    {
        ArgumentNullException.ThrowIfNull(folderIds);
        openedFolders.Clear();
        foreach (var id in folderIds)
            if (library.Find(id) is { IsFolder: true })
                openedFolders.Add(id);
        Redraw();
    }

    public void SelectProject(string? id)
    {
        if (id != null && library.Find(id) is not { IsFolder: false })
            throw new ArgumentException("Unknown original FreeShow project ID.", nameof(id));
        selectedProjectId = id;
        Redraw();
        if (id != null) ProjectSelected?.Invoke(this, new FreeShowProjectSelectedEventArgs(id));
    }

    private void ToggleFolder(string id)
    {
        if (!openedFolders.Add(id)) openedFolders.Remove(id);
        Redraw();
    }

    private void Redraw()
    {
        rows.Children.Clear();
        foreach (var item in library.Tree)
        {
            // Match ProjectList.svelte checkIfShown(): hidden under closed ancestors.
            var currentParentId = item.ParentId;
            bool visible = true;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (library.Find(currentParentId) is { IsFolder: true } ancestor)
            {
                if (!visited.Add(ancestor.Id) || !openedFolders.Contains(ancestor.Id))
                {
                    visible = false;
                    break;
                }
                currentParentId = ancestor.ParentId;
            }
            if (!visible) continue;

            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = item.Id == selectedProjectId
                    ? Brushes.DimGray : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, item.IsFolder ? 5 : 2),
                Margin = new Thickness(8 * item.Depth, 0, 0, 0),
                Opacity = item.Archived ? 0.5 : 1.0,
                Content = new TextBlock
                {
                    Text = item.IsFolder
                        ? (openedFolders.Contains(item.Id) ? "▾ " : "▸ ") + item.Name
                        : item.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontWeight = item.IsFolder ? FontWeight.SemiBold : FontWeight.Normal
                }
            };
            var id = item.Id;
            if (item.IsFolder) button.Click += (_, _) => ToggleFolder(id);
            else button.Click += (_, _) => SelectProject(id);
            rows.Children.Add(button);
        }
    }
}

public sealed class FreeShowProjectSelectedEventArgs : EventArgs
{
    public FreeShowProjectSelectedEventArgs(string projectId) => ProjectId = projectId;
    public string ProjectId { get; }
}
