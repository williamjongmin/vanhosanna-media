using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Read-only media lookup corresponding to Show.media[id]. Some output references
/// are file paths and others are IDs. Keep both; do not rewrite church media.
/// </summary>
public static class FreeShowMediaLookup
{
    private static readonly HashSet<string> StillExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".m4v" };

    public static FreeShowMediaReference Resolve(ShowDocument show, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return new(reference, "", NativeMediaKind.None, false, false, default);

        var source = ShowDocument.Property(show.Media, reference);
        var path = ShowDocument.String(source, "path");
        if (path.Length == 0) path = reference;

        var kind = Classify(path, ShowDocument.String(source, "type"));
        return new(reference, path, kind,
            ShowDocument.IsTrue(source, "loop"), ShowDocument.IsTrue(source, "muted"), source);
    }

    public static NativeMediaKind Classify(string path, string type)
    {
        if (type.Equals("camera", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("screen", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("ndi", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("omt", StringComparison.OrdinalIgnoreCase))
            return NativeMediaKind.LiveInput;
        if (type.Equals("player", StringComparison.OrdinalIgnoreCase))
            return NativeMediaKind.OnlinePlayer;

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
            return NativeMediaKind.OnlinePlayer;

        // Ignore URL query parameters for extension-based classification.
        var noQuery = path.Split('?', '#')[0];
        var extension = Path.GetExtension(noQuery);
        if (StillExtensions.Contains(extension)) return NativeMediaKind.Image;
        if (VideoExtensions.Contains(extension)) return NativeMediaKind.Video;
        if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".flac", StringComparison.OrdinalIgnoreCase))
            return NativeMediaKind.Audio;

        // The "media" type is intentionally ambiguous in original FreeShow.
        return NativeMediaKind.Unknown;
    }

    /// <summary>
    /// Resolve a local video path for native decoding. Never accept a remote URL,
    /// media ID without a file, or an unsupported media kind as a video.
    /// </summary>
    public static string? ExistingLocalVideoPath(
        FreeShowMediaReference media, string freeShowDataRoot)
    {
        if (media.Kind != NativeMediaKind.Video) return null;
        return ExistingLocalMediaPath(media.Path, freeShowDataRoot);
    }

    private static string? ExistingLocalMediaPath(string path, string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile) path = uri.LocalPath;
            else if (uri.Scheme is "http" or "https") return null;
        }

        try
        {
            if (!Path.IsPathFullyQualified(path))
                path = Path.Combine(dataRoot, path);
            var full = Path.GetFullPath(path);
            return File.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                   or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve only a real local still image, for the prototype bitmap layer.
    /// Other source references remain in the media metadata for later engines.
    /// </summary>
    public static string? ExistingStillImagePath(
        FreeShowMediaReference media, string freeShowDataRoot)
    {
        if (media.Kind != NativeMediaKind.Image) return null;
        var path = media.Path;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
            path = uri.LocalPath;
        if (!Path.IsPathFullyQualified(path))
            path = Path.Combine(freeShowDataRoot, path);
        try
        {
            path = Path.GetFullPath(path);
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

public enum NativeMediaKind { None, Image, Video, Audio, OnlinePlayer, LiveInput, Unknown }

public sealed record FreeShowMediaReference(
    string Id, string Path, NativeMediaKind Kind, bool Loop, bool Muted, JsonElement Raw);
