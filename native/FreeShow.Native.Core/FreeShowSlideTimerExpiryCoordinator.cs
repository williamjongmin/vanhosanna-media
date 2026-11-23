using System.Text.Json;
using System.Text.Json.Nodes;

namespace FreeShow.Native.Core;

/// <summary>
/// Snapshot taken when the scheduler delivers its generation-checked
/// elapsed notification. An expired cue cannot be retargeted while queued.
/// </summary>
public sealed record FreeShowSlideTimerExpiry(
    FreeShowSlideTimerElapsed Timer, FreeShowCapturedOutput Source);

/// <summary>
/// Native equivalent of tick.ts's outputs.update(out.transition = null).
/// Must update the authoritative source store and return its actual full
/// snapshot. Returning a hypothetical JSON mutation is not acknowledgment.
/// This operation does not run setOutput or output_changed in the source.
/// </summary>
public interface IFreeShowSlideTimerExpirySource
{
    string OutputId { get; }
    bool CanClearTransition(FreeShowCapturedOutput current);
    Task<JsonElement> ClearAndCaptureTransitionAsync(FreeShowCapturedOutput current,
        CancellationToken cancellationToken = default);
}

public enum FreeShowSlideTimerExpiryStatus
{
    Completed, Refused, TransitionClearedWithoutAdvance, PartialExecution
}

public sealed record FreeShowSlideTimerExpiryResult(
    FreeShowSlideTimerExpiryStatus Status, bool TransitionClearAcknowledged,
    FreeShowSlideTransitionResult? Advance, IReadOnlyList<string> Problems);

public sealed partial class FreeShowOutputCommandCoordinator
{
    /// <summary>Call on the source owner loop, inside the scheduler callback.</summary>
    public FreeShowSlideTimerExpiry CaptureSlideTimerExpiry(FreeShowSlideTimerElapsed elapsed)
    {
        ArgumentNullException.ThrowIfNull(elapsed);
        if (!states.TryGetValue(elapsed.OutputId, out var state))
            throw new ArgumentException("Timer refers to an unknown original output.");
        long revision = state.Revision;
        var snapshot = state.Snapshot();
        if (state.Revision != revision)
            throw new InvalidOperationException("Output changed while accepting timer expiry.");
        return new(elapsed, new(elapsed.OutputId, revision, snapshot));
    }

    /// <summary>
    /// Clear this output's source transition, acknowledge it, then calculate
    /// and execute its Next command under ONE coordinator gate. Supports the
    /// current-Show part of playNext:true; folder/PDF/project paths refuse.
    /// </summary>
    public async Task<FreeShowSlideTimerExpiryResult> DispatchSlideTimerExpiryAsync(
        FreeShowSlideTimerExpiry expiry,
        IFreeShowSlideTimerExpirySource source,
        FreeShowNavigationSourceContext sourceContext,
        Func<string, ShowDocument?> sourceShows,
        FreeShowPreparedSlideRuntimeResolver preparedRuntimes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expiry);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceContext);
        ArgumentNullException.ThrowIfNull(sourceShows);
        ArgumentNullException.ThrowIfNull(preparedRuntimes);
        await executeGate.WaitAsync(cancellationToken);
        try
        {
            var current = expiry.Source;
            string id = expiry.Timer.OutputId;
            if (expiry.Timer.Destination != FreeShowSlideTimerDestination.NextSlide)
                return RefuseTimer("Folder/PDF timer expiry requires its complete source playback controller.");
            if (current.OutputId != id || source.OutputId != id ||
                !configuration.GetActiveNormalOutputs().Any(output => output.Id == id) ||
                !states.TryGetValue(id, out var state))
                return RefuseTimer("Timer target is unknown, inactive or belongs to another source backend.");
            if (slideTransitionNeedsReconciliation.Contains(id))
                return RefuseTimer("Timer output requires authoritative reconciliation.");
            if (state.Revision != current.Revision ||
                !JsonElement.DeepEquals(state.Snapshot(), current.OutData))
                return RefuseTimer("Queued timer expiry is stale; the original output has changed.");
            try
            {
                if (!source.CanClearTransition(current))
                    return RefuseTimer("Authoritative source transition clearing is unavailable.");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return RefuseTimer("Source timer readiness failed: " + error.Message);
            }
            if (state.Revision != current.Revision ||
                !JsonElement.DeepEquals(state.Snapshot(), current.OutData))
                return RefuseTimer("Source output changed during timer readiness inspection.");

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var acknowledged = (await source.ClearAndCaptureTransitionAsync(
                    current, cancellationToken)).Clone();
                var expected = JsonNode.Parse(current.OutData.GetRawText())!.AsObject();
                expected["transition"] = null;
                if (state.Revision != current.Revision ||
                    !JsonElement.DeepEquals(acknowledged, JsonSerializer.SerializeToElement(expected)))
                    throw new InvalidDataException(
                        "Timer cleanup did not acknowledge exactly the original transition clear.");
                state.ReplaceAfterAcknowledgement(acknowledged);
            }
            catch (Exception error)
            {
                // Even cancellation can follow a completed source mutation.
                // Never reuse the old timer snapshot after an uncertain ack.
                slideTransitionNeedsReconciliation.Add(id);
                return new(FreeShowSlideTimerExpiryStatus.PartialExecution, false, null,
                    ["Source timer cleanup requires reconciliation: " + error.Message]);
            }

            // Source clears transition even when the following advance is
            // blocked by output lock or a project boundary. Report both
            // outcomes; do not pretend the cleanup was rolled back.
            FreeShowSlideTransitionResult advance;
            try
            {
                advance = await DispatchSlideAdvanceCoreAsync(sourceContext, sourceShows,
                    FreeShowSlideAdvanceDirection.Next, preparedRuntimes, [id], cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new(FreeShowSlideTimerExpiryStatus.TransitionClearedWithoutAdvance,
                    true, null, ["Timer transition was cleared; advance was cancelled before execution."]);
            }
            return new(advance.Succeeded ? FreeShowSlideTimerExpiryStatus.Completed :
                    advance.Status == FreeShowSlideTransitionStatus.PartialExecution
                        ? FreeShowSlideTimerExpiryStatus.PartialExecution
                        : FreeShowSlideTimerExpiryStatus.TransitionClearedWithoutAdvance,
                true, advance, advance.Problems);
        }
        finally { executeGate.Release(); }
    }

    private static FreeShowSlideTimerExpiryResult RefuseTimer(string reason) =>
        new(FreeShowSlideTimerExpiryStatus.Refused, false, null, [reason]);
}
