using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using FreeShow.Native.Core;
using FreeShow.Native.Operator;

namespace FreeShow.Native.OperatorProbe;

/// <summary>
/// Developer-only, read-only FreeShow source UI comparison. NEVER starts live
/// output, saves shows, edits projects or changes original FreeShow settings.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1 || args.Length % 2 == 0)
        {
            Usage();
            return 2;
        }

        string? settingsPath = null;
        string? dataRoot = null;
        for (int i = 1; i < args.Length; i += 2)
        {
            if (args[i] == "--settings" && settingsPath == null)
                settingsPath = args[i + 1];
            else if (args[i] == "--data-root" && dataRoot == null)
                dataRoot = args[i + 1];
            else
            {
                Usage();
                return 2;
            }
        }

        try
        {
            OperatorProbeApp.Source =
                ShowDocument.ReadAsync(args[0]).GetAwaiter().GetResult();
            if (settingsPath != null)
                OperatorProbeApp.LocalOutputSettings =
                    FreeShowOutputConfiguration.ReadAsync(settingsPath)
                        .GetAwaiter().GetResult();
            if (dataRoot != null)
                OperatorProbeApp.Projects =
                    FreeShowProjectLibrary.OpenAsync(dataRoot)
                        .GetAwaiter().GetResult();

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

    private static void Usage()
    {
        Console.Error.WriteLine(
            "Usage: FreeShow.Native.OperatorProbe <copied-show.show> " +
            "[--settings <original-local-settings.json>] [--data-root <FreeShow-data-root>]");
        Console.Error.WriteLine("Read-only developer preview; not for worship output.");
    }
}

public sealed class OperatorProbeApp : Application
{
    internal static ShowDocument? Source { get; set; }
    internal static FreeShowOutputConfiguration? LocalOutputSettings { get; set; }
    internal static FreeShowProjectLibrary? Projects { get; set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Source == null)
                throw new InvalidOperationException("Show file was not loaded.");

            var state = FreeShowOperatorWorkspaceState.FromLocalSettings(
                LocalOutputSettings?.RawSettings ?? default);
            var workspace = new FreeShowOperatorWorkspace(state);
            var top = new FreeShowTopBar();
            workspace.InstallTopBar(top);

            var originalShow = new FreeShowShowRegion(Source);

            // Source Projects.svelte has a folder/project tree, not a separate
            // fabricated source of media. Keep the list purely read-only.
            Control projectsRegion = Projects != null
                ? new FreeShowProjectTree(Projects) : new Border();

            workspace.RegisterPageRegions("show",
                new FreeShowPageRegions(projectsRegion, originalShow, null));

            if (LocalOutputSettings != null)
            {
                // Preview source uses the original SETTINGS output IDs and
                // present OutData.slide references, not a fake preview stream.
                workspace.RightPreviewHost.Content =
                    new FreeShowMultiOutputPreview(LocalOutputSettings, Source);
            }

            // Original Slide.svelte live click runs custom actions, reveal,
            // backgrounds and overlays; keep developer cards non-live.
            var window = new Window
            {
                Title = "FreeShow Native - source operator comparison (read-only)",
                Width = 1600, Height = 900,
                Background = Brushes.Black,
                Content = workspace
            };
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
