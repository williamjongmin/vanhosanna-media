# Native Port 0.0.27 — queued navigation and slide-timer dispatch

## Implemented

`FreeShowNativeCommandQueue` serializes asynchronous commands in admission order on the source event loop. Each command starts after the preceding task settles. Rapid same-source input is retained, not coalesced. Synchronous/asynchronous failures are observed; cancelled requests are skipped before they start. Disposal cancels unstarted requests while an in-flight operation finishes through its own acknowledgment/reconciliation contract. Dispatcher loss faults outstanding requests explicitly, without automatic retries or a rollback claim.

`FreeShowNativeNavigationCommands` connects navigation intent and the scheduler's slide-expiry callback to the queue and `FreeShowOutputCommandCoordinator`. Keyboard callers must first apply the existing shortcut router's filtering/debounce. Timers bypass key filtering and preserve their original output ID. `OnSlideElapsed` records asynchronous success, refusal and failure receipts (latest 128, plus a total count).

`DispatchSlideAdvanceAsync` acquires the existing clear/restore/transition gate **before** capturing state and preparing navigation. Two admitted Next commands calculate successive targets from confirmed state instead of reusing the same old slide. The resolver may only find already prepared, command-specific runtimes; file access, media decoding and visual prewarming do not belong in this dispatch callback. Every target still requires the complete three-phase source runtime and exact prepared visual from 0.0.24–0.0.25.

The inspector/planner now accept an optional output selection. Null preserves keyboard navigation across all active normal outputs; a slide timer supplies one ID. Empty, duplicate, unknown and inactive selections refuse as a whole. Capture still includes **all enabled normal and stage outputs**, retaining shared reveal/style context for single-output actions. Project selection cannot be inferred from a subset.

`DispatchSlideTimerExpiryAsync` connects ordinary current-Show slide expiry:

1. Capture revision and full OutData when the scheduler delivers its generation-checked notification; queue that snapshot with the intent.
2. Under the coordinator gate, reject a stale snapshot, unreconciled output, folder/PDF destination or missing authoritative backend.
3. Invoke `IFreeShowSlideTimerExpirySource` for the original `tick.ts` transition clear. Verify its actual full acknowledgment differs only by `transition: null`, then commit the acknowledged copy.
4. Without releasing/reacquiring the gate, calculate this output's Next target and invoke the existing complete slide transition path. Other active outputs do not advance because of this timer.

Results distinguish completed advancement, untouched refusal, confirmed cleanup without advancement, and uncertain/partial execution. Lock, project boundaries or unavailable prepared lyrics can leave cleanup acknowledged while advancement refuses. Failed/cancelled cleanup after source work starts quarantines the output. Partial slide execution uses the existing quarantine.

## Source mapping and limits

The reference is the unchanged original source:

- `helpers/tick.ts`: clear `out.transition`, remove timer, then one-output `advanceOutput(id, "", { playNext: true })`; PDF and folder playback have separate branches.
- `helpers/OutputHelper.ts`: `advanceOutputs` targets the active group; `playNext:true` bypasses quickChangeBack and has separate project-item execution.
- `OutputHelper.playSlide`: before-actions precede the pending callback, then complete `setOutput` and `updateOut`. The pending-slide equality check controls pending-map deletion; it **does not cancel older callbacks**.

This checkpoint ports a serialized **within-current-Show** path, not full source event-order parity:

- Pending-slide/custom-action reentrancy, activeShow/focus/quickChangeBack, cleared-slide cache and project transitions remain incomplete. A source handler must not await a child command admitted behind itself in this queue; the eventual action engine needs source-equivalent staging.
- Folder/PDF expiry refuses before cleanup. Overlay/effect expiry remains the separate scheduler callback and needs full membership/toggle handling.
- The queued-expiry revision check is conservative: any acknowledged change, including an unrelated layer, invalidates the old expiry. No silent retry targets newer lyrics. Scheduler cancellation invalidates alarms before notification; accepted commands belong to the queue. Timer restart/cancellation and other mutations must use the same owner/command ordering.
- Configuration, activation and source-context snapshots are fixed for this coordinator instance. Dynamic reconfiguration needs a complete controller.
- FIFO applies to commands submitted through the queue. The coordinator gate also prevents overlap for direct callers, but semaphore acquisition is not a FIFO input policy. The eventual operator must route clear/restore/input/timer mutations through the shared queue.
- No production `IFreeShowSlideTimerExpirySource` or complete `IFreeShowSlideTransitionSourceBackend` is installed. Fake stores in Checks are fixtures only. OperatorProbe remains read-only; no live keyboard/timer output is enabled.
- A selected Native control remains a logical receipt, not physical scanout or proof of sub-50 ms latency.

## Validation status

`NativeNavigationCommandChecks.cs` adds fixtures for rapid asynchronous Next commands, fresh revisions, all-output versus single-output dispatch, scheduler/manual-clock integration and cancellation, stale expiry, invalid selection, folder/PDF refusal, full-capture invalidation, locked/boundary outputs, missing visuals/backends, wrong cleanup acknowledgment, cancellation during cleanup, quarantine, failure isolation, queue disposal and dispatcher loss. It reuses the existing manual clock and requires no church data or output devices.

Fixtures are wired into Checks but **have not been run**. No build, Windows execution, GUI test or latency measurement was performed, following the recorded Beta 2 policy. Only source review and `git diff --check` were performed.

Only `native/` on `native-port` is changed. Operational `church-dev` and church files/output remain outside this checkpoint.
