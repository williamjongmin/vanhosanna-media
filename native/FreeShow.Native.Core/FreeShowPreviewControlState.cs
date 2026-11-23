using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Source-derived read-only model for Preview.svelte, ClearButtons.svelte and
/// isOutCleared() in helpers/output.ts. Output state by itself does not reveal
/// playing audio, overlay locks, timers or media playback state. Such missing
/// evidence is represented as Unknown, NEVER guessed "cleared".
/// </summary>
public static class FreeShowPreviewControlState
{
    public static FreeShowPreviewSnapshot Prepare(
        FreeShowOutputConfiguration outputs,
        JsonElement overlayDefinitions = default,
        FreeShowAuxiliaryPreviewState? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        var active = outputs.GetActiveNormalOutputs();
        bool outLocked = ShowDocument.IsTrue(outputs.RawSettings, "outLocked");
        var effects = active.SelectMany(o => ReadStrings(o.Raw, "effects")).ToArray();
        var overlayIds = active.SelectMany(o => ReadStrings(o.Raw, "overlays")).ToArray();
        bool knownOverlayLocks = overlayDefinitions.ValueKind == JsonValueKind.Object;
        bool hasUnlockedOverlays = false;
        bool hasLockedOverlays = false;

        foreach (var id in overlayIds)
        {
            var overlay = ShowDocument.Property(overlayDefinitions, id);
            if (overlay.ValueKind != JsonValueKind.Object)
            {
                knownOverlayLocks = false;
                continue;
            }
            if (ShowDocument.IsTrue(overlay, "locked")) hasLockedOverlays = true;
            else hasUnlockedOverlays = true;
        }

        var background = ActiveState(active, "background");
        var slide = ActiveState(active, "slide");
        var scene = ActiveState(active, "scene");
        var transition = ActiveState(active, "transition");

        // Original isOutCleared(null) ignores scene and refresh. Their
        // independent presence must not make the Clear all state active.
        var overlay = overlayIds.Length == 0
            ? PreviewLayerPresence.Cleared
            : knownOverlayLocks
                ? (hasUnlockedOverlays ? PreviewLayerPresence.Active : PreviewLayerPresence.Cleared)
                : PreviewLayerPresence.Unknown;
        var effect = effects.Length > 0
            ? PreviewLayerPresence.Active : PreviewLayerPresence.Cleared;
        var audio = runtime?.Audio ?? PreviewLayerPresence.Unknown;
        var timers = runtime?.Timers ?? (transition == PreviewLayerPresence.Active
            ? PreviewLayerPresence.Active : PreviewLayerPresence.Unknown);

        var controls = new[]
        {
            new FreeShowPreviewControl("scene", "scene", PreviewLayerPresence.Cleared != scene
                ? scene : PreviewLayerPresence.Cleared, !outLocked && scene == PreviewLayerPresence.Active,
                true, "clear.scene", ""),
            new FreeShowPreviewControl("all", "clear", Combine(
                background, slide, overlay, effect, audio, timers),
                false, true, "clear.all", "Escape"),
            new FreeShowPreviewControl("background", "image", background,
                !outLocked && background == PreviewLayerPresence.Active,
                true, "clear.background", "F1"),
            new FreeShowPreviewControl("slide", "slide", slide,
                !outLocked && slide == PreviewLayerPresence.Active,
                true, "clear.slide", "F2"),
            new FreeShowPreviewControl("overlays", "overlays",
                Combine(overlay, effect),
                !outLocked && knownOverlayLocks &&
                (hasUnlockedOverlays || effect == PreviewLayerPresence.Active),
                true, "clear.overlays", "F3"),
            new FreeShowPreviewControl("audio", "audio", audio,
                !outLocked && audio == PreviewLayerPresence.Active,
                true, "clear.audio", "F4"),
            new FreeShowPreviewControl("nextTimer", "clock", timers,
                !outLocked && timers == PreviewLayerPresence.Active,
                true, "clear.nextTimer", "")
        };

        string? activeCategory = null;
        // Preview.svelte getActiveClear precedence: timer, audio, overlays,
        // slide, background. Scene / all are special destructive actions only.
        foreach (var candidate in new[] {"nextTimer", "audio", "overlays", "slide", "background"})
        {
            if (controls.First(c => c.Id == candidate).Presence == PreviewLayerPresence.Active)
            {
                activeCategory = candidate;
                break;
            }
        }
        return new(active.Select(x => x.Id).ToArray(), outLocked,
            controls, activeCategory, hasLockedOverlays, knownOverlayLocks);
    }

    private static PreviewLayerPresence ActiveState(
        IReadOnlyList<FreeShowConfiguredOutput> active, string key)
    {
        if (active.Count == 0) return PreviewLayerPresence.Cleared;
        foreach (var output in active)
        {
            var value = ShowDocument.Property(
                ShowDocument.Property(output.Raw, "out"), key);
            if (value.ValueKind is not
                (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False))
                return PreviewLayerPresence.Active;
        }
        return PreviewLayerPresence.Cleared;
    }

    private static IEnumerable<string> ReadStrings(JsonElement source, string key)
    {
        var list = ShowDocument.Property(ShowDocument.Property(source, "out"), key);
        if (list.ValueKind != JsonValueKind.Array) return [];
        return list.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? "").Where(x => x.Length > 0);
    }

    private static PreviewLayerPresence Combine(params PreviewLayerPresence[] values)
    {
        if (values.Any(x => x == PreviewLayerPresence.Active))
            return PreviewLayerPresence.Active;
        if (values.Any(x => x == PreviewLayerPresence.Unknown))
            return PreviewLayerPresence.Unknown;
        return PreviewLayerPresence.Cleared;
    }
}

public enum PreviewLayerPresence { Cleared, Active, Unknown }

/// <summary>
/// Runtime audio and timer state, if supplied by a real FreeShow-equivalent
/// audio/overlay timer engine. Unknown is the only safe default.
/// </summary>
public sealed record FreeShowAuxiliaryPreviewState(
    PreviewLayerPresence Audio,
    PreviewLayerPresence Timers);

public sealed record FreeShowPreviewControl(
    string Id, string OriginalIconId, PreviewLayerPresence Presence,
    bool SourceCanClear, bool Visible, string SourceTranslationId, string KeyboardHint);

public sealed record FreeShowPreviewSnapshot(
    IReadOnlyList<string> ActiveOutputIds,
    bool OutputLocked,
    IReadOnlyList<FreeShowPreviewControl> Controls,
    string? RecommendedCategory,
    bool HasLockedOverlays, bool OverlayLocksAreKnown)
{
    public FreeShowPreviewControl Control(string id) =>
        Controls.First(c => c.Id == id);
}

/// <summary>
/// Original Preview.svelte allows clicking a small expansion selector to
/// inspect one category without changing physical output. Keep this state
/// independent from the destructive clear action.
/// </summary>
public sealed class FreeShowPreviewCategoryNavigation
{
    public string? SelectedCategory { get; private set; }
    public bool FollowSourceAutomatically { get; private set; } = true;

    public void Update(FreeShowPreviewSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (FollowSourceAutomatically || SelectedCategory is null ||
            source.Control(SelectedCategory).Presence == PreviewLayerPresence.Cleared)
        {
            SelectedCategory = source.RecommendedCategory;
            FollowSourceAutomatically = true;
        }
    }

    public void Select(string category)
    {
        if (category is not ("background" or "slide" or "overlays" or "audio" or "nextTimer"))
            throw new ArgumentException("Not a FreeShow Preview section.", nameof(category));
        if (SelectedCategory == category)
        {
            FollowSourceAutomatically = true;
            return;
        }
        SelectedCategory = category;
        FollowSourceAutomatically = false;
    }

    public void ResetAuto() => FollowSourceAutomatically = true;
}
