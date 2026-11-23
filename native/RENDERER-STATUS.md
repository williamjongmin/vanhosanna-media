# Native rendering milestone 0.0.3

This is a developer-only Avalonia rendering prototype, not the production FreeShow UI.

## Original FreeShow source mapping
- src/frontend/components/output/Output.svelte: composited backgrounds, media, effects, overlays, slide and metadata layers
- src/frontend/components/output/layers/SlideContent.svelte: current slide items, line reveal and transitions
- src/frontend/components/slide/Zoomed.svelte: absolute positioning, 400x150 default text boxes, 100px font, 1.1 line height
- src/frontend/components/slide/Textbox.svelte: item styles, shadows and font logic
- src/frontend/components/slide/TextboxLines.svelte: centered rich text with independent style runs
- src/frontend/components/helpers/style.ts: style property parsing

## Implemented
- NativeRenderPlanner builds source-linked plans with original run boundaries, CSS geometry and explicit warnings for unsupported items.
- CssDeclarations recognizes basic px and percent dimensions, retaining unparsed original source.
- NativeSlidePresenter uses Avalonia native text controls and keeps prepared slide controls for selection.
- FreeShow.Native.VisualProbe is a developer-only viewer to compare original show data visually; arrow keys switch slides. It does not mimic the operator UI.
- FreeShow.Native.Checks adds deterministic fixture checks for CSS, geometry, unknown effects and text runs.

## Not yet supported or equivalent
Style templates; exact fonts, colors, text wrapping, shadow and stroke; arbitrary resolution; autosizing; background video/images, media, effects, output bindings, line reveal, animations, transitions, speaker/stage displays, UI workflow or backend integrations. The 1920x1080 coordinate space is an initial prototype choice, not a complete FreeShow output model.

Prebuilding controls is not proof that the text has been shaped or textures uploaded before first presentation. No claim of under-50-ms physical display latency is justified.

## Developer-only commands

~~~powershell
dotnet run --project native/FreeShow.Native.Checks
dotnet run --project native/FreeShow.Native.VisualProbe -- "C:\FreeShowData\Shows\Example.show"
~~~

All source FreeShow shows remain read-only. Preserve existing source and data, and keep this harness off live worship outputs.

## Required before claiming parity
1. Compile the native projects and run deterministic fixtures.
2. Compare identical source shows pixel by pixel against original FreeShow output and log unsupported differences.
3. Port backgrounds, media, templates, line reveal and all output-specific styles without changing existing operator UX.
4. Measure click-to-visible latency on the Dell OptiPlex 7490 across Main/Online/Stage and actual output hardware; include p50/p95/p99 and first-cue vs cached behavior.

Status: committed code only. Build, runtime tests, screenshots and hardware benchmarks have not been verified.


## 0.0.7 update — independent video host
- The earlier still-image preview remains in `NativeSlidePresenter`; it has not been replaced or regressed by this new optional video-backed preview.
- `NativePersistentVideoOutput` now owns a persistent LibVLC player and mounts `NativeSlidePresenter` as its transparent lyric overlay, so `ShowCue` contains no file access or video Play/Stop call.
- `NativeFreeShowOutputSession` allows independent ownership per output ID. It is not yet the full FreeShow operator UI or stage/Online renderer.
- See `VIDEO-OUTPUT-STATUS.md` for exact features, unsupported layers and developer-only invocation.
- No build, pixel parity or target-hardware latency verification has been performed.
