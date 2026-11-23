using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeShow.Native.Core;

namespace FreeShow.Native.Operator;

/// <summary>
/// Native Avalonia REGION HOSTS that follow original FreeShow MainLayout.svelte.
/// It is not a redesigned application window, and makes no claim of pixel parity:
/// every original component must be ported into its matching host (including
/// original theme, icons, accessibility and event handling) before release.
/// No placeholder buttons or fabricated FreeShow behavior are rendered.
/// </summary>
public sealed class FreeShowOperatorWorkspace : UserControl
{
    private readonly Grid shell = new();
    private readonly Grid main = new();
    private readonly Grid drawer = new();
    private readonly Grid drawerColumns = new();
    private readonly Grid rightColumn = new();
    private readonly GridSplitter leftSplitter = new()
    {
        Width = 4, Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly GridSplitter rightSplitter = new()
    {
        Width = 4, Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly GridSplitter drawerLeftSplitter = new()
    {
        Width = 4, Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly GridSplitter drawerRightSplitter = new()
    {
        Width = 4, Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly GridSplitter rightInnerSplitter = new()
    {
        Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };

    private FreeShowOperatorWorkspaceState state;
    private readonly Dictionary<string, FreeShowPageRegions> portedPageRegions = new(
        StringComparer.Ordinal);

    // Exact hierarchy from App.svelte -> MenuBar and MainLayout.svelte:
    // Top; [Resizeable(left) | center | Resizeable(right)]; project timeline;
    // Drawer [Navigation | Content | right]; Tipbar.
    // Content is always supplied by the ported original feature, never a
    // substitute "native UI" or a fake functional preview.
    public ContentControl MenuBarHost { get; } = new();
    public ContentControl TopHost { get; } = new();
    public ContentControl LeftNavigationHost { get; } = new();
    public ContentControl CenterPageHost { get; } = new();
    public ContentControl RightPreviewHost { get; } = new();
    public ContentControl RightToolsHost { get; } = new();
    public ContentControl ProjectTimelineHost { get; } = new();
    public ContentControl DrawerHeaderHost { get; } = new();
    public ContentControl DrawerNavigationHost { get; } = new();
    public ContentControl DrawerContentHost { get; } = new();
    public ContentControl DrawerToolsHost { get; } = new();
    public ContentControl TipbarHost { get; } = new();

    private FreeShowTopBar? installedTopBar;

    /// <summary>
    /// Install the actual original-structure FreeShow top controls.
    /// Only registered native page components are selectable; an unported
    /// Editor/Stage/Settings screen is never silently substituted.
    /// </summary>
    public void InstallTopBar(FreeShowTopBar top)
    {
        ArgumentNullException.ThrowIfNull(top);
        if (installedTopBar != null)
            installedTopBar.PageRequested -= HandleTopNavigation;
        installedTopBar = top;
        top.CanOpenPage = page => portedPageRegions.ContainsKey(page);
        top.PageRequested += HandleTopNavigation;
        top.SetCurrentPage(state.Page);
        TopHost.Content = top;
    }

    private void HandleTopNavigation(object? sender, FreeShowPageRequestedEventArgs e)
    {
        if (portedPageRegions.ContainsKey(e.PageId))
            ChangePage(e.PageId);
    }

    public FreeShowOperatorWorkspaceState State => state;
    public bool ProjectTimelineVisible { get; private set; }

    public FreeShowOperatorWorkspace(FreeShowOperatorWorkspaceState initialState)
    {
        state = initialState ?? throw new ArgumentNullException(nameof(initialState));
        Build();
        ApplyState(initialState);
    }

    /// <summary>
    /// This changes layout/slots only. It never mutates SETTINGS, never writes
    /// output files, and never calls an unported FreeShow feature.
    /// </summary>
    public void ApplyState(FreeShowOperatorWorkspaceState next)
    {
        state = next ?? throw new ArgumentNullException(nameof(next));
        SetColumn(main, 0, state.LeftPanelWidth);
        SetColumn(main, 4, state.RightPanelWidth);
        SetColumn(drawerColumns, 0, state.LeftDrawerPanelWidth);
        SetColumn(drawerColumns, 4, state.RightDrawerPanelWidth);
        SetRow(shell, 0, state.ShowMenuBar ? FreeShowOperatorWorkspaceState.WindowsMenuBarHeight : 0);
        SetRow(shell, 1, state.ShowTopBar ? FreeShowOperatorWorkspaceState.TopBarHeight : 0);
        SetRow(shell, 3, ProjectTimelineVisible && state.Page == "show" ? 290 : 0);
        SetRow(shell, 4, state.ShowDrawer ? state.DrawerHeight : 0);

        MenuBarHost.IsVisible = state.ShowMenuBar;
        TopHost.IsVisible = state.ShowTopBar;
        drawer.IsVisible = state.ShowDrawer;
        ProjectTimelineHost.IsVisible = ProjectTimelineVisible && state.Page == "show";
        ConfigureRightPanel(state.RightPanelIsHorizontal);
        RefreshPageRegions();
        installedTopBar?.SetCurrentPage(state.Page);
        installedTopBar?.RefreshPageAvailability();
    }

    /// <summary>
    /// Register *actual* ported FreeShow components for the page. Original
    /// MainLayout.svelte maps each page to these three slots; this class
    /// does not fabricate screens for pages that have not yet been ported.
    /// </summary>
    public void RegisterPageRegions(string page, FreeShowPageRegions content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (page is not ("show" or "edit" or "stage" or "draw" or "settings"))
            throw new ArgumentException(
                "Page is not mapped by original FreeShow MainLayout.svelte.", nameof(page));
        portedPageRegions[page] = content;
        installedTopBar?.RefreshPageAvailability();
        if (state.Page == page) RefreshPageRegions();
    }

    public void ChangePage(string originalPageId) => ApplyState(state.WithPage(originalPageId));

    private void RefreshPageRegions()
    {
        if (!portedPageRegions.TryGetValue(state.Page, out var regions))
        {
            // An unported screen is *not* replaced with a new custom design.
            LeftNavigationHost.Content = null;
            CenterPageHost.Content = null;
            RightToolsHost.Content = null;
            return;
        }

        LeftNavigationHost.Content = regions.Left;
        CenterPageHost.Content = regions.Center;
        RightToolsHost.Content = regions.RightTools;
    }

    /// <summary>
    /// Original FreeShow displays the optional timeline only when the
    /// projectTimelineActive flag is set for the show page.
    /// </summary>
    public void SetProjectTimeline(bool visible)
    {
        ProjectTimelineVisible = visible;
        ApplyState(state);
    }

    /// <summary>
    /// The original Svelte Resizeable component stores widths in "resized".
    /// Native drag resizing remains local until an explicit, verified settings
    /// implementation is added. No runtime Save call here.
    /// </summary>
    public FreeShowOperatorWorkspaceState SnapshotPanelGeometry() =>
        state.WithPanelWidths(main.ColumnDefinitions[0].ActualWidth,
                main.ColumnDefinitions[4].ActualWidth)
             with { LeftDrawerPanelWidth = drawerColumns.ColumnDefinitions[0].ActualWidth,
                    RightDrawerPanelWidth = drawerColumns.ColumnDefinitions[4].ActualWidth,
                    DrawerHeight = state.ShowDrawer ? shell.RowDefinitions[4].ActualHeight : state.DrawerHeight };

    private void Build()
    {
        shell.RowDefinitions = new RowDefinitions("25,40,*,0,300,Auto");
        main.ColumnDefinitions = new ColumnDefinitions("290,4,*,4,290");
        drawer.RowDefinitions = new RowDefinitions("Auto,*");
        drawerColumns.ColumnDefinitions = new ColumnDefinitions("290,4,*,4,290");

        Put(shell, MenuBarHost, row: 0);
        Put(shell, TopHost, row: 1);
        Put(shell, main, row: 2);
        Put(shell, ProjectTimelineHost, row: 3);
        Put(shell, drawer, row: 4);
        Put(shell, TipbarHost, row: 5);

        Put(main, LeftNavigationHost, column: 0);
        Put(main, leftSplitter, column: 1);
        Put(main, CenterPageHost, column: 2);
        Put(main, rightSplitter, column: 3);
        Put(main, rightColumn, column: 4);

        Put(drawer, DrawerHeaderHost, row: 0);
        Put(drawer, drawerColumns, row: 1);
        Put(drawerColumns, DrawerNavigationHost, column: 0);
        Put(drawerColumns, drawerLeftSplitter, column: 1);
        Put(drawerColumns, DrawerContentHost, column: 2);
        Put(drawerColumns, drawerRightSplitter, column: 3);
        Put(drawerColumns, DrawerToolsHost, column: 4);

        Content = shell;
    }

    private void ConfigureRightPanel(bool horizontal)
    {
        if (rightColumn.RowDefinitions.Count == (horizontal ? 1 : 3)
            && rightColumn.ColumnDefinitions.Count == (horizontal ? 3 : 1))
            return;

        rightColumn.Children.Clear();
        rightColumn.RowDefinitions = horizontal
            ? new RowDefinitions("*") : new RowDefinitions("*,4,*");
        rightColumn.ColumnDefinitions = horizontal
            ? new ColumnDefinitions("*,4,*") : new ColumnDefinitions("*");
        rightInnerSplitter.Width = horizontal ? 4 : double.NaN;
        rightInnerSplitter.Height = horizontal ? double.NaN : 4;

        if (horizontal)
        {
            // .right.row in MainLayout.svelte uses row-reverse: Tools first,
            // Preview second in visual order.
            Put(rightColumn, RightToolsHost, column: 0);
            Put(rightColumn, rightInnerSplitter, column: 1);
            Put(rightColumn, RightPreviewHost, column: 2);
        }
        else
        {
            Put(rightColumn, RightPreviewHost, row: 0);
            Put(rightColumn, rightInnerSplitter, row: 1);
            Put(rightColumn, RightToolsHost, row: 2);
        }
    }

    private static void Put(Grid grid, Control control, int row = 0, int column = 0)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static void SetColumn(Grid grid, int index, double width) =>
        grid.ColumnDefinitions[index].Width = new GridLength(width);

    private static void SetRow(Grid grid, int index, double height) =>
        grid.RowDefinitions[index].Height = new GridLength(height);
}


/// <summary>
/// Exact porting destinations for original MainLayout.svelte page branches:
/// show: Projects / Show / ShowTools
/// edit: Navigation / Editor / EditTools or specialized media tools
/// stage: StageLayouts / StageLayout / StageTools
/// draw: DrawTabs / Slide / DrawSettings
/// settings: SettingsTabs / Settings / SettingsTools
/// RightPreviewHost is shared and remains separate from RightTools.
/// </summary>
public sealed record FreeShowPageRegions(Control Left, Control Center, Control? RightTools);
