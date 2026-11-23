using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Read-only-data, in-memory counterpart of FreeShow clear.ts storeCache()
/// and restoreOutput(). Cached OutData is full original JSON, not a synthetic
/// song index. Audio/playlist and metronome metadata must be supplied by an
/// authoritative runtime; missing global state blocks a complete restore.
/// This class NEVER writes FreeShow settings or performs physical restoration.
/// </summary>
public sealed class FreeShowOutputRestoreCache
{
    private readonly Dictionary<string, JsonElement> originalOutputs =
        new(StringComparer.Ordinal);
    private JsonElement playingAudioData;
    private bool? playingMetronome;

    public bool HasSnapshot => originalOutputs.Count != 0;
    public bool HasCompleteAudioSnapshot =>
        playingAudioData.ValueKind == JsonValueKind.Array && playingMetronome.HasValue;

    /// <summary>
    /// The original storeCache() records ONLY active normal outputs, using
    /// their complete OutData, and stores playingAudioData / playingMetronome.
    /// Unknown audio state is NOT assumed silent. Original UI controls the
    /// eligibility and debounce of Clear All separately.
    /// </summary>
    public void Capture(
        IReadOnlyList<string> activeOutputIds,
        IReadOnlyDictionary<string, FreeShowOutputState> currentStates,
        JsonElement playingAudio = default,
        bool? metronomePlaying = null)
    {
        ArgumentNullException.ThrowIfNull(activeOutputIds);
        ArgumentNullException.ThrowIfNull(currentStates);
        if (playingAudio.ValueKind is not
            (JsonValueKind.Undefined or JsonValueKind.Array))
            throw new ArgumentException("FreeShow playingAudioData must be an array.",
                nameof(playingAudio));

        // Validate before replacing a previous intact backup.
        var saved = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var id in activeOutputIds.Distinct(StringComparer.Ordinal))
        {
            if (!currentStates.TryGetValue(id, out var state))
                throw new InvalidDataException("Cannot cache unknown original output ID: " + id);
            saved.Add(id, state.Snapshot().Clone());
        }
        originalOutputs.Clear();
        foreach (var pair in saved) originalOutputs.Add(pair.Key, pair.Value);
        playingAudioData = playingAudio.ValueKind == JsonValueKind.Array
            ? playingAudio.Clone() : default;
        playingMetronome = metronomePlaying;
    }

    public FreeShowOutputRestorePlan PlanRestore(
        IReadOnlyList<string> currentActiveOutputIds,
        IReadOnlyDictionary<string, FreeShowOutputState> currentStates)
    {
        ArgumentNullException.ThrowIfNull(currentActiveOutputIds);
        ArgumentNullException.ThrowIfNull(currentStates);

        var eligible = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var messages = new List<string>();
        if (!HasSnapshot) messages.Add("Original clearAll outputCache has no stored output.");
        foreach (var id in currentActiveOutputIds.Distinct(StringComparer.Ordinal))
        {
            // Source restoreOutput() restores only currently active outputs
            // that were present when Clear All captured the cache.
            if (!currentStates.ContainsKey(id)) continue;
            if (originalOutputs.TryGetValue(id, out var saved))
                eligible.Add(id, saved.Clone());
        }
        if (eligible.Count == 0)
            messages.Add("No cached source output matches the current active output IDs.");
        if (!HasCompleteAudioSnapshot)
            messages.Add("Original audio/playlist or metronome state is unknown.");
        return new(eligible, playingAudioData.ValueKind == JsonValueKind.Array
                ? playingAudioData.Clone() : default,
            playingMetronome, messages.AsReadOnly());
    }

    /// <summary>
    /// A successful restore consumes the original cache. On failure retain it
    /// so operators do not lose their recovery data.
    /// </summary>
    public void Clear() 
    {
        originalOutputs.Clear();
        playingAudioData = default;
        playingMetronome = null;
    }
}

public sealed record FreeShowOutputRestorePlan(
    IReadOnlyDictionary<string, JsonElement> OutputSnapshots,
    JsonElement PlayingAudioData, bool? PlayingMetronome,
    IReadOnlyList<string> Warnings)
{
    public bool IsSourceComplete => Warnings.Count == 0 &&
        PlayingAudioData.ValueKind == JsonValueKind.Array &&
        PlayingMetronome.HasValue && OutputSnapshots.Count > 0;
}
