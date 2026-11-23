using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Read-only source project-boundary subset of OutputHelper.changeProjectItem.
/// Original source indices are used even when the same song ID occurs twice.
/// The original operator's non-focus move selects the next project item,
/// whereas focus mode immediately attempts output actions. This planner
/// never performs those actions or changes source "played" flags.
/// </summary>
public static class FreeShowProjectAdvancePlanner
{
    public static FreeShowProjectAdvanceResult Prepare(
        FreeShowProjectContent content,
        JsonElement currentOutSlide,
        FreeShowProjectContentItem? activeItem,
        FreeShowSlideAdvanceDirection direction,
        bool focusMode, bool nextItemOnLastSlide,
        bool otherActiveOutputCanAdvance = false)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (direction is not (FreeShowSlideAdvanceDirection.Next or
            FreeShowSlideAdvanceDirection.Previous))
            return Refuse("Original Home/End applies to the current show, not project item selection.");
        if (!focusMode && !nextItemOnLastSlide)
            return Refuse("Original nextItemOnLastSlide setting disables project traversal.");
        if (otherActiveOutputCanAdvance)
            return new(FreeShowProjectAdvanceStatus.WaitForOtherOutputs,
                null, [], "Another FreeShow active output has not reached its show boundary.");

        int fromIndex = ResolveSourceIndex(content, currentOutSlide, activeItem);
        bool next = direction == FreeShowSlideAdvanceDirection.Next;
        int index = fromIndex + (next ? 1 : -1);
        var actions = new List<string>();

        while (true)
        {
            var item = content.ItemAtOriginalIndex(index);
            if (item == null)
                return new(FreeShowProjectAdvanceStatus.AtProjectBoundary,
                    null, actions.AsReadOnly(), "No further source project items.");

            // In focus mode source OutputHelper skips every section; when
            // moving backwards it also skips overlay project entries.
            bool skip = focusMode
                ? item.Type == "section" || !next && item.Type == "overlay"
                : item.Type == "section" && item.Notes.Length == 0;

            if (!skip)
                return new(FreeShowProjectAdvanceStatus.SelectProjectItem,
                    item, actions.AsReadOnly(), null);

            if (item.Type == "section")
            {
                // Source runSectionAction() can execute custom actions while
                // skipping a section. Record the need, never silently run it.
                var rawSettings = ShowDocument.Property(
                    ShowDocument.Property(item.Raw, "data"), "settings");
                var trigger = ShowDocument.String(rawSettings, "triggerAction");
                actions.Add(trigger.Length > 0 ? trigger : "<source-section-default-action>");
            }
            index += next ? 1 : -1;
        }
    }

    /// <summary>
    /// OutputHelper.getProjectItemIndex follows projectIndex first, then
    /// active selection when IDs/layouts agree, then the first matching
    /// entry at or after the active position, finally the first match.
    /// Duplicated show IDs must not collapse to a single project entry.
    /// </summary>
    public static int ResolveSourceIndex(
        FreeShowProjectContent content, JsonElement sourceOutSlide,
        FreeShowProjectContentItem? activeItem)
    {
        var indexElement = ShowDocument.Property(sourceOutSlide, "projectIndex");
        if (indexElement.ValueKind == JsonValueKind.Number &&
            indexElement.TryGetInt32(out var direct) && direct >= 0 &&
            content.ItemAtOriginalIndex(direct) != null)
            return direct;

        var id = ShowDocument.String(sourceOutSlide, "id");
        var layout = ShowDocument.String(sourceOutSlide, "layout");
        if (id.Length == 0) return activeItem?.SourceIndex ?? -1;

        if (activeItem != null && activeItem.Id == id &&
            (activeItem.LayoutId.Length == 0 || activeItem.LayoutId == layout))
            return activeItem.SourceIndex;

        var after = content.Items.FirstOrDefault(x => x.SourceIndex >=
            (activeItem?.SourceIndex ?? -1) && x.Id == id &&
            (x.LayoutId.Length == 0 || x.LayoutId == layout));
        if (after != null) return after.SourceIndex;

        return content.Items.FirstOrDefault(x => x.Id == id &&
            (x.LayoutId.Length == 0 || x.LayoutId == layout))?.SourceIndex ?? -1;
    }

    private static FreeShowProjectAdvanceResult Refuse(string reason) =>
        new(FreeShowProjectAdvanceStatus.Refused, null, [], reason);
}

public enum FreeShowProjectAdvanceStatus
{
    SelectProjectItem, WaitForOtherOutputs, AtProjectBoundary, Refused
}

public sealed record FreeShowProjectAdvanceResult(
    FreeShowProjectAdvanceStatus Status,
    FreeShowProjectContentItem? Target,
    IReadOnlyList<string> SourceSectionActions,
    string? Reason)
{
    // Even if this plan is source-correct, applying it may mark played,
    // invoke section actions, open project/show and start media. Those
    // complete source effects are not ported to the active operator.
    public bool CanDispatchLive => false;
}
