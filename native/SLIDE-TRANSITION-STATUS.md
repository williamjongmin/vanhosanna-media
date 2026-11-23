# Native Port 0.0.24 — original FreeShow slide command lifecycle integration

## Goal
Connect the source-accurate navigation and prewarmed native lyric views to a fail-closed command coordinator **without treating a cached text-frame swap as a complete FreeShow slide action**. No physical output runtime is registered in the read-only OperatorProbe.

## Original source inspected
- \`src/frontend/components/helpers/OutputHelper.ts:playSlide()\`: calls \`checkActionTrigger(layoutData,index)\`, stores a pending per-output slide, then on the next task invokes \`setOutput("slide", data, false, outputId)\` and \`updateOut(data.id,index,layout,slideLayers,outputId)\`.
- \`src/frontend/components/helpers/output.ts:setOutput()\`: slide selection can stop break recording, update original scripture attribution/usage, trigger group/category actions, clear prior foreground background and adjust overlay/timer/playback state; \`output_changed\` propagation is part of that physical lifecycle.
- \`src/frontend/components/helpers/showActions.ts:updateOut()\`: handles show usage and edit position, startShow action, per-output binding, next slide timers, slide/ghost background selection, microphones, audio, overlays, effects and custom actions. Some actions run with source timeouts. This function is NOT equivalent to choosing a renderer frame.
- Existing \`native/FreeShow.Native.Core/FreeShowSlideAdvancePlanner.cs\`: produces read-only original-shaped next OutSlide with per-output reveal states; 0.0.22 source snapshots protect against concurrent output revision changes.
- Existing \`native/FreeShow.Native.Rendering/NativeFreeShowOutputSession.cs\` and \`NativeSlidePresenter.cs\`: LibVLC background remains separated from lyrics and reveal controls may be prewarmed.

## Code changes

1. **\`FreeShowSlideTransitionContract.cs\`** defines \`FreeShowSlideTransitionPlanner\`: checks the enabled output set, locked source config, coherent old OutData revisions, source Show IDs, expanded layout indices, current Show identity and original layout metadata. It creates targets only for actual ChangeSlide/RevealWithinSlide outputs; a different screen's project-boundary state is NOT interpreted as an empty frame.
2. The same core contract exposes three **ordered required source phases**: \`CheckOriginalBeforeSlideActions\`, \`SetOriginalSlideWithFullOutputLifecycle\`, \`RunOriginalUpdateOutAndAfterSlideEffects\`. Each phase must implement its entire original FreeShow responsibility; an \`IFreeShowSlideTransitionRuntime\` is a production integration boundary, not a working action engine.
3. **\`FreeShowSlideTransitionCoordinator.cs\`** shares the original coordinator's serialized command gate with F1–F4, Clear All and Restore. It verifies *every target* has a source-complete runtime supporting all three phases **and the exact prewarmed lyric variant** before touching anything physical. Following all acknowledged phases, it asks the runtime for **the entire original OutData**, validates its Show/layout/index and expected reveal fields, then updates the native copy **once**. It never assumes only the slide field changed.
4. **Partial execution quarantine:** any thrown backend error or cancellation after starting a physical step marks the affected output IDs as requiring authoritative reconciliation. All further slide transitions, F1–F4 and Clear All on those outputs refuse until \`SynchronizeAcknowledgedOutputAsync\` accepts an authoritative full OutData snapshot or a source-complete Restore succeeds. The coordinator never claims an atomic rollback for external devices.
5. **\`NativeSlideTransitionVisualPreparation.cs\`** builds cloned predicted OutData **without changing live output state**, replaces only its future OutSlide, then uses 0.0.22's coherent multi-output reveal batch and Native prewarmer. A single missing/unsupported output prevents a "prepared" result. Live output and media are never activated.
6. **\`NativeSlidePresenter.HasPrewarmedReveal\` → \`NativePersistentVideoOutput\` → \`NativeFreeShowOutputSession\`** exposes pure cached-variant lookups. **\`NativeSourceSlideVisualPreflight\`** validates the exact original source Show/layout/index and future style before a production adapter may report the variant prepared. These operations do not seek or restart video.
7. Synthetic fixture code (not executed) now covers: one output still revealing while another is at the project boundary, refused missing source backend, refused unready prewarmed visual, refused incomplete updateOut phase, successful three-phase acknowledgment, a stale plan, backend returning the wrong original slide, mid-physical cancellation, partial phase failure, output quarantine and reconciliation.

## Explicit limitations / why this is not 1.0.0
- The **actual complete** original \`checkActionTrigger\`, \`setOutput\`, \`updateOut\` side-effect handlers are NOT installed. \`IFreeShowSlideTransitionRuntime\` is an interface that requires a production implementation, not the implementation itself.
- Original song/project selection, focus/activeShow and pendingSlides behavior, synchronized physical output windows, 50ms UI debounce, style animations, variable/scripture text, stage/NDI/capture outputs, active media transitions, custom timers, audio/microphone and category actions are incomplete.
- Prewarmed glyphs cannot prove GPU frame visibility or under-50ms physical latency. No background-video continuity or Windows multi-monitor hardware test was conducted.
- No Windows compilation, synthetic test execution, screenshot comparison, full worship rehearsal or release installation has occurred under the user's **defer all builds/tests until Beta 2** instruction.
- A runtime that reports full source support dishonestly could still cause physical side effects. The production adapter must be source-audited and tested; the developer OperatorProbe continues to install none.

## Data safety
Only \`native/\` files on GitHub branch \`native-port\` were changed. \`church-dev\`, the working original FreeShow, \`.show\` data, \`projects.json\`, media files, local synced styles and live church output were not touched.
