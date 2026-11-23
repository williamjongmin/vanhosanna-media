# FreeShow Native Port 0.0.14 — original Preview clear controls

## Goal
Match FreeShow's existing right-hand Preview + ClearButtons workflow, while keeping the original church outputs and projects unchanged. No fake or substitute control is permitted to issue live commands before its complete source action path exists.

## Original code inspected
- src/frontend/components/output/preview/Preview.svelte: MultiOutputs; actions; sticky ClearButtons; optional background, slide, overlays, audio or timer detail panels; active detail selection priority **nextTimer > audio > overlays > slide > background**.
- src/frontend/components/output/preview/ClearButtons.svelte: scene (when active), clear all/restore, background (F1), slide (F2), overlays/effects (F3), global audio (F4), next slide timer and separate expand buttons. All respect output lock and the individual active/cleared states.
- src/frontend/components/output/clear.ts: clearBackground only removes out.background; clearSlide clears slide output and pending slide timers; the **Preview ClearButtons** clearOverlays() helper retains locked overlay IDs and clears effects; clearScene only removes scene; clearAll additionally needs audio, timer, project and cache state.
- src/frontend/utils/shortcuts.ts: **keyboard F3 differs** from the Preview button: it directly calls setOutput("overlays", []) and setOutput("effects", []), clearing even locked overlays. Native 0.0.15 models these as different source commands; the earlier blanket description that F3 preserves locked overlays was incorrect.
- src/frontend/components/helpers/output.ts: isOutCleared() uses active outputs and ignores scene/refresh in the all-cleared state. For overlays it consults overlay definition lock flags, which do not exist in OutData alone.
- src/frontend/values/icons.ts: original SVG path geometry; public/lang/en.json and ko.json: original clear/preview labels.

## Committed implementation
1. **FreeShowPreviewControlState** — produces a read-only categorized output status with Active/Cleared/Unknown tri-state. It uses the actual active output IDs, configured OutData and optionally original overlay definitions and known runtime audio/timer state. Missing audio/overlay/timer evidence is Unknown, not incorrectly "all clear".
2. **FreeShowPreviewCategoryNavigation** — in-memory selected detail category, allowing the small FreeShow-style expansion buttons to choose background, slide, overlays, audio or next timer without touching output state.
3. **FreeShowClearButtons** — source-ordered Avalonia action and expansion controls, with original icons for scene, clear, image, slide, overlays, audio, clock and expand. Exact original English/Korean clear-action label strings included. The clear actions are **disabled in developer preview**, because only source-complete action implementations may explicitly be installed.
4. **FreeShowPreviewRegion** — composes the original Preview's MultiOutputs + ShowActions slot + ClearButtons + detail slot. Unimplemented detail widgets remain empty; it does not pretend to display functional media controls or audio.
5. **OperatorProbe** — now uses FreeShowPreviewRegion instead of the previous bare MultiOutputs control.
6. **FreeShowOutputState correction** — the earlier generic ClearOverlays branch has been made safe: without original overlay lock metadata, it refuses to wipe overlays. A separate ClearOverlaysKeepingLocked accepts explicit lock IDs and clears effects. Isolated scene and transition clearing have been added for future action routing.
7. Synthetic compatibility checks written for locked overlays, F1/F2/F3 layer isolation, active Preview category selection, source output lock and the Unknown state when runtime data is absent. No test execution yet.

## Critical functional boundaries
- A disabled destructive control is intentional: real FreeShow clear actions also invoke audio, global playback, output cache, timeline, overlay timers, scene and custom action listeners. Merely setting OutData to null is not equivalent.
- When no original overlays store is supplied, the lock status is Unknown. Native UI must not clear all overlay IDs indiscriminately, or church operators could lose locked/persistent overlays.
- The original Preview can display PDFs, PowerPoint, screenshot output, streaming, Stage, image/video backgrounds and output effects. Current native preview still uses a partial source-data projection, not a capture of the real outgoing frame.
- Detail panels (MediaControls, Show, Overlay, Audio, NextTimer), restore output, recording/stream status and actual live output actions are unported. The Native UI shell is not a full replacement yet.
- The current native preview theme is approximate, though actual SVG path data and source labels have been used. Pixel parity, accessibility and event behavior remain unverified.
- No Windows compilation, UI execution, synthetic fixture tests or OptiPlex 7490 timing measurements were carried out, consistent with the user's decision to defer tests until Beta 2.

## Data protection
Only native/ source files on branch native-port were modified. Production FreeShow church-dev, source media, shows, projects, local settings and active worship outputs were untouched.
