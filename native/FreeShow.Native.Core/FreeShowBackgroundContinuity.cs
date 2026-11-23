namespace FreeShow.Native.Core;

/// <summary>
/// FreeShow Background.svelte retains two layers during transitions:
/// style background is distinct from current out.background. An F2 lyric
/// change NEVER runs a media operation. An F1 clear must remove only
/// out.background, not forcibly stop any persistent style background.
/// This conservative planner supports only exact single-video cases where
/// the existing Native LibVLC renderer can reproduce the required behavior.
/// </summary>
public static class FreeShowBackgroundContinuity
{
    public static FreeShowBackgroundClearPlan Plan(
        NativeVideoCue preparedCue,
        string? currentlyOutputBackgroundId,
        string? currentlyPlayingVideoId,
        Func<FreeShowMediaReference, string?> localVideoPath)
    {
        ArgumentNullException.ThrowIfNull(preparedCue);
        ArgumentNullException.ThrowIfNull(localVideoPath);

        string activeId = currentlyOutputBackgroundId ?? "";
        string playingId = currentlyPlayingVideoId ?? "";
        var style = preparedCue.Layers
            .FirstOrDefault(x => x.Layer == "style-background");
        var sourceOutput = preparedCue.Layers
            .FirstOrDefault(x => x.Layer == "output-background");

        if (preparedCue.RequiresUnimplementedMultipleVideoMix)
            return Block("FreeShow uses simultaneous video layers that a single LibVLC output cannot mix.");

        if (preparedCue.Unsupported.Count != 0)
            return Block(string.Join("; ", preparedCue.Unsupported));

        // A template/slide action or a different operator may have changed
        // the background since precomputation. Never trust a stale video cue.
        if (activeId.Length > 0 && sourceOutput?.Media.Id != activeId)
            return Block("Active background differs from the precomputed FreeShow output layer.");

        if (activeId.Length == 0)
        {
            // Clearing an already empty output background must not stop the
            // independent style background currently playing.
            return new(NativeBackgroundClearDecision.NoChange, "", null, null);
        }

        if (sourceOutput?.Media.Kind != NativeMediaKind.Video)
            return Block("Only one local video background is supported by the current native output.");

        if (sourceOutput.Media.Loop != true || sourceOutput.Media.Muted != true)
            return Block("Original background loop/mute options are not reproduced by the fixed LibVLC path.");

        var outputPath = localVideoPath(sourceOutput.Media);
        if (string.IsNullOrWhiteSpace(outputPath))
            return Block("Original FreeShow output video is missing locally.");

        if (style != null)
        {
            if (style.Media.Kind != NativeMediaKind.Video)
                return Block("Style background image/live media requires an independent native compositor.");
            if (!style.Media.Loop || !style.Media.Muted)
                return Block("Style background has non-default playback options.");
            var stylePath = localVideoPath(style.Media);
            if (stylePath == null)
                return Block("Style video path cannot be resolved.");

            // When style/output video IDs are distinct this would require
            // two simultaneously mounted decoders before the clear.
            if (style.Media.Id != activeId)
                return Block("Style and output videos need independent decoders.");
            return new(NativeBackgroundClearDecision.KeepVideo, style.Media.Id,
                stylePath, null);
        }

        // No style background remains. Stop ONLY the active output video
        // that this exact session owns; never stop an unrecognized player.
        if (!string.Equals(playingId, activeId, StringComparison.Ordinal))
            return Block("Current native decoder is not owned by the active FreeShow background.");

        return new(NativeBackgroundClearDecision.StopCurrentOutputVideo,
            activeId, outputPath, null);
    }

    private static FreeShowBackgroundClearPlan Block(string reason) =>
        new(NativeBackgroundClearDecision.Unsupported, "", null, reason);
}

public enum NativeBackgroundClearDecision
{
    NoChange,
    KeepVideo,
    StopCurrentOutputVideo,
    Unsupported
}

public sealed record FreeShowBackgroundClearPlan(
    NativeBackgroundClearDecision Decision, string MediaId,
    string? LocalPath, string? Warning)
{
    public bool CanApply => Decision != NativeBackgroundClearDecision.Unsupported;
}
