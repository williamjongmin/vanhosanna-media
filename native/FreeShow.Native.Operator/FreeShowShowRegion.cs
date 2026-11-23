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
}
