using Avalonia.Media.Imaging;

namespace FreeShow.Native.Rendering;

/// <summary>
/// Prototype still-image decoder. Decode once in preparation, not on a slide
/// change. Production implementation needs async decode, bounded memory and
/// an explicit media lifecycle; video is intentionally unsupported here.
/// </summary>
public sealed class NativeStillImageCache : IDisposable
{
    private readonly Dictionary<string, Bitmap> cache =
        new(StringComparer.OrdinalIgnoreCase);
    public int Count => cache.Count;

    public bool TryGet(string? path, out Bitmap? image)
    {
        image = null;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        if (!cache.TryGetValue(path, out var cached))
        {
            var info = new FileInfo(path);
            if (info.Length > 128L * 1024 * 1024) return false;
            try
            {
                cached = new Bitmap(path);
                cache[path] = cached;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                         or ArgumentException or NotSupportedException)
            {
                return false;
            }
        }
        image = cached;
        return true;
    }

    public void Dispose()
    {
        foreach (var bitmap in cache.Values)
            bitmap.Dispose();
        cache.Clear();
    }
}
