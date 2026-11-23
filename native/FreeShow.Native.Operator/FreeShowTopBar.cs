using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FreeShow.Native.Operator;

/// <summary>
/// Port of source Top.svelte / TopButton.svelte's visible 40px controls.
/// Same FreeShow SVG path geometry, order and original translation labels;
/// not a replacement feature or alternate navigation design.
/// Icons use flat FreeShow accent until full original theme gradient is ported.
/// </summary>
public sealed class FreeShowTopBar : UserControl
{
    private static readonly string[] SourceTopPages = ["show", "edit", "stage", "draw", "settings"];
    private readonly Dictionary<string, Button> buttons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> en = new(StringComparer.Ordinal)
    {
        ["show"] = "Show", ["edit"] = "Edit", ["stage"] = "Stage",
        ["draw"] = "Draw", ["settings"] = "Settings"
    };
    private readonly Dictionary<string, string> ko = new(StringComparer.Ordinal)
    {
        ["show"] = "쇼", ["edit"] = "편집", ["stage"] = "무대",
        ["draw"] = "그리다", ["settings"] = "설정"
    };

    private readonly Grid root = new()
    {
        Height = 40,
        ColumnDefinitions = new ColumnDefinitions("290,*,290")
    };
    private readonly StackPanel center = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        Spacing = 0
    };
    private readonly StackPanel right = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right,
        Spacing = 0
    };
    private readonly Button outputButton;
    private string currentPage = "show";
    private string language = "en";
    private bool labelsDisabled;

    public event EventHandler<FreeShowPageRequestedEventArgs>? PageRequested;
    public event EventHandler? OutputToggleRequested;

    /// <summary>
    /// Enables a page only if its real source-component ports are registered.
    /// Avoids opening fake Show/Edit/Stage screens that do not exist yet.
    /// </summary>
    public Func<string, bool>? CanOpenPage { get; set; }
    public bool DisplayRunning { get; private set; }

    public FreeShowTopBar()
    {
        var logo = new TextBlock
        {
            Text = "FreeShow",
            FontSize = 28.8, // original 1.8em with 16px base
            FontWeight = FontWeight.Bold,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 0)
        };
        Grid.SetColumn(logo, 0);
        root.Children.Add(logo);

        foreach (var page in SourceTopPages)
        {
            var button = MakePageButton(page);
            buttons.Add(page, button);
            if (page is "show" or "edit" or "stage")
                center.Children.Add(button);
            else
                right.Children.Add(button);
        }

        outputButton = new Button
        {
            MinWidth = 60,
            Height = 40,
            Content = FreeShowOriginalIconGeometry.Create("outputs", Brushes.White, 25.6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 2),
            IsEnabled = false, // Until actual display control has been wired.
            Padding = new Thickness(10, 2)
        };
        outputButton.Click += (_, _) => OutputToggleRequested?.Invoke(this, EventArgs.Empty);
        right.Children.Add(outputButton);

        Grid.SetColumn(center, 1);
        Grid.SetColumn(right, 2);
        root.Children.Add(center);
        root.Children.Add(right);
        Content = root;
        UpdateButtons();
    }

    /// <summary>
    /// Mirrors original translated menu.* strings from public/lang/en.json and
    /// ko.json. The complete FreeShow locale provider is still unported.
    /// </summary>
    public void SetLanguage(string locale)
    {
        language = locale.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ? "ko" : "en";
        RebuildPageButtons();
    }

    public void SetLabelsDisabled(bool disabled)
    {
        labelsDisabled = disabled;
        RebuildPageButtons();
    }

    public void SetCurrentPage(string page)
    {
        if (!SourceTopPages.Contains(page)) return;
        currentPage = page;
        UpdateButtons();
    }

    public void SetPagePermission(string page, bool allowed)
    {
        if (!buttons.TryGetValue(page, out var button)) return;
        button.Tag = allowed;
        UpdateButtons();
    }

    public void SetOutputControlAvailable(bool available, bool isRunning)
    {
        DisplayRunning = isRunning;
        outputButton.IsEnabled = available && OutputToggleRequested != null;
        outputButton.Content = FreeShowOriginalIconGeometry.Create(
            isRunning ? "cancelDisplay" : "outputs", Brushes.White, 25.6);
        outputButton.BorderBrush = isRunning
            ? Brushes.IndianRed : Brushes.Transparent;
    }

    public void RefreshPageAvailability() => UpdateButtons();

    private Button MakePageButton(string page)
    {
        var button = new Button
        {
            Tag = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = Brushes.Transparent,
            Padding = new Thickness(12, 4),
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) =>
        {
            if (!button.IsEnabled) return;
            PageRequested?.Invoke(this, new FreeShowPageRequestedEventArgs(page));
        };
        return button;
    }

    private void RebuildPageButtons()
    {
        foreach (var page in SourceTopPages)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(FreeShowOriginalIconGeometry.Create(
                page, page == currentPage ? Brushes.White : Brushes.DeepPink, 24));
            if (!labelsDisabled && page is "show" or "edit" or "stage")
            {
                panel.Children.Add(new TextBlock
                {
                    Text = language == "ko" ? ko[page] : en[page],
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            buttons[page].Content = panel;
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        foreach (var (page, button) in buttons)
        {
            var sourcePermission = button.Tag is bool p && p;
            button.IsEnabled = sourcePermission && CanOpenPage?.Invoke(page) == true;
            button.BorderBrush = page == currentPage
                ? Brushes.DeepPink : Brushes.Transparent;
            // Original icon uses white currentColor when selected.
            if (button.Content is StackPanel panel && panel.Children.Count > 0)
                panel.Children[0] = FreeShowOriginalIconGeometry.Create(
                    page, page == currentPage ? Brushes.White : Brushes.DeepPink, 24);
        }
    }
}

public sealed record FreeShowPageRequestedEventArgs(string PageId);
