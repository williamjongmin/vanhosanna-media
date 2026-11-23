using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeShow.Native.Core;

namespace FreeShow.Native.Operator;

/// <summary>
/// Read-only FreeShow ClearButtons.svelte subset. Original icon paths, order,
/// group structure, keyboard hints, selected section and disabled/locked
/// indicators are preserved. No live clear action is dispatched until the
/// complete source action controller (timers/audio/locked overlays/cache) is
/// installed. Category expansion is non-destructive.
/// </summary>
public sealed class FreeShowClearButtons : UserControl
{
    private FreeShowPreviewSnapshot state;
    private readonly FreeShowPreviewCategoryNavigation navigation = new();
    private readonly Dictionary<string, Button> clearButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> expandButtons = new(StringComparer.Ordinal);
    private readonly StackPanel root = new() { Orientation = Orientation.Vertical, Spacing = 0 };
    private readonly Dictionary<string, Action> sourceCompatibleActions = new(StringComparer.Ordinal);
    private bool actualOutputControllerInstalled;

    public event EventHandler<FreeShowPreviewCategoryEventArgs>? CategorySelected;
    public string? SelectedCategory => navigation.SelectedCategory;

    public FreeShowClearButtons(FreeShowPreviewSnapshot source)
    {
        state = source ?? throw new ArgumentNullException(nameof(source));
        navigation.Update(state);
        Content = root;
        Rebuild();
    }

    public void Refresh(FreeShowPreviewSnapshot updated)
    {
        state = updated ?? throw new ArgumentNullException(nameof(updated));
        navigation.Update(state);
        Rebuild();
    }

    /// <summary>
    /// Explicit opt-in contract for future complete original FreeShow clear
    /// implementations. Merely having the native OutData mutation API does NOT
    /// qualify: clearAll, audio, actions and timers have other dependencies.
    /// A developer preview never calls this.
    /// </summary>
    public void InstallCompleteSourceActions(IReadOnlyDictionary<string, Action> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        sourceCompatibleActions.Clear();
        foreach (var (key, action) in actions)
        {
            if (action != null) sourceCompatibleActions[key] = action;
        }
        actualOutputControllerInstalled = true;
        Rebuild();
    }

    public void RemoveSourceActions()
    {
        sourceCompatibleActions.Clear();
        actualOutputControllerInstalled = false;
        Rebuild();
    }

    private void Rebuild()
    {
        clearButtons.Clear();
        expandButtons.Clear();
        root.Children.Clear();

        var scene = state.Control("scene");
        if (scene.Presence == PreviewLayerPresence.Active)
        {
            root.Children.Add(MakeRow("scene", expanded: false, fullWidth: true));
        }

        root.Children.Add(MakeRow("all", expanded: false, fullWidth: true));

        var activeGroup = new WrapPanel
        {
            Orientation = Orientation.Horizontal
        };
        foreach (var id in new[] {"background", "slide", "overlays", "audio", "nextTimer"})
        {
            activeGroup.Children.Add(MakeRow(id, expanded: true));
        }
        root.Children.Add(activeGroup);
    }

    private Control MakeRow(string id, bool expanded, bool fullWidth = false)
    {
        var entry = state.Control(id);
        var col = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 0
        };

        var button = new Button
        {
            Content = CreateMainButtonContent(entry, fullWidth),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(fullWidth ? 10 : 8, 5),
            Height = fullWidth ? 36 : 34,
            MinWidth = fullWidth ? 0 : 42,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = CanActuallyClear(entry),
            Opacity = entry.Presence == PreviewLayerPresence.Cleared ? 0.4
                : entry.Presence == PreviewLayerPresence.Unknown ? 0.55 : 1.0
        };
        ToolTip.SetTip(button, entry.SourceTranslationId +
            (entry.KeyboardHint.Length > 0 ? " [" + entry.KeyboardHint + "]" : "") +
            (!button.IsEnabled ? " — controller not installed or output locked" : ""));
        button.Click += (_, _) =>
        {
            if (!CanActuallyClear(entry)) return;
            sourceCompatibleActions[entry.Id]();
        };
        clearButtons[id] = button;
        col.Children.Add(button);

        if (expanded && entry.Presence != PreviewLayerPresence.Cleared)
        {
            var expander = new Button
            {
                Content = FreeShowOriginalIconGeometry.Create(
                    "expand", Brushes.White, 12),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsEnabled = true,
                Height = 17,
                MinWidth = 42,
                Padding = new Thickness(1),
                Opacity = SelectedCategory == id ? 1 : 0.5
            };
            ToolTip.SetTip(expander, "preview." + id);
            expander.Click += (_, _) =>
            {
                navigation.Select(id);
                Rebuild();
                CategorySelected?.Invoke(this,
                    new FreeShowPreviewCategoryEventArgs(SelectedCategory));
            };
            expandButtons[id] = expander;
            col.Children.Add(expander);
        }
        return col;
    }

    private static Control CreateMainButtonContent(FreeShowPreviewControl entry, bool text)
    {
        var group = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        group.Children.Add(FreeShowOriginalIconGeometry.Create(
            entry.OriginalIconId, Brushes.White, 20));
        if (text)
            group.Children.Add(new TextBlock
            {
                Text = entry.Id is "all" ? "Clear all" : "Clear scene",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            });
        return group;
    }

    private bool CanActuallyClear(FreeShowPreviewControl entry) =>
        actualOutputControllerInstalled && !state.OutputLocked &&
        entry.SourceCanClear && sourceCompatibleActions.ContainsKey(entry.Id);
}

public sealed class FreeShowPreviewCategoryEventArgs : EventArgs
{
    public FreeShowPreviewCategoryEventArgs(string? category) => Category = category;
    public string? Category { get; }
}
