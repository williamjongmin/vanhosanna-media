namespace FreeShow.Native.Core;

/// <summary>
/// Original NextTimer.svelte playPause, transitionChange and mouseup state.
/// Uses the current active output IDs on each event, as the source does.
/// No Avalonia UI redesign or live operator handler is installed here.
/// </summary>
public sealed class FreeShowNextTimerControls
{
    private readonly FreeShowNativeTimerScheduler scheduler;
    private readonly Func<IReadOnlyList<string>> activeOutputIds;
    private bool autoPause;

    public FreeShowNextTimerControls(FreeShowNativeTimerScheduler timers,
        Func<IReadOnlyList<string>> originalActiveOutputIds)
    {
        scheduler = timers ?? throw new ArgumentNullException(nameof(timers));
        activeOutputIds = originalActiveOutputIds ??
            throw new ArgumentNullException(nameof(originalActiveOutputIds));
    }

    public void TogglePlayPause(bool currentPreviewIsPaused)
    {
        foreach (var timer in ActiveTimers())
        {
            if (currentPreviewIsPaused) scheduler.ResumeSlideTimer(timer.OutputId);
            else scheduler.PauseSlideTimer(timer.OutputId);
        }
    }

    public void ChangeSlider(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0 ||
            elapsedSeconds > int.MaxValue / 1000d)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        foreach (var timer in ActiveTimers())
        {
            if (!timer.Paused)
            {
                autoPause = true;
                scheduler.PauseSlideTimer(timer.OutputId);
            }
            scheduler.SetSlideTime(timer.OutputId, elapsedSeconds);
        }
    }

    public void ReleaseSlider()
    {
        if (!autoPause) return;
        var timers = ActiveTimers();
        // Original mouseup resumes ALL current active timers if at least
        // one was auto-paused during this gesture, including previously
        // paused outputs. Preserve that shared gesture behavior.
        autoPause = false;
        foreach (var timer in timers) scheduler.ResumeSlideTimer(timer.OutputId);
    }

    private IReadOnlyList<FreeShowSlideTimerSnapshot> ActiveTimers() =>
        // Resolve/validate the entire selection before changing any timer.
        activeOutputIds().Distinct(StringComparer.Ordinal)
            .Select(scheduler.GetSlideTimer)
            .OfType<FreeShowSlideTimerSnapshot>().ToArray();
}
