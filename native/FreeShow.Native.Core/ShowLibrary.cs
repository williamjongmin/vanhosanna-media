namespace FreeShow.Native.Core;

public sealed record ShowLibraryResult(string FileName, string ShowId, string Name,
    int Slides, IReadOnlyList<string> Warnings, string? Error);

/// <summary>
/// Read-only folder access. FreeShow uses <data root>/Shows/*.show, configured by
/// src/electron/utils/files.ts. The caller must choose the root; do not infer or
/// modify FreeShow's custom dataPath setting.
/// </summary>
public static class ShowLibrary
{
    public static string ShowsFolder(string freeShowDataRoot) => Path.Combine(freeShowDataRoot, "Shows");

    public static async Task<IReadOnlyList<ShowLibraryResult>> InspectFolderAsync(
        string folder, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException("FreeShow Shows folder not found: " + folder);

        var results = new List<ShowLibraryResult>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.show", SearchOption.TopDirectoryOnly)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var show = await ShowDocument.ReadAsync(path, cancellationToken);
                var warnings = show.ValidateLayout();
                results.Add(new ShowLibraryResult(
                    Path.GetFileName(path), show.ShowId, show.Name,
                    show.ReadLayoutEntries().Count, warnings, null));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                       or System.Text.Json.JsonException or ArgumentException)
            {
                results.Add(new ShowLibraryResult(
                    Path.GetFileName(path), "", "", 0, Array.Empty<string>(),
                    ex.GetType().Name + ": " + ex.Message));
            }
        }
        return results.AsReadOnly();
    }
}
