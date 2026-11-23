using FreeShow.Native.Core;

namespace FreeShow.Native.Rendering;

/// <summary>
/// Narrow adapter for the physical media portion of FreeShow F1:
/// it may stop exactly one known local, muted and looping output video
/// while leaving lyric frames and any independent style media untouched.
/// A source-complete output runtime still needs credits, events, timeline,
/// setOutput() notifications and every other effect separately.
/// </summary>
public static class NativeFreeShowMediaSourceEffects
{
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
                    if (session.OutputId != plan.OutputId ||
                        !coordinator.States.TryGetValue(plan.OutputId, out var state))
                        throw new InvalidOperationException(
                            "The original output ID is not owned by this Native video session.");

                    if (plan.ExpectedNativeRevision.HasValue &&
                        plan.ExpectedNativeRevision.Value != state.Revision)
                        throw new InvalidOperationException(
                            "Native FreeShow output background changed after command preparation.");

                    var index = session.CurrentCue;
                    if (index < 0 || index >= session.VideoCues.Count)
                        throw new NotSupportedException(
                            "The original output's selected source cue is not known.");

                    var currentBackground = ShowDocument.Property(
                        state.Snapshot(), "background");
                    var id = ShowDocument.String(currentBackground, "path");
                    if (id.Length == 0)
                        id = ShowDocument.String(currentBackground, "id");

                    var decision = FreeShowBackgroundContinuity.Plan(
                        session.VideoCues[index],
                        id,
                        session.Visual.PlayingVideoId,
                        media => FreeShowMediaLookup.ExistingLocalVideoPath(
                            media, freeShowDataRoot));

                    if (!decision.CanApply)
                        throw new NotSupportedException(
                            "Native cannot reproduce FreeShow active-background clear: " +
                            decision.Warning);

                    // Crucially no cue/lyric operation or decoder recreation
                    // occurs for KeepVideo or NoChange.
                    if (decision.Decision == NativeBackgroundClearDecision.StopCurrentOutputVideo)
                    {
                        var outcome = await session.Visual.SetBackgroundAsync(
                            null, null, cancellationToken);
                        if (outcome is not
                            (VideoPlaybackDecision.Stop or VideoPlaybackDecision.NoChange))
                            throw new IOException(
                                "Native player did not acknowledge background stop.");
                    }
                }
        };
    }
}
