namespace FreeShow.Native.Core;

/// <summary>
/// The source slideTimers/overlayTimers stores are external to OutData.
/// Merely having zero registered native handles cannot establish that all
/// real FreeShow timers were discovered. A native scheduler must provide an
/// authoritative *current* registration view before these hooks can run.
/// </summary>
public interface IFreeShowTimerInventoryAuthority
{
    bool HasCompleteInventory(string originalOutputId, bool includeOverlayTimers);
}

/// <summary>
/// Connects FreeShowOutputTimerRegistry to the exact source timer phases:
/// F2's clearTimers("", false) cancels slide timers only, while final
/// Clear All clearTimers() includes the overlay/effect timers.
/// The original setOutput("transition", null) and output_changed event MUST
/// be performed by the surrounding source-complete adapter independently.
/// </summary>
public sealed class FreeShowTimerSourceEffects
{
    private readonly FreeShowOutputTimerRegistry registry;
    private readonly IFreeShowTimerInventoryAuthority authority;

    public FreeShowTimerSourceEffects(FreeShowOutputTimerRegistry timers,
        IFreeShowTimerInventoryAuthority inventory)
    {
        registry = timers ?? throw new ArgumentNullException(nameof(timers));
        authority = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    public bool CanCancel(IReadOnlyList<string> outputIds, bool includeOverlayTimers)
    {
        ArgumentNullException.ThrowIfNull(outputIds);
        return outputIds.Count > 0 &&
            outputIds.All(id => !string.IsNullOrWhiteSpace(id) &&
                authority.HasCompleteInventory(id, includeOverlayTimers));
    }

    public async Task<FreeShowTimerCancellationReport> CancelAsync(
        IReadOnlyList<string> outputIds, bool includeOverlayTimers,
        CancellationToken cancellationToken = default)
    {
        if (!CanCancel(outputIds, includeOverlayTimers))
            throw new NotSupportedException(
                "FreeShow timer inventory is not authoritative for all selected outputs.");

        var result = await registry.CancelAsync(
            outputIds, includeOverlayTimers, cancellationToken);
        if (!result.Succeeded)
            throw new IOException(
                "Original FreeShow timer cleanup incomplete: " +
                string.Join("; ", result.Errors));
        return result;
    }

    /// <summary>
    /// Install ONLY the F2 cancellation substep in one output session.
    /// The source CompleteRuntime must still implement slide/cache,
    /// transition propagation and custom-action notifications.
    /// </summary>
    public IReadOnlyDictionary<FreeShowSourceEffect,
        Func<FreeShowOutputCommandPlan, CancellationToken, Task>>
        CreateSlideClearHooks(string originalOutputId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalOutputId);
        return new Dictionary<FreeShowSourceEffect,
            Func<FreeShowOutputCommandPlan, CancellationToken, Task>>
        {
            [FreeShowSourceEffect.CancelNextSlideTimers] =
                async (plan, cancellationToken) =>
                {
                    if (plan.OutputId != originalOutputId)
                        throw new InvalidOperationException(
                            "Timer hook was invoked for the wrong FreeShow output.");
                    await CancelAsync([originalOutputId], false, cancellationToken);
                }
        };
    }

    /// <summary>
    /// ClearAll step hooks do NOT imply that the composite runtime supports
    /// all seven stages. The caller must provide its other complete stages.
    /// </summary>
    public IReadOnlyDictionary<FreeShowClearAllStep,
        Func<FreeShowClearAllPlan, CancellationToken, Task>>
        CreateClearAllTimerHooks() =>
        new Dictionary<FreeShowClearAllStep,
            Func<FreeShowClearAllPlan, CancellationToken, Task>>
        {
            [FreeShowClearAllStep.ClearNextSlideTimers] =
                async (plan, token) =>
                {
                    await CancelAsync(plan.ActiveOutputIds, false, token);
                },
            [FreeShowClearAllStep.ClearAllRemainingTimers] =
                async (plan, token) =>
                {
                    await CancelAsync(plan.ActiveOutputIds, true, token);
                }
        };
    /// <summary>
    /// A timer-only runtime intentionally lacks all five non-timer phases.
    /// Its plan-dependent readiness also refuses the full timer phase if
    /// even one output lacks an authoritative overlay timer inventory.
    /// </summary>
    public FreeShowCompositeClearAllRuntime CreatePartialClearAllRuntime() =>
        new(CreateClearAllTimerHooks(), (step, plan) =>
            step switch
            {
                FreeShowClearAllStep.ClearNextSlideTimers =>
                    CanCancel(plan.ActiveOutputIds, false),
                FreeShowClearAllStep.ClearAllRemainingTimers =>
                    CanCancel(plan.ActiveOutputIds, true),
                _ => false
            });
}

/// <summary>
/// Exact capability-based composition of source Clear All phases. A missing
/// handler makes the entire operation unexecutable. This does not fabricate
/// video or audio implementations and cannot control any output on its own.
/// </summary>
public sealed class FreeShowCompositeClearAllRuntime : IFreeShowClearAllPlanReadiness
{
    private readonly IReadOnlyDictionary<FreeShowClearAllStep,
        Func<FreeShowClearAllPlan, CancellationToken, Task>> handlers;
    private readonly Func<FreeShowClearAllStep, FreeShowClearAllPlan, bool>? readiness;

    public FreeShowCompositeClearAllRuntime(
        IReadOnlyDictionary<FreeShowClearAllStep,
            Func<FreeShowClearAllPlan, CancellationToken, Task>> completeHandlers,
        Func<FreeShowClearAllStep, FreeShowClearAllPlan, bool>? planReadiness = null)
    {
        ArgumentNullException.ThrowIfNull(completeHandlers);
        handlers = new Dictionary<FreeShowClearAllStep,
            Func<FreeShowClearAllPlan, CancellationToken, Task>>(completeHandlers);
        readiness = planReadiness;
    }

    public bool SupportsCompleteSourceStep(FreeShowClearAllStep step) =>
        handlers.ContainsKey(step);

    public bool CanExecuteSourceStep(FreeShowClearAllStep step, FreeShowClearAllPlan plan) =>
        SupportsCompleteSourceStep(step) && plan.IsReady &&
        (readiness?.Invoke(step, plan) ?? true);

    public Task ExecuteCompleteSourceStepAsync(FreeShowClearAllStep step,
        FreeShowClearAllPlan plan, CancellationToken cancellationToken = default)
    {
        if (!plan.IsReady)
            throw new InvalidOperationException("FreeShow Clear All plan is not ready.");
        if (!handlers.TryGetValue(step, out var handler) ||
            !CanExecuteSourceStep(step, plan))
            throw new NotSupportedException(
                "Complete original FreeShow step is unavailable for selected outputs: " + step);
        return handler(plan, cancellationToken);
    }
}
