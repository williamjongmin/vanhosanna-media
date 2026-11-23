# FreeShow Native Port — release gates through 1.0.0

**Goal:** existing upstream FreeShow operator UI, project data and behavior, with lower source-cue to physically visible lyric latency on Dell OptiPlex 7490 (i5, 16 GB). Full historical FreeShow features are the compatibility target, not permission to invent a redesigned operator product.

**Release numbers are planning targets, not completed releases.** Never tag 1.0.0 based on source files or synthetic checks alone.

## Current baseline (2026-11-23)
- Original FreeShow fork / church-dev operational source preserved.
- native-port contains lossless .show parsing, project/source preview models, core OutData and clear/restore plans, a persistent single-video LibVLC output prototype, and read-only Avalonia operator panels and navigation/reveal preparation.
- 0.0.25 adds the prepared-lyric application adapter and logical visual acknowledgment, plus source slide-assignment semantics. A complete native source action backend and live operator/output-window integration are still missing; the adapter does not enable production output by itself.
- No Windows build or fixture suite has been executed under the user's explicit **defer all builds/tests until Beta 2** request.
- No physical output has been measured; under-50ms is an objective, not an observed result.
- Full FreeShow source parity is **not** achieved; native developer output is not approved for live worship.

## Development and validation gates

### Alpha (0.1.0 planning gate)
- [ ] Original Show/Edit/Stage/Draw/Settings UI sections connected with source-equivalent controls.
- [ ] Project open/select/expand and original activeShow/activeProject state complete.
- [ ] Source lineReveal/clickReveal states visible on an isolated Native output, including transitions and styles without silent omissions.
- [ ] Source output commands can be executed only when the entire native playback effect chain is present.
- [ ] Critical missing media/scene/audio features are classified, with no misleading "works" UI.
- [ ] Original files remain read-only unless user explicitly invokes supported save/export.

### Beta 2 (0.2.0 — first permitted Windows build/test gate)
- [ ] Windows solution builds without errors on the supported target .NET/Avalonia runtime.
- [ ] Existing synthetic tests actually execute and pass; add precise regression fixtures for original .show and projects.
- [ ] Full source song/project and operator keyboard workflows tested in an isolated workspace.
- [ ] Visual screenshots compared against upstream UI in same resolution, style and output state.
- [ ] Confirm with multiple outputs, background video, line/click reveal, F1–F4, Clear All and Restore. Unsupported cases must fail closed.
- [ ] Profile high-percentile key press → final physically visible output frame with a high-speed camera/photodiode or synchronized frame capture; report click-to-visible p50/p95/p99, not only synchronous dispatch time.
- [ ] Re-run with OBS/streaming or FoMaKo M1 as used in the church setup; validate that streaming does not change output-layer timing.
- [ ] No original FreeShow files overwritten in test.

### Release Candidate (0.3.0 planning gate)
- [ ] All original church-required playlists, assets, templates, layout modes and media sources preserved or migrated losslessly.
- [ ] All actual output windows and stage/online outputs behave as original; no ghost overlays, stale frames or video seeking on lyric-only changes.
- [ ] Audio playback, microphone/metronome/global playlist changes and restore tested.
- [ ] 2–4 hour continuous full-size worship rehearsal, monitor CPU, GPU, RAM, decoder lifetime and memory leaks.
- [ ] Safe rollback to upstream church FreeShow and backup restoration demonstrated.
- [ ] Hardware/Windows-specific video-overlays, transparency and rendering ordering verified.

### 1.0.0 General Availability
- [ ] Windows installer and signed/checksummed release artifacts produced.
- [ ] Exact original UI and keyboard behaviors signed off after real operator rehearsal.
- [ ] No known data-loss or output-correctness regression; source formats and settings remain compatible.
- [ ] Full required feature checklist has documented passing evidence; no critical silent omissions.
- [ ] Physically visible lyric delay improved meaningfully on Dell OptiPlex 7490; target <=50ms measured across realistic worship cues (must report failures and hardware constraints honestly).
- [ ] Release notes, known limitations and restore/rollback guide available.
- [ ] Existing church FreeShow remains available until user explicitly chooses production replacement.

## Release policy
Do not label internal checkpoint branches "tested", "usable" or "production ready" without real evidence. Checkpoints may be created autonomously on native-port. A physical deployment cannot be performed automatically without permission and access to the user's actual Windows church hardware. Work does not continue after a conversation turn ends unless an authorized external execution/scheduling facility is actually started.
