using System.Text.Json;
using System.Text.Json.Nodes;

namespace FreeShow.Native.Core;

/// <summary>
/// In-memory port of original OutData state (src/types/Output.ts).
/// Clear operations must affect separate fields, never wipe the entire
/// output or close the decoder when only a lyric is cleared.
/// No file is changed; physical output side effects belong to a later
/// FreeShow-equivalent dispatcher and compositor.
/// </summary>
public sealed class FreeShowOutputState
{
    private readonly JsonObject data;
    public string OutputId { get; }

    public FreeShowOutputState(FreeShowConfiguredOutput configured)
    {
        ArgumentNullException.ThrowIfNull(configured);
        OutputId = configured.Id;
        var state = ShowDocument.Property(configured.Raw, "out");
        data = state.ValueKind == JsonValueKind.Object
            ? (JsonNode.Parse(state.GetRawText()) as JsonObject ?? new JsonObject())
            : new JsonObject();
    }

    public JsonElement Snapshot() => JsonSerializer.SerializeToElement(data);

    public bool HasSlide => data["slide"] is JsonObject;
    public bool HasActiveBackground => data["background"] is JsonObject;
    public string BackgroundId =>
        GetStateBackgroundId(data["background"] as JsonObject);

    public IReadOnlyList<string> Overlays => ReadArray("overlays");
    public IReadOnlyList<string> Effects => ReadArray("effects");

    public bool TryApply(FreeShowShortcutAction command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OutputId != OutputId) return false;

        switch (command.Command)
        {
            case NativeShortcutCommand.ClearSlide:
                data["slide"] = null;
                return true;
            case NativeShortcutCommand.ClearBackground:
                // Only active output background clears. Original style background,
                // scene media and underlying output template are independent.
                data["background"] = null;
                return true;
            case NativeShortcutCommand.ClearOverlays:
                // The original FreeShow clearOverlays() *preserves locked
                // overlays*. The output data cannot tell which IDs are
                // locked. Never erase them without original overlay metadata.
                return false;
            case NativeShortcutCommand.ClearEffects:
                data["effects"] = new JsonArray();
                return true;
            default:
                return false; // Navigation/audio require the real controller.
        }
    }

    /// <summary>
    /// Original clearOverlays() retains every overlay whose definition is
    /// locked, and clears effects separately. Call with complete locked-ID
    /// metadata from the original overlays store; never infer it from OutData.
    /// This method modifies only the in-memory clone.
    /// </summary>
    public void ClearOverlaysKeepingLocked(IReadOnlySet<string> lockedOverlayIds)
    {
        ArgumentNullException.ThrowIfNull(lockedOverlayIds);
        var kept = Overlays.Where(lockedOverlayIds.Contains).ToArray();
        data["overlays"] = new JsonArray(kept.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        data["effects"] = new JsonArray();
    }

    /// <summary>
    /// Original clearScene() affects scene only, not the background or slide.
    /// </summary>
    public void ClearScene() => data["scene"] = null;

    /// <summary>
    /// Source clearTimers() clears the output transition state separately
    /// from the actual runtime timers. Those runtime timers are not ported.
    /// </summary>
    public void ClearTransition() => data["transition"] = null;

    public void SetSlide(JsonElement originalFreeShowOutSlide)
    {
        if (originalFreeShowOutSlide.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Original OutSlide must be an object.",
                nameof(originalFreeShowOutSlide));
        data["slide"] = JsonNode.Parse(originalFreeShowOutSlide.GetRawText());
    }

    public void SetBackground(JsonElement originalFreeShowBackground)
    {
        if (originalFreeShowBackground.ValueKind is not
            (JsonValueKind.Object or JsonValueKind.Null))
            throw new ArgumentException("Original OutBackground must be an object or null.",
                nameof(originalFreeShowBackground));
        data["background"] = originalFreeShowBackground.ValueKind == JsonValueKind.Null
            ? null : JsonNode.Parse(originalFreeShowBackground.GetRawText());
    }

    private IReadOnlyList<string> ReadArray(string name)
    {
        if (data[name] is not JsonArray list) return Array.Empty<string>();
        return list.OfType<JsonValue>()
            .Select(x => x.TryGetValue<string>(out var s) ? s : "")
            .Where(x => x.Length > 0).ToArray();
    }

    private static string GetStateBackgroundId(JsonObject? data)
    {
        if (data == null) return "";
        return GetString("path") is { Length: > 0 } path ? path : GetString("id");
        string GetString(string key)
            => data[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    }
}
