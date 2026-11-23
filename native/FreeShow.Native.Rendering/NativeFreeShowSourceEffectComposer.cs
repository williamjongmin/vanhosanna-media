using FreeShow.Native.Core;

namespace FreeShow.Native.Rendering;

/// <summary>
/// Assemble *only* the implemented subeffects of the original output
/// lifecycle. This factory deliberately does NOT register its result in
/// the coordinator. F1/F2 require credits, output_changed, timeline,
/// custom-action activation and other complete source hooks to be
/// supplied by the production operator controller before being enabled.
/// </summary>
public static class NativeFreeShowSourceEffectComposer
{
    public static NativeFreeShowSessionCommandRuntime CreatePartialRuntime(
        NativeFreeShowOutputSession session,
        FreeShowOutputCommandCoordinator coordinator,
        FreeShowOutputSlidePositionCache slideCache,
        FreeShowTimerSourceEffects timers,
        Func<string, ShowDocument?> originalShowLookup,
        Func<bool> focusMode,
        string freeShowDataRoot)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(slideCache);
        ArgumentNullException.ThrowIfNull(timers);
        ArgumentNullException.ThrowIfNull(originalShowLookup);
        ArgumentNullException.ThrowIfNull(focusMode);

        var hooks = new Dictionary<FreeShowSourceEffect,
            Func<FreeShowOutputCommandPlan, CancellationToken, Task>>();

        void Add(IReadOnlyDictionary<FreeShowSourceEffect,
            Func<FreeShowOutputCommandPlan, CancellationToken, Task>> group)
        {
            foreach (var (effect, handler) in group)
            {
                if (!hooks.TryAdd(effect, handler))
                    throw new InvalidOperationException(
                        "Conflicting FreeShow source effect adapter: " + effect);
            }
        }

        Add(FreeShowSlideCacheSourceEffects.Create(
            slideCache, coordinator, originalShowLookup, focusMode));
        Add(timers.CreateSlideClearHooks(session.OutputId));
        Add(NativeFreeShowMediaSourceEffects.CreateSingleVideoBackgroundHooks(
            session, coordinator, freeShowDataRoot));

        // ClearSlideOutput is provided natively by this runtime.
        // All remaining callbacks are genuinely unimplemented and thus
        // not advertised as supported. The coordinator refuses partial
        // action plans instead of silently performing only one subeffect.
        bool ReadyForPlan(
            FreeShowSourceEffect effect, FreeShowOutputCommandPlan plan)
        {
            if (plan.OutputId != session.OutputId) return false;
            switch (effect)
            {
                case FreeShowSourceEffect.CancelNextSlideTimers:
                    return timers.CanCancel([session.OutputId], includeOverlayTimers: false);

                case FreeShowSourceEffect.ClearActiveBackground:
                    return NativeFreeShowMediaSourceEffects.CanClearBackground(
                        session, coordinator, freeShowDataRoot, plan);

                case FreeShowSourceEffect.CaptureLastSlidePositionIfEligible:
                    if (!coordinator.States.TryGetValue(plan.OutputId, out var source))
                        return false;
                    var active = ShowDocument.Property(source.Snapshot(), "slide");
                    var showId = ShowDocument.String(active, "id");
                    if (showId.Length == 0) return true;
                    try
                    {
                        // A missing original source Show must refuse F2 before
                        // canceling timers or changing any physical layer.
                        return originalShowLookup(showId)?.ShowId == showId;
                    }
                    catch { return false; }

                default:
                    return true; // Static SupportsSourceEffect must also pass.
            }
        }

        return new NativeFreeShowSessionCommandRuntime(
            session, hooks, ReadyForPlan);
    }
}
