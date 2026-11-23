using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using FreeShow.Native.Core;
using FreeShow.Native.Operator;

namespace FreeShow.Native.OperatorProbe;

/// <summary>
/// Developer-only, read-only workspace comparison. NOT a beta application.
/// It is deliberately missing unported project tree, editing, preview controls,
/// media, menus, original theme styles and all live output interaction.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is not (1 or 3) ||
            (args.Length == 3 && args[1] != "--settings"))
        {
            Console.Error.WriteLine("Usage: FreeShow.Native.OperatorProbe <original.show>");
            Console.Error.WriteLine("   or: FreeShow.Native.OperatorProbe <original.show> --settings <local-SETTINGS.json>");
            return 2;
        }

        try
        {
            OperatorProbeApp.Source = ShowDocument.ReadAsync(args[0]).GetAwaiter().GetResult();
            if (args.Length == 3)
            {
                OperatorProbeApp.LocalOutputSettings =
                    FreeShowOutputConfiguration.ReadAsync(args[2]).GetAwaiter().GetResult();
            }
            return AppBuilder.Configure<OperatorProbeApp>()
                .UsePlatformDetect().LogToTrace()
                .StartWithClassicDesktopLifetime(Array.Empty<string>());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }
}

public sealed class OperatorProbeApp : Application
{
    internal static ShowDocument? Source { get; set; }
    internal static FreeShowOutputConfiguration? LocalOutputSettings { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Source == null) throw new InvalidOperationException("Show file was not loaded.");
            var state = FreeShowOperatorWorkspaceState.FromLocalSettings(
                LocalOutputSettings?.RawSettings ?? default);
            var workspace = new FreeShowOperatorWorkspace(state);
            var top = new FreeShowTopBar();
            workspace.InstallTopBar(top);

            // Only a part of Show.svelte is ported. Do not populate other
            // FreeShow page areas with replacement or fabricated controls.
            var originalShow = new FreeShowShowRegion(Source);
            workspace.RegisterPageRegions("show",
                new FreeShowPageRegions(new Border(), originalShow, null));

            // Never bind SendToOutput here. Original Slide.svelte click triggers
            // actions, line-reveal, backgrounds and overlays, not simply index++.
            // Keep the developer cards non-live until entire source path is ported.
            var window = new Window
            {
                Title = "FreeShow Native - internal operator workspace comparison only",
                Width = 1600, Height = 900,
                Background = Brushes.Black,
                Content = workspace
            };
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
