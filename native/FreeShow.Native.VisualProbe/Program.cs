using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FreeShow.Native.Core;
using FreeShow.Native.Rendering;

namespace FreeShow.Native.VisualProbe;

/// <summary>
/// Developer-only visual comparison harness, NOT the ported FreeShow UI.
/// It never modifies the source .show file.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool useOriginalStyle = args.Length == 5 && args[1] == "--data-root" && args[3] == "--style";
        if (args.Length != 1 && !useOriginalStyle)
        {
            Console.Error.WriteLine("Usage: FreeShow.Native.VisualProbe <source.show>");
            Console.Error.WriteLine("   or: FreeShow.Native.VisualProbe <source.show> --data-root <FreeShow folder> --style <output style id>");
            return 2;
        }

        try
        {
            var source = ShowDocument.ReadAsync(args[0]).GetAwaiter().GetResult();
            ProbeApp.Frames = NativeRenderPlanner.Prepare(source);
            if (useOriginalStyle)
            {
                var dataRoot = args[2];
                var settings = FreeShowVisualSettings.OpenAsync(dataRoot).GetAwaiter().GetResult();
                // Apply original FreeShow lyric template styles to a temporary
                // output view, never to the original .show data.
                ProbeApp.Frames = FreeShowStyledRenderPlanner.Prepare(source, settings, args[4]);
                ProbeApp.OutputPlans = FreeShowOutputLayerPlanner.Prepare(source, settings, args[4]);
                ProbeApp.LocalStillImagePath = id => FreeShowMediaLookup.ExistingStillImagePath(
                    FreeShowMediaLookup.Resolve(source, id), dataRoot);
            }
            return AppBuilder.Configure<ProbeApp>()
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(Array.Empty<string>());
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.GetType().Name + ": " + e.Message);
            return 1;
        }
    }
}

public sealed class ProbeApp : Application
{
    internal static IReadOnlyList<NativeFramePlan> Frames { get; set; } = [];
    internal static IReadOnlyList<FreeShowOutputLayerPlan>? OutputPlans { get; set; }
    internal static Func<string, string?>? LocalStillImagePath { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var presenter = new NativeSlidePresenter();
            presenter.Prepare(Frames, OutputPlans, LocalStillImagePath);
            var status = new TextBlock
            {
                Foreground = Brushes.Gold,
                Margin = new Thickness(12),
                TextWrapping = TextWrapping.Wrap
            };
            int index = -1;
            void Show(int next)
            {
                if (!presenter.Show(next)) return;
                index = next;
                var plan = Frames[index];
                var layer = OutputPlans?[index];
                var warnings = layer == null
                    ? plan.Unsupported
                    : plan.Unsupported.Concat(layer.Unsupported).Distinct().ToArray();
                status.Text = $"Native comparison probe | {index + 1}/{Frames.Count} " +
                    $"| {plan.Source.Group} | Source {plan.Source.SlideId} " +
                    $"| Template {layer?.Template.TemplateId ?? "(none)"} | " +
                    $"Unsupported: {string.Join(", ", warnings)}";
            }

            var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            grid.Children.Add(presenter);
            Grid.SetRow(status, 1);
            grid.Children.Add(status);

            var window = new Window
            {
                Title = "FreeShow Native - internal renderer comparison (not production UI)",
                Width = 1280, Height = 800,
                Background = Brushes.Black,
                Content = grid
            };
            window.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Right) { Show(index + 1); e.Handled = true; }
                if (e.Key == Key.Left) { Show(index - 1); e.Handled = true; }
            };
            desktop.MainWindow = window;
            if (Frames.Count > 0) Show(0);
            else status.Text = "No enabled slides found in selected FreeShow show.";
        }
        base.OnFrameworkInitializationCompleted();
    }
}
