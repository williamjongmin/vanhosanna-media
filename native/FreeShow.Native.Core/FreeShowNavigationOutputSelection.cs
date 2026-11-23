namespace FreeShow.Native.Core;

// A timer advances one original output; a keyboard command advances the
// whole active group. Keep source ordering even for an explicit selection.
internal static class FreeShowNavigationOutputSelection
{
    public static IReadOnlyList<FreeShowConfiguredOutput> Select(
        FreeShowOutputConfiguration configuration,
        IReadOnlyCollection<string>? outputIds, ICollection<string> problems)
    {
        var active = configuration.GetActiveNormalOutputs();
        if (outputIds == null) return active;
        var selected = outputIds.ToHashSet(StringComparer.Ordinal);
        if (outputIds.Count == 0 || selected.Count != outputIds.Count)
            problems.Add("Explicit navigation output IDs must be nonempty and unique.");
        if (selected.Any(id => !active.Any(output => output.Id == id)))
            problems.Add("Explicit navigation includes an inactive or unknown output.");
        return active.Where(output => selected.Contains(output.Id)).ToArray();
    }
}
