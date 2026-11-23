using FreeShow.Native.Core;

namespace FreeShow.Native.Rendering;

/// <summary>
/// Narrow adapter for original FreeShow F1's physical media subeffect.
/// It stops exactly one independently owned, supported local video. Style
/// backgrounds, simultaneous media, nonlooping video, unmuted sound and
/// uncertain cue state MUST NOT be passed to the single-decoder prototype.
/// Credits, output_changed, custom actions and timeline are other effects.
/// </summary>
public static class NativeFreeShowMediaSourceEffects
{
    /// <summary>
    /// Plan-specific readiness so a registered hook cannot make a command
    /// look ready if the original cue changed, a style video remains, or the
    /// native decoder is playing an unrelated media ID.
    /// </summary>
    public static FreeShowBackgroundClearPlan PrepareBackgroundClear(
        NativeFreeShowOutputSession session,
        FreeShowOutputCommandCoordinator coordinator,
        string freeShowDataRoot,
        FreeShowOutputCommandPlan plan)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(freeShowDataRoot);

        if (session.OutputId != plan.OutputId ||
            !coordinator.States.TryGetValue(plan.OutputId, out var state))
            return Block("The original output ID is not owned by this native video session.");

        if (plan.ExpectedNativeRevision.HasValue &&
            plan.ExpectedNativeRevision.Value != state.Revision)
            return Block("Native FreeShow output state changed since command preparation.");

        var index = session.CurrentCue;
        if (index < 0 || index >= session.VideoCues.Count)
            return Block("The selected source video cue is unknown.");

        var currentBackground = ShowDocument.Property(state.Snapshot(), "background");
        var id = ShowDocument.String(currentBackground, "path");
        if (id.Length == 0) id = ShowDocument.String(currentBackground, "id");

        return FreeShowBackgroundContinuity.Plan(
            session.VideoCues[index], id, session.Visual.PlayingVideoId,
            media => FreeShowMediaLookup.ExistingLocalVideoPath(
                media, freeShowDataRoot));
    }

    public static bool CanClearBackground(
        NativeFreeShowOutputSession session,
        FreeShowOutputCommandCoordinator coordinator,
        string freeShowDataRoot,
        FreeShowOutputCommandPlan plan) =>
        PrepareBackgroundClear(session, coordinator, freeShowDataRoot, plan).CanApply;

    public static IReadOnlyDictionary<FreeShowSourceEffect,
        Func<FreeShowOutputCommandPlan, CancellationToken, Task>>
        CreateSingleVideoBackgroundHooks(
            NativeFreeShowOutputSession session,
            FreeShowOutputCommandCoordinator coordinator,
            string freeShowDataRoot)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentException.ThrowIfNullOrWhiteSpace(freeShowDataRoot);

        return new Dictionary<FreeShowSourceEffect,
            Func<FreeShowOutputCommandPlan, CancellationToken, Task>>
        {
            [FreeShowSourceEffect.ClearActiveBackground] =
                async (plan, cancellationToken) =>
                {
                    var decision = PrepareBackgroundClear(
                        session, coordinator, freeShowDataRoot, plan);
                    if (!decision.CanApply)
                        throw new NotSupportedException(
                            "Native cannot reproduce FreeShow active background: " +
                            decision.Warning);

                    // F1 is independent from lyric/slide ShowCue. Stop only
                    // the player known to own the exact active background.
                    // An empty background or independently playing style
                    // video is untouched; decoder position is preserved.
                    if (decision.Decision == NativeBackgroundClearDecision.StopCurrentOutputVideo)
                    {
                        var outcome = await session.Visual.SetBackgroundAsync(
                            null, null, cancellationToken);
                        if (outcome is not
                            (VideoPlaybackDecision.Stop or VideoPlaybackDecision.NoChange))
                            throw new IOException(
                                "Native media player did not acknowledge background stop.");
                    }
                }
        };
    }

    private static FreeShowBackgroundClearPlan Block(string reason) =>
        new(NativeBackgroundClearDecision.Unsupported, "", null, reason);
}
