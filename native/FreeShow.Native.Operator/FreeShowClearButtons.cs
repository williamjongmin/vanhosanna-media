using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
    private FreeShowOutputSlidePositionCache? clearAllSlideCache;
    private IFreeShowClearAllRuntime? clearAllRuntime;
    private Func<FreeShowClearAllContext>? clearAllContext;
    private Func<string, ShowDocument?>? clearAllShowLookup;
    private Func<FreeShowAuxiliaryPreviewState?>? currentPlaybackState;
    private DispatcherTimer? restoreEnableTimer;
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
    public event EventHandler<FreeShowClearAllCompletedEventArgs>? ClearAllCompleted;
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

    /// <summary>
    /// The original "Clear all" button calls clearAll(true). This native
    /// binding is explicit and still requires the complete original backend
    /// for timeline, background, slide/timers, overlays and global audio.
    /// The internal read-only OperatorProbe never installs one.
    /// </summary>
    public void BindSourceCompleteClearAll(
        FreeShowOutputCommandCoordinator coordinator,
        FreeShowOutputRestoreCache cache,
        FreeShowOutputSlidePositionCache slideCache,
        IFreeShowClearAllRuntime completeRuntime,
        Func<FreeShowClearAllContext> sourceContext,
        Func<string, ShowDocument?>? showLookup = null,
        Func<FreeShowAuxiliaryPreviewState?>? playbackState = null)
    {
        guardedCoordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        restoreCache = cache ?? throw new ArgumentNullException(nameof(cache));
        clearAllSlideCache = slideCache ?? throw new ArgumentNullException(nameof(slideCache));
        clearAllRuntime = completeRuntime ?? throw new ArgumentNullException(nameof(completeRuntime));
        clearAllContext = sourceContext ?? throw new ArgumentNullException(nameof(sourceContext));
        clearAllShowLookup = showLookup;
        currentPlaybackState = playbackState;
        Rebuild();
    }

    public void RemoveSourceActions()
    {
        guardedCoordinator = null;
        restoreRuntime = null;
        clearAllRuntime = null;
        clearAllContext = null;
        clearAllShowLookup = null;
        clearAllSlideCache = null;
        currentPlaybackState = null;
        sourceCompatibleActions.Clear();
        actualOutputControllerInstalled = false;
        Rebuild();
    }

    private void Rebuild()
    {
        restoreEnableTimer?.Stop();
        restoreEnableTimer = null;
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
        root.Children.Add(showRestore ? MakeRestoreRow() : MakeClearAllRow());

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

    private Control MakeClearAllRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        row.Children.Add(FreeShowOriginalIconGeometry.Create("clear", Brushes.White, 20));
        row.Children.Add(new TextBlock
        {
            Text = Label("all"),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12
        });

        var context = clearAllContext?.Invoke();
        var plan = guardedCoordinator != null && context != null
            ? guardedCoordinator.PlanClearAll(
                context with { FromButton = true }, clearAllShowLookup != null)
            : null;
        bool ready = !commandRunning && state.Control("all").Presence ==
                PreviewLayerPresence.Active &&
            plan != null && clearAllRuntime != null &&
            guardedCoordinator!.CheckClearAllReadiness(plan, clearAllRuntime).Count == 0;

        var button = new Button
        {
            Content = row,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Height = 36,
            Padding = new Thickness(10, 5),
            IsEnabled = ready
        };
        ToolTip.SetTip(button, Label("all") +
            (ready ? "" : " — source-complete Clear All backend unavailable"));

        button.Click += async (_, _) =>
        {
            if (commandRunning || !button.IsEnabled || guardedCoordinator == null ||
                restoreCache == null || clearAllSlideCache == null ||
                clearAllRuntime == null || clearAllContext == null)
                return;

            commandRunning = true;
            button.IsEnabled = false;
            try
            {
                var source = clearAllContext() with { FromButton = true };
                var result = await guardedCoordinator.ClearAllAsync(
                    source, restoreCache, clearAllSlideCache,
                    clearAllRuntime, clearAllShowLookup);
                if (restoreCache.HasSnapshot)
                    restoreSavedAt = currentTime();
                state = guardedCoordinator.CurrentPreview(currentPlaybackState?.Invoke());
                navigation.Update(state);
                ClearAllCompleted?.Invoke(this,
                    new FreeShowClearAllCompletedEventArgs(result));
            }
            catch (Exception exception)
            {
                ClearAllCompleted?.Invoke(this,
                    new FreeShowClearAllCompletedEventArgs(new FreeShowClearAllResult(
                        FreeShowClearAllStatus.Failed, 0,
                        [exception.GetType().Name + ": " + exception.Message])));
            }
            finally { commandRunning = false; Rebuild(); }
        };
        return button;
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

        // Original Svelte enables Restore after 1000ms even when no other
        // UI store changes. Schedule a single UI-thread refresh at that
        // deadline. Do not schedule when no full restore backend exists.
        if (!ready && restoreRuntime != null && guardedCoordinator != null &&
            restoreCache?.HasCompleteAudioSnapshot == true &&
            state.Control("all").Presence == PreviewLayerPresence.Cleared)
        {
            var remaining = restoreSavedAt.AddSeconds(1) - currentTime();
            if (remaining > TimeSpan.Zero)
            {
                var timer = new DispatcherTimer { Interval = remaining };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (ReferenceEquals(restoreEnableTimer, timer))
                        restoreEnableTimer = null;
                    Rebuild();
                };
                restoreEnableTimer = timer;
                timer.Start();
            }
        }
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

public sealed class FreeShowClearAllCompletedEventArgs : EventArgs
{
    public FreeShowClearAllCompletedEventArgs(FreeShowClearAllResult result)
        => Result = result;
    public FreeShowClearAllResult Result { get; }
}
