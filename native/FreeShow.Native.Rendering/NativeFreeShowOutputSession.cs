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
    private readonly IReadOnlyList<NativeFramePlan> preparedFrames;
    private readonly string dataRoot;
    private bool disposed;

    public string OutputId { get; }
    public string PreparedShowId { get; }
    public string PreparedLayoutId { get; }
    public NativePersistentVideoOutput Visual { get; }
    public IReadOnlyList<NativeVideoCue> VideoCues => videoCues;
    public int CurrentCue { get; private set; } = -1;
    public int LastClearedCue { get; private set; } = -1;

    /// <summary>
    /// Bind a source SETTINGS output rather than a hand-invented Main/Stage enum.
    /// Stage layout and network outputs need other backends and are rejected
    /// until ported (they must not silently become normal lyric outputs).
    /// </summary>
    public NativeFreeShowOutputSession(
        FreeShowConfiguredOutput profile, ShowDocument source,
        FreeShowVisualSettings settings, string freeShowDataRoot,
        Func<string, FreeShowMediaReference>? mediaResolver = null)
        : this(
            ValidatePhysicalOutput(profile),
            source, settings, profile.StyleId, freeShowDataRoot,
            ActiveBackgroundId(profile), mediaResolver)
    {
    }

    private static string ValidatePhysicalOutput(FreeShowConfiguredOutput? profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.StageSourceId != null)
            throw new NotSupportedException(
                "Original FreeShow Stage output requires stage-specific native rendering.");
        if (profile.NeedsUnportedStreamingBackend || profile.Invisible)
            throw new NotSupportedException(
                "Original FreeShow capture/network output requires its native backend.");
        if (profile.Transparent)
            throw new NotSupportedException(
                "Original FreeShow transparent output composition has not been ported.");
        if (!profile.Enabled)
            throw new InvalidOperationException(
                "Native cannot activate a source FreeShow output which is disabled.");
        return profile.Id;
    }

    private static string? ActiveBackgroundId(FreeShowConfiguredOutput profile)
    {
        var output = ShowDocument.Property(profile.Raw, "out");
        var background = ShowDocument.Property(output, "background");
        var path = ShowDocument.String(background, "path");
        return path.Length > 0 ? path : ShowDocument.String(background, "id");
    }

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
        PreparedShowId = source.ShowId;
        PreparedLayoutId = source.ActiveLayoutId;
        dataRoot = freeShowDataRoot;
        var frames = FreeShowStyledRenderPlanner.Prepare(source, settings, outputStyleId);
        preparedFrames = frames;
        var plans = FreeShowOutputLayerPlanner.Prepare(
            source, settings, outputStyleId, existingOutputBackgroundId);
        if (frames.Count != plans.Count)
            throw new InvalidDataException("Native source output plans are not aligned.");

        videoCues = NativeVideoCuePlanner.Prepare(
            plans, mediaResolver ?? (id => FreeShowMediaLookup.Resolve(source, id)));
        Visual = new NativePersistentVideoOutput();
        Visual.Prepare(frames, plans);
    }

    /// <summary>
    /// Safely convert original FreeShow OutSlide.layout/index into the
    /// preprepared native cue ordinal. Original source indexes are NOT equal
    /// to native ordinal after filtering disabled slides. This method
    /// does not show any slide, execute an action, or control media.
    /// The full source output lifecycle is still necessary to go live.
    /// </summary>
    public NativeSourceCueLookup InspectPreparedNavigation(
        FreeShowSlideAdvanceResult navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        if (disposed)
            return new(null, "Native output session is already disposed.");
        if (!navigation.HasTarget || navigation.LayoutIndex == null ||
            navigation.Kind == FreeShowSlideAdvanceKind.RevealWithinSlide)
            return new(null, "Original reveal or project navigation requires source compositor.");
        if (navigation.ShowId != PreparedShowId ||
            navigation.LayoutId != PreparedLayoutId)
            return new(null, "Native session is prepared for a different original Show/layout.");

        for (int index = 0; index < preparedFrames.Count; index++)
        {
            var frame = preparedFrames[index];
            if (frame.Source.LayoutIndex != navigation.LayoutIndex) continue;
            if (videoCues[index].LayoutIndex != navigation.LayoutIndex)
                return new(null, "Original renderer/video cue indices do not match.");
            if (!frame.CanRenderWithoutKnownOmissions ||
                videoCues[index].Unsupported.Count != 0 ||
                navigation.RequiresUnportedSourceEffects)
                return new(null, "Original slide includes unported media/reveal/actions or CSS.");
            return new(index, null);
        }
        return new(null,
            "Original source slide is disabled, missing or not prepared in this native output.");
    }

    /// <summary>
    /// Build one source-outSlide reveal variant during preparation. This
    /// path checks Show/layout/source indexes and media support first.
    /// Full updateOut/actions/timer parity is still unported, so these
    /// variants are developer previews rather than a live command bridge.
    /// </summary>
    public bool PrewarmReveal(
        System.Text.Json.JsonElement outSlide,
        FreeShowRevealStyleContext style)
    {
        if (disposed) return false;
        int? cue = FindPreparedRevealCue(outSlide);
        return cue.HasValue && Visual.PrewarmReveal(cue.Value, outSlide, style);
    }

    /// <summary>
    /// Present an already-prepared reveal variant without interacting with
    /// the decoder. No missing variant is generated in this hot path.
    /// </summary>
    public bool ShowPrewarmedReveal(
        System.Text.Json.JsonElement outSlide,
        FreeShowRevealStyleContext style)
    {
        if (disposed) return false;
        int? cue = FindPreparedRevealCue(outSlide);
        if (!cue.HasValue) return false;
        bool shown = Visual.ShowPrewarmedReveal(cue.Value, outSlide, style);
        if (shown)
        {
            CurrentCue = cue.Value;
            LastClearedCue = -1;
        }
        return shown;
    }

    private int? FindPreparedRevealCue(System.Text.Json.JsonElement outSlide)
    {
        if (outSlide.ValueKind != System.Text.Json.JsonValueKind.Object ||
            ShowDocument.String(outSlide, "id") != PreparedShowId ||
            ShowDocument.String(outSlide, "layout") != PreparedLayoutId)
            return null;
        var index = ShowDocument.Property(outSlide, "index");
        if (index.ValueKind != System.Text.Json.JsonValueKind.Number ||
            !index.TryGetInt32(out int original) || original < 0)
            return null;

        for (int cue = 0; cue < preparedFrames.Count; cue++)
        {
            if (preparedFrames[cue].Source.LayoutIndex != original) continue;
            if (cue >= videoCues.Count ||
                videoCues[cue].LayoutIndex != original ||
                videoCues[cue].Unsupported.Count != 0)
                return null;
            return cue;
        }
        return null;
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

public sealed record NativeSourceCueLookup(int? NativeCueOrdinal, string? Refusal)
{
    public bool CanPreviewNativeFrame => NativeCueOrdinal.HasValue && Refusal == null;
    public bool CanDispatchLive => false;
}
