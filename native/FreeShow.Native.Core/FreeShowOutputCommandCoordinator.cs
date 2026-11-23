using System.Text.Json;

namespace FreeShow.Native.Core;

/// <summary>
/// Output command integration gate. Owns the current Native in-memory copy of
/// each original FreeShow OutData and delegates effects only to explicitly
/// registered complete source-compatible runtime adapters.
/// Nothing in this class opens windows, runs media or writes FreeShow files.
/// </summary>
public sealed class FreeShowOutputCommandCoordinator
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
            .Select(command => FreeShowOutputCommandPlanner.Prepare(
                command, configuration, overlays))
            .ToArray();
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
            var problems = new List<string>();
            foreach (var plan in plans)
            {
                foreach (var warning in plan.Warnings)
                    problems.Add(plan.OutputId + ": " + warning);

                if (!plan.CanPlan) continue;
                if (!runtimes.TryGetValue(plan.OutputId, out var runtime))
                {
                    problems.Add(plan.OutputId + ": original output runtime not installed.");
                    continue;
                }
                foreach (var effect in plan.RequiredEffects.Distinct())
                    if (!runtime.SupportsSourceEffect(effect))
                        problems.Add(plan.OutputId + ": missing source-complete effect " + effect);
            }
            if (problems.Count != 0)
                return new(FreeShowCommandResultStatus.Refused,
                    0, plans.Count, problems.AsReadOnly());

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
