# FreeShow Native Port 0.0.15 — guarded output-command integration

## Objective
Join the existing source-derived FreeShow keyboard/Preview controls, OutData state, and native Avalonia lyric surface without declaring incomplete media, timer, cache, overlay, audio or project behavior to be working. Source FreeShow and church data are never written.

## Original source inspected
- src/frontend/utils/shortcuts.ts: F1-F4, repeat/double-F2 behavior and timeline recording. In particular keyboard F3 calls setOutput("overlays", []) followed by setOutput("effects", []).
- src/frontend/components/output/preview/ClearButtons.svelte: Preview-button clear("overlays") calls clearOverlays() rather than the direct keyboard F3 path.
- src/frontend/components/output/clear.ts: clearBackground, clearSlide, clearOverlays, clearAll, restoreOutput, clearTimers and clearScene; F2 cache and timers, Preview overlay lock preservation, and global audio handling.
- src/frontend/components/helpers/output.ts: setOutput() performs output_changed callback, output-specific media and audio checks, overlay timer cleanup, lock-index persistence and other source events. A direct change to OutData alone is NOT complete source behavior.
- src/frontend/audio/audioFading.ts: global audio clear includes metronome handling, active playlist, microphone options, fades and asynchronous audio cleanup.
- src/frontend/components/helpers/OutputHelper.ts: real next/previous/home/end needs line reveal, project navigation, layering and source actions—not index++.

## Source discrepancy discovered (important)
**The upstream FreeShow keyboard F3 and Preview overlay-clear button do NOT have the same locked-overlay behavior.**
- **Keyboard F3:** direct setOutput("overlays", []) clears all overlays, even those marked locked. It then clears effects and records the timeline action.
- **Preview overlay clear button:** calls clearOverlays(), which retains IDs in the original overlays store whose "locked" field is true, then clears effects.
The Native code uses two distinct enum commands: ClearAllOverlaysForShortcut vs ClearOverlays. Earlier milestone descriptions saying all F3 operations preserve locked overlays were inaccurate; this document supersedes that simplification.

## Implemented
- **FreeShowOutputCommandPlanner**: translates each source shortcut/Preview clear action into an ordered list of original-side effects. Uses existing output IDs, enabled/active state, source output lock and actual overlay lock metadata. Unsupported navigation is refused. All commands explicitly require output_changed and/or timeline event callbacks as necessary.
- **FreeShowOutputCommandCoordinator**: serializes commands; verifies that **every target output** has a backend supporting **every required source effect** before performing even the first step. Refuses missing/incomplete/unregistered backends without mutating native output state. After an adapter returns successfully, commits the corresponding state change only for that output. Multi-output mid-command failures are marked PartialExecution, because external physical actions cannot be rolled back atomically.
- **IFreeShowOutputCommandRuntime**: source-complete runtime contract, including global audio identity (empty output ID). This is an integration boundary, not a claim of existing functionality.
- **NativeFreeShowSessionCommandRuntime**: connects already-implemented lyric surface ClearSlide() to the command pipeline. It only implements the visual lyric clear automatically. Next-slide timer cancellation, cache handling, custom action notifications, output_changed, audio, overlay compositing, scene and true background-layer handling still require separately implemented source hooks. Without those the coordinator refuses execution.
- **FreeShowOutputSlidePositionCache**: conservative original F2 in-memory slide cache with original active slide JSON, show ID, layout ID, original index, last-slide/focus-mode rules and clear-on-double-F2. Does not write .show or settings.
- **FreeShowSlideCacheSourceEffects**: supplies the F2 capture/clear hooks to an external runtime, but cannot satisfy timer/action/media requirements by itself.
- **FreeShowPreviewControlState**: optional dynamic Native OutData states, so a successfully acknowledged command changes the Preview status without altering original SETTINGS.
- **FreeShowClearButtons / FreeShowPreviewRegion**: source Preview buttons can invoke the guarded coordinator only if its complete-runtime capability preflight succeeds. They report completed/refused/partial/failed outcomes. The internal OperatorProbe registers NO real runtime, so destructive controls remain disabled.
- **FreeShow.Native.Checks**: synthetic scenarios for F2 cache and ordered side effects, keyboard F3 vs Preview overlay lock behavior, F4 global-backend refusal, active-output settings, all-output preflight, acknowledged state update, stale SETTINGS separation, and completion/partial status. These tests have been authored but **not executed** under the user's Beta 2 test policy.

## Still not production-ready
- Full actual FreeShow setOutput() side-effect parity has not been demonstrated. A runtime hook must implement its ENTIRE claimed effect (including overlay timers, lock persistence, output_changed notifications, media/credits and other source behavior) rather than just toggling a JSON field.
- F1 media clear must keep style background/media independent from active background and handle dual media layering correctly. This is not currently wired to a source-complete renderer.
- F2 native lyric visibility is implemented, but the full source timing, cache, notifications and transition cancellation need live runtime integrations.
- Keyboard F3 overlay cleanup and Preview F3-looking clear button are distinct by source; some current UI variants are still styling approximations.
- F4 audio engine, microphone clear, playlist/metronome/fade and timeline callbacks are unported. Clear All, Restore, Scene, timers, project advance, line reveal, stage and online output remain unsupported.
- Core controller currently maintains its own in-memory copy of output state. Production must synchronize changes from the actual FreeShow-equivalent global state store and handle external controllers/remote input.
- No actual Windows compilation, fixture execution, UI screenshot parity, decoder test or Dell OptiPlex 7490 physical latency measurement has been performed. The program is not safe for live worship as a replacement.

## Data safety
All native code is committed only under native/ on GitHub branch native-port. Church source data, original FreeShow working branch church-dev, settings, project records, videos and hardware output are not touched. The developer OperatorProbe stays read-only and cannot turn on live output.
