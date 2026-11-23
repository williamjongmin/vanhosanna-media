using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FreeShow.Native.Core;

namespace FreeShow.Native.Operator;

/// <summary>
/// Source Preview.svelte region composition: MultiOutputs / ShowActions slot /
/// ClearButtons / selected detail slot. Read-only by default. Original output
/// controls remain disabled until their complete source actions are ported.
/// </summary>
public sealed class FreeShowPreviewRegion : UserControl
{
    private readonly FreeShowOutputConfiguration settings;
    private readonly FreeShowMultiOutputPreview multiOutputs;
    private readonly FreeShowClearButtons clears;
    private readonly FreeShowOutputCommandCoordinator? coordinator;
    private readonly ContentControl detailHost = new();

    public FreeShowClearButtons ClearButtons => clears;
    public FreeShowMultiOutputPreview Outputs => multiOutputs;
    public string? SelectedDetailCategory => clears.SelectedCategory;

    /// <summary>
    /// These slots are for actual original source feature ports, not arbitrary
    /// substitute controls. All remain empty in the read-only operator probe.
    /// </summary>
    public ContentControl ShowActionsHost { get; } = new();
    public ContentControl SelectedDetailHost => detailHost;

    public FreeShowPreviewRegion(FreeShowOutputConfiguration source,
        ShowDocument? currentShow = null,
        JsonElement originalOverlayDefinitions = default,
        FreeShowAuxiliaryPreviewState? runtime = null,
        FreeShowOutputCommandCoordinator? outputController = null)
    {
        settings = source ?? throw new ArgumentNullException(nameof(source));
        coordinator = outputController;
        multiOutputs = new FreeShowMultiOutputPreview(settings, currentShow);
        clears = new FreeShowClearButtons(coordinator?.CurrentPreview(runtime) ??
            FreeShowPreviewControlState.Prepare(settings, originalOverlayDefinitions, runtime));
        if (coordinator != null)
        {
            // The internal OperatorProbe supplies no physical runtime, so
            // the UI remains read-only. When a complete runtime is installed,
            // the actual source action can run through one guarded path.
            clears.BindGuardedCoordinator(coordinator);
        }
        clears.CategorySelected += (_, _) =>
        {
            // The original preview switches detail sections. No fake media,
            // audio or overlays controllers are rendered until actually ported.
            detailHost.Content = null;
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto")
        };
        var clearSection = new Border
        {
            Margin = new Thickness(5, 2),
            CornerRadius = new CornerRadius(10),
            BorderBrush = Brushes.DimGray,
            BorderThickness = new Thickness(1),
            Child = clears
        };
        Grid.SetRow(multiOutputs, 0);
        Grid.SetRow(ShowActionsHost, 1);
        Grid.SetRow(clearSection, 2);
        Grid.SetRow(detailHost, 3);
        layout.Children.Add(multiOutputs);
        layout.Children.Add(ShowActionsHost);
        layout.Children.Add(clearSection);
        layout.Children.Add(detailHost);
        Content = new ScrollViewer
        {
            Content = layout,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
    }

    public void SetLanguage(string originalLocale) =>
        clears.SetLanguage(originalLocale);

    /// <summary>
    /// Display the original Restore control when a complete original
    /// outputCache snapshot is supplied. The internal read-only probe never
    /// supplies a live restore backend, so Restore stays disabled.
    /// </summary>
    public void BindRestoreCache(
        FreeShowOutputRestoreCache cache,
        DateTimeOffset savedAt,
        IFreeShowOutputRestoreRuntime? completeRuntime = null,
        Func<DateTimeOffset>? clock = null) =>
        clears.BindRestore(cache, savedAt, completeRuntime, clock);

    public void UpdateClearState(JsonElement originalOverlayDefinitions = default,
        FreeShowAuxiliaryPreviewState? runtime = null)
    {
        clears.Refresh(coordinator?.CurrentPreview(runtime) ??
            FreeShowPreviewControlState.Prepare(
                settings, originalOverlayDefinitions, runtime));
    }
}
