# FreeShow Native Port 0.0.9 — Source-aligned output settings and keyboard actions

## Principle
FreeShow's original UI and behavior are authoritative. Windows-only native output must use the *existing FreeShow output IDs*, not invent hard-coded Main/Online/Stage identifiers.

## Verified upstream references
- `src/electron/data/defaults.ts`: `SETTINGS.outputs.default` is named "Primary", with default 1920×1080 bounds; `outLocked` and `transitionData` are local settings
- `src/electron/data/store.ts`: SETTINGS is **not portable**, whereas SYNCED_SETTINGS and TEMPLATES are portable stores. Original local settings must be read from the actual Electron app-data `settings.json`, **not** from `<FreeShowDataRoot>/Config/settings_synced.json`
- `src/types/Output.ts`: output IDs, normal/stage relationship, style ID, display bounds, enabled/active flags, screen ID, capture/NDI/RTMP/WebRTC settings and `OutData`
- `src/frontend/components/helpers/output.ts`: `getAllEnabledOutputs()`, `getAllNormalOutputs()`, `getAllActiveOutputs()`, `getAllActiveOutputIds()`; active normal output filtering, fallback to first when nothing is active
- `src/frontend/components/helpers/array.ts`: sort output names using `localeCompare(...,{numeric:true,sensitivity:"base"})`
- `src/frontend/utils/shortcuts.ts`: presentation keys F1–F5, Space, arrows, PgUp/PgDn, Home/End and editor/popup/Alt+F4 checks
- `src/frontend/components/helpers/OutputHelper.ts`: normal output advance and 80ms cross-source duplicate suppression, project/focus navigation and slide-layer options
- `src/frontend/components/output/clear.ts`: clearBackground() clears `out.background`; clearSlide() clears `out.slide` and manages an optional slide cache

## Implemented code (source-derived subsets)
- `FreeShowOutputConfiguration`: read-only local `settings.json` parser with original IDs, names, style IDs, physical display screen strings, bounds and source stage references. Unknown settings preserved. Does not change local settings or attempt to move native windows automatically.
- `GetActiveNormalOutputs`: excludes disabled and stage-only outputs; returns active normal outputs, else first enabled normal output without mutating user settings.
- `FreeShowShortcutRouter`: semantic action routing for unmodified F1–F5, Space, ArrowLeft/Right, PageUp/Down, Home/End. Includes context guardrails and 80ms *different-source* duplicate suppression, plus the original 500ms F2 double press handling.
- `FreeShowOutputState`: an independent in-memory copy of OutData, allowing F1 background clear, F2 slide clear, and explicitly distinct keyboard F3 (clear all overlays, including locked) vs Preview-button overlay clear (keep locked). The source sets them differently; do not conflate them.
- `NativeSlidePresenter.ClearVisible()` and `NativePersistentVideoOutput.ClearLyrics()`: F2 can clear visible lyrics while keeping the prepared slides and independent background video player. Session tracks the last cleared cue separately.
- `NativeFreeShowOutputSession` accepts a `FreeShowConfiguredOutput` and rejects unsupported stage/network/transparent outputs rather than silently changing their semantics.
- Synthetic fixture checks cover IDs, source data retention, output selection, F1–F5 semantic routing, double-F2, Alt+F4, source deduplication and F1/F2/F3 state independence.

## Critical limitations
- These are source-aligned foundational components, not a complete replacement operator UI. The shortcut router currently **emits commands**, but it is **not wired to the exact full FreeShow main window, popup and selection lifecycle**. F2's native lyric clear is implemented but not wired to the real operator's keyboard event yet.
- Native `NativeFreeShowOutputSession` currently supports a *subset* of standard local physical outputs; Stage renderer, NDI/OMT, capture, RTMP/WebRTC, transparency and multi-video layer composition are still unported.
- FreeShow's project navigation, focus mode, slide-layer continuation, timer, audio actions and popup context must be implemented with their exact source behavior, not assumed to be `index++`. Native code deliberately does not fabricate these.
- Name sorting uses natural ASCII-style order and may differ from JavaScript `localeCompare` for unusual locale/Unicode names. Not declared full fidelity.
- Real monitor identity/bounds mapping and USB D3100/DisplayLink routing remain unimplemented and unverified.
- Clearing active output background must not remove the separate style background or background media supplied by a template. A future compositor needs to bind the state changes to its actual media layers correctly.
- The original native preview is still a **developer harness**, not the UI already familiar to the media team.
- Compilation, synthetic checks, visual diffs and Dell OptiPlex 7490 latency benchmarks have **not been run**, consistent with the requested Beta 2 test gate.

## Data safety
- Source FreeShow settings, projects, show files and church media remain untouched.
- Only development files in GitHub's `native-port` branch are changed.
- Do not deploy the native prototype to live worship until feature and performance parity is established.
