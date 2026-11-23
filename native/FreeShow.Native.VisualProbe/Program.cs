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
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: dotnet run --project native/FreeShow.Native.VisualProbe -- <source.show>");
            return 2;
        }

        try
        {
            var source = ShowDocument.ReadAsync(args[0]).GetAwaiter().GetResult();
            ProbeApp.Frames = NativeRenderPlanner.Prepare(source);
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

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var presenter = new NativeSlidePresenter();
            presenter.Prepare(Frames);
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
                status.Text = $"Native comparison probe | {index + 1}/{Frames.Count} " +
                    $"| {plan.Source.Group} | Source {plan.Source.SlideId} " +
                    $"| Unsupported features: {string.Join(", ", plan.Unsupported)}";
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
