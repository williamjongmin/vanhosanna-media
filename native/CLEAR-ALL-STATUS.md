# Native Port 0.0.17 — source-ordered Clear All orchestration

## Goal
Preserve **exact FreeShow source behavior** rather than treating F1+F2+F3+F4 as a new approximate command. New logic is guarded and cannot operate real church output without explicit, source-complete runtime integrations.

## Source files actually reviewed
- src/frontend/components/output/clear.ts: clearAll(button), storeCache, restoreOutput, clearBackground, clearSlide, clearOverlays, clearTimers.
- src/frontend/components/output/preview/ClearButtons.svelte: explicit button clearAll(true), restore and active output state.
- src/frontend/utils/shortcuts.ts: Escape/dot route to clearAll(), with popup/editor/selection context limitations.
- src/frontend/components/helpers/output.ts: isOutCleared(null) ignores scene/refresh and locked overlays, setOutput() has output_changed and other runtime side effects.
- src/frontend/audio/audioFading.ts: audio/playlist/microphone and commonClear behavior remains a separate unported dependency.

## Original Clear All order
1. Refuse when output is locked. For keyboard-origin clearAll(false), refuse when a popup, non-scripture selection, editing/stage selection, or context menu is active.
2. Reset outputSlideCache. If no visible output/audio/metronome needs clearing, return immediately.
3. storeCache: save **full OutData for each active output** and capture playingAudioData (path/metadata/currentTime) + playingMetronome.
4. Stop currently active timeline playback.
5. clearBackground: clear current output background and invoke its original events/credits.
6. clearSlide(!focusMode): cancel next-slide timers without clearing overlay timers, then clear visible lyrics/notify source listeners. Focus mode may re-cache valid slides.
7. clearOverlays: preserve source-defined locked overlays; clear effects. This is the *Clear All / Preview button* path, **not** keyboard F3.
8. clearAudio("", {clearPlaylist:true, commonClear:true}): full original audio and playlist behavior.
9. clearTimers(): clear output transition and remaining slide/overlay timers.

## Implemented changes
- **FreeShowClearAllCoordinator.cs**: partial FreeShowOutputCommandCoordinator shares its existing serialized execution gate with F1–F4/Restore.
- **FreeShowClearAllContext**: distinguishes direct button vs keyboard/context source, tracks focus mode, popup/selection/menu blockers and authoritative audio/metronome state.
- **PlanClearAll / CheckClearAllReadiness**: records active output IDs, full original overlay lock definitions, Native OutData revisions and source context. Refuses unknown lock metadata, missing authoritative playback, missing source Show for focus-mode cache, absent/partial runtime capabilities and stale output revisions.
- **IFreeShowClearAllRuntime**: requires a complete source-equivalent implementation of *all seven chronological runtime phases*. Each phase must include its original internal setOutput, custom action, timer, compositing and timeline effects. A mock or JSON-only change must **not** be attached in production.
- **ClearAllAsync**: verifies everything before affecting the output or original backup. Resets the slide cache, stores a full OutData+playback snapshot before timeline stop, executes seven ordered runtime steps, and only commits the Native OutData layer changes after the relevant phase acknowledges success. On failure it returns Failed/PartialExecution and retains recovery data; it never claims physical rollback.
- **Conservative protection**: refuses to overwrite an existing restore cache from a previous partial clear. Source JavaScript normally replaces cache during each nonempty clear; Native adds this stricter rule to avoid unrecoverable half-cleared state.
- **FreeShowClearButtons / FreeShowPreviewRegion**: the existing original-shaped Clear All button can now be wired to the guarded full-action executor. No complete runtime is installed by OperatorProbe, so the destructive control **stays disabled**. Restore timing includes a one-time Avalonia UI-thread refresh at the original 1000ms deadline if a complete restore backend is available.
- **Synthetic fixtures**: author-only checks for order, locked/unlocked overlay behavior, UI-context constraints, full capability refusal, partial failure recovery, focus-mode position cache, successful acknowledged state changes, and source plan invalidation. Checks are **not executed** under the Beta 2 test policy.

## Not yet implemented / validated
- No physical playback/audio/timeline implementation of IFreeShowClearAllRuntime has been installed. Current source-complete effect enforcement does not mean the missing functionality already exists.
- No complete media, effects, overlay timer, microphone, audio fade/playlist, scene, stage or online output parity.
- No native app installation or live Windows operator workflow validation.
- In a running program, the Clear All adapter must be able to reconcile a step that partially changed output and then threw.
- Windows compile and synthetic tests remain unexecuted; all screenshot comparisons and Dell OptiPlex 7490 click-to-visible measurements await Beta 2. The under-50ms target is unproven.

## Safety
Only native/ development files on native-port are modified. Original FreeShow church-dev, church media, project files, local SETTINGS, and worship outputs remain untouched. Developer OperatorProbe is read-only.
