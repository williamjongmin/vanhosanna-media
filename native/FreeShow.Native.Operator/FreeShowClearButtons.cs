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
    private FreeShowOutputCommandCoordinator? guardedCoordinator;
    private bool commandRunning;
    private FreeShowOutputRestoreCache? restoreCache;
    private IFreeShowOutputRestoreRuntime? restoreRuntime;
    private DateTimeOffset restoreSavedAt;
    private Func<DateTimeOffset> currentTime = () => DateTimeOffset.UtcNow;
    private string locale = "en";
    private static readonly IReadOnlyDictionary<string, string> EnglishLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["all"] = "Clear all", ["scene"] = "Clear scene",
            ["background"] = "Clear background", ["slide"] = "Clear slide",
            ["overlays"] = "Clear overlays", ["audio"] = "Clear audio",
            ["nextTimer"] = "Clear next slide timer",
            ["restore"] = "Restore output"
        };
    private static readonly IReadOnlyDictionary<string, string> KoreanLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["all"] = "모두 지우기", ["scene"] = "장면 지우기",
            ["background"] = "배경 지우기", ["slide"] = "슬라이드 지우기",
            ["overlays"] = "오버레이 지우기", ["audio"] = "소리 지우기",
            ["nextTimer"] = "다음 슬라이드 전환 타이머 지우기",
            ["restore"] = "출력 복원"
        };

    public event EventHandler<FreeShowPreviewCategoryEventArgs>? CategorySelected;
    public event EventHandler<FreeShowClearCommandCompletedEventArgs>? ClearCommandCompleted;
    public event EventHandler<FreeShowRestoreCompletedEventArgs>? RestoreCompleted;
    public string? SelectedCategory => navigation.SelectedCategory;

    public FreeShowClearButtons(FreeShowPreviewSnapshot source)
    {
        state = source ?? throw new ArgumentNullException(nameof(source));
        navigation.Update(state);
        Content = root;
        Rebuild();
    }

    public void SetLanguage(string language)
    {
        locale = language.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ? "ko" : "en";
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
        guardedCoordinator = null;
        sourceCompatibleActions.Clear();
        foreach (var (key, action) in actions)
        {
            if (action != null) sourceCompatibleActions[key] = action;
        }
        actualOutputControllerInstalled = true;
        Rebuild();
    }

    /// <summary>
    /// Attach the source-aware native command coordinator. It uses
    /// all-effect preflight before any physical operation; an empty or
    /// incomplete runtime registry keeps the original clear controls
    /// disabled. The developer OperatorProbe only uses this safe mode.
    /// </summary>
    public void BindGuardedCoordinator(FreeShowOutputCommandCoordinator controller)
    {
        guardedCoordinator = controller ?? throw new ArgumentNullException(nameof(controller));
        sourceCompatibleActions.Clear();
        actualOutputControllerInstalled = false;
        state = controller.CurrentPreview();
        navigation.Update(state);
        Rebuild();
    }

    /// <summary>
    /// Source ClearButtons.svelte: Restore replaces Clear All when outputs
    /// are cleared and outputCache exists. It is enabled only after 1000ms
    /// AND after a source-complete audio + physical output runtime is bound.
    /// No background timer or automatic restore is started.
    /// </summary>
    public void BindRestore(
        FreeShowOutputRestoreCache saved,
        DateTimeOffset savedAt,
        IFreeShowOutputRestoreRuntime? completeRuntime = null,
        Func<DateTimeOffset>? clock = null)
    {
        restoreCache = saved ?? throw new ArgumentNullException(nameof(saved));
        restoreSavedAt = savedAt;
        restoreRuntime = completeRuntime;
        currentTime = clock ?? (() => DateTimeOffset.UtcNow);
        Rebuild();
    }

    public void RemoveSourceActions()
    {
        guardedCoordinator = null;
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

        bool showRestore = restoreCache?.HasSnapshot == true &&
            state.Control("all").Presence == PreviewLayerPresence.Cleared;
        root.Children.Add(showRestore ? MakeRestoreRow() :
            MakeRow("all", expanded: false, fullWidth: true));

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

    private Control MakeRestoreRow()
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(FreeShowOriginalIconGeometry.Create(
            "reset", Brushes.White, 20));
        content.Children.Add(new TextBlock
        {
            Text = Label("restore"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });

        bool ready = guardedCoordinator != null && restoreRuntime != null &&
            restoreCache != null &&
            FreeShowRestoreEligibility.CanOffer(
                restoreCache, state, restoreSavedAt, currentTime());
        var button = new Button
        {
            Content = content,
            Height = 36,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 5),
            IsEnabled = ready && !commandRunning
        };
        ToolTip.SetTip(button, Label("restore") +
            (button.IsEnabled ? "" : " — source-complete restore backend unavailable"));
        button.Click += async (_, _) =>
        {
            if (!button.IsEnabled || commandRunning || guardedCoordinator == null ||
                restoreRuntime == null || restoreCache == null)
                return;
            commandRunning = true;
            button.IsEnabled = false;
            try
            {
                var result = await guardedCoordinator.RestoreAsync(
                    restoreCache, restoreRuntime);
                state = guardedCoordinator.CurrentPreview();
                navigation.Update(state);
                RestoreCompleted?.Invoke(this,
                    new FreeShowRestoreCompletedEventArgs(result));
            }
            catch (Exception exception)
            {
                RestoreCompleted?.Invoke(this,
                    new FreeShowRestoreCompletedEventArgs(new FreeShowRestoreResult(
                        FreeShowRestoreStatus.Failed, 0,
                        [exception.GetType().Name + ": " + exception.Message])));
            }
            finally
            {
                commandRunning = false;
                Rebuild();
            }
        };
        return button;
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
        ToolTip.SetTip(button, Label(entry.Id) +
            (entry.KeyboardHint.Length > 0 ? " [" + entry.KeyboardHint + "]" : "") +
            (!button.IsEnabled ? " — controller not installed or output locked" : ""));
        button.Click += async (_, _) =>
        {
            if (commandRunning || !CanActuallyClear(entry)) return;
            if (guardedCoordinator == null)
            {
                sourceCompatibleActions[entry.Id]();
                return;
            }

            // No fire-and-forget physical commands. Report runtime failures,
            // and update Preview only from acknowledged native output state.
            commandRunning = true;
            button.IsEnabled = false;
            try
            {
                var plans = guardedCoordinator.PlanPreviewClearAction(entry.Id);
                var result = await guardedCoordinator.DispatchPreparedAsync(plans);
                state = guardedCoordinator.CurrentPreview();
                navigation.Update(state);
                ClearCommandCompleted?.Invoke(this,
                    new FreeShowClearCommandCompletedEventArgs(entry.Id, result));
            }
            catch (Exception exception)
            {
                ClearCommandCompleted?.Invoke(this,
                    new FreeShowClearCommandCompletedEventArgs(entry.Id,
                        new FreeShowCommandBatchResult(
                            FreeShowCommandResultStatus.Failed, 0, 0,
                            [exception.GetType().Name + ": " + exception.Message])));
            }
            finally
            {
                commandRunning = false;
                Rebuild();
            }
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

    private Control CreateMainButtonContent(FreeShowPreviewControl entry, bool text)
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
                Text = Label(entry.Id),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            });
        return group;
    }

    private string Label(string category) =>
        (locale == "ko" ? KoreanLabels : EnglishLabels).TryGetValue(category, out var label)
            ? label : category;

    private bool CanActuallyClear(FreeShowPreviewControl entry)
    {
        if (commandRunning || state.OutputLocked || !entry.SourceCanClear) return false;
        if (guardedCoordinator != null)
        {
            var plans = guardedCoordinator.PlanPreviewClearAction(entry.Id);
            return plans.Count > 0 && guardedCoordinator.CheckReadiness(plans).Count == 0;
        }
        return actualOutputControllerInstalled &&
               sourceCompatibleActions.ContainsKey(entry.Id);
    }
}

public sealed class FreeShowPreviewCategoryEventArgs : EventArgs
{
    public FreeShowPreviewCategoryEventArgs(string? category) => Category = category;
    public string? Category { get; }
}

public sealed class FreeShowClearCommandCompletedEventArgs : EventArgs
{
    public FreeShowClearCommandCompletedEventArgs(string category,
        FreeShowCommandBatchResult result)
    {
        Category = category;
        Result = result;
    }
    public string Category { get; }
    public FreeShowCommandBatchResult Result { get; }
}

public sealed class FreeShowRestoreCompletedEventArgs : EventArgs
{
    public FreeShowRestoreCompletedEventArgs(FreeShowRestoreResult result)
        => Result = result;
    public FreeShowRestoreResult Result { get; }
}
