using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// The original action engine, exclusive of the prepared Native lyric swap.
/// Implementations must own all other source effects and output layers.
/// No implementation is installed by the developer OperatorProbe.
/// </summary>
public interface IFreeShowSlideTransitionSourceBackend
{
    string OutputId { get; }
    bool SupportsCompletePhase(FreeShowSlideTransitionPhase phase,
        FreeShowSlideTransitionTarget target);
    Task ExecuteCompletePhaseAsync(FreeShowSlideTransitionPhase phase,
        FreeShowSlideTransitionTarget target, CancellationToken cancellationToken);
    /// <summary>
    /// Capture actual source OutData and style from the same current output
    /// batch. Do not return the predicted prewarm snapshot as acknowledgment.
    /// Other output layers must already reflect this snapshot. The backend
    /// must serialize external source mutations with this command; the
    /// coordinator serializes Native commands, not independent devices.
    /// </summary>
    Task<FreeShowSlideRuntimeSnapshot> CaptureAcknowledgedStateAsync(
        FreeShowSlideTransitionTarget target, CancellationToken cancellationToken);
}

public sealed record FreeShowSlideRuntimeSnapshot(
    string OutputId, JsonElement OutData, FreeShowRevealStyleContext Style);

/// <summary>
/// Output-owned prepared lyric controls. A successful presentation verifies
/// the selected native control, not GPU scanout or physical display latency.
/// </summary>
public interface IFreeShowPreparedSlideSurface
{
    string OutputId { get; }
    bool HasPrepared(FreeShowSlideTransitionTarget target, FreeShowRevealStyleContext style);
    Task PresentAsync(FreeShowSlideTransitionTarget target,
        FreeShowRevealStyleContext style, CancellationToken cancellationToken);
    Task<bool> IsPresentingAsync(FreeShowSlideTransitionTarget target,
        FreeShowRevealStyleContext style, CancellationToken cancellationToken);
}

/// <summary>
/// A single-command bridge between the complete source action engine and
/// prepared Native lyrics: before actions -> setOutput -> native swap ->
/// updateOut -> full-state AND selected-control acknowledgment. The bridge
/// does not substitute empty handlers for any missing source phase.
/// </summary>
public sealed class FreeShowPreparedSlideTransitionRuntime : IFreeShowSlideTransitionRuntime
{
    private readonly FreeShowSlideTransitionTarget prepared;
    private readonly FreeShowSlideTransitionTarget requested;
    private readonly FreeShowRevealStyleContext style;
    private readonly IFreeShowSlideTransitionSourceBackend source;
    private readonly IFreeShowPreparedSlideSurface surface;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private int nextPhase;
    private bool faulted;
    private bool acknowledged;

    public string OutputId => prepared.OutputId;

    public FreeShowPreparedSlideTransitionRuntime(
        FreeShowSlideTransitionTarget target, FreeShowRevealStyleContext preparedStyle,
        IFreeShowSlideTransitionSourceBackend sourceBackend,
        IFreeShowPreparedSlideSurface preparedSurface)
    {
        ArgumentNullException.ThrowIfNull(target);
        style = preparedStyle ?? throw new ArgumentNullException(nameof(preparedStyle));
        source = sourceBackend ?? throw new ArgumentNullException(nameof(sourceBackend));
        surface = preparedSurface ?? throw new ArgumentNullException(nameof(preparedSurface));
        if (source.OutputId != target.OutputId || surface.OutputId != target.OutputId)
            throw new ArgumentException("Source backend and visual must own the same original output.");
        requested = target;
        prepared = target with
        {
            PreviousOutData = target.PreviousOutData.Clone(),
            PreviousOutSlide = target.PreviousOutSlide.Clone(),
            NextOutSlide = target.NextOutSlide.Clone(),
            OriginalLayoutEntry = target.OriginalLayoutEntry.Clone()
        };
    }

    public bool HasPreparedSourceVisual(FreeShowSlideTransitionTarget target) =>
        !faulted && !acknowledged && nextPhase == 0 && MatchesTarget(target) &&
        surface.HasPrepared(prepared, style);

    public bool SupportsCompletePhase(FreeShowSlideTransitionPhase phase,
        FreeShowSlideTransitionTarget target) =>
        !faulted && !acknowledged && MatchesTarget(target) &&
        FreeShowSlideTransitionOrder.Phases.Contains(phase) &&
        source.SupportsCompletePhase(phase, prepared);

    public async Task ExecuteCompletePhaseAsync(FreeShowSlideTransitionPhase phase,
        FreeShowSlideTransitionTarget target, CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (!SupportsCompletePhase(phase, target) ||
                nextPhase >= FreeShowSlideTransitionOrder.Phases.Count ||
                phase != FreeShowSlideTransitionOrder.Phases[nextPhase])
                throw new InvalidOperationException("Original slide phases must execute once, in order.");

            // Also guard direct callers, not just the coordinator preflight.
            if (nextPhase == 0 && (!surface.HasPrepared(prepared, style) ||
                FreeShowSlideTransitionOrder.Phases.Any(p =>
                    !source.SupportsCompletePhase(p, prepared))))
                throw new NotSupportedException("Full source lifecycle and prepared lyrics are required.");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await source.ExecuteCompletePhaseAsync(phase, prepared, cancellationToken);
                if (phase == FreeShowSlideTransitionPhase.SetOriginalSlideWithFullOutputLifecycle)
                {
                    // Source actions may have changed the target or style.
                    // Never use the old prewarm prediction as an actual ack.
                    VerifySnapshot(await source.CaptureAcknowledgedStateAsync(
                        prepared, cancellationToken));
                    await surface.PresentAsync(prepared, style, cancellationToken);
                }
                nextPhase++;
            }
            catch
            {
                faulted = true;
                throw; // Coordinator quarantines outputs after physical work.
            }
        }
        finally { operationGate.Release(); }
    }

    public async Task<JsonElement> CaptureAcknowledgedOutDataAsync(
        FreeShowSlideTransitionTarget target, CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (faulted || acknowledged || !MatchesTarget(target) ||
                nextPhase != FreeShowSlideTransitionOrder.Phases.Count)
                throw new InvalidOperationException("Slide lifecycle has not completed or was already consumed.");

            try
            {
                var snapshot = await source.CaptureAcknowledgedStateAsync(prepared, cancellationToken);
                VerifySnapshot(snapshot);
                if (!await surface.IsPresentingAsync(prepared, style, cancellationToken))
                    throw new InvalidDataException("Native lyrics no longer match the acknowledged source slide.");
                acknowledged = true;
                return snapshot.OutData.Clone();
            }
            catch
            {
                faulted = true;
                throw;
            }
        }
        finally { operationGate.Release(); }
    }

    private void VerifySnapshot(FreeShowSlideRuntimeSnapshot snapshot)
    {
        if (snapshot.OutputId != OutputId || snapshot.Style != style ||
            !FreeShowSlideAcknowledgement.Matches(prepared, snapshot.OutData))
            throw new InvalidDataException("Source output or style differs from the prepared Native cue.");
    }

    private bool MatchesTarget(FreeShowSlideTransitionTarget target) =>
        // Targets are immutable records. Ordinary coordinator dispatch uses
        // the same target object, avoiding repeated full-state comparisons.
        ReferenceEquals(target, requested) ||
        (target.OutputId == prepared.OutputId &&
        target.ExpectedRevision == prepared.ExpectedRevision &&
        target.ShowId == prepared.ShowId && target.LayoutId == prepared.LayoutId &&
        target.LayoutIndex == prepared.LayoutIndex &&
        target.NavigationKind == prepared.NavigationKind &&
        target.RequiresUnportedSourceEffects == prepared.RequiresUnportedSourceEffects &&
        JsonElement.DeepEquals(target.PreviousOutData, prepared.PreviousOutData) &&
        JsonElement.DeepEquals(target.PreviousOutSlide, prepared.PreviousOutSlide) &&
        JsonElement.DeepEquals(target.NextOutSlide, prepared.NextOutSlide) &&
        JsonElement.DeepEquals(target.OriginalLayoutEntry, prepared.OriginalLayoutEntry));
}
