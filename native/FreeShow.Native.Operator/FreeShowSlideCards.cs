using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeShow.Native.Core;
using FreeShow.Native.Rendering;

namespace FreeShow.Native.Operator;

/// <summary>
/// Source-derived operator slide card region for Show/Slides.svelte and
/// slide/Slide.svelte. Real FreeShow show data, grouping, slide order, rich
/// text and disabled state are used. The pre-existing native renderer is
/// reused for thumbnails, not a newly designed presentation engine.
///
/// Does not claim exact Svelte visual parity. Original actions, click-reveal,
/// overlays, drag editing, group shortcuts, lazy virtualization, audio and
/// project switching need complete source-specific ports before release.
/// </summary>
public sealed class FreeShowSlideCards : UserControl
{
    private readonly ShowDocument show;
    private readonly ScrollViewer scroller;
    private readonly WrapPanel tiles = new()
    {
        Orientation = Orientation.Horizontal
    };
    private readonly List<Button> buttons = new();
    private IReadOnlyList<SlideFrame> frames = [];
    private int columns = 4;
    private string layoutId;
    private int? activeSlideIndex;
    private Action<NativeSlideCueSelection>? outputCueHandler;

    public int Columns => columns;
    public string LayoutId => layoutId;
    public int SlideCount => frames.Count;

    public FreeShowSlideCards(ShowDocument original)
    {
        show = original ?? throw new ArgumentNullException(nameof(original));
        layoutId = show.ActiveLayoutId;
        scroller = new ScrollViewer
        {
            Content = tiles,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        scroller.SizeChanged += (_, _) => ResizeCards();
        Content = scroller;
        LoadLayout(layoutId);
    }

    public void LoadLayout(string sourceLayoutId)
    {
        if (ShowDocument.Property(show.Layouts, sourceLayoutId).ValueKind !=
            System.Text.Json.JsonValueKind.Object)
            throw new InvalidDataException("Unknown original FreeShow layout ID: " + sourceLayoutId);

        layoutId = sourceLayoutId;
        frames = SlideProjection.ReadFrames(show, layoutId, includeDisabled: true);
        var prepared = NativeRenderPlanner.Prepare(show, layoutId, includeDisabled: true);
        if (prepared.Count != frames.Count)
            throw new InvalidDataException("FreeShow slide order does not match native thumbnails.");

        buttons.Clear();
        tiles.Children.Clear();
        activeSlideIndex = null;

        for (int i = 0; i < prepared.Count; i++)
        {
            var plan = prepared[i];
            var originalFrame = frames[i];
            if (plan.Source.SlideId != originalFrame.SlideId ||
                plan.Source.LayoutIndex != originalFrame.LayoutIndex)
                throw new InvalidDataException("Source slide and thumbnail index mismatch.");

            var preview = new NativeSlidePresenter
            {
                Height = 130,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            preview.Prepare([plan]);
            preview.Show(0);

            var thumbnail = new Border
            {
                Background = Brushes.Black,
                Height = 130,
                Child = preview
            };

            var caption = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Height = 24,
                Spacing = 8,
                Margin = new Thickness(5, 0)
            };
            caption.Children.Add(new TextBlock
            {
                Text = (i + 1).ToString(),
                Foreground = Brushes.LightGray,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11
            });
            if (!string.IsNullOrEmpty(originalFrame.Group))
                caption.Children.Add(new TextBlock
                {
                    Text = originalFrame.Group,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = Brushes.DeepPink,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeight.Bold,
                    FontSize = 11
                });

            // The original Slide.svelte gives a two-pixel outer margin and
            // uses a dark slide with source group label and active outline.
            var content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 0,
                Opacity = originalFrame.Disabled ? 0.2 : 1.0
            };
            content.Children.Add(thumbnail);
            content.Children.Add(caption);

            var index = i;
            var button = new Button
            {
                Content = content,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Padding = new Thickness(2),
                Margin = new Thickness(2),
                IsEnabled = false, // Cannot trigger a partial live-show action.
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            button.Click += (_, _) =>
            {
                if (outputCueHandler == null || originalFrame.Disabled) return;
                outputCueHandler(new NativeSlideCueSelection(
                    show.ShowId, layoutId, originalFrame.LayoutIndex,
                    originalFrame.SlideId, originalFrame.RawLayoutData,
                    originalFrame.RawSlide));
            };
            buttons.Add(button);
            tiles.Children.Add(button);
        }
        RefreshEnabled();
        ResizeCards();
    }

    /// <summary>
    /// Only attach this when the host can execute the original complete
    /// FreeShow Slides.svelte click path (including actions, line reveal,
    /// overlay and background state) — never merely increment cue index.
    /// </summary>
    public void BindSourceCompatibleCueHandler(Action<NativeSlideCueSelection>? handler)
    {
        outputCueHandler = handler;
        RefreshEnabled();
    }

    public void SetGridColumns(int newColumns)
    {
        if (newColumns is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(newColumns));
        columns = newColumns;
        ResizeCards();
    }

    public void SetActiveSlideIndex(int? index)
    {
        activeSlideIndex = index;
        for (int i = 0; i < buttons.Count; i++)
            buttons[i].BorderBrush = frames[i].LayoutIndex == index
                ? Brushes.DeepPink : Brushes.Transparent;
    }

    private void RefreshEnabled()
    {
        for (int i = 0; i < buttons.Count; i++)
            buttons[i].IsEnabled = outputCueHandler != null && !frames[i].Disabled;
    }

    private void ResizeCards()
    {
        var available = scroller.Bounds.Width;
        if (available <= 0) available = 1200;
        // Original Slides.svelte uses width 100 / columns percent, with
        // 2 px outer padding for each card.
        var width = Math.Max(80, (available - 20) / columns);
        foreach (var b in buttons)
            b.Width = width;
    }
}

public sealed record NativeSlideCueSelection(
    string ShowId, string LayoutId, int LayoutIndex, string SlideId,
    System.Text.Json.JsonElement RawLayoutData, System.Text.Json.JsonElement RawSlide);
