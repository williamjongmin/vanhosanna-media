using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FreeShow.Native.Core;

/// <summary>
/// Explicit copy-only editing of FreeShow .show tuples [id, Show].
/// Based on src/electron/data/save.ts: saveShow uses JSON.stringify([id, value]).
/// Existing shows are NEVER opened for writing. Unknown fields are retained
/// as JSON nodes; only the requested property is changed.
/// </summary>
public sealed class FreeShowShowExport
{
    private readonly ShowDocument source;
    private readonly JsonArray envelope;
    private readonly JsonObject show;
    private bool changed;

    public string ShowId => source.ShowId;
    public bool IsModified => changed;

    public FreeShowShowExport(ShowDocument source)
    {
        if (!source.IsFreeShowFileEnvelope)
            throw new InvalidDataException("Copy-edit requires an original [id, Show] FreeShow file.");

        this.source = source;
        var parsed = JsonNode.Parse(Encoding.UTF8.GetString(source.OriginalBytes()));
        if (parsed is not JsonArray arr || arr.Count != 2 || arr[1] is not JsonObject root)
            throw new InvalidDataException("Invalid FreeShow show envelope.");

        envelope = arr;
        show = root;
    }

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("FreeShow show title must not be empty.", nameof(newName));
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Name contains invalid filename characters.", nameof(newName));

        if (show["name"]?.GetValue<string>() == newName) return;
        show["name"] = newName;
        changed = true;
        TouchModified();
    }

    /// <summary>
    /// Changes only one existing rich-text run's value. Never coalesces runs,
    /// removes unsupported styling, changes slide IDs or reorders children.
    /// </summary>
    public void ReplaceTextRun(string slideId, int itemIndex, int lineIndex,
        int runIndex, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slideId);
        ArgumentNullException.ThrowIfNull(value);

        if (show["slides"] is not JsonObject slides || slides[slideId] is not JsonObject slide ||
            slide["items"] is not JsonArray items ||
            !ValidIndex(items, itemIndex) || items[itemIndex] is not JsonObject item ||
            item["lines"] is not JsonArray lines ||
            !ValidIndex(lines, lineIndex) || lines[lineIndex] is not JsonObject line ||
            line["text"] is not JsonArray runs ||
            !ValidIndex(runs, runIndex) || runs[runIndex] is not JsonObject run)
            throw new ArgumentOutOfRangeException(nameof(slideId), "Source text run does not exist.");

        if (run["value"]?.GetValue<string>() == value) return;
        run["value"] = value;
        changed = true;
        TouchModified();
    }

    private static bool ValidIndex(JsonArray array, int index) =>
        index >= 0 && index < array.Count;

    private void TouchModified()
    {
        // src/frontend/components/helpers/shows.ts touchShowModified() uses Date.now().
        // Leave unknown timestamp keys and the original created/used values intact.
        if (show["timestamps"] is not JsonObject timestamps)
        {
            timestamps = new JsonObject
            {
                ["created"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["modified"] = null,
                ["used"] = null
            };
            show["timestamps"] = timestamps;
        }
        timestamps["modified"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public byte[] ToShowBytes()
    {
        // A true no-op export stays byte-for-byte identical, including whitespace.
        // Modified JSON is normalized, but preserves all semantically distinct
        // fields, unknown nodes, rich text run boundaries and IDs.
        var bytes = changed
            ? Encoding.UTF8.GetBytes(envelope.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = false
            }))
            : source.OriginalBytes();

        var parsed = ShowDocument.Parse(bytes);
        if (!parsed.IsFreeShowFileEnvelope || parsed.ShowId != source.ShowId)
            throw new InvalidDataException("Exported FreeShow show failed identity validation.");
        return bytes;
    }

    /// <summary>
    /// Exclusive-create .show copy. NEVER overwrite an existing file or source.
    /// The target directory must already exist; production UI should request an
    /// explicit export destination rather than silently reusing the song folder.
    /// </summary>
    public async Task ExportNewFileAsync(string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var path = Path.GetFullPath(destinationPath);
        if (!Path.GetExtension(path).Equals(".show", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("FreeShow export destination must have .show extension.",
                nameof(destinationPath));

        var bytes = ToShowBytes();
        bool created = false;
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 16384, FileOptions.Asynchronous);
            created = true;
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch
        {
            // Only remove a partial file we successfully created ourselves.
            // FileMode.CreateNew never truncates an existing source.
            if (created)
            {
                try { File.Delete(path); }
                catch (IOException) { /* report the original failure */ }
                catch (UnauthorizedAccessException) { /* report the original failure */ }
            }
            throw;
        }
    }
}
