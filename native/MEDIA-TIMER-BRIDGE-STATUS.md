# Native Port 0.0.18 — FreeShow timer and persistent media bridge

Follow-up: [0.0.26 native timer scheduler](TIMER-SCHEDULER-STATUS.md) implements concrete timers and source timer-control logic against this registry/effect bridge. It is not installed in OperatorProbe; full elapsed-action/output integration remains incomplete. The limitations below describe the original 0.0.18 checkpoint.

## Goal and source of truth
Retain the existing FreeShow output behavior and UI. A lyric cue must not restart a persistent video decoder. F2 cancels next-slide timers only; F1 removes active out.background but must not remove the independent style background. These bridge components are a conservative **subset**, not complete output parity.

## Original FreeShow sources reviewed
- \`src/frontend/components/output/clear.ts\`: \`clearSlide()\` calls \`clearTimers("", false)\`; the final \`clearAll()\` calls \`clearTimers()\` with overlays enabled; transition state is updated separately through \`setOutput("transition", null)\`.
- \`src/frontend/components/helpers/output.ts\`: \`setOutput("background", ...)\` coordinates actual background VideoPlayer start/stop, and \`setOutput("slide", ...)\` updates foreground video items. \`setOutput\` also performs callbacks and source store changes; writing a JSON field is insufficient.
- \`src/frontend/components/output/layers/Background.svelte\`: two overlapping background instances and timed transitions during media replacement; rapid changes can race.
- \`src/frontend/components/output/layers/BackgroundMedia.svelte\`: images/video, online player, NDI, OMT, Blackmagic, camera and screen sources have independent renderers and fade/transition conditions.
- Existing native \`NativePersistentVideoOutput.cs\`: LibVLCSharp/Avalonia video player already has a persistent player and a separately prepared \`NativeSlidePresenter\`. \`ShowCue()\` only changes the lyric presenter; video start/stop is a separate method. The current player hardcodes looped, muted local video and cannot claim arbitrary original FreeShow media options.

## Code committed in 0.0.18
1. **\`FreeShowTimerSourceEffects\`** connects the existing \`FreeShowOutputTimerRegistry\` to F2 and the two Clear All timer phases. F2 cancels *only the selected output's slide timer*. Full Clear All final phase cancels slide and overlay/effect timers across original selected output IDs. Failed cancellations remain registered and produce errors.
2. **\`IFreeShowTimerInventoryAuthority\`** is mandatory for timer cancellation. An empty registry is **not** proof that no source timers exist; an actual scheduler must attest that its inventory is complete for each output before cancellation.
3. **\`FreeShowCompositeClearAllRuntime\`** composes source-complete step delegates and refuses missing steps. \`CreatePartialClearAllRuntime()\` intentionally supports **only timer phases**; it additionally checks the current selected-output timer inventory. It does not advertise complete Clear All or global audio/video/timeline support.
4. **\`FreeShowBackgroundContinuity\`** is a pure, source-conservative planner for an F1 active-background clear. It distinguishes the independent style background from \`out.background\`. It blocks outdated background IDs, missing or unowned decoders, simultaneous video layers, unported images/live sources, and media whose original loop/mute options do not match the current fixed LibVLC path.
5. **\`NativeFreeShowMediaSourceEffects\`** provides a single-video F1 physical-media subeffect, restricted to an exact known local video owned by the current output. It does not call \`ShowCue\` or recreate the decoder when the background remains unchanged.
6. **\`NativeFreeShowSourceEffectComposer\`** joins the existing F2 source slide-cache hooks, authoritative slide-timer cancellation hooks, conservative single-video background hooks, and native lyric clearing into one **partial native session runtime**. It deliberately does **not** install this runtime into any production output controller.
7. **Plan-specific preflight** — \`IFreeShowOutputRuntimePlanReadiness\` and \`IFreeShowClearAllPlanReadiness\` make the command coordinator test *current* media/cue ownership, Show source lookup and timer authority **before** accepting a command. Merely registering a callback is not proof that it can serve the selected live output.
8. Synthetic check code written for incomplete timer authority, F2 not affecting overlay/other outputs, final Clear All timer phase, stale/unowned video IDs, retained style media, dual-video refusal and unsupported video options. Per existing Beta 2 gate, none of these checks have been executed.

## Important limits
- **No complete timer scheduler** is installed: the registry only operates on handles registered by a future authoritative scheduler. No timer transitions are scheduled by this code. Transition \`OutData\` updates and \`output_changed\` notifications remain separate source effects.
- **No full video compositor**: the existing persistent player handles one local muted, looped video in a subset of cases. Original FreeShow's two overlapping transitioning backgrounds, still-image/style/video mixes, online video/NDI/OMT/camera/Blackmagic, effects and media control options remain unported.
- **No complete F1/F2/Clear All runtime**: all source callbacks, timeline effects, credits, overlay cleanup, global audio and complete Clear All must be implemented separately. Runtime compatibility gates intentionally keep output actions disabled in the read-only developer OperatorProbe.
- **No performance claim**: keeping the decoder outside the \`ShowCue()\` path is an architectural property of the source; real video continuity and <50ms cue latency have not been verified on Windows or the OptiPlex 7490.
- **No build or tests executed** under the user's requested Beta 2 validation policy. The code has not yet been proven to compile or render identically to FreeShow.

## Source data safety
Only \`native/\` on the \`native-port\` GitHub branch changed. \`church-dev\`, real .show files, FreeShow local/portable stores, videos and church output hardware remain untouched.
