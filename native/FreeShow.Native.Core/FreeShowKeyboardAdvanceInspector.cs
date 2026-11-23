namespace FreeShow.Native.Core;

/// <summary>
/// Compose the already-source-derived FreeShowShortcutRouter with the
/// independent per-output / project read-only navigators. This is the
/// inspect-only equivalent of OutputHelper.advanceOutputs(); it never
/// installs a keyboard listener or calls physical output ShowCue().
/// </summary>
public sealed class FreeShowKeyboardAdvanceInspector
{
    private readonly FreeShowShortcutRouter sourceRouter = new();

    public FreeShowMultiOutputAdvanceResult? Inspect(
        NativeShortcutInput originalKeyboardEvent,
        FreeShowOutputConfiguration outputs,
        IReadOnlyDictionary<string, FreeShowOutputState> currentStates,
        FreeShowNavigationSourceContext sourceContext,
        Func<string, ShowDocument?> knownShowLookup,
        FreeShowProjectContent? project = null,
        FreeShowProjectContentItem? activeItem = null,
        bool focusMode = false,
        bool nextItemOnLastSlide = true)
    {
        ArgumentNullException.ThrowIfNull(originalKeyboardEvent);
        ArgumentNullException.ThrowIfNull(outputs);

        var active = outputs.GetActiveNormalOutputs().Select(x => x.Id).ToArray();
        var guarded = originalKeyboardEvent with
        {
            OutputLocked = originalKeyboardEvent.OutputLocked ||
                ShowDocument.IsTrue(outputs.RawSettings, "outLocked")
        };
        var commands = sourceRouter.Route(guarded, active);
        if (commands.Count == 0 || commands.Count != active.Length)
            return null;

        // F1–F4 are routed by the guarded source command coordinator,
        // not by the slide-advance inspector.
        var primary = commands[0].Command;
        if (commands.Any(command => command.Command != primary) ||
            !commands.Select(c => c.OutputId).SequenceEqual(active))
            return null;

        FreeShowSlideAdvanceDirection? direction = primary switch
        {
            NativeShortcutCommand.Next => FreeShowSlideAdvanceDirection.Next,
            NativeShortcutCommand.Previous => FreeShowSlideAdvanceDirection.Previous,
            NativeShortcutCommand.First => FreeShowSlideAdvanceDirection.First,
            NativeShortcutCommand.Last => FreeShowSlideAdvanceDirection.Last,
            _ => null
        };
        if (direction == null) return null;

        return FreeShowMultiOutputAdvanceInspection.Prepare(
            outputs, currentStates, sourceContext, knownShowLookup,
            direction.Value, project, activeItem, focusMode, nextItemOnLastSlide);
    }
}
