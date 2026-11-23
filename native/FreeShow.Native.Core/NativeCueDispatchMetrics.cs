namespace FreeShow.Native.Core;

/// <summary>
/// Thread-safe, bounded CPU/UI cue dispatch samples. These numbers are NEVER
/// physical click-to-visible/display latency. Avalonia may still layout, shape
/// text, upload textures and wait for vsync after this stopwatch completes.
/// </summary>
public sealed class NativeCueDispatchMetrics
{
    private readonly object gate = new();
    private readonly Queue<CueDispatchSample> samples = new();
    private readonly int capacity;

    public NativeCueDispatchMetrics(int capacity = 256)
    {
        if (capacity is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    public void Record(int layoutIndex, TimeSpan synchronousUiCost)
    {
        if (layoutIndex < 0) return;
        var ms = synchronousUiCost.TotalMilliseconds;
        if (!double.IsFinite(ms) || ms < 0) return;
        lock (gate)
        {
            if (samples.Count == capacity) samples.Dequeue();
            samples.Enqueue(new CueDispatchSample(layoutIndex, ms,
                DateTimeOffset.UtcNow));
        }
    }

    public CueDispatchSummary Snapshot()
    {
        double[] sorted;
        lock (gate) sorted = samples.Select(x => x.CpuMilliseconds)
            .OrderBy(x => x).ToArray();
        if (sorted.Length == 0)
            return new(0, 0, 0, 0, 0);
        return new(sorted.Length, Percentile(sorted, 0.50),
            Percentile(sorted, 0.95), Percentile(sorted, 0.99), sorted[^1]);
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];
        double index = (sorted.Length - 1) * p;
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }
}

public sealed record CueDispatchSample(int LayoutIndex, double CpuMilliseconds,
    DateTimeOffset RecordedUtc);
public sealed record CueDispatchSummary(
    int Count, double P50Ms, double P95Ms, double P99Ms, double MaxMs);
