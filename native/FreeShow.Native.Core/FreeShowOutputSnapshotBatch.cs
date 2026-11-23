using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Coherent read-only snapshot of the original OutData for each enabled
/// normal/stage output. No FreeShow data is written. It refuses a batch if
/// any output's acknowledged native revision changed while snapshots were
/// collected, rather than combining a new slide with old reveal state.
/// </summary>
public static class FreeShowOutputSnapshotBatch
{
    public static FreeShowCapturedOutputBatch Capture(
        FreeShowOutputConfiguration configuration,
        IReadOnlyDictionary<string, FreeShowOutputState> originalStates)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(originalStates);

        var captured = new Dictionary<string, FreeShowCapturedOutput>(
            StringComparer.Ordinal);
        var issues = new List<string>();
        var enabled = configuration.All.Where(x => x.Enabled).ToArray();

        foreach (var output in enabled)
        {
            if (!originalStates.TryGetValue(output.Id, out var source))
            {
                issues.Add("Missing acknowledged original OutData for output " + output.Id);
                continue;
            }
            long before = source.Revision;
            JsonElement state;
            try { state = source.Snapshot().Clone(); }
            catch (Exception exception)
            {
                issues.Add("Original output snapshot failed for " + output.Id + ": " +
                    exception.Message);
                continue;
            }

            if (source.Revision != before)
            {
                issues.Add("Original output changed while snapshotting " + output.Id);
                continue;
            }

            captured.Add(output.Id, new(output.Id, before, state));
        }

        // Verify the entire captured batch after reading the final output;
        // do not trust a mixture of pre-/post-command versions.
        foreach (var output in enabled)
        {
            if (captured.TryGetValue(output.Id, out var frozen) &&
                (!originalStates.TryGetValue(output.Id, out var live) ||
                 live.Revision != frozen.Revision))
                issues.Add("Original output revision changed during batch capture: " + output.Id);
        }

        return new(captured, issues.AsReadOnly());
    }
}

public sealed record FreeShowCapturedOutput(
    string OutputId, long Revision, JsonElement OutData)
{
    public JsonElement OutSlide => ShowDocument.Property(OutData, "slide");
}

public sealed record FreeShowCapturedOutputBatch(
    IReadOnlyDictionary<string, FreeShowCapturedOutput> Outputs,
    IReadOnlyList<string> Problems)
{
    public bool IsCoherent => Problems.Count == 0;

    public IReadOnlyDictionary<string, JsonElement> OutDataById =>
        Outputs.ToDictionary(x => x.Key, x => x.Value.OutData, StringComparer.Ordinal);
}
