# FreeShow native Windows port

Baseline: church-dev / package version 1.6.8-beta.5. The Electron implementation is the behavioral reference, not a design suggestion.

## Non-negotiable requirements
- Preserve the original UI layout, labels, shortcuts, interaction patterns, features and existing user data. No discretionary redesign or new features.
- Windows desktop runtime: Avalonia, C#, .NET 10. No Electron, Chromium, Node.js or WebView in the application runtime. Existing source remains reference code.
- Preserve existing FreeShow project/show/settings formats and unknown fields. Do not overwrite real church files during development. First implement read-only compatibility, then verified writes against copies.
- Target Dell OptiPlex 7490 with Intel integrated graphics and 16 GB RAM; Main, Online and Stage outputs. FoMaKo M1 handles broadcast encoding.
- Aim for physical click-to-display latency below 50 ms for prepared zero-transition lyrics; record median, p95, p99 and worst case. Do not confuse UI event dispatch with physical display latency. Preserve intentional transition settings.
- Maintain GPL-3.0 notices and obligations; preserve upstream attribution and track third-party dependencies.

## Porting gates
1. Inventory original source modules and persistence formats, capture baseline screenshots and interaction behavior.
2. Implement lossless native data access, layout ordering, and compatible output state; compare against original fixtures.
3. Port renderer and all layer types, retain the original slide geometry, text styles, media, output bindings and transitions; precompute where safe.
4. Port operator UI and editors against screenshots and original behavior, not substitute workflows.
5. Port remaining backend, middleware, device and network integrations, preserving configuration and behavior.
6. Compare functionality using actual copied FreeShow projects, including failure cases; run performance measurements on the target hardware.
7. Release only after functional parity and latency acceptance. No claim of parity before verification.

## Verified source mappings (original FreeShow tree)
| Native concept | Existing FreeShow source | Observed behavior |
|---|---|---|
| Show storage | `src/electron/utils/files.ts` (`parseShow`) | Each `.show` file is a JSON tuple `[showId, Show]` |
| Show directory | `src/electron/utils/files.ts` (`dataFolderNames`) | `<data root>/Shows/*.show`; data root may be customized |
| Data root | `src/electron/utils/files.ts` | Defaults to `Documents/FreeShow` or configured `dataPath` |
| Show schema | `src/types/Show.ts` | Slides, layouts, media and unknown extension fields coexist |
| Output slide ordering | `src/frontend/components/helpers/shows.ts` (`layouts().ref()`) | Parent layout entry followed by the source slide's child ID list |
| Child layout overrides | `src/frontend/components/helpers/shows.ts` | Stored under `layout.slides[parentIndex].children[childId]` |
| Rich text | `src/types/Show.ts` | Item → lines → rich text runs with individual style strings |

## Current code
- `FreeShow.Native.Core/ShowDocument.cs`: read-only `.show` tuple parser with exact original byte preservation, active layout, parent/child expansion, and missing-slide diagnostics.
- `FreeShow.Native.Core/SlideProjection.cs`: source-based, non-rendering slide projection. Preserves original JSON, text runs, per-item styles, and child overrides.
- `FreeShow.Native.Core/ShowLibrary.cs`: read-only inspector for a caller-selected `Shows` folder. Never writes or deletes.
- `FreeShow.Native.Probe`: command-line source inspector; no UI port yet.
- `FreeShow.Native.Checks`: synthetic fixture checks for data ordering, losslessness and basic CSS rendering plans. Fixtures are **not** real church data.
- `FreeShow.Native.Rendering`: early Avalonia native text renderer; original FreeShow-style box geometry and cached controls, with explicit unsupported-feature warnings.
- `FreeShow.Native.VisualProbe`: **developer-only** comparison viewer, not the ported operator UI; see RENDERER-STATUS.md.

Run locally with .NET 10 SDK:
```powershell
dotnet run --project native/FreeShow.Native.Probe -- --show "C:\...\YourSong.show"
dotnet run --project native/FreeShow.Native.Probe -- --folder "$env:USERPROFILE\Documents\FreeShow\Shows"
dotnet run --project native/FreeShow.Native.Checks
```
If FreeShow uses a custom data directory, choose its actual `Shows` folder.

**Status:** Code committed; compilation, fixture execution, church-data compatibility, GUI, physical latency and hardware decoding have not been verified. This code cannot yet replace the FreeShow application.

## Native milestones after 0.0.3

- **0.0.4 Visual settings / background prototype:** read-only `FreeShowVisualSettings` loads original synced style and template JSON; `FreeShowOutputLayerPlanner` preserves output layer ordering, template selection, background references and transition metadata. `NativeStillImageCache` can prepare local still backgrounds for the developer preview. Video/media state, true template merge and output-layer parity are not implemented. The resulting output is not pixel-identical to FreeShow.
- **0.0.5 Copy-only .show export foundation:** `FreeShowShowExport` edits title or a single rich-text run only in a private JSON tree, preserves the original show ID and other fields, updates the modified timestamp, validates the resulting tuple and exports only through exclusive new-file creation; never overwrites an original. See `DATA-COMPATIBILITY.md`. No integrated editor or autosave yet.
- The companion synthetic checks exercise these paths but are **not executed**, following the user-requested Beta 2 testing policy.

- **0.0.6 Regular lyric templates:** original `mergeWithTemplate()` subset for standard (non-dynamic) text boxes has been ported as an output-only transformation. It preserves source lyric strings and mixed rich-text formatting while applying template box geometry and styles. `FreeShowStyledRenderPlanner` is wired into the developer visual probe. Unsupported scripture/variables, bindings, media, autosizing, overlays, and special text behavior are reported and left unmodified. See `TEMPLATE-COMPATIBILITY.md` for explicit limits.
- **Build/test status:** GitHub code has been committed; no .NET build, test execution, screenshot parity check, or measured OptiPlex performance in this milestone. Do not deploy during worship.

- **0.0.7 Persistent local video architecture:** LibVLCSharp.Avalonia output keeps the background decoder outside the `ShowCue` path; prebuilt lyrics are swapped in a transparent native overlay. A dedicated owner is provided per physical output ID. Local single-background video only; two-layer compositing, media transitions and external feeds remain unsupported. See `VIDEO-OUTPUT-STATUS.md`. No physical speed measurement.

- **0.0.8 Instrumentation:** `NativeCueDispatchMetrics` records bounded p50/p95/p99/max synchronous Avalonia content-swap timings. This is NOT user-input-to-physical-display latency; see `PERFORMANCE-MEASUREMENT.md`. Code checks are written but not executed.

- **0.0.9 Output identity and control compatibility:** original local `SETTINGS.outputs` IDs, enabled/active states, style mapping and display bounds are now read without replacing them with Main/Online/Stage enums. Presentation shortcut dispatch models original F1–F5, Space, arrows and Home/End context guards; F1/F2/F3 are modeled as independent output data layers, with F2 clearing the native lyric overlay without restarting video. The source-based shortcut rules are not yet wired to the production operator UI. See `OUTPUT-CONTROL-COMPATIBILITY.md`. Build/behavior/hardware checks remain unexecuted.

- **0.0.10 Original operator workspace shell:** source-derived `FreeShowOperatorWorkspaceState` and `FreeShow.Native.Operator/FreeShowOperatorWorkspace` add the original Windows MenuBar/Top/3-column layout/right preview/optional timeline/drawer/Tipbar region structure, saved panel dimensions and a per-page content registry. The actual FreeShow page controls, menus, icons, style themes and keyboard event integration are **not yet** ported. See `UI-STRUCTURE-STATUS.md`. No pixel or runtime parity claim.

- **0.0.11 Source operator components:** original FreeShow top navigation tabs and SVG icon path geometry, English/Korean source labels, page registration wiring, show-title region and renderer-backed slide cards are implemented as partial Avalonia controls. Added a **read-only developer-only** operator comparison executable and a .NET 10 solution. Live click/output control remains disabled until the original complete actions/reveal/overlay semantics are available. Existing menu bar, project tree, media drawer, stage/editor/settings and exact UI styling remain incomplete. See `UI-COMPONENT-STATUS.md`; no runtime or visual parity verification has been performed.

- **0.0.12 Projects / Preview:** the original `Config/projects.json` folder/project tree, per-project show/arrangement references, and multi-output `SETTINGS.outputs` preview metadata are now represented as read-only native models and Avalonia controls. The developer OperatorProbe can display left Projects and right Preview when supplied source folders/settings. Unported stage/media/capture views are not faked; actual project editing and full FreeShow output Preview controls remain unported. See `PROJECT-PREVIEW-STATUS.md`. No Windows build, synthetic test execution or physical latency measurement completed.

- **0.0.13 Original project item navigation:** ported the original project contents section/type grouping and per-item source index into a read-only native model. The left project browser now switches from folder tree into project items and preserves active selection when returning. A lazily indexed read-only `Shows` catalog resolves selected song IDs from actual `.show` tuple contents, allowing the developer OperatorProbe to show source-matched slides without sending anything live. See `PROJECT-ITEM-NAVIGATION-STATUS.md`. Full project/scene/timeline/output interactions and visual parity remain unported, and no Windows build or performance checks have run.

- **0.0.14 Source Preview clear controls:** original right Preview category controls, F1 background, F2 slide, F3 overlay/effects, F4 audio, scene, all-clear, timer and non-destructive category selection are now represented in Avalonia with original SVG icon paths and English/Korean translations. In the read-only OperatorProbe all destructive actions remain disabled until a complete source-compatible controller is installed. Native models now explicitly handle unknown audio/timer state, overlay lock safety, and original `Config/overlays.json` lookups; `FreeShowOutputState` refuses overlay clear without lock metadata. See `PREVIEW-CLEAR-STATUS.md`. No Windows build or latency measurement executed.

- **0.0.15 Source-guarded output commands:** integrated original FreeShow F1/F2/F3/F4 and Preview clear action planning with capability-gated per-output runtime execution, explicit side-effect hooks, output state acknowledgement and UI Preview readiness. Original F2 slide cache is now modeled and connected through optional runtime hooks. **Important correction:** upstream keyboard F3 clears even locked overlays, while Preview's clearOverlays() preserves them; the Native command model now treats the two paths differently. In the developer OperatorProbe no physical runtime is installed, so clear buttons remain disabled. See `OUTPUT-COMMAND-INTEGRATION.md`. Compilation, tests, complete feature parity and physical latency remain unverified.

- **0.0.16 Clear All / Restore / timer and output-state contracts:** a full-JSON active-output cache, original global-audio/metronome restore requirements, conditional 1000ms Restore UI, explicit slide-versus-overlay timer cancellation registry, and fail-closed multi-output restore runtime are now modeled. Added optimistic output revision guards and rejected empty/stale command plans. In the developer OperatorProbe there is no complete physical/audio restore backend and no live timer scheduler, so the new controls cannot affect church outputs. See `OUTPUT-RESTORE-TIMER-STATUS.md`. Windows compilation, fixture execution and physical latency remain unverified.

- **0.0.17 Original Clear All orchestration:** source `clearAll(button)` context gating, global audio+OutData cache capture, timeline stop, background clear, F2-only slide timer cancellation, slide clear, locked-aware overlay/effect clear, global audio clear and final timers are represented in an ordered, capability-gated command coordinator. The original Preview button is now wired to this controller but stays disabled in the read-only developer probe, because physical rendering, audio and timeline backends are incomplete. Partial failures preserve the original output cache; no source data is changed. See `CLEAR-ALL-STATUS.md`. No Windows build, synthetic checks or physical latency results yet.

- **0.0.18 Timer / persistent-video source bridges:** implemented F2-only slide timer cancellation hooks and Clear All's separate full overlay-timer cleanup, requiring authoritative native scheduler inventory. Added conservative F1 single-video clear continuity planning that leaves lyric cue updates and independent style backgrounds alone and rejects mismatched or unsupported media; composed timer, cache, video and lyric subeffects into a **partial, not registered** Native session runtime. Plan-specific readiness now refuses unverified current video/scheduler conditions before any physical step. See `MEDIA-TIMER-BRIDGE-STATUS.md`. Full media transitions, actual scheduler, source callbacks, production UI bindings, Windows build, fixtures and hardware latency remain unverified/unimplemented.

- **0.0.19 Source slide/project navigation:** added read-only FreeShow `OutputHelper.ts` Next/Previous/Home/End and line-splitting/click/line-reveal target calculation, output-binding and disabled-slide checks, end-marker wrapping, original indexed project boundary selection and multi-active-output boundary synchronization. Existing keyboard router is connected through a non-live inspector, and the source layout index can be resolved to an already prepared Native cue ordinal without touching LibVLC or output state. Full source `setOutput/updateOut` actions, real per-line reveal composition, media/PDF/PPT, focus/active-item lifecycle and physical keyboard dispatch remain unported. See `NAVIGATION-SOURCE-STATUS.md`. Synthetic fixtures are authored but no Windows build or runtime test has been performed.

- **0.0.20 Native line/click reveal:** projected original FreeShow `getOutputLines`, `TextboxLines` and `SlideContent` reveal/click visibility into nonmutating Native text frames. Avalonia renders reveal variants during explicit prewarm; cue-time selection swaps prepared controls without parsing/IO/video seek. Added per-output style resolution, precise source Show/layout/index gating, and safe read-only Preview display that never leaks still-hidden lyrics when unsupported. See `REVEAL-NATIVE-STATUS.md`. No Windows build or timing verification.
- **0.0.21 Bounded reveal prewarm and release gates:** added source navigation-derived single-output reveal sequences, bounded preparation and refusal for unsupported multi-output timelines, alongside concrete Alpha/Beta 2/RC/1.0.0 release acceptance gates. This is not an enabled production output path; no physical controller/actions or full CSS/source parity has been demonstrated. See `REVEAL-NATIVE-STATUS.md` and `RELEASE-1.0.0-GATES.md`.

## Performance acceptance
- Measure physical click-to-visible change with transitions disabled and a controlled 1080p test; report median/p95/p99, output path and graphics-adapter configuration.
- Repeat with motion backgrounds, projector, Online output and Stage connected. Separate physical display/D3100/HDMI buffering from application latency.
- Performance improvement is a hypothesis until measured. 50 ms is a goal, not a guarantee.
- Preserve FreeShow-configured intentional transitions: do not silently change durations to improve benchmark scores.

The previously developed VanHosanna native prototype is a separate reference for rendering and display lifecycle; its custom UI and database schema are not the port's specification.
