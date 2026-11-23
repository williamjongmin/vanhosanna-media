# FreeShow Native Port 0.0.11 — actual source-derived operator UI components

## Goal
Keep the **original FreeShow interface and workflow**, not design a replacement. Port its actual UI components into Avalonia one by one, while continuing to preserve original source code and files.

## Upstream files inspected
- src/frontend/components/main/Top.svelte: 40 px navigation, centered FreeShow title, Show/Edit/Stage tabs, right Draw/Settings/output controls.
- src/frontend/components/inputs/TopButton.svelte: exact tab order, menu.show/edit/stage/draw/settings, original page keyboard labels, selected icon fill, label visibility and disabled state.
- src/frontend/values/icons.ts: SVG path geometry for show, edit, stage, draw, settings, outputs, cancelDisplay and close.
- public/lang/en.json and ko.json: original English and Korean labels (e.g. Show/쇼, Edit/편집, Stage/무대, Draw/그리다, Settings/설정).
- src/frontend/components/show/Slides.svelte: active show layout ID, four-column grid default from slidesOptions, slide ordering, active output markers, group cards, disabled slides, slide click actions and contextual event handling.
- src/frontend/components/slide/Slide.svelte: the full thumbnail composition, with original dark slide surface, active outline, disabled opacity, optional group labels and conditional visual layers.
- src/frontend/components/show/ShowHeader.svelte: original title, optional arrangement selection, notes and overflow actions.
- src/frontend/MainLayout.svelte: source-defined positioning and page mapping.

## What is now implemented
1. **FreeShowOriginalIconGeometry**: exact upstream SVG *path data* for eight top icons. The project does not replace these with Lucide/Fluent/Material icons. Original theme gradients are not yet ported.
2. **FreeShowTopBar**: the source's FreeShow wordmark, Show/Edit/Stage center controls and Draw/Settings/output right-side slots, 40 px height, and the original en/ko menu labels. A page can open only when its real source components have been registered in the workspace. The output control stays disabled unless a real output toggle controller is provided.
3. **FreeShowOperatorWorkspace.InstallTopBar**: joins the original tab event with ChangePage and the per-page source component registry. Unported screens are not replaced with invented designs.
4. **FreeShowSlideCards**: reads actual .show ordering including parent/child entries; displays group, disabled states and renderer-based slide content previews. It reuses NativeRenderPlanner/NativeSlidePresenter instead of building a new renderer. Card clicking is disabled until the original complete live-click behavior has a real controller; it must not silently skip actions, line reveal or overlay changes.
5. **FreeShowShowRegion**: source-derived show header title plus slide-card region in the existing operator layout.
6. **FreeShow.Native.OperatorProbe**: read-only developer comparison entry point for a copied .show file, optionally loading original local settings. It is **not the complete application**, does not start a video or output, and cannot edit church data.
7. **FreeShow.Native.slnx**: includes native core, rendering, operator, diagnostic probes and checks in one future .NET 10 solution. No build/test execution has occurred.

## Developer-only invocation (after Beta 2 testing is authorized)
~~~powershell
dotnet run --project native/FreeShow.Native.OperatorProbe -- "C:\CopiedFreeShow\Shows\Song.show"
dotnet run --project native/FreeShow.Native.OperatorProbe -- "C:\CopiedFreeShow\Shows\Song.show" --settings "C:\Users\Example\AppData\Roaming\FreeShow\settings.json"
~~~
The actual FreeShow local SETTINGS path can vary; use the real location rather than assuming this example path.

## Explicit gaps / no false parity claims
- Full original top-menu popups and MenuBar; theme gradients, fonts, precise colors, icon hover behavior, profile and collaboration state
- Left Projects tree and source project data, original Slide thumbnails' overlays, animation, text measurement and optional media composition
- Right Output Preview, ShowTools, Drawer, Stage, Edit, Settings, Draw, and all underlying functionality
- Live show click behavior including custom actions, background changes, line reveal, project transitions and output synchronization
- Original Svelte thumbnail styling for all view modes (grid / simple / groups / lyrics), group color mapping, virtualized loading, drag/drop, folder/media entries and settings persistence
- Main window is a *developer harness* with empty unported regions. It cannot be deployed for worship. Visual UI is not pixel-identical, and actual screen screenshots have not been compared.
- Current UI preview eagerly instantiates card controls and may consume too much memory on large shows; virtualization and cached thumbnail lifecycle need further work before large deployments.
- Windows compile, synthetic tests, graphics timing and OptiPlex 7490 hardware verification are unexecuted per user-requested Beta 2 gate.

### Safety
Original FreeShow branch church-dev, real media, projects and settings remain unchanged. All native work is isolated in native-port. Native show editing remains copy-export-only. No automatic deployment or Windows installer has been created.
