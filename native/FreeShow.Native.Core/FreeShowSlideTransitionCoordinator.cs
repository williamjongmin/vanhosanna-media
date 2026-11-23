using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Guarded bridge from original FreeShow slide-navigation intent to a
/// complete backend. Serialized alongside F1/F2/F3, Restore, Clear All.
/// An output is acknowledged only after all source phases finish and the
/// backend supplies its whole physical OutData snapshot.
/// The internal developer OperatorProbe installs no slide transition
/// runtime; this API cannot change church output without one.
/// </summary>
public sealed partial class FreeShowOutputCommandCoordinator
{
    public IReadOnlyList<string> CheckSlideTransitionReadiness(
        FreeShowSlideTransitionPlan plan,
        IReadOnlyDictionary<string, IFreeShowSlideTransitionRuntime> completeRuntimes)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(completeRuntimes);

        var problems = plan.Problems.ToList();
        if (ShowDocument.IsTrue(configuration.RawSettings, "outLocked"))
            problems.Add("Original FreeShow output is locked.");

        if (!plan.IsSourcePrepared)
            problems.Add("Original FreeShow slide transition plan is not source-ready.");

        if (!plan.SourceCapture.StillMatches(states))
            problems.Add("Original OutData changed since slide navigation was prepared.");

        if (plan.Targets.Select(x => x.OutputId).Distinct(StringComparer.Ordinal).Count() !=
            plan.Targets.Count)
            problems.Add("Duplicate physical FreeShow output IDs in transition.");

        var active = configuration.GetActiveNormalOutputs()
            .Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var target in plan.Targets)
        {
            if (!active.Contains(target.OutputId) ||
                !states.TryGetValue(target.OutputId, out var state) ||
                state.Revision != target.ExpectedRevision)
            {
                problems.Add(target.OutputId +
                    ": target is inactive or the original output revision changed.");
                continue;
            }

            if (!completeRuntimes.TryGetValue(target.OutputId, out var runtime) ||
                runtime.OutputId != target.OutputId)
            {
                problems.Add(target.OutputId +
                    ": source-complete slide action runtime is not installed.");
                continue;
            }

            foreach (var phase in FreeShowSlideTransitionOrder.Phases)
            {
                try
                {
                    if (!runtime.SupportsCompletePhase(phase, target))
                        problems.Add(target.OutputId +
                            ": missing complete source phase " + phase);
                }
                catch (Exception error)
                {
                    problems.Add(target.OutputId +
                        ": source phase readiness failed: " + error.Message);
                }
            }
        }
        return problems.AsReadOnly();
    }

    public async Task<FreeShowSlideTransitionResult> DispatchSlideTransitionAsync(
        FreeShowSlideTransitionPlan plan,
        IReadOnlyDictionary<string, IFreeShowSlideTransitionRuntime> completeRuntimes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(completeRuntimes);

        await executeGate.WaitAsync(cancellationToken);
        try
        {
            var problems = CheckSlideTransitionReadiness(plan, completeRuntimes);
            if (problems.Count != 0)
                return new(FreeShowSlideTransitionStatus.Refused, 0, problems);

            int acknowledged = 0;
            bool physicalStepAttempted = false;

            try
            {
                foreach (var target in plan.Targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var runtime = completeRuntimes[target.OutputId];

                    foreach (var phase in FreeShowSlideTransitionOrder.Phases)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // External operations may partially complete even if
                        // the first awaited phase throws. Never report such a
                        // failure as an atomic no-op.
                        physicalStepAttempted = true;
                        await runtime.ExecuteCompletePhaseAsync(
                            phase, target, cancellationToken);
                    }

                    JsonElement acknowledgedState =
                        (await runtime.CaptureAcknowledgedOutDataAsync(
                            target, cancellationToken)).Clone();

                    if (!MatchesAcknowledgedOriginalSlide(
                            target, acknowledgedState))
                        throw new InvalidDataException(
                            "Original setOutput/updateOut returned a different Show, " +
                            "slide index or reveal state than the requested command.");

                    if (states[target.OutputId].Revision != target.ExpectedRevision)
                        throw new InvalidOperationException(
                            "Original output changed before confirmed slide state commit.");

                    // This updates an in-memory clone only. It preserves
                    // other source fields (background/audio-overlay state)
                    // instead of assuming updateOut changed just lyrics.
                    states[target.OutputId].ReplaceAfterAcknowledgement(
                        acknowledgedState);
                    acknowledged++;
                }

                return new(FreeShowSlideTransitionStatus.Completed, acknowledged,
                    Array.Empty<string>());
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return new(physicalStepAttempted
                        ? FreeShowSlideTransitionStatus.PartialExecution
                        : FreeShowSlideTransitionStatus.Failed,
                    acknowledged,
                    ["Source slide runtime failure: " + error.GetType().Name +
                     " — " + error.Message +
                     ". External outputs require explicit reconciliation."]);
            }
        }
        finally { executeGate.Release(); }
    }

    private static bool MatchesAcknowledgedOriginalSlide(
        FreeShowSlideTransitionTarget target, JsonElement originalOutData)
    {
        if (originalOutData.ValueKind != JsonValueKind.Object)
            return false;
        var slide = ShowDocument.Property(originalOutData, "slide");
        if (slide.ValueKind != JsonValueKind.Object ||
            ShowDocument.String(slide, "id") != target.ShowId ||
            ShowDocument.String(slide, "layout") != target.LayoutId)
            return false;

        var index = ShowDocument.Property(slide, "index");
        if (index.ValueKind != JsonValueKind.Number ||
            !index.TryGetInt32(out var actual) || actual != target.LayoutIndex)
            return false;

        foreach (var key in new[] { "line", "revealCount", "itemClickReveal" })
        {
            var expected = ShowDocument.Property(target.NextOutSlide, key);
            if (expected.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                continue;
            var got = ShowDocument.Property(slide, key);
            if (expected.ValueKind != got.ValueKind ||
                expected.GetRawText() != got.GetRawText())
                return false;
        }

        return true;
    }
}
