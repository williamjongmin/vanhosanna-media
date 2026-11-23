using Avalonia;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FreeShow.Native.Core;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;

namespace FreeShow.Native.Rendering;

/// <summary>
/// Windows-only FreeShow output-video prototype, modeled after the existing
/// VanHosanna.Media/Views/OutputWindow.cs lifecycle. The presenter is the
/// LibVLCSharp.Avalonia VideoView's full-size overlay when video is active.
/// VIDEO IS NOT RESTARTED during ShowCue(); the only cue path swaps cached
/// lyric controls. Each FreeShow physical output needs a distinct instance.
///
/// Not yet production ready: multi-video compositing, camera/NDI/OMT,
/// output effects, alpha/filter and transition parity are not implemented.
/// </summary>
public sealed class NativePersistentVideoOutput : ContentControl, IAsyncDisposable
{
    private readonly NativeSlidePresenter lyrics = new();
    private readonly Border fallbackSurface;
    private readonly Border videoOverlay;
    private readonly VideoView videoView;
    private readonly SemaphoreSlim mediaGate = new(1, 1);
    private readonly NativeVideoPlaybackState playback = new();
    private LibVLC? engine;
    private MediaPlayer? player;
    private bool closing;
    private bool videoAttached;

    public NativePersistentVideoOutput()
    {
        fallbackSurface = new Border
        {
            Background = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = lyrics
        };
        videoOverlay = new Border
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        videoView = new VideoView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Content = fallbackSurface;
    }

    public NativeSlidePresenter Lyrics => lyrics;
    public NativeCueDispatchMetrics CueDispatchMetrics { get; } = new();
    public string PlayingVideoId => playback.CurrentVideoId;
    public bool IsVideoAttached => videoAttached;
    public long BackgroundTimeMs => player?.Time ?? -1;

    /// <summary>
    /// Run preparation BEFORE worship playback. Native lyrics are transparent
    /// because video (or the future still-image compositor) is a separate layer.
    /// This prototype does not show still-image backgrounds when video is absent.
    /// </summary>
    public void Prepare(
        IReadOnlyList<NativeFramePlan> frames,
        IReadOnlyList<FreeShowOutputLayerPlan>? outputPlans = null)
    {
        if (closing) throw new ObjectDisposedException(nameof(NativePersistentVideoOutput));
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Native output preparation belongs to the UI thread.");
        lyrics.Prepare(frames, outputPlans, lyricsOnlyOverlay: true);
    }

    public bool ShowCue(int index)
    {
        if (closing) return false;
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Slide selection belongs to the Avalonia UI thread.");
        // Do not access the decoder or video view here. A shared persistent
        // video background must not be restarted during a lyric cue change.
        var started = Stopwatch.GetTimestamp();
        bool shown = lyrics.Show(index);
        if (shown)
            CueDispatchMetrics.Record(index, Stopwatch.GetElapsedTime(started));
        return shown;
    }

    /// <summary>
    /// Prebuild one original FreeShow line/click-reveal state. The source
    /// output must be resolved before worship playback. This may allocate
    /// text controls during preparation, but never opens a video decoder.
    /// </summary>
    public bool PrewarmReveal(
        int cue, System.Text.Json.JsonElement outSlide,
        FreeShowRevealStyleContext style)
    {
        if (closing) throw new ObjectDisposedException(nameof(NativePersistentVideoOutput));
        return lyrics.PrewarmReveal(cue, outSlide, style);
    }

    /// <summary>
    /// Read-only check that the entire source lyric reveal visual was
    /// prewarmed for this specific output; never touches LibVLC.
    /// </summary>
    public bool HasPrewarmedReveal(
        int cue, System.Text.Json.JsonElement outSlide,
        FreeShowRevealStyleContext style) =>
        !closing && lyrics.HasPrewarmedReveal(cue, outSlide, style);

    public bool IsPresentingPrewarmedReveal(
        int cue, System.Text.Json.JsonElement outSlide,
        FreeShowRevealStyleContext style)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Native visual acknowledgment belongs to the UI thread.");
        return !closing && (videoAttached
            ? ReferenceEquals(Content, videoView) &&
              ReferenceEquals(videoView.Content, videoOverlay) &&
              ReferenceEquals(videoOverlay.Child, lyrics)
            : ReferenceEquals(Content, fallbackSurface) &&
              ReferenceEquals(fallbackSurface.Child, lyrics)) &&
        lyrics.IsPresentingPrewarmedReveal(cue, outSlide, style);
    }

    /// <summary>
    /// Swap a prebuilt reveal view only. Like ShowCue, this must never touch
    /// LibVLC, seek video or allocate new lyric controls in the hot path.
    /// Metrics still measure synchronous dispatch, not visible-frame latency.
    /// </summary>
    public bool ShowPrewarmedReveal(
        int cue, System.Text.Json.JsonElement outSlide,
        FreeShowRevealStyleContext style)
    {
        if (closing) return false;
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Native reveal selection belongs to the UI thread.");
        var started = Stopwatch.GetTimestamp();
        var shown = lyrics.ShowPrewarmedReveal(cue, outSlide, style);
        if (shown)
            CueDispatchMetrics.Record(cue, Stopwatch.GetElapsedTime(started));
        return shown;
    }

    /// <summary>
    /// FreeShow clearSlide()/F2: hide lyric overlay only; keep player,
    /// video timestamp and prepared slide cache unchanged.
    /// </summary>
    public void ClearLyrics()
    {
        if (closing) throw new ObjectDisposedException(nameof(NativePersistentVideoOutput));
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Native lyrics must be cleared on the UI thread.");
        lyrics.ClearVisible();
    }

    /// <summary>
    /// Set the background independently from lyric slide selection.
    /// Call only on a confirmed background change, not for each cue.
    /// Null/empty clears. Local video file paths only; online/NDI are separate
    /// FreeShow media backends and are not accepted as equivalent.
    /// </summary>
    public async Task<VideoPlaybackDecision> SetBackgroundAsync(
        string? mediaId, string? existingLocalVideoPath,
        CancellationToken cancellationToken = default)
    {
        await mediaGate.WaitAsync(cancellationToken);
        try
        {
            if (closing) throw new ObjectDisposedException(nameof(NativePersistentVideoOutput));
            var decision = playback.Plan(mediaId, existingLocalVideoPath);
            if (decision is VideoPlaybackDecision.Keep or VideoPlaybackDecision.NoChange
                or VideoPlaybackDecision.Unresolved)
                return decision;

            if (decision == VideoPlaybackDecision.Stop)
            {
                await StopPlaybackCoreAsync();
                return decision;
            }

            var path = Path.GetFullPath(existingLocalVideoPath!);
            if (!File.Exists(path)) return VideoPlaybackDecision.Unresolved;

            // Reuse the same LibVLC decoder/MediaPlayer for subsequent cue
            // updates. On a new file, Play(newMedia) changes the video source.
            if (engine == null || player == null)
            {
                var pair = await Task.Run(() =>
                {
                    Core.Initialize();
                    var vlc = new LibVLC("--no-video-title-show", "--no-audio");
                    try
                    {
                        var mediaPlayer = new MediaPlayer(vlc)
                        {
                            EnableHardwareDecoding = true,
                            Mute = true
                        };
                        return (vlc, mediaPlayer);
                    }
                    catch { vlc.Dispose(); throw; }
                }, cancellationToken);
                engine = pair.vlc;
                player = pair.mediaPlayer;
            }

            await Dispatcher.UIThread.InvokeAsync(AttachVideo);
            using (var source = new LibVLCSharp.Shared.Media(engine, new Uri(path)))
            {
                source.AddOption(":input-repeat=65535");
                source.AddOption(":no-audio");
                var active = player;
                bool started = await Task.Run(() => active.Play(source), cancellationToken);
                if (!started)
                    throw new IOException("Native background video could not start: " + path);
            }
            playback.MarkStarted(mediaId!, path);
            return decision;
        }
        catch
        {
            // Player may have stopped the previous media during replacement.
            // Fail closed rather than leaving an invalid "playing" state.
            try { await StopPlaybackCoreAsync(); }
            catch { playback.MarkStopped(); }
            throw;
        }
        finally { mediaGate.Release(); }
    }

    private void AttachVideo()
    {
        if (videoAttached) return;
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("VideoView must be mounted on the UI thread.");

        // Avalonia VideoView uses a floating native overlay on some Windows
        // configurations. Match the original native prototype's full-size
        // overlay root to avoid positioning lyrics from a small text box.
        Content = null;
        fallbackSurface.Child = null;
        videoOverlay.Child = lyrics;
        videoView.Content = videoOverlay;
        videoView.MediaPlayer = player;
        Content = videoView;
        videoAttached = true;
    }

    private async Task StopPlaybackCoreAsync()
    {
        var active = player;
        if (active != null)
            await Task.Run(() => active.Stop());

        playback.MarkStopped();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!videoAttached) return;
            Content = null;
            videoView.MediaPlayer = null;
            videoView.Content = null;
            videoOverlay.Child = null;
            fallbackSurface.Child = lyrics;
            Content = fallbackSurface;
            videoAttached = false;
        });
    }

    public async ValueTask DisposeAsync()
    {
        await mediaGate.WaitAsync();
        try
        {
            if (closing) return;
            closing = true;
            await StopPlaybackCoreAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Content = null;
                fallbackSurface.Child = null;
                lyrics.ClearPrepared();
            });
            var oldPlayer = player;
            var oldEngine = engine;
            player = null;
            engine = null;
            if (oldPlayer != null || oldEngine != null)
                await Task.Run(() =>
                {
                    oldPlayer?.Dispose();
                    oldEngine?.Dispose();
                });
        }
        finally { mediaGate.Release(); }
        // Gate is deliberately retained until this instance becomes unreachable.
        // Races with late calls receive an ObjectDisposedException via closing.
    }
}
