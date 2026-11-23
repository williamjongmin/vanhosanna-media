# FreeShow Native Port 0.0.10 — original Windows operator workspace map

## Strict objective
Reproduce the existing FreeShow operator UI and its interaction model; do not invent a replacement UI. This milestone is **the spatial host only**. No operator-facing production program is available yet.

## Source files inspected
| Original FreeShow source | Observed layout/function | Native region or contract |
|---|---|---|
| src/frontend/App.svelte | Windows MenuBar before MainLayout, output windows separate | MenuBarHost and separate NativePersistentVideoOutput |
| src/frontend/components/main/MenuBar.svelte | Windows title/menu bar fixed 25 px | MenuBarHost, row 0 |
| src/frontend/components/main/Top.svelte | Top navigation fixed 40 px, FreeShow logo, Show/Edit/Stage tabs, Draw/Settings/output control | TopHost, row 1 |
| src/frontend/MainLayout.svelte | Horizontal left/center/right with dynamic right panel and bottom optional timeline, Drawer and Tipbar | FreeShowOperatorWorkspace |
| src/frontend/components/system/Resizeable.svelte | Left/right default 290 px, min 4 px, max 638 px; panel widths stored in resized keys | FreeShowOperatorWorkspaceState and native GridSplitter |
| src/frontend/components/show/Projects.svelte | Project tree at left of Show page | Show page Left slot |
| src/frontend/components/show/Show.svelte | Central source show, Slides and Layouts with conditional ShowNotes/Timeline | Show page Center slot |
| src/frontend/components/output/preview/Preview.svelte | Output preview on right plus active clear/overlay/media/audio controls | RightPreviewHost |
| src/frontend/components/show/ShowTools.svelte | Source show tools on right when current song is selected | Show page RightTools slot |
| src/frontend/components/drawer/Drawer.svelte | Bottom Drawer, default 300 px, collapsed 40 px, top tabs and search | DrawerHeaderHost + Drawer region |
| src/frontend/components/drawer/Navigation.svelte | Drawer left navigation by selected category | DrawerNavigationHost |
| src/frontend/components/drawer/Content.svelte | Drawer main content by category | DrawerContentHost |
| src/frontend/components/main/Tipbar.svelte | Optional final tip/help bar | TipbarHost |
| src/frontend/utils/common.ts | DEFAULT_WIDTH=290, DEFAULT_DRAWER_HEIGHT=300, MENU_BAR_HEIGHT=25 | FreeShowOperatorWorkspaceState |
| src/frontend/stores.ts | resized defaults left/right 290, drawer tab default shows, activePage default show | FreeShowOperatorWorkspaceState |
| src/types/Tabs.ts | TopViews page IDs: show, edit, reflow, draw, stage, calendar, settings | Original page IDs accepted, only explicitly mapped pages get content |

## Original MainLayout page slot assignments
- show: Projects | Show | Preview + ShowTools
- edit: Navigation | Editor | Preview + type-conditional edit/media/audio/effect/scene tools
- stage: StageLayouts | StageLayout | Preview + StageTools
- draw: DrawTabs | Slide | Preview + DrawSettings
- settings: SettingsTabs | Settings | Preview + SettingsTools

The original MainLayout does not provide standalone source branches for reflow/calendar at this point; Native will not silently invent equivalent screens.

## Implemented code
- FreeShowOperatorWorkspaceState: read-only original local SETTINGS resized/drawer values with sourced constants, valid original page IDs, visibility and right-panel wide layout threshold.
- FreeShow.Native.Operator: Avalonia-native structural region host with 25 px Windows menu bar, 40 px top bar, left/center/right resizable areas, optional timeline, bottom drawer and optional tip bar. Right preview/tools become side-by-side (row-reverse) when right panel width exceeds 1.8 * 290 px.
- Page slot registry: uses only matching original UI regions. Empty/unported regions stay empty; no fake menu or alternate UI is substituted.
- Synthetic checks in FreeShow.Native.Checks: original dimensions, saved panel widths, focus mode, drawer visibility, wide right panel, panel clamp ranges and unmodified unknown SETTINGS fields.

## Not finished
- Original operator components themselves (project tree, cards/thumbnails, drawer navigation and media, show editor, preview controls, settings UI, icon collection, exact typography/colors/hover/highlight styles) are **not** yet ported.
- Splitter geometries are local-only. Runtime drag results are not written to FreeShow SETTINGS; no autosave is wired yet.
- Top-level UI keyboard event dispatch and actual F1–F5 behavior wiring remain unconnected; source-based command routing exists only in Core.
- Tipbar conditional display, original display controls, popup/menu behavior, right tools page-state eligibility and custom profiles still need full source parity work.
- Focus mode and timeline conditions are only partially mapped. No exact screenshot comparison performed.
- All physical outputs must respect original output IDs and format; Stage/NDI/RTMP/capture still require dedicated renderer implementation.
- No Windows build, screenshot, rendering benchmark or real church hardware execution was performed. Structural code is NOT evidence of parity or the <50 ms goal.

## Usage contract for future ported UI modules
Construct FreeShowOperatorWorkspace with FreeShowOperatorWorkspaceState.FromLocalSettings(loadedLocalSettings). Then:
1. Populate MenuBarHost, TopHost, RightPreviewHost, DrawerHeaderHost, DrawerNavigationHost, DrawerContentHost, DrawerToolsHost, TipbarHost with native ports of their *original* components.
2. For each original page, register its actual Left, Center and RightTools controls using RegisterPageRegions(page, regions).
3. Call ChangePage(originalPageId) on original top navigation selection, preserving context state.
4. Use SetProjectTimeline only when the original project's timeline is enabled.
5. Do not attach a new UI design to an unported region merely to make the shell appear finished.

Original church data and FreeShow branches remain untouched. Native files live under native/ in the native-port development branch.
