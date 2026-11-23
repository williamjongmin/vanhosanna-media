using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Output command integration gate. Owns the current Native in-memory copy of
/// each original FreeShow OutData and delegates effects only to explicitly
/// registered complete source-compatible runtime adapters.
/// Nothing in this class opens windows, runs media or writes FreeShow files.
/// </summary>
public sealed partial class FreeShowOutputCommandCoordinator
{
    private readonly FreeShowOutputConfiguration configuration;
    private readonly JsonElement overlays;
    private readonly FreeShowShortcutRouter shortcuts = new();
    private readonly Dictionary<string, FreeShowOutputState> states;
    private readonly Dictionary<string, IFreeShowOutputCommandRuntime> runtimes =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim executeGate = new(1, 1);

    public FreeShowOutputCommandCoordinator(
        FreeShowOutputConfiguration source,
        JsonElement overlayDefinitions = default)
    {
        configuration = source ?? throw new ArgumentNullException(nameof(source));
        overlays = overlayDefinitions.ValueKind == JsonValueKind.Object
            ? overlayDefinitions.Clone() : default;
        states = source.All.ToDictionary(x => x.Id,
            x => new FreeShowOutputState(x), StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, FreeShowOutputState> States => states;

    /// <summary>
    /// The source keeps independent output IDs; snapshots are restored only
    /// for output IDs which remain active. This gate is shared with ordinary
    /// commands so native state is never committed between a clear and its
    /// acknowledged full restore.
    /// </summary>
    public async Task<FreeShowRestoreResult> RestoreAsync(
        FreeShowOutputRestoreCache cache,
        IFreeShowOutputRestoreRuntime runtime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(runtime);

        await executeGate.WaitAsync(cancellationToken);
        try
        {
            if (ShowDocument.IsTrue(configuration.RawSettings, "outLocked"))
                return new(FreeShowRestoreStatus.Refused, 0,
                    ["Original FreeShow output is locked."]);

            var active = configuration.GetActiveNormalOutputs()
                .Select(x => x.Id).ToArray();
            var plan = cache.PlanRestore(active, states);
            if (!plan.IsSourceComplete)
                return new(FreeShowRestoreStatus.Refused, 0, plan.Warnings);

            var missing = new List<string>();
            foreach (var pair in plan.OutputSnapshots)
                if (!runtime.CanRestoreOutput(pair.Key, pair.Value))
                    missing.Add("Restore runtime missing original output " + pair.Key);

            if (!runtime.CanRestoreGlobalAudio(
                    plan.PlayingAudioData, plan.PlayingMetronome!.Value))
                missing.Add("Restore runtime missing global audio/metronome playback.");

            if (missing.Count != 0)
                return new(FreeShowRestoreStatus.Refused, 0, missing.AsReadOnly());

            int restoredCount = 0;
            try
            {
                foreach (var pair in plan.OutputSnapshots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await runtime.RestoreOutputAsync(pair.Key, pair.Value, cancellationToken);
                    states[pair.Key].ReplaceAfterAcknowledgement(pair.Value);
                    restoredCount++;
                }

                // FreeShow restores playingAudioData / playingMetronome after
                // restoring the selected output OutData. Keep cached data if
                // the audio backend fails, because a retry may be necessary.
                await runtime.RestoreGlobalAudioAsync(
                    plan.PlayingAudioData, plan.PlayingMetronome!.Value, cancellationToken);
                cache.Clear();
                return new(FreeShowRestoreStatus.Completed, restoredCount,
                    Array.Empty<string>());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new(restoredCount > 0
                        ? FreeShowRestoreStatus.PartialExecution
                        : FreeShowRestoreStatus.Failed,
                    restoredCount, ["Restore backend failed: " + ex.Message +
                        " (physical output state requires reconciliation)"]);
            }
        }
        finally { executeGate.Release(); }
    }

    /// <summary>
    /// Synchronize a source-complete, externally acknowledged original
    /// OutData snapshot. This is not callable from the developer OperatorProbe,
    /// and never controls a real output or writes source data. Reject stale
    /// revisions rather than overwriting concurrent user changes.
    /// </summary>
    public async Task<bool> SynchronizeAcknowledgedOutputAsync(
        string outputId, JsonElement originalOutData, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputId);
        await executeGate.WaitAsync(cancellationToken);
        try
        {
            if (!states.TryGetValue(outputId, out var state) ||
                state.Revision != expectedRevision ||
                originalOutData.ValueKind != JsonValueKind.Object)
                return false;
            state.ReplaceAfterAcknowledgement(originalOutData);
            return true;
        }
        finally { executeGate.Release(); }
    }


    /// <summary>
    /// The original Preview must reflect acknowledged native output changes,
    /// not stale SETTINGS JSON. A successful command updates this model only
    /// after its complete registered runtime reports success.
    /// </summary>
    public FreeShowPreviewSnapshot CurrentPreview(
        FreeShowAuxiliaryPreviewState? auxiliary = null) =>
        FreeShowPreviewControlState.Prepare(configuration, overlays, auxiliary, states);



    /// <summary>
    /// Registration is explicit. The read-only developer OperatorProbe does not
    /// call this method: its clear buttons and live shortcut handlers stay
    /// disabled. Runtime may represent a physical output or the one global
    /// audio/playlist service (global ID = "").
    /// </summary>
    public void RegisterSourceCompleteRuntime(IFreeShowOutputCommandRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (runtime.OutputId.Length != 0 && !states.ContainsKey(runtime.OutputId))
            throw new ArgumentException("Runtime output ID is absent from original FreeShow SETTINGS.");
        runtimes[runtime.OutputId] = runtime;
    }

    /// <summary>
    /// Preview ClearButtons.svelte uses clearOverlays(), which retains locked
    /// overlays. This deliberately differs from the keyboard F3 code path,
    /// which directly sets every overlay list to empty.
    /// Scene / Clear All / timer remain unported and produce NO plan.
    /// </summary>
    public IReadOnlyList<FreeShowOutputCommandPlan> PlanPreviewClearAction(string category)
    {
        NativeShortcutCommand? command = category switch
        {
            "background" => NativeShortcutCommand.ClearBackground,
            "slide" => NativeShortcutCommand.ClearSlide,
            "overlays" => NativeShortcutCommand.ClearOverlays,
            "audio" => NativeShortcutCommand.ClearAudioAndPlaylist,
            _ => null
        };
        if (command == null) return Array.Empty<FreeShowOutputCommandPlan>();
        var targetIds = category == "audio"
            ? new[] { "" }
            : configuration.GetActiveNormalOutputs().Select(o => o.Id).ToArray();
        return targetIds.Select(id => PrepareAgainstCurrent(
            new FreeShowShortcutAction(command.Value, id))).ToArray();
    }

    public IReadOnlyList<FreeShowOutputCommandPlan> PreviewShortcut(NativeShortcutInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var active = configuration.GetActiveNormalOutputs().Select(x => x.Id).ToArray();
        // The source SETTINGS lock is authoritative regardless of the
        // optional current keyboard event's OutputLocked property.
        var guarded = input with
        {
            OutputLocked = input.OutputLocked ||
                ShowDocument.IsTrue(configuration.RawSettings, "outLocked")
        };
        return shortcuts.Route(guarded, active)
            .Select(PrepareAgainstCurrent).ToArray();
    }

    public async Task<FreeShowCommandBatchResult> DispatchShortcutAsync(
        NativeShortcutInput input,
        CancellationToken cancellationToken = default)
    {
        // Keyboard events are routed only when a real caller explicitly
        // invokes this method. No global key event listener is installed.
        var plans = PreviewShortcut(input);
        return await DispatchPreparedAsync(plans, cancellationToken);
    }

    private FreeShowOutputCommandPlan PrepareAgainstCurrent(
        FreeShowShortcutAction action)
    {
        var known = states.TryGetValue(action.OutputId, out var current);
        var plan = FreeShowOutputCommandPlanner.Prepare(
            action, configuration, overlays, known ? current!.Snapshot() : default);
        return known ? plan with { ExpectedNativeRevision = current!.Revision } : plan;
    }

    /// <summary>
    /// Non-executing preflight used by a future source-UI action bridge.
    /// Unimplemented actions stay disabled; this does not alter the
    /// shortcut router's timing/deduplication state.
    /// </summary>
    public IReadOnlyList<string> CheckReadiness(
        IReadOnlyList<FreeShowOutputCommandPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        var problems = new List<string>();
        if (plans.Count == 0)
            problems.Add("No original FreeShow action plan exists for this command.");
        foreach (var plan in plans)
        {
            if (!plan.CanPlan && plan.Warnings.Count == 0)
                problems.Add(plan.OutputId + ": original action is not implemented.");
            if (plan.ExpectedNativeRevision.HasValue &&
                (!states.TryGetValue(plan.OutputId, out var current) ||
                 current.Revision != plan.ExpectedNativeRevision.Value))
                problems.Add(plan.OutputId + ": source output changed since action preparation.");
            foreach (var warning in plan.Warnings)
                problems.Add(plan.OutputId + ": " + warning);
            if (!plan.CanPlan) continue;
            if (!runtimes.TryGetValue(plan.OutputId, out var runtime))
            {
                problems.Add(plan.OutputId + ": original output runtime not installed.");
                continue;
            }
            foreach (var effect in plan.RequiredEffects.Distinct())
            {
                bool ready = runtime.SupportsSourceEffect(effect) &&
                    (runtime is not IFreeShowOutputRuntimePlanReadiness dynamicRuntime ||
                     dynamicRuntime.CanExecuteSourceEffect(effect, plan));
                if (!ready)
                    problems.Add(plan.OutputId + ": missing or not ready source effect " + effect);
            }
        }
        return problems.AsReadOnly();
    }

    public async Task<FreeShowCommandBatchResult> DispatchPreparedAsync(
        IReadOnlyList<FreeShowOutputCommandPlan> plans,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plans);
        await executeGate.WaitAsync(cancellationToken);
        try
        {
            // All-or-nothing PREFLIGHT: never start F1/F2/F3 on some
            // physical screens if another screen is missing a required
            // backend capability. Physical execution is not a transaction:
            // if a backend throws mid-flight, report PartialExecution.
            var problems = CheckReadiness(plans);
            if (problems.Count != 0)
                return new(FreeShowCommandResultStatus.Refused,
                    0, plans.Count, problems);

            int executed = 0;
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!plan.CanPlan) continue;
                var runtime = runtimes[plan.OutputId];
                try
                {
                    await runtime.ExecuteSourceEffectsAsync(plan, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return new(executed > 0
                            ? FreeShowCommandResultStatus.PartialExecution
                            : FreeShowCommandResultStatus.Failed,
                        executed, plans.Count,
                        [plan.OutputId + ": runtime reported " + ex.GetType().Name + " — " +
                         ex.Message + " (physical state must be reconciled)"]);
                }

                CommitStateAfterRuntimeSuccess(plan);
                executed++;
            }

            return new(FreeShowCommandResultStatus.Completed, executed, plans.Count,
                Array.Empty<string>());
        }
        finally { executeGate.Release(); }
    }

    private void CommitStateAfterRuntimeSuccess(FreeShowOutputCommandPlan plan)
    {
        if (!states.TryGetValue(plan.OutputId, out var state))
            return; // F4 audio is global, not part of OutData.

        // Original clearSlide() invokes clearTimers("", false), which
        // clears out.transition separately from the lyric and does NOT
        // cancel running overlay timers. Update the in-memory state only
        // after the corresponding source runtime has acknowledged it.
        if (plan.RequiredEffects.Contains(FreeShowSourceEffect.CancelNextSlideTimers))
            state.ClearTransition();

        switch (plan.SourceAction.Command)
        {
            case NativeShortcutCommand.ClearBackground:
            case NativeShortcutCommand.ClearSlide:
            case NativeShortcutCommand.ClearAllOverlaysForShortcut:
            case NativeShortcutCommand.ClearEffects:
                state.TryApply(plan.SourceAction);
                break;
            case NativeShortcutCommand.ClearOverlays:
                state.ClearOverlaysKeepingLocked(
                    new HashSet<string>(plan.LockedOverlayIds, StringComparer.Ordinal));
                break;
        }
    }
}

/// <summary>
/// A production backend must implement *every* side effect in its command
/// plan, including original FreeShow timers, output cache, custom action
/// events and media layers. Supporting only a lyric Content=null is not
/// source-complete. Never register an incomplete runtime to enable controls.
/// </summary>
public interface IFreeShowOutputCommandRuntime
{
    string OutputId { get; }
    bool SupportsSourceEffect(FreeShowSourceEffect effect);
    Task ExecuteSourceEffectsAsync(FreeShowOutputCommandPlan plan,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional stricter preflight for runtime effects depending on the current
/// FreeShow video cue, source Show ID or authoritative timer registry. Static
/// SupportsSourceEffect alone cannot establish that the active media layer
/// or live scheduler can execute a particular prepared output command.
/// </summary>
public interface IFreeShowOutputRuntimePlanReadiness : IFreeShowOutputCommandRuntime
{
    bool CanExecuteSourceEffect(
        FreeShowSourceEffect effect, FreeShowOutputCommandPlan plan);
}

public enum FreeShowCommandResultStatus
{
    Completed, Refused, Failed, PartialExecution
}

public sealed record FreeShowCommandBatchResult(
    FreeShowCommandResultStatus Status,
    int ExecutedPlans, int RequestedPlans,
    IReadOnlyList<string> Problems)
{
    public bool Succeeded => Status == FreeShowCommandResultStatus.Completed;
}
