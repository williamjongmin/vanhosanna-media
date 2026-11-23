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
            {
                OperatorProbeApp.ReadOnlyCatalog = new FreeShowReadOnlyShowCatalog(dataRoot);
                OperatorProbeApp.Projects =
                    FreeShowProjectLibrary.OpenAsync(dataRoot)
                        .GetAwaiter().GetResult();
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
    internal static FreeShowReadOnlyShowCatalog? ReadOnlyCatalog { get; set; }

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
            var browser = Projects != null ? new FreeShowProjectBrowser(Projects) : null;
            Control projectsRegion = browser ?? (Control)new Border();

            void DisplaySelectedShow(ShowDocument? selected, string? arrangementId = null)
            {
                // The chosen source item may be a section or media file.
                // Those types must not be shown using an unrelated lyric show.
                Control center = selected == null
                    ? new Border() : new FreeShowShowRegion(selected, arrangementId);
                workspace.RegisterPageRegions("show",
                    new FreeShowPageRegions(projectsRegion, center, null));
                if (LocalOutputSettings != null)
                    workspace.RightPreviewHost.Content =
                        new FreeShowPreviewRegion(LocalOutputSettings, selected);
            }

            workspace.RegisterPageRegions("show",
                new FreeShowPageRegions(projectsRegion, originalShow, null));
            if (LocalOutputSettings != null)
                workspace.RightPreviewHost.Content =
                    new FreeShowPreviewRegion(LocalOutputSettings, Source);

            int selectionVersion = 0;
            CancellationTokenSource? selectionCancellation = null;
            if (browser != null)
            {
                browser.ItemSelected += async (_, args) =>
                {
                    // User-selection-only operation. No output, writes, history,
                    // media playback or FreeShow global store mutation.
                    selectionCancellation?.Cancel();
                    var nextRequest = new CancellationTokenSource();
                    selectionCancellation = nextRequest;
                    int myVersion = ++selectionVersion;

                    ShowDocument? selected = null;
                    try
                    {
                        if (args.Item.IsShow)
                        {
                            if (args.Item.Id == Source.ShowId)
                                selected = Source;
                            else if (ReadOnlyCatalog != null)
                                selected = await Task.Run(
                                    () => ReadOnlyCatalog.FindAsync(
                                        args.Item.Id, nextRequest.Token),
                                    nextRequest.Token);
                        }

                        if (myVersion == selectionVersion &&
                            !nextRequest.IsCancellationRequested)
                            DisplaySelectedShow(selected, args.Item.LayoutId);
                    }
                    catch (OperationCanceledException) { /* newer selection wins */ }
                    catch (Exception error)
                    {
                        Console.Error.WriteLine("Read-only project item load: " +
                            error.GetType().Name + ": " + error.Message);
                        if (myVersion == selectionVersion)
                            DisplaySelectedShow(null);
                    }
                    finally
                    {
                        if (ReferenceEquals(selectionCancellation, nextRequest))
                            selectionCancellation = null;
                        nextRequest.Dispose();
                    }
                };
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
            window.Closed += (_, _) =>
            {
                selectionVersion++;
                selectionCancellation?.Cancel();
            };
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
