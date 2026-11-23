using Avalonia.Threading;
using FreeShow.Native.Core;

namespace FreeShow.Native.Rendering;

/// <summary>
/// One independently owned native output (Main, Online or Stage).
/// OutputSession.ShowCue() only changes prepared lyrics; video playback
/// remains persistent in its own VideoView and decoder instance.
/// </summary>
public sealed class NativeFreeShowOutputSession : IAsyncDisposable
{
    private readonly IReadOnlyList<NativeVideoCue> videoCues;
    private readonly string dataRoot;
    private bool disposed;

    public string OutputId { get; }
    public NativePersistentVideoOutput Visual { get; }
    public IReadOnlyList<NativeVideoCue> VideoCues => videoCues;
    public int CurrentCue { get; private set; } = -1;
    public int LastClearedCue { get; private set; } = -1;

    public NativeFreeShowOutputSession(
        string outputId, ShowDocument source,
        FreeShowVisualSettings settings, string outputStyleId,
        string freeShowDataRoot,
        string? existingOutputBackgroundId = null,
        Func<string, FreeShowMediaReference>? mediaResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputId);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(freeShowDataRoot);
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Output session construction belongs to UI thread.");

        OutputId = outputId;
        dataRoot = freeShowDataRoot;
        var frames = FreeShowStyledRenderPlanner.Prepare(source, settings, outputStyleId);
        var plans = FreeShowOutputLayerPlanner.Prepare(
            source, settings, outputStyleId, existingOutputBackgroundId);
        if (frames.Count != plans.Count)
            throw new InvalidDataException("Native source output plans are not aligned.");

        videoCues = NativeVideoCuePlanner.Prepare(
            plans, mediaResolver ?? (id => FreeShowMediaLookup.Resolve(source, id)));
        Visual = new NativePersistentVideoOutput();
        Visual.Prepare(frames, plans);
    }

    public bool ShowCue(int index)
    {
        if (disposed) return false;
        // This exact code path does not open/reseek/restart a media file.
        var shown = Visual.ShowCue(index);
        if (shown)
        {
            CurrentCue = index;
            LastClearedCue = -1;
        }
        return shown;
    }

    /// <summary>
    /// FreeShow F2 clears only slide output. The first press remembers the
    /// previous slide index; the double press clears that navigation cache.
    /// It must never reset or dispose the native background video player.
    /// </summary>
    public void ClearSlide(bool clearCachedSlidePosition = false)
    {
        if (disposed) throw new ObjectDisposedException(nameof(NativeFreeShowOutputSession));
        if (!clearCachedSlidePosition && CurrentCue >= 0)
            LastClearedCue = CurrentCue;
        if (clearCachedSlidePosition) LastClearedCue = -1;
        Visual.ClearLyrics();
        CurrentCue = -1;
    }

    /// <summary>
    /// Call only when applying/confirming output background state.
    /// It is independent of ShowCue, so regular lyric changes do not block on
    /// LibVLC or file I/O. Reapplying an unchanged background returns Keep.
    /// Source media ID resolution is conservative; video requires a local file.
    /// </summary>
    public async Task<VideoPlaybackDecision> ApplyBackgroundAsync(
        int cueIndex, CancellationToken cancellationToken = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(NativeFreeShowOutputSession));
        if (cueIndex < 0 || cueIndex >= videoCues.Count)
            throw new ArgumentOutOfRangeException(nameof(cueIndex));
        var cue = videoCues[cueIndex];
        if (cue.RequiresUnimplementedMultipleVideoMix)
            throw new NotSupportedException(
                "FreeShow's simultaneous style/output videos require a multi-layer native decoder.");

        var selected = cue.SingleSupportedVideo;
        if (selected == null)
        {
            if (cue.Layers.Any(l => l.Media.Kind is
                    NativeMediaKind.LiveInput or NativeMediaKind.OnlinePlayer or NativeMediaKind.Unknown))
                return VideoPlaybackDecision.Unresolved;
            return await Visual.SetBackgroundAsync(null, null, cancellationToken);
        }

        var path = FreeShowMediaLookup.ExistingLocalVideoPath(selected.Media, dataRoot);
        return await Visual.SetBackgroundAsync(selected.Media.Id, path, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await Visual.DisposeAsync();
    }
}
