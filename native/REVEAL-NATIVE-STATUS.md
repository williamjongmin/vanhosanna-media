# Native Port 0.0.20–0.0.21 — native FreeShow line/click reveal and prewarming

## Intent
Restore FreeShow's original **Output.svelte / SlideContent.svelte / TextboxLines.svelte** semantics with a Windows Avalonia visual-only preparation layer. This is not a replacement for the original source output command/action lifecycle.

## Upstream implementation inspected
- \`src/frontend/components/helpers/output.ts:getOutputLines()\`: \`OutSlide.line\`, \`OutSlide.revealCount\`, \`OutSlide.itemClickReveal\`, current output \`style.lines\`, minimum active style line count, and highest active output line position jointly determine per-item \`start/end\`, \`linesStart/linesEnd\` and click visibility.
- \`src/frontend/components/output/Output.svelte\`: line state is computed per output, keyed by show ID and expanded layout index; the current upstream output has a 50ms line-state debounce.
- \`src/frontend/components/output/layers/SlideContent.svelte\`: a click-reveal item is omitted until \`itemClickReveal\`; \`Textbox\` receives either normal or lineReveal ranges; same-slide state changes avoid the full slide-hide/show transition.
- \`src/frontend/components/slide/TextboxLines.svelte\`: lines outside the half-open [start,end) range are hidden unless style.showAsFaded; empty-text lines are filtered when a range exists; it also supports original virtual breaks, chords, animations and variable-resolution behavior absent from the native subset.
- \`native/FreeShow.Native.Core/FreeShowSlideAdvancePlanner.cs\`: existing read-only original navigation state (Next/Previous/First/Last, clickReveal, lineReveal).

## New code / changes
1. **FreeShowRevealRenderProjection**: copies a native text-frame plan for the exact source OutSlide.index and output-specific style, retaining all original run styling and source JSON. It filters click-reveal items and crops ordinary/line-reveal text lines with the source's state, while refusing source showAsFaded alpha-compositing, virtual breaks, unsupported text actions, non-number/negative state, and other previously declared renderer omissions. It never alters the saved .show.
2. **FreeShowRevealStyleResolver**: reads a real output's original \`Config/settings_synced.json\` style.lines plus the authoritative current \`OutData.slide.line\` from every active output, reproducing the output-specific and shared-progress inputs to original getOutputLines. Unknown output IDs, styles and line state are refused.
3. **NativeSlidePresenter**: exposes \`PrewarmReveal()\` and \`ShowPrewarmedReveal()\`. Each distinct source cue/reveal state is built into an Avalonia control during preparation, bounded at 384 variants. Cue-time selection only swaps the prepared Viewbox (no source parsing, file I/O, text-control creation or decoder calls). Unprepared variants return false, never silently use full lyrics. It does not alter existing plain \`Show()\` behavior.
4. **NativePersistentVideoOutput** and **NativeFreeShowOutputSession**: carry that prepared reveal path through the persistent video output and original Show ID / arrangement / layout-index checks. Each output remains independent; the path does not seek/restart LibVLC. The existing cue metrics still only measure synchronous dispatch, NOT physical visible-frame latency.
5. **FreeShowMultiOutputPreview** and **FreeShowPreviewRegion**: when the original output actually displays a matching source slide containing click/line reveal, display its visible lines if styles and native output state are known. Otherwise keep Preview blank with a diagnostic; never expose lyrics that source FreeShow is deliberately hiding. OperatorProbe passes the original read-only styles and output state into that Preview.
6. **FreeShowRevealPrewarmSequence**: optionally walks the original Next/reveal sequence for ONE active output, with a bounded maximum state count and source output-binding resolver. The native output session can prewarm all accepted states before cue input. Multiple active outputs require an authoritative shared timeline; a hypothetical sequence is refused rather than treated as complete.
7. **FreeShow.Native.Checks**: authored synthetic checks for initially hidden click-reveal, first/second visible lines, missing style state, multistyle source context, alpha-fade refusal, original slide index guard, prewarm sequence bounds and multi-output refusal. **Not executed** under the Beta 2 validation agreement.

## Explicitly incomplete
- The original \`TextboxLines\` code supports virtual line breaks, chords, CSS gradients/masks, text strokes, highlights, shape-outside, fade/hide animation, auto-size, dynamic variables, effects, section media and line-specific action triggers. Native does not yet provide pixel-perfect behavior for these; unsupported frames remain guarded.
- A prewarmed reveal visual is **NOT** authorization for a keyboard shortcut to change physical output. Original \`updateOut()\`, timer cancellations, project actions, global media, style transitions and output_changed notifications still require a complete controller.
- Multiscreen reveal sequence generation is not complete. Preview's current source data is a read-only snapshot, not a live output capture.
- No Windows compilation, fixture execution, lyric rendering screenshot comparison, hardware video continuity test or Dell OptiPlex 7490 click-to-visible latency measurement has occurred. Actual performance improvement remains unverified.
- No live worship install, update or launch was performed.

## Safety
Changes are confined to \`native/\` on branch \`native-port\`. Original \`church-dev\`, FreeShow .show, project library, synced styles, active output settings and church media files remain untouched.
