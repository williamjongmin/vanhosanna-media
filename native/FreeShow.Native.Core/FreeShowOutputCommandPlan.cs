using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// FreeShow clear.ts / OutputHelper.ts command preparation. This is a
/// strict, source-based preflight contract, NOT a display mutation.
/// Original active output IDs and keys are retained; unported actions never
/// get silently approximated as "success".
/// </summary>
public static class FreeShowOutputCommandPlanner
{
    public static FreeShowOutputCommandPlan Prepare(
        FreeShowShortcutAction input,
        FreeShowOutputConfiguration configuration,
        JsonElement overlayDefinitions = default,
        JsonElement currentNativeOutData = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);

        if (ShowDocument.IsTrue(configuration.RawSettings, "outLocked"))
            return Unsupported(input, "FreeShow output is locked.");

        if (input.Command == NativeShortcutCommand.ClearAudioAndPlaylist)
        {
            if (input.OutputId.Length != 0)
                return Unsupported(input, "Audio belongs to FreeShow global playback, not a physical output ID.");
            return new(input, null,
                [FreeShowSourceEffect.ClearGlobalAudioAndPlaylist,
                 FreeShowSourceEffect.RecordTimelineClearAudio],
                Array.Empty<string>(), Array.Empty<string>());
        }

        var output = configuration.Find(input.OutputId);
        if (output == null || !output.Enabled || output.StageSourceId != null)
            return Unsupported(input, "No enabled normal FreeShow output matches the original ID.");

        if (!configuration.GetActiveNormalOutputs().Any(x => x.Id == input.OutputId))
            return Unsupported(input, "Source FreeShow output is not currently active.");

        var effects = new List<FreeShowSourceEffect>();
        var lockedOverlayIds = new List<string>();
        var warnings = new List<string>();

        switch (input.Command)
        {
            case NativeShortcutCommand.ClearBackground:
                // clearBackground() clears the active out.background; the
                // independent output style background remains in place.
                effects.Add(FreeShowSourceEffect.ClearActiveBackground);
                effects.Add(FreeShowSourceEffect.PropagateOutputChanged);
                effects.Add(FreeShowSourceEffect.ClearBackgroundCredits);
                effects.Add(FreeShowSourceEffect.NotifyBackgroundCleared);
                effects.Add(FreeShowSourceEffect.RecordTimelineClearBackground);
                break;

            case NativeShortcutCommand.ClearSlide:
                // clearSlide() first cancels *next slide* timers. For first
                // F2, position may be cached if it isn't the last slide.
                // Double F2 / shouldClearAll clears that source cache.
                effects.Add(input.ClearCachedSlidePosition
                    ? FreeShowSourceEffect.ClearSlideCache
                    : FreeShowSourceEffect.CaptureLastSlidePositionIfEligible);
                effects.Add(FreeShowSourceEffect.CancelNextSlideTimers);
                effects.Add(FreeShowSourceEffect.ClearSlideOutput);
                effects.Add(FreeShowSourceEffect.PropagateOutputChanged);
                effects.Add(FreeShowSourceEffect.NotifySlideCleared);
                effects.Add(FreeShowSourceEffect.RecordTimelineClearSlide);
                break;

            case NativeShortcutCommand.ClearOverlays:
                // clearOverlays() requires the original portable OVERLAYS
                // store to preserve locked IDs. Never guess lock metadata.
                var knownOutData = currentNativeOutData.ValueKind == JsonValueKind.Object
                    ? currentNativeOutData : ShowDocument.Property(output.Raw, "out");
                var ids = OutputStrings(knownOutData, "overlays");
                if (ids.Count > 0 && overlayDefinitions.ValueKind != JsonValueKind.Object)
                    return Unsupported(input, "Overlay definitions unavailable: cannot protect locked overlays.");
                foreach (var id in ids)
                {
                    var definition = ShowDocument.Property(overlayDefinitions, id);
                    if (definition.ValueKind != JsonValueKind.Object)
                        return Unsupported(input, "Overlay lock metadata missing: " + id);
                    if (ShowDocument.IsTrue(definition, "locked"))
                        lockedOverlayIds.Add(id);
                }
                effects.Add(FreeShowSourceEffect.ClearUnlockedOverlaysKeepingLocked);
                effects.Add(FreeShowSourceEffect.PropagateOutputChanged);
                effects.Add(FreeShowSourceEffect.ClearEffects);
                effects.Add(FreeShowSourceEffect.PropagateOutputChanged);
                effects.Add(FreeShowSourceEffect.RecordTimelineClearOverlays);
                break;

            case NativeShortcutCommand.ClearAllOverlaysForShortcut:
                // shortcuts.ts F3 uses setOutput("overlays", []) directly,
                // unlike the Preview ClearButtons.svelte clearOverlays()
                // function that preserves locked overlay IDs.
                effects.Add(FreeShowSourceEffect.ClearAllOverlays);
                effects.Add(FreeShowSourceEffect.PropagateOutputChanged);
                break;

            case NativeShortcutCommand.ClearEffects:
                effects.Add(FreeShowSourceEffect.ClearEffects);
                effects.Add(FreeShowSourceEffect.PropagateOutputChanged);
                effects.Add(FreeShowSourceEffect.RecordTimelineClearOverlays);
                break;

            case NativeShortcutCommand.Next:
            case NativeShortcutCommand.Previous:
            case NativeShortcutCommand.First:
            case NativeShortcutCommand.Last:
                // The real original OutputHelper can navigate project items,
                // line reveal, slide layers, and actions—not index + 1.
                return Unsupported(input,
                    "Source-complete project/slide-layer/action navigation is not ported.");

            default:
                return Unsupported(input, "Native output command is not supported.");
        }

        return new(input, output, effects.AsReadOnly(),
            lockedOverlayIds.AsReadOnly(), warnings.AsReadOnly());
    }

    private static IReadOnlyList<string> OutputStrings(JsonElement outData, string name)
    {
        var values = ShowDocument.Property(outData, name);
        return values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString() ?? "").Where(v => v.Length != 0).ToArray()
            : Array.Empty<string>();
    }

    private static FreeShowOutputCommandPlan Unsupported(
        FreeShowShortcutAction input, string warning) =>
        new(input, null, Array.Empty<FreeShowSourceEffect>(),
            Array.Empty<string>(), [warning]);
}

/// <summary>
/// The major original FreeShow side effects are explicitly enumerated; each
/// effect adapter must also cover that operation's underlying setOutput
/// lifecycle (overlay timers, locked-overlay persistence, playback control)
/// before declaring support. If any capability is missing, reject the plan.
/// A later visual parity pass must still verify the complete source behavior.
/// </summary>
public enum FreeShowSourceEffect
{
    ClearActiveBackground,
    PropagateOutputChanged,
    ClearBackgroundCredits,
    NotifyBackgroundCleared,
    RecordTimelineClearBackground,
    CaptureLastSlidePositionIfEligible,
    ClearSlideCache,
    CancelNextSlideTimers,
    ClearSlideOutput,
    NotifySlideCleared,
    RecordTimelineClearSlide,
    ClearUnlockedOverlaysKeepingLocked,
    ClearAllOverlays,
    ClearEffects,
    RecordTimelineClearOverlays,
    ClearGlobalAudioAndPlaylist,
    RecordTimelineClearAudio
}

public sealed record FreeShowOutputCommandPlan(
    FreeShowShortcutAction SourceAction,
    FreeShowConfiguredOutput? Output,
    IReadOnlyList<FreeShowSourceEffect> RequiredEffects,
    IReadOnlyList<string> LockedOverlayIds,
    IReadOnlyList<string> Warnings)
{
    public string OutputId => SourceAction.OutputId;
    public bool CanPlan => Warnings.Count == 0 && RequiredEffects.Count > 0;

    /// <summary>Optimistic concurrency guard for source output changes. Direct
    /// source fixture plans can omit it; native UI plans always set it.</summary>
    public long? ExpectedNativeRevision { get; init; }
}
