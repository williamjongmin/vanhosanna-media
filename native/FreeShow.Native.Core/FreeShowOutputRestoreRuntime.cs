using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// A source-complete RestoreOutput runtime must restore actual physical output
/// composition (all media, style, scene, effects, overlays and slide layers)
/// AND global audio, playlist and metronome. A JSON-only update or lyrics-only
/// renderer is not a complete implementation.
/// </summary>
public interface IFreeShowOutputRestoreRuntime
{
    bool CanRestoreOutput(string originalOutputId, JsonElement originalOutData);
    bool CanRestoreGlobalAudio(JsonElement playingAudioData, bool playingMetronome);

    Task RestoreOutputAsync(string originalOutputId, JsonElement originalOutData,
        CancellationToken cancellationToken = default);
    Task RestoreGlobalAudioAsync(JsonElement playingAudioData, bool playingMetronome,
        CancellationToken cancellationToken = default);
}

public enum FreeShowRestoreStatus
{
    Completed,
    Refused,
    Failed,
    PartialExecution
}

public sealed record FreeShowRestoreResult(
    FreeShowRestoreStatus Status,
    int OutputsRestored,
    IReadOnlyList<string> Problems)
{
    public bool Succeeded => Status == FreeShowRestoreStatus.Completed;
}

/// <summary>
/// Source ClearButtons.svelte shows Restore only when there is a cache and
/// the presentation is actually cleared, with a 1000ms enable delay.
/// Unknown audio/overlay status cannot be promoted into "cleared".
/// No timer is scheduled by this value-only policy.
/// </summary>
public static class FreeShowRestoreEligibility
{
    public static bool CanOffer(
        FreeShowOutputRestoreCache cached,
        FreeShowPreviewSnapshot currentPreview,
        DateTimeOffset savedAt,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cached);
        ArgumentNullException.ThrowIfNull(currentPreview);

        if (currentPreview.OutputLocked || !cached.HasSnapshot ||
            !cached.HasCompleteAudioSnapshot)
            return false;
        if (now - savedAt < TimeSpan.FromSeconds(1))
            return false;
        return currentPreview.Control("all").Presence ==
            PreviewLayerPresence.Cleared;
    }
}
