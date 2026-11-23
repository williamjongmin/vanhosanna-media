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
        FreeShowAuxiliaryPreviewState? runtime = null)
    {
        settings = source ?? throw new ArgumentNullException(nameof(source));
        multiOutputs = new FreeShowMultiOutputPreview(settings, currentShow);
        clears = new FreeShowClearButtons(
            FreeShowPreviewControlState.Prepare(settings, originalOverlayDefinitions, runtime));
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

    public void UpdateClearState(JsonElement originalOverlayDefinitions = default,
        FreeShowAuxiliaryPreviewState? runtime = null)
    {
        clears.Refresh(FreeShowPreviewControlState.Prepare(
            settings, originalOverlayDefinitions, runtime));
    }
}
