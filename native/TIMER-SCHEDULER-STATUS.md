# Native Port 0.0.26 — native countdowns and source timer controls

## Implemented

`FreeShowNativeTimerScheduler` is the first concrete Native timer scheduler. It uses .NET `TimeProvider` one-shot alarms and posts notifications onto a single source event loop. `AvaloniaFreeShowTimerEventLoop` connects this to the actual Avalonia dispatcher. These components are not instantiated in OperatorProbe yet.

The implementation follows the repository's original `helpers/tick.ts`, `output/preview/Preview.svelte`, `output/tools/NextTimer.svelte`, `output/clear.ts` and `helpers/output.ts`:

- One next-slide timer per original output ID. Initial state is paused with autoPlay enabled; the original 10 ms startup task resumes the countdown. A nonpositive `newSlideTimer` request leaves an existing timer alone.
- Pause, resume and elapsed-time changes preserve the current position. A monotonic clock prevents system-clock corrections from changing remaining duration. Reading the current snapshot computes elapsed time directly; the old JS 50 ms/10 ms slider repaint loop is not reproduced as a second timing authority.
- `EnsureSlideTimer` follows Preview's create-only-if-missing behavior. Repeated preview reads cannot restart a running countdown. Source `updateOut` must explicitly cancel/recreate when restarting its next timer.
- `FreeShowNextTimerControls` ports play/pause and the shared slider gesture across the current active output set. If any active timer was automatically paused during a drag, mouseup resumes all current active timers, matching the original source. A shorter output whose chosen elapsed time exceeds its duration expires on the next event-loop turn after resume.
- Overlay/effect expiry timers preserve output ID, overlay/effect ID and target layer. Overlay and effect timers with the same ID share the same source slot. Output ownership uses separate tuple components rather than ambiguous ID concatenation.
- Every alarm carries both its registration identity and a generation. Cancel, replace, pause, scrub and disposal invalidate already-queued startup/expiry callbacks. An expired handle is unregistered **before** invoking the source action, so a callback may install a new timer without the old timer deleting it.
- `FreeShowOutputTimerRegistry` now supports exact-handle removal at completion/replacement. The scheduler privately owns that registry and supplies real handles to the existing `FreeShowTimerSourceEffects`: F2 cancels slide timers only; the final Clear All timer step includes overlay/effect timers. Off-thread cancellation is acknowledged only after the owner event loop processes it.
- The scheduler exposes an isolated inventory only for its explicitly owned Native output IDs, and relinquishes that authority on disposal. It cannot discover, cancel or adopt Electron timers.
- Source callback failures are recorded with the original output/overlay ID, a total failure count and the latest 128 exceptions. A failed expiry is not silently retried. The full backend must observe these diagnostics and handle any source/physical reconciliation; the scheduler does not mark output commands successful.

## Source action boundary

The scheduler generates **elapsed notifications**, not physical output commands. Its required callbacks run synchronously on the source event loop and must be connected to the complete source command backend:

1. On slide expiry, clear the source output's transition, then advance that output with `playNext:true`. For a folder path use the original folder playback action. For the case-sensitive `.pdf` path, restart the source folder timer before advancing. Removing the expired timer registration alone does not fulfill these effects.
2. On overlay/effect expiry, recheck that the ID is still in its original current layer, then apply the source `setOutput(type, id, true, outputId)` lifecycle. The timer must not toggle an overlay that was already removed by another source action.
3. F2/Clear All cancellation hooks still require original `OutData.transition` clearing, `output_changed` and the other source effects. This implementation does not claim that the complete F2/Clear All engine is finished.
4. Create/schedule/control/snapshot/dispose calls belong on the owner event loop. Its `Post` contract must queue rather than invoke inline. Shutdown must dispose the scheduler before the owner loop is stopped. Already-dispatched source commands belong to the source command queue, not to the expired timer's cancellation handle.

Source output-state changes, action/event dispatch, full `updateOut`, project/focus navigation, the actual NextTimer Avalonia widget and its repaint cadence, and live output windows remain to be connected. No complete `IFreeShowSlideTransitionSourceBackend` is registered in OperatorProbe. Invalid non-finite/overflowing durations and malformed transition values are rejected instead of being converted into immediate cues; negative overlay durations are unsupported.

## Validation

`FreeShow.Native.Checks/NativeTimerSchedulerChecks.cs` adds deterministic manual-clock/manual-event-loop fixtures for:

- Startup timing, pause/resume/seek, elapsed-time preservation and system-clock changes.
- Shared slider behavior with different output durations, paused timers and invalid selections.
- Queued callbacks after replacement, cancellation, pause and disposal.
- F2 versus Clear All cancellation and independent output/overlay ownership.
- Overlay/effect replacement, replacement timers created inside an expiry callback, and PDF/folder destination classification.
- Callback errors, off-thread cancellation acknowledgment, unknown outputs and non-finite durations.

The fixtures are wired into the existing Checks executable but **have not been executed**. Only source/diff review was performed. No .NET build, GUI run, Windows test or physical latency measurement occurred under the recorded Beta 2 validation policy. This checkpoint does not establish release readiness or completed FreeShow behavior parity.
