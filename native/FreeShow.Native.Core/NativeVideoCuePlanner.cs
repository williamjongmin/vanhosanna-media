namespace FreeShow.Native.Core;

/// <summary>
/// FreeShow has separate style-background and active-background layers.
/// A cue can change lyrics while both media IDs stay unchanged. This planner
/// records those layers independently and NEVER equates cue selection with
/// starting a new player. It only observes already-selected background state;
/// it does not execute layout.background actions.
/// </summary>
public static class NativeVideoCuePlanner
{
    public static IReadOnlyList<NativeVideoCue> Prepare(
        IReadOnlyList<FreeShowOutputLayerPlan> plans,
        Func<string, FreeShowMediaReference> resolve)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(resolve);
        var result = new List<NativeVideoCue>(plans.Count);
        foreach (var plan in plans)
        {
            var layers = new List<NativeVideoLayer>();
            if (!string.IsNullOrWhiteSpace(plan.StyleBackgroundId))
                layers.Add(Make("style-background", plan.StyleBackgroundId, resolve));
            if (plan.BackgroundEnabled && !string.IsNullOrWhiteSpace(plan.OutputBackgroundId))
                layers.Add(Make("output-background", plan.OutputBackgroundId, resolve));

            var videoLayers = layers.Where(l => l.Media.Kind == NativeMediaKind.Video).ToArray();
            var warnings = new List<string>();
            if (videoLayers.Length > 1)
                warnings.Add("FreeShow has multiple simultaneous video layers; one-player prototype cannot reproduce both.");
            foreach (var layer in layers.Where(l => l.Media.Kind is NativeMediaKind.LiveInput
                                                       or NativeMediaKind.OnlinePlayer
                                                       or NativeMediaKind.Unknown))
                warnings.Add("Background layer requires a dedicated FreeShow media backend: " +
                    layer.Layer + ":" + layer.Media.Id);

            result.Add(new NativeVideoCue(plan.Frame.LayoutIndex, plan.Frame.SlideId,
                layers.AsReadOnly(), videoLayers.Length == 1 ? videoLayers[0] : null,
                warnings.AsReadOnly()));
        }
        return result.AsReadOnly();
    }

    private static NativeVideoLayer Make(string layer, string id,
        Func<string, FreeShowMediaReference> resolve)
        => new(layer, resolve(id));
}

public sealed record NativeVideoLayer(string Layer, FreeShowMediaReference Media);
public sealed record NativeVideoCue(
    int LayoutIndex, string SlideId, IReadOnlyList<NativeVideoLayer> Layers,
    NativeVideoLayer? SingleSupportedVideo, IReadOnlyList<string> Unsupported)
{
    public bool RequiresUnimplementedMultipleVideoMix =>
        Layers.Count(l => l.Media.Kind == NativeMediaKind.Video) > 1;
}

/// <summary>
/// Pure state decisions for an output-specific persistent decoder.
/// Same video ID/path means "Keep", including between different lyric cues.
/// Each output (Main, Online, Stage) MUST own its own state object.
/// </summary>
public sealed class NativeVideoPlaybackState
{
    public string CurrentVideoId { get; private set; } = "";
    public string CurrentVideoPath { get; private set; } = "";

    public VideoPlaybackDecision Plan(string? videoId, string? resolvedLocalPath)
    {
        if (string.IsNullOrWhiteSpace(videoId))
            return CurrentVideoPath.Length == 0
                ? VideoPlaybackDecision.NoChange : VideoPlaybackDecision.Stop;
        if (string.IsNullOrWhiteSpace(resolvedLocalPath))
            return VideoPlaybackDecision.Unresolved;
        return string.Equals(CurrentVideoId, videoId, StringComparison.Ordinal) &&
               string.Equals(CurrentVideoPath, resolvedLocalPath, StringComparison.OrdinalIgnoreCase)
            ? VideoPlaybackDecision.Keep
            : VideoPlaybackDecision.Open;
    }

    public void MarkStarted(string videoId, string localPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoId);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        CurrentVideoId = videoId;
        CurrentVideoPath = localPath;
    }

    public void MarkStopped()
    {
        CurrentVideoId = "";
        CurrentVideoPath = "";
    }
}

public enum VideoPlaybackDecision { NoChange, Keep, Open, Stop, Unresolved }
