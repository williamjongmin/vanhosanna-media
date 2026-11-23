# FreeShow Native Port 0.0.13 — project-item browsing and source show selection

## Goal
Port original FreeShow Projects.svelte / ProjectContentList.svelte navigation into the native operator structure without inventing a different workflow or changing any original church data.

## Source behavior inspected
- src/frontend/components/show/Projects.svelte: project tree or project contents depending on projectView and activeProject; back() sets projectView=true without clearing the active selection.
- src/frontend/components/show/ProjectContentList.svelte: sections split from project.shows in **original array order**, with distinct section colors, virtual DIVIDER entries between differing media/show types, stored arrangement layout, notes, section collapse state and selection indices.
- src/frontend/components/show/project.ts: openProject() selects a project and opens its first item unless the current item is already in that project. openProjectItem() uses the original project item index and honors per-item layout override; history/provider side effects are not replicated in the read-only developer preview.
- src/types/Projects.ts: every project item may be a show, section, video, image, audio, placeholder, provider-specific item or other source type; a repeated show ID is possible at multiple different project indices.
- src/electron/data/save.ts and src/electron/utils/files.ts: source .show file name does not define its ID; file contents are a tuple [show ID, show data].

## Native components added
- FreeShowProjectContentProjection: reads the **original project Raw.shows** array without rewriting it, tracks each source index, item type, color, optional layout, notes and raw extension data. Applies the source's section/synthetic-divider grouping rules.
- FreeShowProjectNavigation: in-memory active-project/active-item navigation, using the original indices, retaining selection across Back, and choosing the first source item when opening a new project. Never changes project.used or active FreeShow process state.
- FreeShowProjectBrowser: a source-shaped left-panel view that switches between the existing project tree and the project item content list. Supports section collapsed state only in memory, source item selection, and the original Back flow. Emits selection events, not video playback or output actions.
- FreeShowReadOnlyShowCatalog: scans the explicitly chosen Shows folder to match each .show's actual tuple ID to its path. It warns on duplicate IDs and does not write files, rename shows or rely on filename guesses. Lazy scanning occurs on the first developer-UI item selection, not in the presentation cue hot path.
- FreeShow.Native.OperatorProbe: --data-root now installs the project browser. Selecting a song can load it by true Show ID and switch the center source slide-card view. The selected source Show ID is also used to decide whether the right Preview can legitimately display the output's existing OutData.slide. Non-show or unresolved selections show no fabricated song.
- Added synthetic compatibility checks for colored sections, source indices, virtual dividers, repeated song references, typed media and placeholders, Back and reopen semantics, tuple-ID lookup and source file non-mutation. **Not executed yet.**

## Developer-only invocation
~~~powershell
dotnet run --project native/FreeShow.Native.OperatorProbe -- "C:\CopyOfFreeShow\Shows\Opening.show" --data-root "C:\CopyOfFreeShow" --settings "C:\CopyOfFreeShow\local-settings.json"
~~~

The --settings path represents original Electron-local SETTINGS, not portable Config/settings_synced.json. Use the actual location in an isolated copy.

## What this milestone does NOT implement
- Project item selection is **not** the original complete operator output action (e.g., custom actions, media startup, scene/background changes, line reveal, stage sync, timeline, provider integration). No song is sent live by clicking a project entry.
- Advanced original project editing, drag/drop, archived project visibility options, project template editing, permissions, provider links, contextual menus, project history and recently used lists are still unported.
- Section titles/styles are approximate native controls, not pixel-identical upstream CSS. The collapse state is read from the source but not persisted back; all changes remain in-memory.
- File catalog enumeration loads each song to inspect the tuple ID; this is a **developer tool**, not a performance-optimized background index. It needs memory/latency profiling and incremental indexing before production.
- The right output Preview is a limited source-based data projection, not actual captured output frames. Outputs showing other songs, videos or unported stage renderers remain blank/unknown rather than being incorrectly fabricated.
- Windows compilation, UI and source fixture execution, screenshot comparison and Dell OptiPlex 7490 physical click-to-visible latency measurements are **not performed** in this milestone, per the user's Beta 2 test gate.

## Data safety
Only the native-port branch's native/ subtree was changed. Original church-dev, prior checkpoints, source shows, projects, templates, output settings, media and live worship outputs were not modified.
