using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Source-mapped layout dimensions from FreeShow MainLayout.svelte,
/// Resizeable.svelte, Drawer.svelte, Top.svelte and utils/common.ts.
/// Defaults are exact SOURCE constants; this does not yet port visual styling.
/// </summary>
public sealed record FreeShowOperatorWorkspaceState(
    double LeftPanelWidth,
    double RightPanelWidth,
    double DrawerHeight,
    string Page,
    bool FocusMode,
    bool DrawerOpenedInEdit,
    double LeftDrawerPanelWidth,
    double RightDrawerPanelWidth,
    JsonElement RawSettings)
{
    public const double DefaultPanelWidth = 290; // DEFAULT_WIDTH
    public const double DefaultDrawerHeight = 300; // DEFAULT_DRAWER_HEIGHT
    public const double WindowsMenuBarHeight = 25; // MENU_BAR_HEIGHT
    public const double TopBarHeight = 40; // Top.svelte
    public const double MinimumPanelWidth = 4; // Resizeable.svelte
    public const double MaximumPanelWidth = DefaultPanelWidth * 2.2; // Resizeable.svelte
    public const double MinimumDrawerHeight = 40; // Drawer.svelte
    public const double ExpandedRightPanelThreshold = DefaultPanelWidth * 1.8;

    // On Windows App.svelte keeps the 25px MenuBar even when MainLayout
    // hides Top in focus mode.
    public bool ShowMenuBar => true;
    public bool ShowTopBar => !FocusMode;
    public bool ShowDrawer => Page is "show" or "edit";
    public bool RightPanelIsHorizontal => RightPanelWidth > ExpandedRightPanelThreshold;
    public bool ShowShowTools => Page == "show" && !FocusMode;
    public bool ShowOutputPreview => true;

    public static FreeShowOperatorWorkspaceState FromLocalSettings(
        JsonElement settings, string page = "show", bool focusMode = false,
        bool drawerOpenedInEdit = false)
    {
        if (settings.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            throw new ArgumentException("Original FreeShow local SETTINGS must be an object.",
                nameof(settings));
        var raw = settings.ValueKind == JsonValueKind.Object ? settings.Clone() : default;
        var resized = ShowDocument.Property(raw, "resized");
        var drawer = ShowDocument.Property(raw, "drawer");

        return new FreeShowOperatorWorkspaceState(
            ReadWidth(resized, "leftPanel"),
            ReadWidth(resized, "rightPanel"),
            ReadDrawerHeight(drawer),
            ValidatePage(page), focusMode, drawerOpenedInEdit,
            ReadWidth(resized, "leftPanelDrawer"),
            ReadWidth(resized, "rightPanelDrawer"),
            raw);
    }

    public FreeShowOperatorWorkspaceState WithPage(string page)
        => this with { Page = ValidatePage(page) };

    public FreeShowOperatorWorkspaceState WithPanelWidths(double left, double right)
        => this with
        {
            LeftPanelWidth = ClampPanel(left),
            RightPanelWidth = ClampPanel(right)
        };

    public FreeShowOperatorWorkspaceState WithDrawerHeight(double height)
        => this with { DrawerHeight = ClampDrawer(height) };

    private static double ReadWidth(JsonElement source, string name)
    {
        var width = Number(source, name, DefaultPanelWidth);
        // Resizeable.svelte reopens left/right panels that were collapsed on
        // startup, while drawer panel collapses may remain collapsed.
        if (name is "leftPanel" or "rightPanel" && width <= MinimumPanelWidth)
            return DefaultPanelWidth;
        return ClampPanel(width);
    }

    private static double ReadDrawerHeight(JsonElement source)
        => ClampDrawer(Number(source, "height", DefaultDrawerHeight));

    private static double Number(JsonElement source, string key, double fallback)
    {
        var value = ShowDocument.Property(source, key);
        return value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number : fallback;
    }

    private static double ClampPanel(double x) =>
        double.IsFinite(x) ? Math.Clamp(x, MinimumPanelWidth, MaximumPanelWidth)
        : DefaultPanelWidth;

    private static double ClampDrawer(double x) =>
        double.IsFinite(x) ? Math.Max(x, MinimumDrawerHeight)
        : DefaultDrawerHeight;

    private static string ValidatePage(string page) =>
        page is "show" or "edit" or "stage" or "draw" or "settings" or "reflow" or "calendar"
        ? page : throw new ArgumentException("Not an original FreeShow TopViews page.", nameof(page));
}
