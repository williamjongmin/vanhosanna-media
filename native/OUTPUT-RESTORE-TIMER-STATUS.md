# Native Port 0.0.16 — FreeShow original Restore, timers, state sync

## Objective
Preserve FreeShow's existing Output Clear All → Restore workflow and the distinction between next-slide and overlay/effect timers. No new UI workflow is introduced; the read-only developer OperatorProbe cannot control physical outputs.

## Original source inspected
- \`src/frontend/components/output/clear.ts\`:
  - \`clearAll(button)\` checks output lock and operator-selection context, resets \`outputSlideCache\`, snapshots active outputs plus global audio via \`storeCache()\`, stops active timeline playback, clears background/slide/overlays/audio and finally calls \`clearTimers()\`.
  - \`storeCache()\` stores **full original OutData**, keyed by active output ID. Also stores \`playingAudioData\` including path/metadata/startAt and \`playingMetronome\`.
  - \`restoreOutput()\` restores only currently active output IDs present in the cache, then resumes cached audio/metronome and sets \`outputCache=null\`. It refuses when locked or there is no outputCache.
  - \`clearSlide(shouldClearAll=false)\` conditionally caches its position and calls \`clearTimers("", false)\`. This clears **next-slide timer/transition**, NOT overlay timers.
  - \`clearTimers(specificOutputId="", clearOverlayTimers=true)\` clears \`out.transition\`, slideTimers and, when requested, overlayTimers, scoped to the indicated output or active output IDs.
- \`src/frontend/components/output/preview/ClearButtons.svelte\`:
  - When presentation is fully cleared and \`outputCache\` exists, the UI swaps Clear All for **Restore** (\`reset\` icon, \`preview.restore_output\`).
  - Restore is initially disabled and activates after 1000ms. When output content becomes active again, Restore is hidden and the source UI clears outputCache.
  - The clear-all status also needs global audio/metronome state; unknown Native audio is *not* assumed empty.
- \`src/frontend/components/helpers/output.ts\`: clearing the output transition routes through \`setOutput()\` and therefore requires its \`output_changed\` side effects.
- \`native/FreeShow.Native.Core/FreeShowOutputCommandCoordinator.cs\` and \`NativeFreeShowOutputSession.cs\`: previously created guarded source-effect command execution and separately persistent video/lyric presentation.

## Implementation in this milestone
1. **FreeShowOutputRestoreCache**: caches the entire original OutData JSON of active normal outputs and separately stores original playingAudioData/metronome metadata without inferring absent state. The cache preserves unknown source fields and never writes settings or .show.
2. **FreeShowOutputRestorePlan**: intersects saved output IDs with current active output IDs (original Restore behavior), and refuses to call a restore complete if global audio/metronome metadata is missing.
3. **IFreeShowOutputRestoreRuntime** and **FreeShowOutputCommandCoordinator.RestoreAsync**: preflights every output's physical restore capability **and** global audio/metronome restore before running any restoration. Applies output snapshots only after that output runtime acknowledges success; retains cache on partial failure and reports exactly which output(s) were acknowledged. Clears the backup only after all outputs and global audio return successfully. Physical side effects are not transactional; partial results require reconciliation.
4. **FreeShowOutputTimerRegistry**: distinct source slideTimer and overlayTimer cancellation handles scoped by output ID. The F2/slide-only path does not cancel overlays; full timer cleanup can cancel both. Failed cancellation remains registered for retry; concurrent cancel/registration cannot silently remove or overwrite a different timer handle.
5. **FreeShowOutputState.Revision** plus **SynchronizeAcknowledgedOutputAsync**: optimistic source revision check prevents stale external control events from overwriting a newer Native OutData. Fully acknowledged snapshots preserve unknown JSON keys.
6. **FreeShowOutputCommandPlanner** and **Coordinator.CheckReadiness**: Preview/shortcut commands planned against the most recent in-memory OutData; revision is recorded and checked before dispatch. Empty or unsupported action batches are refused instead of falsely reporting Completed.
7. **FreeShowRestoreEligibility**: source 1000ms Restore timing and full-clear/locked-state conditions modeled as a pure, clock-injected policy with no hidden timer or active output.
8. **FreeShowClearButtons / FreeShowPreviewRegion**: source reset icon and Restore label used when the cached output is fully clear. The Restore button stays disabled without a complete source-compatible physical/output/audio runtime. The read-only developer probe never supplies such a runtime.
9. **FreeShow.Native.Checks**: synthetic, unexecuted fixtures for active-output-only cache, unknown audio refusal, output/microphone-independent restoration contracts, two-output restore with one failing, cache persistence, full restore with audio, revision races, 1000ms Restore eligibility, F2 vs full timer cancellation, unknown/stale event rejection and duplicate timer handle safety.

## Explicit limitations
- The **original full Clear All operation** is not yet implemented end-to-end. FreeShow controls selection/popup context, timeline stop, persistent overlay behavior, audio/microphones, cached output and action callbacks. Only the foundational cache/restore/timer primitives and fail-closed contracts exist.
- Actual restore of video/image/style backgrounds, scenes, lyric layers, output effects, overlays, captures and Stage/network outputs remains unported. \`IFreeShowOutputRestoreRuntime\` is an interface, NOT a working playback implementation.
- Global audio, metronome, playlists, fades and track-offset resumption are not ported; missing metadata means restoration is refused.
- The timer registry does not create, schedule or discover timers. An actual FreeShow-equivalent scheduler must register its live handles and correctly mirror the source store. Timer state does not become "known cleared" merely because this registry has no entries.
- UI progress can only be verified with a Windows build and screenshot comparisons. This milestone has NOT run a build, fixtures, live output, display timing, or OptiPlex 7490 benchmarks.
- Earlier 0.0.15 keyboard F3 vs Preview overlay-clear distinction remains: keyboard F3 clears locked overlays; the Preview button retains them.

## Data safety
Only \`native/\` source on branch \`native-port\` was changed. No changes to \`church-dev\`, original .show, local SETTINGS, projects.json, templates, media, or active church output devices. Developer Probe remains intentionally read-only.

## Future work
- Source-complete Clear All orchestration, including timeline and global audio effects
- Registered native timer scheduler with transitions, actual playback and overlay timer control
- Physical output restoration with synchronized media compositing and complete audio resumption
- Original project/line navigation and UI/feature parity, followed by the agreed Beta 2 Windows/hardware validation gate
