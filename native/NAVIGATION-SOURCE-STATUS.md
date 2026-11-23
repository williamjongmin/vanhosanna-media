# Native Port 0.0.19 — original FreeShow slide and project navigation

## Goal
Carry the original FreeShow **Next, Previous, Home, End, F5, Space, ArrowRight/Left and PageUp/Down** semantics forward without changing the operator's existing workflow, song files, output IDs, media status or UI design. All new navigation methods are **inspection-only** at this milestone.

## Source examined directly
- src/frontend/utils/shortcuts.ts — source key mapping and editor/timeline focus guards. Original F5 and navigation keys pass into OutputHelper.advanceOutputs() (the existing NativeShortcutRouter already emits these semantic commands).
- src/frontend/components/helpers/OutputHelper.ts — authoritative advanceOutput, playFirst/Last/Next/Previous, getShow, getNextSlide/getPreviousSlide, style lines, clickReveal, lineReveal, disabled/bound slide skip, end:true wrapping, getProjectItemIndex, changeProjectItem, playSlide, playItem, quickChangeBack and pendingSlides.
- src/frontend/components/helpers/showActions.ts — getFewestOutputLines() reads the minimum positive style.lines on active slide-rendering outputs; getItemWithMostLines() counts original lines with text-run values; updateOut() handles original side effects, actions, background changes, timers and media.
- src/frontend/components/helpers/shows.ts — _show().layouts().ref() emits one expanded parent/child array. IMPORTANT: each ref's "data" is the original layout entry or child override itself; it is **not** a nested "data" within the source. The implementation was corrected to access ShowDocument.ReadLayoutEntries().RawEntry directly.
- src/frontend/components/helpers/output.ts — isOutputBound() resolves original output ID, synced registry IDs or normalized local output names; setOutput() performs media and callback side effects.

## New source-grounded code
1. **FreeShowSlideAdvancePlanner** reads the original .show layout/slide and OutSlide fields (layout, index, line, itemClickReveal, revealCount) to produce original-shaped **next OutSlide JSON** for Next/Previous/Home/End. Preserves the source's style-line → clickReveal → lineReveal → slide-index priority; matches source previous-step and "end" loop logic; refuses missing/mismatched Show ID, invalid layout, ambiguous missing expanded child indices, unsupported media types and unresolved output bindings. It never writes the real OutData.
2. **FreeShowProjectAdvancePlanner** uses the *original project shows array source index*, not song ID, so repeat entries keep distinct identities and per-item layouts. Non-focus skips empty sections; focus skips sections and reverse overlay entries. Records the need for unported section custom actions. Respects nextItemOnLastSlide and wait-for-other-active-outputs; it never marks a project item as played or changes operator selection.
3. **FreeShowNavigationSourceContext** computes the minimum positive source style.lines across active configured outputs, honors slide-hidden layers, and optionally resolves synced output bindings. If original synced styles are unavailable or a bound ID cannot be resolved, it refuses instead of guessing.
4. **FreeShowMultiOutputAdvanceInspection** inspects the **actual OutSlide state per active output ID**. One output's show boundary never forces a project jump while another active output can still reveal or advance a slide. When all reach a boundary, it yields a read-only project-selection plan. Output state remains unchanged.
5. **NativeFreeShowOutputSession.InspectPreparedNavigation** maps the original layoutIndex to the already prepared native renderer/video cue ordinal (which can differ when disabled source slides are filtered). Requires the same original Show ID and layout; refuses missing/disallowed frames, unported reveal, media, source actions and known unsupported renderer omissions. It does **not** call ShowCue, start video or dispatch live output.
6. Existing **FreeShowShowRegion** and **FreeShowProjectBrowser** now offer read-only navigation-inspection methods based on these core planners, without new operator UI design or enabled live controls.
7. **FreeShowKeyboardAdvanceInspector** combines the existing FreeShowShortcutRouter with multi-output navigation inspection, so F5/Space/arrows/PageUp/PageDown/Home/End use original keyboard focus guards and 80ms cross-source debounce without installing a live key listener.
8. Synthetic check fixtures have been authored covering source-expanded slides, initial/last, style split, click/line reveal priority, previous restore state, disabled/output bindings, unknown synced-output ID refusal, end:true loop, duplicate project show IDs, section skips, style.lines minimum, multi-screen project-boundary sync, keyboard routing and cross-input debounce. **Checks not run.**

## Deliberately unimplemented
- Original OutputHelper.getShow chooses active vs outputted Show based on focus mode, Space, active selection, in-memory pendingSlides and outputSlideCache. The new inspect-only planner requires a known current source Show; it **does not claim full auto-selection parity**.
- quickChangeBack, activeFocus vs activeShow lifecycle, project played flags, special.nextItemOnLastSlide propagation, section actions, next slide timer, project auto-play, media/PDF/PPT/folder handling and source line action triggers remain outside the supported live adapter.
- The existing native text renderer does **not yet reproduce style-line splitting and click/line reveal rendering**. Computing next OutSlide is NOT showing it correctly; navigation CanDispatchLive is intentionally false everywhere.
- Source OutputHelper can trigger background/foreground audio/video, custom actions and setOutput callbacks on slide changes. Real show switching must not simply call NativeFreeShowOutputSession.ShowCue(index). The physical UI/engine action bridge is therefore not active.
- The developer OperatorProbe still displays source-based UI read-only, with no live navigation shortcut dispatch.
- Native Windows compilation, fixture execution, screenshot parity and OptiPlex 7490 click-to-visible measurements have **not** been performed. The <50 ms target remains unverified.

## Safety
Changes are in native/ on the GitHub branch native-port only. Original church-dev, source .show, projects.json, media assets, SETTINGS and live church output devices have not been altered. Previous checkpoints remain available.
