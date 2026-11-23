namespace FreeShow.Native.Core;

/// <summary>
/// Optional read-only source song catalog. The FreeShow show ID is stored in
/// each file's JSON tuple, not inferred from a filename. The first lookup
/// scans the selected Shows directory once and retains only ID/path pairs;
/// source files remain untouched. This is developer-operator infrastructure,
/// not the live performance-sensitive song-switch path.
/// </summary>
public sealed class FreeShowReadOnlyShowCatalog
{
    private readonly string folder;
    private readonly SemaphoreSlim scanLock = new(1, 1);
    private readonly Dictionary<string, string> paths = new(StringComparer.Ordinal);
    private readonly List<string> warnings = new();
    private bool indexed;

    public FreeShowReadOnlyShowCatalog(string freeShowDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(freeShowDataRoot);
        folder = ShowLibrary.ShowsFolder(freeShowDataRoot);
    }

    public IReadOnlyList<string> Warnings
    {
        get { lock (warnings) return warnings.ToArray(); }
    }

    public async Task<ShowDocument?> FindAsync(string showId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(showId);
        await EnsureIndexedAsync(cancellationToken);
        if (!paths.TryGetValue(showId, out var path)) return null;
        return await ShowDocument.ReadAsync(path, cancellationToken);
    }

    public async Task EnsureIndexedAsync(CancellationToken cancellationToken = default)
    {
        if (indexed) return;
        await scanLock.WaitAsync(cancellationToken);
        try
        {
            if (indexed) return;
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException("Original FreeShow Shows folder missing: " + folder);

            foreach (var path in Directory.EnumerateFiles(folder, "*.show")
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var file = await ShowDocument.ReadAsync(path, cancellationToken);
                    if (paths.TryGetValue(file.ShowId, out var existing))
                    {
                        lock (warnings)
                            warnings.Add("Duplicate show ID " + file.ShowId +
                                " found in " + Path.GetFileName(existing) + " and " +
                                Path.GetFileName(path) + "; first is retained.");
                        continue;
                    }
                    paths.Add(file.ShowId, path);
                }
                catch (Exception ex) when (ex is IOException or
                    System.Text.Json.JsonException or UnauthorizedAccessException)
                {
                    lock (warnings)
                        warnings.Add("Unreadable .show file: " + Path.GetFileName(path) +
                            " (" + ex.GetType().Name + ")");
                }
            }
            indexed = true;
        }
        finally { scanLock.Release(); }
    }
}
