namespace FreeShow.Native.Core;

/// <summary>
/// Provides the two F2-related FreeShow source effects already ported fully
/// to an in-memory position cache. The other F2 effects (next-slide timer
/// cleanup, actual native lyric surface clear and custom action propagation)
/// still need their own real runtime hooks before the coordinator can enable
/// the operator button.
/// </summary>
public static class FreeShowSlideCacheSourceEffects
{
    public static IReadOnlyDictionary<FreeShowSourceEffect,
        Func<FreeShowOutputCommandPlan, CancellationToken, Task>> Create(
        FreeShowOutputSlidePositionCache cache,
        FreeShowOutputCommandCoordinator controller,
        Func<string, ShowDocument?> sourceShowLookup,
        Func<bool> isFocusMode)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(sourceShowLookup);
        ArgumentNullException.ThrowIfNull(isFocusMode);

        return new Dictionary<FreeShowSourceEffect,
            Func<FreeShowOutputCommandPlan, CancellationToken, Task>>
        {
            [FreeShowSourceEffect.ClearSlideCache] = (_, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                cache.Clear();
                return Task.CompletedTask;
            },
            [FreeShowSourceEffect.CaptureLastSlidePositionIfEligible] =
                (plan, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!controller.States.TryGetValue(plan.OutputId, out var state))
                    throw new InvalidOperationException("Source output ID has no native state.");

                var slide = ShowDocument.Property(state.Snapshot(), "slide");
                var showId = ShowDocument.String(slide, "id");
                if (showId.Length == 0) return Task.CompletedTask;

                var knownShow = sourceShowLookup(showId);
                if (knownShow == null)
                    throw new NotSupportedException(
                        "Cannot implement original F2 slide cache without the active original Show.");
                cache.TryCapture(state, knownShow, isFocusMode());
                return Task.CompletedTask;
            }
        };
    }
}
