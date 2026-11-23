using Avalonia.Threading;
using FreeShow.Native.Core;

namespace FreeShow.Native.Rendering;

/// <summary>
/// Prepare one known original OutSlide state on each physical output's
/// isolated lyric presenter. Never calls ShowCue(), LibVLC, output_changed
/// or project actions; actual simultaneous live transition remains unported.
/// </summary>
public static class NativeMultiOutputRevealPrewarmer
{
    public static NativeMultiOutputRevealPrewarmResult Prewarm(
        FreeShowOutputRevealBatchPlan plan,
        IReadOnlyDictionary<string, NativeFreeShowOutputSession> sessions,
        IReadOnlyDictionary<string, FreeShowOutputState> currentStates)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(currentStates);

        if (!Dispatcher.UIThread.CheckAccess())
            return Refuse(0, "Native output visual preparation requires Avalonia UI thread.");
        if (!plan.CanPrewarmAll)
            return Refuse(0, string.Join("; ", plan.Problems));
        if (!plan.Captured.StillMatches(currentStates))
            return Refuse(0, "A FreeShow source output changed before reveal prewarm.");

        // Validate every source session before allocating a single view. Do
        // not swap one screen's lyrics if another screen is missing.
        foreach (var target in plan.Targets.Values)
        {
            if (!sessions.TryGetValue(target.OutputId, out var session))
                return Refuse(0, "Native source output session missing: " + target.OutputId);
            if (session.OutputId != target.OutputId ||
                session.PreparedShowId != target.ShowId ||
                session.PreparedLayoutId != target.LayoutId)
                return Refuse(0,
                    "Prepared native Show/layout differs from original output " +
                    target.OutputId);
        }

        int warmed = 0;
        try
        {
            foreach (var target in plan.Targets.Values)
            {
                if (!sessions[target.OutputId].PrewarmReveal(
                        target.OutSlide, target.Style))
                    return Refuse(warmed,
                        "Native renderer refused a source reveal state for " +
                        target.OutputId);
                warmed++;
            }
        }
        catch (Exception exception)
        {
            return Refuse(warmed,
                "Native reveal preparation failed: " +
                exception.GetType().Name + ": " + exception.Message);
        }

        if (!plan.Captured.StillMatches(currentStates))
            return Refuse(warmed,
                "FreeShow source state changed during prewarm; cached visuals are stale.");

        return new(warmed, true, null);
    }

    private static NativeMultiOutputRevealPrewarmResult Refuse(int count, string message) =>
        new(count, false, message);
}

public sealed record NativeMultiOutputRevealPrewarmResult(
    int PreparedOutputCount, bool PreparedAllOutputs, string? Problem)
{
    public bool CanDispatchLive => false;
}
