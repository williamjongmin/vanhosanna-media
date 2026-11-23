using Avalonia.Threading;
using FreeShow.Native.Core;

namespace FreeShow.Native.Rendering;

/// <summary>
/// Bridges the source-complete command coordinator to one already-prepared
/// native physical output. The ONLY built-in effect is the previously ported
/// native lyric clear; background effects, timers, source callbacks, overlay
/// compositing and audio remain unavailable without explicitly supplied
/// original-equivalent hooks.
///
/// Merely constructing this object does NOT make F1/F2/F3 executable.
/// The coordinator refuses entire plans until all required hooks exist.
/// </summary>
public sealed class NativeFreeShowSessionCommandRuntime : IFreeShowOutputRuntimePlanReadiness
{
    private readonly NativeFreeShowOutputSession session;
    private readonly Func<FreeShowSourceEffect, FreeShowOutputCommandPlan, bool>? canExecutePlanEffect;
    private readonly IReadOnlyDictionary<FreeShowSourceEffect,
        Func<FreeShowOutputCommandPlan, CancellationToken, Task>> hookedEffects;

    public NativeFreeShowSessionCommandRuntime(
        NativeFreeShowOutputSession source,
        IReadOnlyDictionary<FreeShowSourceEffect,
            Func<FreeShowOutputCommandPlan, CancellationToken, Task>>? originalEffects = null,
        Func<FreeShowSourceEffect, FreeShowOutputCommandPlan, bool>? planReadiness = null)
    {
        session = source ?? throw new ArgumentNullException(nameof(source));
        canExecutePlanEffect = planReadiness;
        hookedEffects = originalEffects == null
            ? new Dictionary<FreeShowSourceEffect,
                Func<FreeShowOutputCommandPlan, CancellationToken, Task>>()
            : new Dictionary<FreeShowSourceEffect,
                Func<FreeShowOutputCommandPlan, CancellationToken, Task>>(originalEffects);
        if (hookedEffects.ContainsKey(FreeShowSourceEffect.ClearSlideOutput))
            throw new ArgumentException(
                "Native lyrics have a dedicated clear implementation; do not install a duplicate.",
                nameof(originalEffects));
    }

    public string OutputId => session.OutputId;

    public bool SupportsSourceEffect(FreeShowSourceEffect effect) =>
        effect == FreeShowSourceEffect.ClearSlideOutput || hookedEffects.ContainsKey(effect);

    public bool CanExecuteSourceEffect(
        FreeShowSourceEffect effect, FreeShowOutputCommandPlan plan) =>
        plan.OutputId == OutputId && SupportsSourceEffect(effect) &&
        (canExecutePlanEffect?.Invoke(effect, plan) ?? true);

    public async Task ExecuteSourceEffectsAsync(
        FreeShowOutputCommandPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanPlan || plan.OutputId != OutputId)
            throw new InvalidOperationException("Original FreeShow output plan is not valid for this session.");

        // Repeat preflight at the backend boundary: it must not rely on a
        // caller bypassing the coordinator's capability check.
        var missing = plan.RequiredEffects.Where(
            effect => !CanExecuteSourceEffect(effect, plan)).ToArray();
        if (missing.Length > 0)
            throw new NotSupportedException("Incomplete original FreeShow runtime hooks: " +
                                            string.Join(", ", missing));

        // This is an explicit runtime call, NOT connected to the read-only
        // OperatorProbe. Original clearSlide ordering: cache -> timers ->
        // visual slide clear -> source customActionActivation.
        foreach (var effect in plan.RequiredEffects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (effect == FreeShowSourceEffect.ClearSlideOutput)
            {
                await Dispatcher.UIThread.InvokeAsync(
                    () => session.ClearSlide(plan.SourceAction.ClearCachedSlidePosition));
            }
            else
            {
                await hookedEffects[effect](plan, cancellationToken);
            }
        }
    }
}
