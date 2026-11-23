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

## Performance acceptance
- Measure physical click-to-visible change with transitions disabled and a controlled 1080p test; report median/p95/p99, output path and graphics-adapter configuration.
- Repeat with motion backgrounds, projector, Online output and Stage connected. Separate physical display/D3100/HDMI buffering from application latency.
- Performance improvement is a hypothesis until measured. 50 ms is a goal, not a guarantee.
- Preserve FreeShow-configured intentional transitions: do not silently change durations to improve benchmark scores.

The previously developed VanHosanna native prototype is a separate reference for rendering and display lifecycle; its custom UI and database schema are not the port's specification.
