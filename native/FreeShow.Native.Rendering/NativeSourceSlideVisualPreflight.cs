using FreeShow.Native.Core;

namespace FreeShow.Native.Rendering;

/// <summary>
/// Narrow source-to-native adapter for
/// IFreeShowSlideTransitionRuntime.HasPreparedSourceVisual().
/// It does NOT satisfy the other original FreeShow phases or install a
/// production transition runtime. The caller must use the style computed
/// from the proposed complete multi-output snapshot, not a stale style.
/// </summary>
public static class NativeSourceSlideVisualPreflight
{
    public static bool HasPreparedOriginalTarget(
        FreeShowSlideTransitionTarget target,
        NativeFreeShowOutputSession session,
        FreeShowRevealStyleContext predictedStyle)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(predictedStyle);

        if (target.OutputId != session.OutputId ||
            target.ShowId != session.PreparedShowId ||
            target.LayoutId != session.PreparedLayoutId ||
            ShowDocument.String(target.NextOutSlide, "id") != target.ShowId ||
            ShowDocument.String(target.NextOutSlide, "layout") != target.LayoutId)
            return false;

        var index = ShowDocument.Property(target.NextOutSlide, "index");
        if (index.ValueKind != System.Text.Json.JsonValueKind.Number ||
            !index.TryGetInt32(out int original) ||
            original != target.LayoutIndex)
            return false;

        // Exact original state + style variant must have been generated
        // before the operator asks to change an actual output.
        return session.HasPrewarmedReveal(
            target.NextOutSlide, predictedStyle);
    }
}
