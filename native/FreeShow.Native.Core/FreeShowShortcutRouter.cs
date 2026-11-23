namespace FreeShow.Native.Core;

/// <summary>
/// Source-based command routing, not a new FreeShow keyboard map.
/// src/frontend/utils/shortcuts.ts and
/// src/frontend/components/helpers/OutputHelper.ts are authoritative.
/// This class emits semantic commands; editor focus, output event dispatch,
/// audio playback and project navigation are implemented by their future
/// native controller components, not fabricated here.
/// </summary>
public sealed class FreeShowShortcutRouter
{
    // Original OutputHelper.ts suppresses duplicate advances arriving
    // from DIFFERENT input sources within 80 ms.
    private const int DiffSourceDebounceMs = 80;
    private const int DoubleF2Ms = 500;

    private DateTimeOffset? lastAdvanceAt;
    private NativeShortcutOrigin lastAdvanceOrigin;
    private DateTimeOffset? lastF2At;

    public IReadOnlyList<FreeShowShortcutAction> Route(
        NativeShortcutInput input,
        IReadOnlyList<string> activeOutputIds)
    {
        ArgumentNullException.ThrowIfNull(activeOutputIds);
        if (input.Alt && input.Key == "F4") return []; // Windows close shortcut.
        if (input.PopupOrGuideOpen || input.TypingInEditableControl || input.TimelineOwnsKey)
            return [];
        if (input.Ctrl || input.Meta || input.Alt)
            return []; // Contextual modified-key shortcuts are not mapped here.

        var ids = activeOutputIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        var now = input.EventAt;
        switch (input.Key)
        {
            case "F1":
                if (input.OutputLocked) return [];
                return ids.Select(x => new FreeShowShortcutAction(
                    NativeShortcutCommand.ClearBackground, x)).ToArray();
            case "F2":
                if (input.OutputLocked || input.Repeat || input.SelectionBlocksClear) return [];
                bool doublePressed = lastF2At.HasValue &&
                    (now - lastF2At.Value).TotalMilliseconds >= 0 &&
                    (now - lastF2At.Value).TotalMilliseconds < DoubleF2Ms;
                lastF2At = doublePressed ? null : now;
                // Clearing the output slide and clearing the output slide
                // cache are independent in FreeShow; double-F2 does both.
                return ids.Select(x => new FreeShowShortcutAction(
                    NativeShortcutCommand.ClearSlide, x, doublePressed)).ToArray();
            case "F3":
                if (input.OutputLocked) return [];
                return ids.SelectMany(x => new[]
                {
                    new FreeShowShortcutAction(NativeShortcutCommand.ClearOverlays, x),
                    new FreeShowShortcutAction(NativeShortcutCommand.ClearEffects, x)
                }).ToArray();
            case "F4":
                if (input.OutputLocked) return [];
                // Audio is global to FreeShow, not one player per output.
                return [new FreeShowShortcutAction(NativeShortcutCommand.ClearAudioAndPlaylist, "")];
            case "F5":
            case " ":
            case "ArrowRight":
            case "ArrowLeft":
            case "PageDown":
            case "PageUp":
            case "Home":
            case "End":
                if (input.OutputLocked) return [];
                if ((input.Key is "ArrowRight" or "ArrowLeft") && input.SlideEditorSelectionActive)
                    return [];
                if (lastAdvanceAt.HasValue && lastAdvanceOrigin != input.Origin &&
                    (now - lastAdvanceAt.Value).TotalMilliseconds >= 0 &&
                    (now - lastAdvanceAt.Value).TotalMilliseconds < DiffSourceDebounceMs)
                    return [];

                lastAdvanceAt = now;
                lastAdvanceOrigin = input.Origin;
                var command = input.Key switch
                {
                    "ArrowLeft" or "PageUp" => NativeShortcutCommand.Previous,
                    "Home" => NativeShortcutCommand.First,
                    "End" => NativeShortcutCommand.Last,
                    _ => NativeShortcutCommand.Next
                };
                return ids.Select(x => new FreeShowShortcutAction(command, x)).ToArray();
            default:
                return []; // Unmapped or UI-specific keys are not hijacked.
        }
    }
}

public enum NativeShortcutOrigin { Keyboard, Action, ExternalController, OutputWindow }
public enum NativeShortcutCommand
{
    Next, Previous, First, Last,
    ClearSlide, ClearBackground, ClearOverlays, ClearEffects, ClearAudioAndPlaylist
}

public sealed record NativeShortcutInput(
    string Key,
    DateTimeOffset EventAt,
    NativeShortcutOrigin Origin = NativeShortcutOrigin.Keyboard,
    bool Ctrl = false,
    bool Meta = false,
    bool Alt = false,
    bool Repeat = false,
    bool OutputLocked = false,
    bool SelectionBlocksClear = false,
    bool PopupOrGuideOpen = false,
    bool TypingInEditableControl = false,
    bool TimelineOwnsKey = false,
    bool SlideEditorSelectionActive = false);

public sealed record FreeShowShortcutAction(
    NativeShortcutCommand Command, string OutputId, bool ClearCachedSlidePosition = false);
