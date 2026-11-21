# VanHosanna Media — Requirements
Status: agreed scope, not implemented. Windows first; macOS optional. Offline-first, no cloud/login/subscription/forced updates. 1080p maximum target, Intel integrated GPU, Korean/English UI. YouTube playback is the explicit internet exception.
## Worship and library
Local-only song title/lyrics search; manual registration; port existing tools.williamjongmin.com VanHosanna lyrics converter after inspecting source. Categories and nested folders for songs and reusable content (preacher, prayer leader, congratulatory address, announcements). Verse/chorus/bridge labels, drag reorder, multiple arrangements, bilingual lyrics and safe live editing. No CCLI integration. Respect content licensing.
## Scripture
Offline multiple translations, XML import, bilingual display, reference/keyword search, ranges and multiple verses, immediate previous/next, scripture design templates.
## Editor
Text, image, shapes, video objects; drag/drop, resize, rotate, fonts, color, weight, spacing, outline, shadow, opacity, alignment, snap/grid/guides, layer order/visibility/lock, undo/redo, copy/paste/duplicate, object animations and slide transitions, reusable templates, independent output layouts.
## Media and presentation
Local image/video/audio library, folders/search, motion backgrounds, loops, seek, trim, per-item volume, thumbnails, missing-file relink, bulk imports, overlays, YouTube direct playback only (no Vimeo). PPT/PPTX via installed LibreOffice: test slideshow animation and layout fidelity; bulk cached static fallback; PDF and images bulk ordered import. No Microsoft Office dependency; do not promise exact PPT fidelity.
## Services and automation
Reusable service templates copied per service, history, reorder/skip, automatic advance and end-of-media actions, countdown, sermon timer, speaker notes, output lock, configurable shortcuts, slide actions including optional camera presets/tracking. No MIDI/OSC.
## Outputs
Main projector, Online, Stage independently mapped. Blackout (with mute), Clear foreground, Freeze output, Mute, Blackout without mute, Restore; per-output controls where audio routing permits. Layer visibility and lock. Stage current/next lyrics, clock, timers, notes, messages from desktop and remote, configurable layout. Stage messages never appear on other outputs.
## Camera and broadcast
Keep FoMaKo M1 as independent mixer/encoder; investigate supported control API. K20UH VISCA IP PTZ, six named presets with protected save/recall, focus/zoom/speed, arrow key hold/release and emergency stop in dedicated PTZ mode. AI tracking on/off and supported modes subject to firmware verification. Optional preview disabled by default. Avoid conflicting commands with M1. No native broadcast encoder in initial scope.
## Remote, reliability
ER706W-4G Wi-Fi browser remote, no external exposure; authenticate, role separation and admin override. Service navigation, output controls, stage messages, PTZ, diagnostics, display remapping, recovery, logs, optional low-rate previews. Local persistence, autosave, crash recovery, portable backup/restore, missing assets handling, no forced updates. Existing RustDesk remains separate.
## Migration
Import existing FreeShow songs, bilingual lyrics, categories, media, Bible data and where feasible layouts/projects. Dry-run and report unsupported features. Never overwrite original data.
