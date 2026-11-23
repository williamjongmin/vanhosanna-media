# FreeShow Native Port 0.0.12 — project library and output preview

## Goal
Reproduce the existing FreeShow **Projects** panel and **Preview** region in Avalonia, preserving the original data, IDs, operator workflow and disabled/missing content behavior. This milestone is a read-only subset, not the complete UI.

## Original source inspected
- src/electron/data/store.ts: portable PROJECTS store = Config/projects.json, containing {projects, folders, projectTemplates}.
- src/types/Projects.ts: project IDs, folder parent relation, archived/deleted status, show refs, layout overrides and unknown provider-specific fields.
- src/frontend/components/show/Projects.svelte: original tree assembly, separate sorting of folders/projects, archived projects at end, deleted entries filtered, missing parents normalized to "/" in view.
- src/frontend/components/show/ProjectList.svelte: tree indentation, openedFolders visibility, project selection, original styles and root-folder grouping.
- src/frontend/components/output/preview/Preview.svelte: preview region contains MultiOutputs, optional hide/show, actions, separate clearing controls, media/audio status.
- src/frontend/components/output/preview/MultiOutputs.svelte: preview all enabled outputs, omit hideFromPreview when there are multiple, stage outputs sorted later, grid default two columns, per-output name/color and live OutData.
- src/types/Output.ts: original Output profile fields, enabled/active, stageOutput, bounds, color, style, OutData.slide.

## Committed code
- FreeShowProjectLibrary: reads original Config/projects.json **without writing it**. Uses existing folder/project IDs, embedded project show refs, arrangement/layout refs, archive/deleted flags, unknown JSON values. Missing parents and cycles are handled in a read-only display projection with warnings, never edited in the source data. Sorting uses native approximate natural string comparison and must be checked against original JS localeCompare for non-ASCII names.
- FreeShowProjectTree: read-only native project/folder list, indentation by original tree depth, folder expand/collapse and project selection. Project selection emits only a library selection event, not a real FreeShow playback action.
- FreeShowOutputPreviewProjection: derives what preview can safely show from actual local-settings output IDs, enabled/show/hideFromPreview states, OutData.slide.id/layout/index, and a matching loaded source ShowDocument. It NEVER presents a selected song as "live" when the output is displaying another song.
- FreeShowMultiOutputPreview: renders normal outputs with the existing native slide renderer only where the current source is known; renders a blank surface for unavailable/currently unsupported stage or network output content, preserving its label/resolution. Original media layers are not yet reproduced.
- FreeShow.Native.OperatorProbe: optional --data-root and --settings paths load projects and output settings in the read-only developer window, on the Show page's original left tree and shared right preview slots.
- Synthetic fixtures cover nested folders, missing parent, archived and deleted entries, show-reference layout preservation, output hidden flags, stage separation and real source output slide matching. No fixture has been executed yet.

## Developer-only command
~~~powershell
dotnet run --project native/FreeShow.Native.OperatorProbe -- "C:\CopiedFreeShow\Shows\Song.show" --data-root "C:\CopiedFreeShow" --settings "C:\CopiedFreeShow\local-settings.json"
~~~
Use the actual external FreeShow local SETTINGS file when available. The sample paths above are illustrative. The program reads all source files; it does not open the original FreeShow app or change its files.

## Still not parity-complete
- Original ProjectContentList with project show/section schedule, rearrangement, editing, provider sync, history, templates, create/add menus, archive controls, drag/drop and complete selection lifecycle.
- Project list styling, icons and all FreeShow theme CSS are not reproduced exactly.
- Preview is **not** a live GPU capture of the output window. Video/image/style backgrounds, stage contents, overlays, gradients, effects, animations, line-reveal state and audio meters require their original backends. Blank surfaces must be understood as unsupported/unknown, not as confirmation that the physical output is black.
- Preview's source output panel controls (ClearButtons, MediaControls, ShowActions, audio etc.) are not wired to active output. No hidden live behavior or destructive shortcut is added.
- Native UI still lacks full original project navigation, advanced editor and menus; not usable as a FreeShow replacement.
- No Windows compile, synthetic fixture execution, screenshots or OptiPlex 7490 latency measurements have been performed in this milestone. <50 ms remains only a target.

## Data safety
Original .show, projects.json, settings.json and all church media files stay unchanged. Only the native-port branch under native/ is edited, and the previous checkpoints remain intact.
