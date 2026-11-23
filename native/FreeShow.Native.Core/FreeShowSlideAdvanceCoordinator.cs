namespace FreeShow.Native.Core;

/// <summary>
/// Resolves already prepared source runtimes for the exact current plan.
/// Must not execute source actions, decode media, warm visuals or read files.
/// Each returned runtime belongs to this command, not an earlier request.
/// </summary>
public delegate IReadOnlyDictionary<string, IFreeShowSlideTransitionRuntime>
    FreeShowPreparedSlideRuntimeResolver(FreeShowSlideTransitionPlan plan);

public sealed partial class FreeShowOutputCommandCoordinator
{
    /// <summary>
    /// Plan AFTER obtaining the same gate used by clear/restore/transition.
    /// Successive Next requests therefore consume successive acknowledged
    /// states instead of replaying a plan prepared at key-down time.
    /// Null outputIds selects all active normal outputs, as advanceOutputs.
    /// An explicit selection is the within-Show advanceOutput subset only;
    /// active-item/focus/project-boundary resolution remains unsupported.
    /// </summary>
    public async Task<FreeShowSlideTransitionResult> DispatchSlideAdvanceAsync(
        FreeShowNavigationSourceContext sourceContext,
        Func<string, ShowDocument?> sourceShows,
        FreeShowSlideAdvanceDirection direction,
        FreeShowPreparedSlideRuntimeResolver preparedRuntimes,
        IReadOnlyCollection<string>? outputIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceContext);
        ArgumentNullException.ThrowIfNull(sourceShows);
        ArgumentNullException.ThrowIfNull(preparedRuntimes);
        var selection = outputIds?.ToArray();
        await executeGate.WaitAsync(cancellationToken);
        try
        {
            return await DispatchSlideAdvanceCoreAsync(sourceContext, sourceShows,
                direction, preparedRuntimes, selection, cancellationToken);
        }
        finally { executeGate.Release(); }
    }

    private async Task<FreeShowSlideTransitionResult> DispatchSlideAdvanceCoreAsync(
        FreeShowNavigationSourceContext sourceContext,
        Func<string, ShowDocument?> sourceShows,
        FreeShowSlideAdvanceDirection direction,
        FreeShowPreparedSlideRuntimeResolver preparedRuntimes,
        IReadOnlyCollection<string>? outputIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FreeShowSlideTransitionPlan plan;
        IReadOnlyDictionary<string, IFreeShowSlideTransitionRuntime> runtimes;
        try
        {
            if (!Enum.IsDefined(direction))
                return new(FreeShowSlideTransitionStatus.Refused, 0,
                    ["Unknown original slide navigation direction."]);

            // Still capture ALL outputs: line/reveal style calculations for
            // one timer must retain the other outputs' acknowledged state.
            var captured = FreeShowOutputSnapshotBatch.Capture(configuration, states);
            var inspected = FreeShowMultiOutputAdvanceInspection.Prepare(
                configuration, states, sourceContext, sourceShows, direction,
                outputIds: outputIds);
            plan = FreeShowSlideTransitionPlanner.Prepare(
                configuration, captured, inspected, sourceShows, outputIds);
            if (!plan.IsSourcePrepared)
                return new(FreeShowSlideTransitionStatus.Refused, 0, plan.Problems);

            runtimes = preparedRuntimes(plan) ?? throw new InvalidOperationException(
                "Prepared source runtime resolver returned no runtime collection.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new(FreeShowSlideTransitionStatus.Failed, 0,
                ["Current source navigation preparation failed: " + error.Message]);
        }

        // Readiness rechecks the whole capture after the resolver, before
        // any source phase. The outer method still owns executeGate.
        return await DispatchSlideTransitionCoreAsync(plan, runtimes, cancellationToken);
    }
}
