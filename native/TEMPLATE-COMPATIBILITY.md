# FreeShow Native Port 0.0.6 — lyric template compatibility

## Objective
Match FreeShow's ordinary lyric template styling, without changing the FreeShow operator UI, rewriting .show files, or degrading the already-defined source data model.

## Verified upstream sources
- `src/frontend/components/helpers/output.ts`: `setTemplateStyle()`, `getStyleTemplate()`, `mergeWithTemplate()`, `applyMixedTextFormatting()`, `getTextFormatSets()`
- `src/frontend/components/slide/wordOverride.ts`: dynamic template style override regexes
- `src/frontend/components/output/layers/SlideContent.svelte`: actual output text rendering path
- `src/types/Show.ts`: template schema and per-item rich text structure

## Implemented
- `FreeShowTemplateMerger.Apply()` creates a **new output-only copy** for supported regular-lyric templates, retaining the original slide's text values, ID, rich-text run boundaries and unknown source fields.
- Replaces text item geometry/style/align, maps source lines to template line styles, and uses first template line/run as the fallback when the template has fewer lines/runs, following original FreeShow behavior.
- Respects original mixed source color, bold, italic, underline and strike-through formatting by appending overrides after template styles (for styles that vary within the same source item).
- Preserves source output bindings if the template provides none; currently *any* nonempty binding is conservatively unsupported because no real output ID is wired.
- Non-dynamic ordinary text templates can control the geometry and formatted runs sent to `NativeRenderPlanner.BuildFrame()`. Source `ShowDocument` is never mutated.
- `FreeShowStyledRenderPlanner.Prepare()` selects the correct output template per source slide, passes the in-memory result to Native rendering, and reports unported effects.
- Developer-only `FreeShow.Native.VisualProbe` uses this style adapter whenever `--data-root` and `--style` are provided.
- Synthetic compatibility checks for Korean lyric content, text box geometry, mixed bold formatting, style colors, and unsupported dynamic scripture templates have been added.

## Deliberate fail-closed behavior
Some templates need data, media, runtime state or UI features not currently present in Native. **Such templates are not partially transformed and presented as if they were supported.** The current merger returns original content with explicit warnings for scripture/dynamic variables, unsupported template modes, word-style override regexes, media or camera items, auto text fit, conditional animations, scrolling, click reveal, bindings and overlay references.

This is narrower than original `mergeWithTemplate()`. It is not yet a full implementation of scripture layout, theme cascading, text auto-sizing, metadata, video media, output bindings, or full operator parity.

## Rendering caveats
- The earlier Avalonia renderer does not yet implement every CSS property, text decoration, source font loading, font metrics, shadow/stroke variants, line breaks or output cropping.
- Original CSS data is retained and unsupported properties are reported where recognized. Exact pixel-level visual parity is **not** verified.
- Controls are prepared ahead of switching, but GPU prerendering and physical click-to-visible latency remain unmeasured. There is no evidence yet of <50 ms output.
- Target environment: Dell OptiPlex 7490, Windows, original FreeShow song files.

## Compatibility checks
The source contains added checks in `FreeShow.Native.Checks`, but the user requested delaying execution/testing until Beta 2. No compilation or execution was performed as part of this milestone. During Beta 2 run comparisons against the original FreeShow with actual copied songs and ensure equivalent geometry, typography, output bindings and all layer types.

## What remains next
- Implement complete output-scoped template bindings, word overrides, auto text size, and scripture dynamic substitution.
- Reproduce media/video background lifecycle independently from lyric cue changes (persistent hardware decoder and compositing layers).
- Port visual UI/UX elements unchanged from upstream screenshots, integrate real settings and editing workflows.
- Compare actual pixel output and physical latency before any live church usage.
