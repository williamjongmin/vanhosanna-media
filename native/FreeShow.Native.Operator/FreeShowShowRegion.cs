using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeShow.Native.Core;

namespace FreeShow.Native.Operator;

/// <summary>
/// Native host for source Show.svelte -> Slides.svelte -> ShowHeader.svelte.
/// The *current supported native region* is original show title and the
/// source-derived slide card grid. Does not fabricate arrangement editing,
/// effects, show notes or timeline actions, which remain unported.
/// </summary>
public sealed class FreeShowShowRegion : UserControl
{
    private readonly ShowDocument sourceShow;
    private readonly TextBlock title = new()
    {
        FontSize = 16,
        FontWeight = FontWeight.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly TextBlock arrangement = new()
    {
        FontSize = 12,
        Opacity = 0.65,
        VerticalAlignment = VerticalAlignment.Center
    };

    public FreeShowSlideCards Slides { get; }
    public string ShowId { get; }

    public FreeShowShowRegion(ShowDocument source, string? projectLayoutId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        sourceShow = source;
        ShowId = source.ShowId;
        Slides = new FreeShowSlideCards(source);
        string effectiveLayoutId = source.ActiveLayoutId;
        // project.ts openProjectItem honors the saved per-item layout, only
        // when that arrangement still exists in the selected source show.
        if (!string.IsNullOrEmpty(projectLayoutId) &&
            ShowDocument.Property(source.Layouts, projectLayoutId).ValueKind ==
                System.Text.Json.JsonValueKind.Object)
        {
            effectiveLayoutId = projectLayoutId;
            if (effectiveLayoutId != source.ActiveLayoutId)
                Slides.LoadLayout(effectiveLayoutId);
        }
        title.Text = source.Name;

        var currentLayout = source.GetLayout(effectiveLayoutId);
        var layoutTitle = ShowDocument.String(currentLayout, "name");
        var layouts = source.Layouts.EnumerateObject().ToArray();
        arrangement.Text = layouts.Length > 1 ? layoutTitle : "";

        var header = new Grid
        {
            Height = 36,
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Brushes.Transparent
        };
        Grid.SetColumn(title, 0);
        Grid.SetColumn(arrangement, 1);
        header.Children.Add(title);
        header.Children.Add(arrangement);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("36,*")
        };
        Grid.SetRow(header, 0);
        Grid.SetRow(Slides, 1);
        root.Children.Add(header);
        root.Children.Add(Slides);
        Content = root;
    }

    /// <summary>
    /// Developer-only inspect of the original arrow/Home/End navigation
    /// for this selected Show. Does not change Native cards, output state,
    /// project history or real worship output.
    /// </summary>
    public FreeShowSlideAdvanceResult InspectNavigation(
        System.Text.Json.JsonElement originalOutSlide,
        string originalOutputId,
        FreeShowSlideAdvanceDirection direction,
        int? minimumStyleLines,
        Func<string, string, bool?>? bindingResolver = null)
    {
        // A project arrangement may differ from the .show active layout.
        // Require the inspected output to name that arrangement explicitly.
        var requested = ShowDocument.String(originalOutSlide, "layout");
        if (requested.Length > 0 && requested != Slides.LayoutId)
            return new(FreeShowSlideAdvanceKind.Refused, "", "", null,
                default, "The operator is displaying a different source arrangement.", true);
        return FreeShowSlideAdvancePlanner.Prepare(
            sourceShow, originalOutSlide, originalOutputId, direction,
            minimumStyleLines, bindingResolver);
    }
}
