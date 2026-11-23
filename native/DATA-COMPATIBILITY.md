# FreeShow .show compatibility: Native port 0.0.5

## Upstream format verified from source
- `src/electron/data/save.ts` saves each show with `writeFile(path.join(showsPath, String(value.name || id) + ".show"), JSON.stringify([id, value]), id)`.
- `src/electron/utils/files.ts` parses the exact JSON tuple format `[id, Show]`. FreeShow may attempt recovery of truncated JSON in its own parser, which this native writer **does not emulate**; corrupted files must not be silently repaired by the port.
- `src/electron/utils/files.ts` points to `<dataRoot>/Shows`. The data root can be customized; it typically defaults to the user's `Documents/FreeShow`.
- `src/frontend/components/helpers/shows.ts` changes modified timestamps when a show is edited.

## Native implementation
- `ShowDocument` parses the original tuple and retains the exact source bytes and raw JSON fields.
- `FreeShowShowExport` starts from a complete source tuple and allows only explicit `Rename` and `ReplaceTextRun` mutations. It does not delete unknown properties, merge rich-text runs or rewrite slides or layout references. The `timestamps.modified` value is updated for actual edits.
- Calling `ToShowBytes()` without any mutations returns the original bytes unchanged (whitespace included).
- Modified JSON is serialized as a compact original-format `[id, Show]` tuple, parsed again for basic validation and retains source ID and unknown JSON fields. Whitespace/escape sequences may change in edited copies, but JSON data is preserved.
- `ExportNewFileAsync` exclusively creates a **new** .show file with `FileMode.CreateNew`. It refuses an existing destination and never writes to the loaded original.
- Added synthetic fixture checks for unchanged byte round-trip, title edits, Unicode text replacements, rich-text-run fidelity, ID/layout preservation and overwrite refusal. **Checks are written but not executed** (Beta 2 policy).

## Where this is not complete
This is a safe **copy-export API**, not production FreeShow editing or an autosave implementation. Nothing is connected to the existing FreeShow library UI or cloud sync. FreeShow indexes songs in `shows.json` and stores projects/settings separately; a .show copy alone is not a complete migration. Full editing must implement relevant history, backups, renaming, index/cache refresh and cloud coordination without changing user workflows.

### Existing FreeShow source remains authoritative
`church-dev` and other checkpoint branches are preserved. Never use the copy-export module to overwrite active church files. No guarantee of full FreeShow compatibility, build success or output performance is made before verified testing.
