using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeShow.Native.Core;

namespace FreeShow.Native.Operator;

/// <summary>
/// Read-only port region for Projects.svelte and ProjectContentList.svelte.
/// The source project's sections and item indices are never modified.
/// Browsing does not broadcast, play, mutate timestamps, or edit files.
/// </summary>
public sealed class FreeShowProjectBrowser : UserControl
{
    private readonly FreeShowProjectLibrary library;
    private readonly FreeShowProjectNavigation navigation;
    private readonly FreeShowProjectTree tree;
    private readonly ContentControl body = new();
    private readonly HashSet<int> manuallyExpanded = new();
    private readonly HashSet<int> manuallyCollapsed = new();
    private FreeShowProjectContent? current;

    public event EventHandler<FreeShowProjectItemSelectedEventArgs>? ItemSelected;
    public string? ActiveProjectId => navigation.ActiveProjectId;
    public FreeShowProjectContentItem? ActiveItem => navigation.ActiveItem;

    public FreeShowProjectBrowser(FreeShowProjectLibrary source)
    {
        library = source ?? throw new ArgumentNullException(nameof(source));
        navigation = new FreeShowProjectNavigation(source);
        tree = new FreeShowProjectTree(source);
        tree.ProjectSelected += (_, args) => OpenProject(args.ProjectId);

        Content = body;
        body.Content = tree;
    }

    public void OpenProject(string projectId)
    {
        current = navigation.OpenProject(projectId);
        manuallyExpanded.Clear();
        manuallyCollapsed.Clear();
        DrawProjectItems();
        if (navigation.ActiveItem is { } selected)
            ItemSelected?.Invoke(this,
                new FreeShowProjectItemSelectedEventArgs(current.ProjectId, selected));
    }

    public void Back()
    {
        navigation.BackToProjects();
        current = null;
        body.Content = tree;
    }

    public void SelectSourceItem(int index)
    {
        if (current == null)
            throw new InvalidOperationException("Project content has not been opened.");
        var item = navigation.OpenItem(index);
        DrawProjectItems();
        // This is solely an operator selection event: original project.ts may
        // open editors and change the active layout; the live output action
        // path must not be silently substituted for it.
        ItemSelected?.Invoke(this, new FreeShowProjectItemSelectedEventArgs(
            current.ProjectId, item));
    }

    private void ToggleSection(int groupIndex, bool currentlyCollapsed)
    {
        if (currentlyCollapsed)
        {
            manuallyCollapsed.Remove(groupIndex);
            manuallyExpanded.Add(groupIndex);
        }
        else
        {
            manuallyExpanded.Remove(groupIndex);
            manuallyCollapsed.Add(groupIndex);
        }
        DrawProjectItems();
    }

    private bool IsCollapsed(FreeShowProjectSectionGroup section)
    {
        var raw = library.Find(current!.ProjectId)!.Raw;
        if (!ShowDocument.IsTrue(raw, "sectionsCollapsed") || !section.BeginsWithSection)
            return false;
        if (manuallyExpanded.Contains(section.GroupIndex)) return false;
        if (manuallyCollapsed.Contains(section.GroupIndex)) return true;
        return !section.Rows.Any(x => x.Item?.SourceIndex == navigation.ActiveItem?.SourceIndex);
    }

    private void DrawProjectItems()
    {
        if (current == null) return;

        var root = new Grid { RowDefinitions = new RowDefinitions("40,*") };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("42,*"),
            Height = 40
        };
        var back = new Button
        {
            Content = "‹",
            Padding = new Thickness(4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 24
        };
        back.Click += (_, _) => Back();
        header.Children.Add(back);
        var title = new TextBlock
        {
            Text = current.ProjectName,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        root.Children.Add(header);

        var list = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 5, 0) };
        foreach (var section in current.Groups)
        {
            var groupPanel = new StackPanel { Spacing = 0 };
            bool collapsed = IsCollapsed(section);
            foreach (var row in section.Rows)
            {
                if (row.SyntheticDivider)
                {
                    if (!collapsed)
                        groupPanel.Children.Add(new Border
                        {
                            Height = 1,
                            Background = Brushes.DimGray,
                            Margin = new Thickness(8, 5)
                        });
                    continue;
                }
                if (row.Item == null) continue;
                var item = row.Item;
                if (collapsed && item != section.Rows[0].Item) continue;

                var name = item.Name.Length > 0 ? item.Name :
                    (item.Id.Length > 0 ? item.Id : item.Type);
                var label = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8
                };
                if (item.IsSection)
                    label.Children.Add(new TextBlock
                    {
                        Text = collapsed ? "▸" : "▾",
                        VerticalAlignment = VerticalAlignment.Center
                    });
                label.Children.Add(new TextBlock
                {
                    Text = name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontSize = item.IsSection ? 13 : 12,
                    FontWeight = item.IsSection ? FontWeight.SemiBold : FontWeight.Normal,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var button = new Button
                {
                    Content = label,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(item.IsSection ? 12 : 14, item.IsSection ? 4 : 7),
                    Background = item.SourceIndex == navigation.ActiveItem?.SourceIndex
                        ? Brushes.DimGray : Brushes.Transparent
                };

                int capturedIndex = item.SourceIndex;
                int capturedGroup = section.GroupIndex;
                bool capturedCollapsed = collapsed;
                button.Click += (_, _) =>
                {
                    if (item.IsSection && ShowDocument.IsTrue(
                        library.Find(current.ProjectId)!.Raw, "sectionsCollapsed"))
                        ToggleSection(capturedGroup, capturedCollapsed);
                    SelectSourceItem(capturedIndex);
                };
                groupPanel.Children.Add(button);
                if (item.Notes.Length > 0 && !collapsed)
                    groupPanel.Children.Add(new TextBlock
                    {
                        Text = item.Notes,
                        Opacity = 0.5,
                        FontSize = 11,
                        Margin = new Thickness(18, 0, 6, 3),
                        TextWrapping = TextWrapping.Wrap
                    });
            }
            list.Children.Add(new Border
            {
                Child = groupPanel,
                CornerRadius = new CornerRadius(0, 10, 10, 0),
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(0, 1, 1, 1),
                Background = Brushes.Transparent
            });
        }

        var scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        body.Content = root;
    }
}

public sealed class FreeShowProjectItemSelectedEventArgs : EventArgs
{
    public string ProjectId { get; }
    public FreeShowProjectContentItem Item { get; }

    public FreeShowProjectItemSelectedEventArgs(
        string projectId, FreeShowProjectContentItem item)
    {
        ProjectId = projectId;
        Item = item;
    }
}
