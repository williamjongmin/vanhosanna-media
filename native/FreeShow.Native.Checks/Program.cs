using System.Text;
using System.Text.Json;
using FreeShow.Native.Core;

var path = Path.Combine(AppContext.BaseDirectory, "sample-show.json");
var bytes = await File.ReadAllBytesAsync(path);
var show = ShowDocument.Parse(bytes);

Check(show.IsFreeShowFileEnvelope, "Original .show tuple recognized");
Check(show.ShowId == "original-show-id", "Show ID preserved");
Check(show.Name == "Native Compatibility Fixture", "Show title");
Check(show.ActiveLayoutId == "main", "Active layout");
Check(show.OriginalBytes().SequenceEqual(bytes), "Original bytes unchanged");
Check(ShowDocument.Property(show.Root, "unknownFutureField").ValueKind == JsonValueKind.Object,
    "Unknown fields preserved");

var raw = show.ReadLayoutEntries();
Check(raw.Count == 4, "Parent/children and trailing parent expanded");
Check(raw.Select(x => x.SlideId).SequenceEqual(
    new[] { "verse", "verse-child", "verse-child-2", "chorus" }),
    "Original FreeShow parent-child order");
Check(raw[1].ParentSlideId == "verse" && raw[1].SourceIndex == 0 && raw[1].ChildIndex == 0,
    "Child origin retained");
Check(raw[2].Disabled && raw[2].EffectiveDisabled, "Child disabled override retained");
Check(ShowDocument.Property(raw[1].RawEntry, "transition").ValueKind == JsonValueKind.Object,
    "Child layout transition retained");
Check(show.ValidateLayout().Count == 0, "No missing slide references");

var output = SlideProjection.ReadFrames(show);
Check(output.Count == 3, "Disabled slide omitted only from output projection");
Check(output[0].PlainText == "주님을 찬양합니다", "Korean rich text runs joined without alteration");
Check(output[1].PlainText == "Praise the Lord", "English source text retained");
Check(output[0].Items[0].Lines[0].Runs.Count == 2, "Text run styling boundaries preserved");
Check(!output[0].HasNonTextItems, "Pure lyrics projection");
Check(SlideProjection.ReadFrames(show, includeDisabled:true)[2].HasNonTextItems,
    "Non-text items retained for renderer");

var changed = Encoding.UTF8.GetBytes(
    Encoding.UTF8.GetString(bytes).Replace("\"verse-child-2\":{", "\"verse-child-2-renamed\":{"));
var invalid = ShowDocument.Parse(changed);
Check(invalid.ValidateLayout().Any(x => x.Contains("verse-child-2")),
    "Broken slide reference detected without modifying source");

var plans = NativeRenderPlanner.Prepare(show);
Check(plans.Count == 3, "Enabled native render plans include all visible source slides");
Check(plans[0].TextItems.Count == 1, "Original first-slide text item count");
Check(plans[0].TextItems[0].FontSize == 80, "Source font-size: 80px preserved");
Check(plans[0].TextItems[0].Bounds.Width == 400 &&
      plans[0].TextItems[0].Bounds.Height == 150, "Original Zoomed.svelte CSS box defaults");
Check(plans[0].TextItems[0].Source.Lines[0].Runs.Count == 2, "Text formatting runs survive native plan");
Check(plans[0].TextItems[0].Alignment == "center", "Original TextboxLines.svelte default center alignment");
Check(NativeRenderPlanner.Prepare(show, includeDisabled: true)[2].Unsupported
      .Any(w => w.Contains("Non-text item")), "Unsupported media flagged, never silently dropped");

var css = new CssDeclarations("left: 25%; width: 960px; opacity: 0.4; color: #ffffff; left: 10%;");
Check(css.Length("left")?.Resolve(1920) == 192, "CSS last declaration wins and percent width resolves");
Check(css.Length("width")?.Resolve(1920) == 960, "CSS px values resolve without conversion");
Check(css.Number("opacity") == 0.4, "CSS opacity parsed invariantly");
Check(css.Length("widthx") == null && css.Length("font-size") == null, "Missing CSS stays missing");
Check(CssDeclarations.ParseLength("calc(100% - 20px)") is null,
      "Unsupported CSS expressions are flagged, not silently guessed");

// Exact ordinary-lyrics template subset from FreeShow mergeWithTemplate():
// copy box styling, preserve source lyric strings and mixed bold formatting.
using (var tempDoc = JsonDocument.Parse("""
{
  "name": "Basic lyric text",
  "settings": {"mode":"default"},
  "items": [
    {
      "type": "text",
      "style": "top: 240px;left: 180px;width: 1560px;height: 540px;font-size: 72px;",
      "align": "text-align: center;align-items: center;",
      "lines": [{"align":"center","text":[{"value":"Template sample","style":"font-size: 72px;color: #ffff00;"}]}]
    }
  ]
}
"""))
{
    var template = new TemplateSelection("basic-lyrics", tempDoc.RootElement.Clone(),
        "output-style", false, false);
    var merged = FreeShowTemplateMerger.Apply(output[0], template);
    Check(merged.WasApplied && merged.Unsupported.Count == 0,
        "Native ordinary-lyrics template merge recognized");
    Check(merged.Items[0].PlainText == output[0].Items[0].PlainText,
        "Source Korean lyric text preserved under template");
    Check(merged.Items[0].Style.Contains("top: 240px;") &&
          merged.Items[0].Style.Contains("width: 1560px;"),
        "Template textbox geometry replaces the source item geometry");
    Check(merged.Items[0].Lines[0].Runs.Count == 2,
        "Mixed rich-text runs preserved through template merge");
    Check(merged.Items[0].Lines[0].Runs[0].Style.Contains("font-weight: bold;") &&
          merged.Items[0].Lines[0].Runs[1].Style.Contains("font-weight: normal;"),
        "Original mixed bold/normal emphasis is preserved after applying template style");
    Check(merged.Items[0].Lines[0].Runs[1].Style.Contains("#ffff00"),
        "Template color applies to a source with a single original color");
    var mergedNative = NativeRenderPlanner.BuildFrame(output[0] with { Items = merged.Items });
    Check(mergedNative.TextItems[0].Bounds.X == 180 &&
          mergedNative.TextItems[0].Bounds.Y == 240 &&
          mergedNative.TextItems[0].FontSize == 72,
        "Merged template geometry enters the Avalonia native render plan");
    Check(output[0].Items[0].Style == "font-size: 80px;",
        "Original source frame remains unchanged by template rendering");
}

using (var unsupportedDoc = JsonDocument.Parse("""
{
 "name":"Scripture template",
 "settings":{"mode":"scripture"},
 "items":[{"type":"text","style":"font-size:88px;","lines":[{"text":[{"value":"{scripture_text}","style":""}]}]}]
}
"""))
{
    var selected = new TemplateSelection("scripture", unsupportedDoc.RootElement.Clone(),
        "output-style", true, false);
    var result = FreeShowTemplateMerger.Apply(output[0], selected);
    Check(!result.WasApplied && result.Unsupported.Count > 0,
        "Unimplemented scripture variable template reported instead of incorrectly merged");
    Check(result.Items[0].PlainText == output[0].Items[0].PlainText,
        "Unsupported dynamic template never mutates source or discards lyrics");
}

// Copy-only FreeShow edit/export checks. These operate on synthetic fixture
// files in a temporary directory and never touch the user's church data.
var noop = new FreeShowShowExport(show);
Check(!noop.IsModified, "New edit session is unchanged");
Check(noop.ToShowBytes().SequenceEqual(bytes), "No-op export preserves source bytes exactly");

var edits = new FreeShowShowExport(show);
edits.Rename("Native Export Test");
edits.ReplaceTextRun("verse", 0, 0, 0, "사랑의 하나님");
var edited = ShowDocument.Parse(edits.ToShowBytes());
Check(edits.IsModified, "Export session detects change");
Check(edited.ShowId == show.ShowId, "Edited copy preserves FreeShow ID");
Check(edited.Name == "Native Export Test", "Edited copy updates only requested title");
Check(ShowDocument.Property(edited.Root, "unknownFutureField").ValueKind == JsonValueKind.Object,
    "Edited copy preserves unknown future fields");
Check(edited.ReadLayoutEntries().Select(x => x.SlideId).SequenceEqual(raw.Select(x => x.SlideId)),
    "Edited copy preserves slide IDs and layout ordering");
var editedFrame = SlideProjection.ReadFrames(edited)[0];
Check(editedFrame.Items[0].Lines[0].Runs.Count == 2,
    "Text editing preserves source run segmentation");
Check(editedFrame.Items[0].Lines[0].Runs[0].Text == "사랑의 하나님" &&
      editedFrame.Items[0].Lines[0].Runs[1].Text == " 찬양합니다",
    "Text editing modifies only requested run");
Check(editedFrame.Items[0].Lines[0].Runs[0].Style == "font-weight: bold;",
    "Text run style is retained after copy edit");
Check(show.OriginalBytes().SequenceEqual(bytes) && show.Name == "Native Compatibility Fixture",
    "Original loaded source remains unchanged after copy edit");

var temp = Path.Combine(Path.GetTempPath(), "freeshow-native-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var exportPath = Path.Combine(temp, "Native Export Test.show");
    await edits.ExportNewFileAsync(exportPath);
    Check(File.Exists(exportPath), "Copy-only export creates new .show file");
    Check((await ShowDocument.ReadAsync(exportPath)).ShowId == show.ShowId,
        "Copied file can be re-read by original tuple parser");

    bool collisionRefused = false;
    try { await edits.ExportNewFileAsync(exportPath); }
    catch (IOException) { collisionRefused = true; }
    Check(collisionRefused, "Existing export file is never overwritten");
    Check((await ShowDocument.ReadAsync(exportPath)).Name == "Native Export Test",
        "Refused overwrite leaves existing copy intact");
}
finally
{
    Directory.Delete(temp, recursive: true);
}

// Visual settings and independent output-layer checks. Original files never
// opened: fixture settings are created under a throwaway directory.
var visualRoot = Path.Combine(Path.GetTempPath(), "freeshow-visual-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(visualRoot, "Config"));
try
{
    await File.WriteAllTextAsync(Path.Combine(visualRoot, "Config", "settings_synced.json"),
        """{"styles":{"worship":{"name":"Worship","background":"#111111","backgroundImage":"local-background.png","template":"lyrics","layers":["background","slide","overlays"],"transition":{"text":{"type":"fade","duration":300}}}}}""");
    await File.WriteAllTextAsync(Path.Combine(visualRoot, "Config", "templates.json"),
        """{"lyrics":{"name":"Lyrics","items":[],"settings":{"backgroundColor":"#222222","firstSlideTemplate":"lyrics-first"}},"lyrics-first":{"name":"First","items":[],"settings":{"backgroundColor":"#333333"}}}""");
    var visual = await FreeShowVisualSettings.OpenAsync(visualRoot);
    Check(visual.HasSettingsFile && visual.HasTemplatesFile, "Original FreeShow config files discovered");
    var layers = FreeShowOutputLayerPlanner.Prepare(show, visual, "worship");
    Check(layers.Count == 3, "One output compositing plan for every enabled FreeShow slide");
    Check(layers[0].Template.TemplateId == "lyrics-first" &&
          layers[0].Template.UsedFirstSlideOverride, "Original first-slide template selection");
    Check(layers[1].Template.TemplateId == "lyrics" &&
          !layers[1].Template.UsedFirstSlideOverride, "Normal template after first slide");
    Check(layers[0].BackgroundColor == "#333333", "Template background color overrides output style");
    Check(layers[0].StyleBackgroundId == "local-background.png", "Style background layer retained");
    Check(layers[0].EnabledLayers.SequenceEqual(new[] {"background", "slide", "overlays"}),
          "Original output layer order retained");
    Check(layers[0].TextTransition.Type == "fade" && layers[0].TextTransition.DurationMs == 300,
          "Original output transition duration retained");
    Check(layers[0].Unsupported.Any(x => x.Contains("not rendered yet")),
          "Unsupported image/video rendering cannot silently appear equivalent");

    Check(FreeShowMediaLookup.Classify("background.png", "") == NativeMediaKind.Image,
          "Native still background detection");
    Check(FreeShowMediaLookup.Classify("background.mp4", "") == NativeMediaKind.Video,
          "Video is classified but never mistaken for static image");
    Check(FreeShowMediaLookup.Resolve(show, "asset://existing").Id == "asset://existing",
          "Original FreeShow media identity retained");

    var videoLayerPlans = layers.Select(x =>
        x with { StyleBackgroundId = "unchanged-loop.mp4", OutputBackgroundId = "" }).ToArray();
    var singleVideo = NativeVideoCuePlanner.Prepare(
        videoLayerPlans, id => new FreeShowMediaReference(id, id,
            NativeMediaKind.Video, true, true, default));
    Check(singleVideo.Count == layers.Count &&
          singleVideo.All(x => x.SingleSupportedVideo?.Media.Id == "unchanged-loop.mp4"),
          "Same background is selected for every source lyric cue");
    Check(singleVideo.All(x => !x.RequiresUnimplementedMultipleVideoMix),
          "Single video layer stays supported independent of slide index");
    var dualVideo = NativeVideoCuePlanner.Prepare(
        videoLayerPlans.Select(x => x with { OutputBackgroundId = "additional.mp4" }).ToArray(),
        id => new FreeShowMediaReference(id, id, NativeMediaKind.Video, true, true, default));
    Check(dualVideo.All(x => x.RequiresUnimplementedMultipleVideoMix &&
          x.Unsupported.Count > 0 && x.SingleSupportedVideo == null),
          "Multiple FreeShow video layers are flagged, not silently dropped");
}
finally
{
    Directory.Delete(visualRoot, recursive: true);
}

// Persistent video state: these pure checks require no LibVLC decoder.
var videoState = new NativeVideoPlaybackState();
Check(videoState.Plan("loop", @"C:\Media\loop.mp4") == VideoPlaybackDecision.Open,
    "New local video requests decoder start");
videoState.MarkStarted("loop", @"C:\Media\loop.mp4");
Check(videoState.Plan("loop", @"C:\Media\loop.mp4") == VideoPlaybackDecision.Keep,
    "Same background remains active across lyric cue changes");
Check(videoState.Plan("loop", @"c:\media\LOOP.mp4") == VideoPlaybackDecision.Keep,
    "Windows media file case does not trigger decoder restart");
Check(videoState.Plan("different", @"C:\Media\other.mp4") == VideoPlaybackDecision.Open,
    "Different media requires an explicit background change");
Check(videoState.Plan("missing", null) == VideoPlaybackDecision.Unresolved,
    "Unresolved media does not falsely start decoder");
Check(videoState.Plan("", null) == VideoPlaybackDecision.Stop,
    "Explicitly clearing background stops persistent decoder");
videoState.MarkStopped();
Check(videoState.Plan("", null) == VideoPlaybackDecision.NoChange,
    "No video and no request requires no decoder operation");

using (var videoDoc = JsonDocument.Parse("""
{"style":"#111111","media":[{"path":"background.mp4","type":"video","loop":true}]}
"""))
{
    var media = FreeShowMediaLookup.Resolve(show, "background.mp4");
    Check(media.Kind == NativeMediaKind.Video, "Local video file classified as video");
    Check(FreeShowMediaLookup.ExistingLocalVideoPath(media,
        Path.Combine(Path.GetTempPath(), "nonexistent-video-root")) == null,
        "Nonexistent video is not assumed to be playable");
}

// In-memory UI dispatch profiling is distinct from physical output latency.
var metrics = new NativeCueDispatchMetrics(capacity: 4);
metrics.Record(0, TimeSpan.FromMilliseconds(1));
metrics.Record(1, TimeSpan.FromMilliseconds(3));
metrics.Record(2, TimeSpan.FromMilliseconds(5));
metrics.Record(3, TimeSpan.FromMilliseconds(7));
metrics.Record(4, TimeSpan.FromMilliseconds(9)); // first oldest sample is evicted
var summary = metrics.Snapshot();
Check(summary.Count == 4 && summary.P50Ms == 6, "Bounded UI dispatch sample percentile");
Check(summary.MaxMs == 9 && summary.P95Ms > summary.P50Ms,
    "UI dispatch max and p95 are computed independently");
Check(new NativeCueDispatchMetrics().Snapshot().Count == 0,
    "No dispatch latency claims before any samples are observed");

// Native port keeps the original FreeShow output IDs from local SETTINGS
// rather than inventing a fixed Main/Online/Stage enum.
using (var settingsDoc = JsonDocument.Parse("""
{
 "outputs":{
   "stageA":{"name":"Confidence","enabled":true,"active":true,"stageOutput":"mainA",
     "bounds":{"x":1920,"y":0,"width":1280,"height":720},"style":"confidence","screen":"display2"},
   "mainB":{"name":"Screen 2","enabled":true,"active":false,
     "bounds":{"x":0,"y":0,"width":1920,"height":1080},"style":"s2","screen":"display1"},
   "mainA":{"name":"Screen 1","enabled":true,"active":true,
     "bounds":{"x":1920,"y":0,"width":1920,"height":1080},"style":"s1","screen":"display3"},
   "disabled":{"name":"Old","enabled":false,"active":true,
     "bounds":{"x":0,"y":0,"width":1024,"height":768},"style":"old","screen":null}
 },
 "outLocked":false,
 "unsupportedExistingSettings":{"preserve":"raw"}
}
"""))
{
    var config = FreeShowOutputConfiguration.Parse(settingsDoc.RootElement);
    Check(config.All.Count == 4 && config.Find("mainA")?.StyleId == "s1",
        "Original nonfixed output IDs and independent style IDs are preserved");
    Check(config.GetActiveNormalOutputs().Select(o => o.Id).SequenceEqual(["mainA"]),
        "Active output selection excludes stage and disabled displays");
    Check(config.GetEnabledStageOutputs().Single().StageSourceId == "mainA",
        "Stage output references original source output ID");
    Check(config.Find("stageA")!.Bounds.Width == 1280,
        "Stage confidence monitor size and coordinates are retained");
    Check(ShowDocument.Property(config.RawSettings, "unsupportedExistingSettings")
        .ValueKind == JsonValueKind.Object,
        "Original unknown SETTINGS keys remain available");
}

var router = new FreeShowShortcutRouter();
var at = DateTimeOffset.UnixEpoch;
string[] activeIds = ["out-a", "out-b"];
var f1 = router.Route(new NativeShortcutInput("F1", at), activeIds);
Check(f1.Count == 2 && f1.All(x => x.Command == NativeShortcutCommand.ClearBackground),
    "Original F1 clears only background on active normal outputs");
var f2 = router.Route(new NativeShortcutInput("F2", at.AddSeconds(1)), activeIds);
Check(f2.Count == 2 && f2.All(x => x.Command == NativeShortcutCommand.ClearSlide
    && !x.ClearCachedSlidePosition), "First F2 clears lyrics but retains cached slide position");
var f2Again = router.Route(new NativeShortcutInput("F2", at.AddMilliseconds(1400)), activeIds);
Check(f2Again.Count == 2 && f2Again.All(x => x.ClearCachedSlidePosition),
    "Second F2 within 500 ms also clears saved slide position");
Check(router.Route(new NativeShortcutInput("F2", at.AddMilliseconds(1410),
    Repeat:true), activeIds).Count == 0, "Repeated F2 key events are ignored");
var f3 = router.Route(new NativeShortcutInput("F3", at.AddSeconds(3)), activeIds);
Check(f3.Count == 4 && f3.Count(x => x.Command == NativeShortcutCommand.ClearAllOverlaysForShortcut) == 2
    && f3.Count(x => x.Command == NativeShortcutCommand.ClearEffects) == 2,
    "Original F3 clears overlays and effects separately");
var f4 = router.Route(new NativeShortcutInput("F4", at.AddSeconds(4)), activeIds);
Check(f4.Count == 1 && f4[0].Command == NativeShortcutCommand.ClearAudioAndPlaylist
    && f4[0].OutputId == "", "Original F4 is global audio clear, not output clear");
Check(router.Route(new NativeShortcutInput("F4", at.AddSeconds(4), Alt:true),
    activeIds).Count == 0, "Alt+F4 is never hijacked");
var next = router.Route(new NativeShortcutInput(" ", at.AddSeconds(6)), activeIds);
Check(next.Count == 2 && next.All(x => x.Command == NativeShortcutCommand.Next),
    "Space advances all active normal outputs");
var duplicate = router.Route(new NativeShortcutInput("ArrowRight", at.AddMilliseconds(6040),
    Origin: NativeShortcutOrigin.OutputWindow), activeIds);
Check(duplicate.Count == 0, "Cross-source advance duplicate within 80 ms is suppressed");
var independent = router.Route(new NativeShortcutInput("ArrowRight", at.AddMilliseconds(6040)),
    activeIds);
Check(independent.Count == 2, "Same-origin rapid lyric advances are not artificially delayed");
var first = router.Route(new NativeShortcutInput("Home", at.AddSeconds(7)), activeIds);
var last = router.Route(new NativeShortcutInput("End", at.AddSeconds(8)), activeIds);
var previous = router.Route(new NativeShortcutInput("PageUp", at.AddSeconds(9)), activeIds);
Check(first.All(x => x.Command == NativeShortcutCommand.First)
    && last.All(x => x.Command == NativeShortcutCommand.Last)
    && previous.All(x => x.Command == NativeShortcutCommand.Previous),
    "Home/End/PageUp preserve original direction semantics");
Check(router.Route(new NativeShortcutInput("ArrowRight", at.AddSeconds(10),
    TypingInEditableControl:true), activeIds).Count == 0,
    "Typing a lyric does not accidentally advance live outputs");
Check(router.Route(new NativeShortcutInput("F5", at.AddSeconds(11),
    OutputLocked:true), activeIds).Count == 0,
    "Locked outputs do not accept advancement in Native");

// FreeShow OutData stores background, slide, effects, and overlays as
// independent fields. F1/F2/F3 must not accidentally clear each other.
using (var liveSettings = JsonDocument.Parse("""
{"outputs":{"worship":{"name":"Primary","enabled":true,"active":true,
"bounds":{"width":1920,"height":1080,"x":0,"y":0},
"out":{"background":{"path":"loop.mp4","type":"video"},
"slide":{"id":"song123","index":2},
"overlays":["offering","announcement"],"effects":["vignette"],
"scene":{"id":"stage-scene"},"keepUnknown":{"future":true}}
}}}
"""))
{
    var profile = FreeShowOutputConfiguration.Parse(liveSettings.RootElement).Find("worship")!;
    var state = new FreeShowOutputState(profile);
    var snapshot = state.Snapshot().GetRawText();
    Check(state.HasActiveBackground && state.HasSlide && state.Overlays.Count == 2
        && state.Effects.Count == 1, "Original OutData layers are independently preserved");
    Check(state.TryApply(new FreeShowShortcutAction(NativeShortcutCommand.ClearSlide, "worship"))
        && !state.HasSlide && state.HasActiveBackground && state.Overlays.Count == 2,
        "F2 clears slide while preserving video, overlays and effects");
    Check(state.TryApply(new FreeShowShortcutAction(NativeShortcutCommand.ClearBackground, "worship"))
        && !state.HasActiveBackground && state.Overlays.Count == 2,
        "F1 clears active output background without touching other layers");
    Check(!state.TryApply(new FreeShowShortcutAction(NativeShortcutCommand.ClearOverlays, "worship"))
        && state.Overlays.Count == 2,
        "Locked overlays cannot be cleared blindly without original metadata");
    state.ClearOverlaysKeepingLocked(new HashSet<string> { "announcement" });
    Check(state.Overlays.SequenceEqual(["announcement"]) && state.Effects.Count == 0,
        "Original Preview clear button preserves locked overlays while clearing effects");
    Check(state.TryApply(new FreeShowShortcutAction(NativeShortcutCommand.ClearEffects, "worship"))
        && state.Effects.Count == 0,
        "F3 effects clearing is independent and idempotent");
    Check(!state.TryApply(new FreeShowShortcutAction(NativeShortcutCommand.Next, "worship"))
        && !state.TryApply(new FreeShowShortcutAction(NativeShortcutCommand.ClearSlide, "different")),
        "Unported project navigation and foreign-output commands never silently execute");
    Check(ShowDocument.Property(state.Snapshot(), "keepUnknown").ValueKind == JsonValueKind.Object
        && ShowDocument.Property(state.Snapshot(), "scene").ValueKind == JsonValueKind.Object,
        "Native OutData clone retains unknown metadata and scene state");
    Check(profile.Raw.GetRawText().Contains("loop.mp4") &&
        ShowDocument.Property(ShowDocument.Property(profile.Raw, "out"), "slide")
            .ValueKind == JsonValueKind.Object,
        "Mutating native state never edits the original FreeShow SETTINGS object");
}

// Original source-aligned MainLayout.svelte native geometry state.
// These are synthetic fixtures only; no real FreeShow SETTINGS file is edited.
using (var workspaceSettings = JsonDocument.Parse("""
{
  "resized": {
    "leftPanel": 310, "rightPanel": 530,
    "leftPanelDrawer": 250, "rightPanelDrawer": 400
  },
  "drawer": {"height": 360, "stored": null},
  "unknownLayoutExtension": {"keep": true}
}
"""))
{
    var state = FreeShowOperatorWorkspaceState.FromLocalSettings(workspaceSettings.RootElement);
    Check(FreeShowOperatorWorkspaceState.WindowsMenuBarHeight == 25
        && FreeShowOperatorWorkspaceState.TopBarHeight == 40,
        "Original Windows menu bar 25px and top navigation 40px");
    Check(state.LeftPanelWidth == 310 && state.RightPanelWidth == 530
        && state.RightPanelIsHorizontal,
        "Original right panel uses row-reverse when its width exceeds 1.8*290px");
    Check(state.DrawerHeight == 360
        && state.LeftDrawerPanelWidth == 250 && state.RightDrawerPanelWidth == 400,
        "Saved Drawer and inner side panel sizes are read without rewriting settings");
    Check(state.ShowDrawer && state.ShowTopBar && state.ShowMenuBar,
        "Original Show page contains menu, top and drawer");
    Check(!state.WithPage("stage").ShowDrawer
        && state.WithPage("stage").ShowTopBar,
        "Stage page has original top and no Drawer");
    Check(!state.WithPage("settings").ShowDrawer
        && state.WithPage("edit").ShowDrawer,
        "Only Show/Edit display the FreeShow drawer");
    Check(!FreeShowOperatorWorkspaceState.FromLocalSettings(
        workspaceSettings.RootElement, focusMode:true).ShowTopBar,
        "Focus mode hides original main Top component");
    Check(FreeShowOperatorWorkspaceState.FromLocalSettings(
        workspaceSettings.RootElement, focusMode:true).ShowMenuBar,
        "Original Windows menu bar is still managed by App.svelte in focus mode");
    Check(state.WithPanelWidths(1, 10000).LeftPanelWidth == 4
        && state.WithPanelWidths(1, 10000).RightPanelWidth == 638,
        "Original Resizeable minimum 4px and 2.2 times maximum 290px are honored");
    Check(state.WithDrawerHeight(2).DrawerHeight == 40,
        "Original Drawer.svelte minimum 40px retained");
    Check(ShowDocument.Property(state.RawSettings, "unknownLayoutExtension")
        .ValueKind == JsonValueKind.Object, "Unknown local layout settings preserved");
}

var fallbackLayout = FreeShowOperatorWorkspaceState.FromLocalSettings(default);
Check(fallbackLayout.LeftPanelWidth == 290 && fallbackLayout.RightPanelWidth == 290
    && fallbackLayout.DrawerHeight == 300 && fallbackLayout.Page == "show",
    "Original workspace dimensions and initial show page fallbacks");

// Original Config/projects.json contains independent projects and folders.
// Use a synthetic file only; nothing here touches a real church project.
using (var projectJson = JsonDocument.Parse("""
{
 "projects": {
  "song-project": {"name":"Sunday Worship","parent":"folder-worship","created":1,
    "shows":[{"id":"original-show-id","type":"show","layout":"main"}]},
  "archived": {"name":"Last Month","parent":"/","created":1,"archived":true,
    "shows":[{"id":"archived-song","type":"show"}]},
  "root": {"name":"Bible Study","parent":"/","created":2,"shows":[]},
  "orphan": {"name":"Orphan","parent":"missing-folder","created":3,"shows":[]},
  "deleted": {"name":"Removed","deleted":true,"parent":"/","created":4,"shows":[]}
 },
 "folders":{
  "folder-worship":{"name":"Worship","parent":"/"},
  "folder-nested":{"name":"Setlists","parent":"folder-worship"},
  "folder-deleted":{"name":"Deleted","parent":"/","deleted":true}
 },
 "projectTemplates":{"futureTemplate":{"name":"Original preserved"}},
 "unknownConfig":{"preserve":true}
}
"""))
{
    var lib = FreeShowProjectLibrary.Parse(projectJson.RootElement);
    Check(lib.Tree.Count == 6 && lib.Find("deleted") == null,
        "Original project library filters deleted entries without modifying JSON");
    Check(lib.Find("song-project")?.ParentId == "folder-worship" &&
          lib.Find("song-project")?.Shows[0].ShowId == "original-show-id",
        "Original project folder relation and show ID preserved");
    Check(lib.Find("song-project")?.Shows[0].LayoutId == "main",
        "Original per-project arrangement override preserved");
    Check(lib.Find("folder-nested")?.Depth == 1,
        "Project nesting depth follows original folder IDs");
    Check(lib.Find("orphan")?.ParentId == "/" &&
          lib.Warnings.Any(w => w.Contains("Missing parent")),
        "Missing folder reference displayed at root with warning");
    Check(lib.Tree.ToList().FindIndex(x => x.Id == "root") <
          lib.Tree.ToList().FindIndex(x => x.Id == "archived"),
        "Archived projects remain after normal projects");
    Check(ShowDocument.Property(lib.Raw, "projectTemplates").ValueKind == JsonValueKind.Object &&
          ShowDocument.Property(lib.Raw, "unknownConfig").ValueKind == JsonValueKind.Object,
        "Original project templates and unknown store fields preserved");
}

using (var previewJson = JsonDocument.Parse("""
{"outputs":{
 "outputB":{"name":"B","enabled":true,"active":true,
  "bounds":{"width":1920,"height":1080},"out":{"slide":{"id":"other-song","layout":"main","index":0}}},
 "outputA":{"name":"A","enabled":true,"active":true,
  "bounds":{"width":1920,"height":1080},
  "out":{"slide":{"id":"original-show-id","layout":"main","index":1}}},
 "stage":{"name":"Confidence","enabled":true,"active":true,"stageOutput":"outputA",
  "bounds":{"width":1920,"height":1080},"out":{"slide":{"id":"original-show-id","index":0}}},
 "hidden":{"name":"Hidden","enabled":true,"active":true,"hideFromPreview":true,
  "bounds":{"width":1920,"height":1080},"out":{"slide":{"id":"original-show-id","index":0}}}
}}
"""))
{
    var configuration = FreeShowOutputConfiguration.Parse(previewJson.RootElement);
    var projected = FreeShowOutputPreviewProjection.Prepare(configuration, show);
    Check(projected.Count == 3 && projected.All(x => x.Output.Id != "hidden"),
        "Original MultiOutputs hides configured hideFromPreview output");
    Check(projected[0].Output.Id == "outputA" && projected[^1].Output.Id == "stage",
        "Normal outputs ordered before stage confidence view");
    Check(projected[0].SourceFrame?.SlideId == "verse-child",
        "Real output OutData index resolves source child-slide frame");
    Check(projected[1].SourceFrame == null && projected[1].Omission != null,
        "Different live show never falsely previews selected song");
    Check(projected[2].SourceFrame == null && projected[2].Omission != null,
        "Unported Stage compositor never falsely rendered as a normal lyric output");
}

// ProjectContentList.svelte source grouping, original indices and back flow.
using (var sourceProjects = JsonDocument.Parse("""
{
  "projects": {
    "sunday": {
      "name":"Sunday Morning", "parent":"/", "created":1,
      "sectionsCollapsed":true,
      "shows":[
        {"id":"intro","type":"section","name":"Opening","color":"#d551ff"},
        {"id":"original-show-id","type":"show","name":"Praise","layout":"main"},
        {"id":"video-1","type":"video","name":"Background","loop":true},
        {"id":"original-show-id","type":"show","name":"Praise Again"},
        {"id":"prayer","type":"section","name":"Prayer","color":"#33aa66"},
        {"id":"scripture","type":"show","name":"Bible"},
        {"id":"later","type":"show_placeholder","name":"Coming Up"}
      ],
      "extraProjectField":{"keep":true}
    }
  },
  "folders":{},
  "projectTemplates":{}
}
"""))
{
    var library = FreeShowProjectLibrary.Parse(sourceProjects.RootElement);
    var project = library.Find("sunday")!;
    var content = FreeShowProjectContentProjection.Prepare(project);
    Check(content.Groups.Count == 2 &&
        content.Groups[0].Color == "#d551ff" &&
        content.Groups[1].Color == "#33aa66",
        "FreeShow source section colors and grouping are retained");
    Check(content.Items.Count == 7 &&
        content.Items.Select(x => x.SourceIndex).SequenceEqual(Enumerable.Range(0, 7)),
        "Project source indices preserved without consuming indexes for dividers");
    Check(content.Groups[0].Rows.Any(x => x.SyntheticDivider),
        "Source inserts synthetic divider when item type switches in a colored section");
    Check(content.Items[1].Id == content.Items[3].Id &&
        content.Items[1].SourceIndex != content.Items[3].SourceIndex,
        "Repeated show references remain separate project entries");
    Check(content.Items[1].LayoutId == "main" &&
        content.Items[2].IsMedia && content.Items[^1].Type == "show_placeholder",
        "Per-project layout overrides and video/placeholder types preserved");
    Check(ShowDocument.Property(project.Raw, "extraProjectField").ValueKind == JsonValueKind.Object,
        "Project source unknown fields remain untouched by content projection");

    var nav = new FreeShowProjectNavigation(library);
    Check(nav.ProjectListViewOpen && nav.ActiveProjectId == null,
        "Project browser starts in original project-list view");
    nav.OpenProject("sunday");
    Check(!nav.ProjectListViewOpen && nav.ActiveItem?.SourceIndex == 0 &&
        nav.ActiveItem.IsSection, "Opening project selects first source item, including section");
    var mediaItem = nav.OpenItem(2);
    Check(mediaItem.Type == "video" && nav.ActiveItem?.SourceIndex == 2,
        "Selecting a media project item does not reinterpret it as a song");
    nav.BackToProjects();
    Check(nav.ProjectListViewOpen && nav.ActiveProjectId == "sunday" &&
          nav.ActiveItem?.SourceIndex == 2,
        "Original Projects.svelte back keeps active project and item");
    nav.OpenProject("sunday");
    Check(nav.ActiveItem?.SourceIndex == 2, "Reopening same project retains active selection");
}

var catalogRoot = Path.Combine(Path.GetTempPath(),
    "freeshow-native-catalog-" + Guid.NewGuid().ToString("N"));
var catalogFolder = ShowLibrary.ShowsFolder(catalogRoot);
Directory.CreateDirectory(catalogFolder);
try
{
    var firstPath = Path.Combine(catalogFolder, "first.show");
    var secondPath = Path.Combine(catalogFolder, "another-name.show");
    await File.WriteAllBytesAsync(firstPath, bytes);
    await File.WriteAllBytesAsync(secondPath, bytes);
    var catalog = new FreeShowReadOnlyShowCatalog(catalogRoot);
    var found = await catalog.FindAsync(show.ShowId);
    Check(found?.ShowId == show.ShowId &&
          found.OriginalBytes().SequenceEqual(bytes),
          "Native song lookup matches tuple ID, not source filename");
    Check(catalog.Warnings.Any(x => x.Contains("Duplicate show ID")),
          "Duplicate .show IDs are reported instead of silently overwriting");
    Check((await File.ReadAllBytesAsync(firstPath)).SequenceEqual(bytes) &&
          (await File.ReadAllBytesAsync(secondPath)).SequenceEqual(bytes),
          "Read-only catalog does not modify either duplicate source song");
}
finally
{
    Directory.Delete(catalogRoot, recursive:true);
}

// Source Preview.svelte/ClearButtons.svelte + helpers/output.ts states.
using (var outputSettings = JsonDocument.Parse("""
{
 "outLocked":false,
 "outputs":{
  "main-1":{
    "name":"Primary","enabled":true,"active":true,
    "bounds":{"x":0,"y":0,"width":1920,"height":1080},
    "out":{
      "background":{"path":"background-video.mp4"},
      "slide":{"id":"original-show-id","index":1,"layout":"main"},
      "overlays":["locked-1","open-1"],
      "effects":["glow"],
      "scene":{"id":"scene-1"},
      "transition":null
    }
  }
 }
}
"""))
using (var lockedOverlays = JsonDocument.Parse("""
{"locked-1":{"name":"Pinned","locked":true},"open-1":{"name":"Announcement","locked":false}}
"""))
{
    var outputConfiguration = FreeShowOutputConfiguration.Parse(outputSettings.RootElement);
    var snapshot = FreeShowPreviewControlState.Prepare(
        outputConfiguration, lockedOverlays.RootElement);
    Check(snapshot.ActiveOutputIds.SequenceEqual(["main-1"]) &&
          !snapshot.OutputLocked, "Original active output IDs used by ClearButtons");
    Check(snapshot.Control("slide").Presence == PreviewLayerPresence.Active &&
          snapshot.Control("background").Presence == PreviewLayerPresence.Active,
        "Original FreeShow Preview identifies active lyric and media background separately");
    Check(snapshot.Control("overlays").SourceCanClear &&
          snapshot.HasLockedOverlays && snapshot.OverlayLocksAreKnown,
        "F3 knows unlocked vs locked overlays from original overlays store");
    Check(snapshot.Control("audio").Presence == PreviewLayerPresence.Unknown &&
          snapshot.Control("nextTimer").Presence == PreviewLayerPresence.Unknown,
        "Audio/timers unknown until their runtime stores are connected");
    Check(snapshot.RecommendedCategory == "overlays",
        "Preview active-section precedence respects overlays before lyric and background");
    Check(snapshot.Control("all").SourceCanClear == false,
        "Original Clear all remains gated until complete audio/cache/timer controller");

    var nav = new FreeShowPreviewCategoryNavigation();
    nav.Update(snapshot);
    Check(nav.SelectedCategory == "overlays" && nav.FollowSourceAutomatically,
        "Native preview initially follows original recommended category");
    nav.Select("slide");
    Check(nav.SelectedCategory == "slide" && !nav.FollowSourceAutomatically,
        "Operator preview expansion selects slide section without affecting output");
    nav.Update(snapshot);
    Check(nav.SelectedCategory == "slide",
        "Manual preview category stays selected while content exists");

    var lackingLocks = FreeShowPreviewControlState.Prepare(outputConfiguration);
    Check(lackingLocks.Control("overlays").Presence == PreviewLayerPresence.Unknown &&
          !lackingLocks.Control("overlays").SourceCanClear,
        "Missing overlay lock data never allows unsafe overlay clear");

    using var blockedSource = JsonDocument.Parse("""
{
 "outLocked":true,
 "outputs":{"main-1":{
  "name":"Primary","enabled":true,"active":true,
  "bounds":{"x":0,"y":0,"width":1920,"height":1080},
  "out":{"background":{"path":"background-video.mp4"},"slide":{"id":"original-show-id"}}}}
}
""");
    var blocked = FreeShowPreviewControlState.Prepare(
        FreeShowOutputConfiguration.Parse(blockedSource.RootElement), lockedOverlays.RootElement);
    Check(blocked.OutputLocked &&
          !blocked.Control("slide").SourceCanClear &&
          !blocked.Control("background").SourceCanClear,
        "FreeShow outLocked prevents direct preview clear commands");
}

// Native output controller: original IDs, complete-effect preflight and
// state changes ONLY after a complete runtime acknowledges the command.
using (var controlSettings = JsonDocument.Parse("""
{
 "outLocked":false,
 "outputs":{
  "screen1":{"name":"Primary","enabled":true,"active":true,
   "bounds":{"x":0,"y":0,"width":1920,"height":1080},
   "out":{"background":{"path":"song-background.mp4"},
     "slide":{"id":"original-show-id","layout":"main","index":1,"line":0},
     "overlays":["pinned","temporary"],"effects":["fade"],"scene":{"id":"scene1"}}},
  "screen2":{"name":"Online","enabled":true,"active":true,
   "bounds":{"x":1920,"y":0,"width":1920,"height":1080},
   "out":{"background":{"path":"song-background.mp4"},
     "slide":{"id":"original-show-id","layout":"main","index":1},
     "overlays":[],"effects":[]}}
 }
}
"""))
using (var originalOverlays = JsonDocument.Parse("""
{"pinned":{"name":"Persistent","locked":true},
 "temporary":{"name":"Lyrics Banner","locked":false}}
"""))
{
    var config = FreeShowOutputConfiguration.Parse(controlSettings.RootElement);
    var controller = new FreeShowOutputCommandCoordinator(
        config, originalOverlays.RootElement);

    var clearSlide = FreeShowOutputCommandPlanner.Prepare(
        new FreeShowShortcutAction(NativeShortcutCommand.ClearSlide, "screen1"), config);
    Check(clearSlide.CanPlan &&
          clearSlide.RequiredEffects.SequenceEqual([
              FreeShowSourceEffect.CaptureLastSlidePositionIfEligible,
              FreeShowSourceEffect.CancelNextSlideTimers,
              FreeShowSourceEffect.ClearSlideOutput,
              FreeShowSourceEffect.PropagateOutputChanged,
              FreeShowSourceEffect.NotifySlideCleared,
              FreeShowSourceEffect.RecordTimelineClearSlide]),
          "Original F2 effect order caches position, cancels timer, clears lyrics and notifies actions");
    Check(clearSlide.SourceAction.OutputId == "screen1",
        "F2 source output identity is preserved");

    var denied = await controller.DispatchPreparedAsync([clearSlide]);
    Check(denied.Status == FreeShowCommandResultStatus.Refused &&
          controller.States["screen1"].HasSlide,
        "Missing live original runtime refuses F2 and preserves state");

    var fake1 = new RecordingOutputCommandRuntime("screen1", Enum.GetValues<FreeShowSourceEffect>());
    controller.RegisterSourceCompleteRuntime(fake1);
    var fake2 = new RecordingOutputCommandRuntime("screen2",
        [FreeShowSourceEffect.ClearSlideOutput]);
    controller.RegisterSourceCompleteRuntime(fake2);

    var firstScreenClear = FreeShowOutputCommandPlanner.Prepare(
        new FreeShowShortcutAction(NativeShortcutCommand.ClearBackground, "screen1"),
        config);
    var secondScreenClear = FreeShowOutputCommandPlanner.Prepare(
        new FreeShowShortcutAction(NativeShortcutCommand.ClearBackground, "screen2"),
        config);
    var batchDenied = await controller.DispatchPreparedAsync(
        [firstScreenClear, secondScreenClear]);
    Check(batchDenied.Status == FreeShowCommandResultStatus.Refused &&
          fake1.Executed.Count == 0 &&
          controller.States["screen1"].HasActiveBackground,
        "Multi-output batch rejects missing capability BEFORE changing any screen");

    var f2Ok = await controller.DispatchPreparedAsync([clearSlide]);
    Check(f2Ok.Succeeded && f2Ok.ExecutedPlans == 1 &&
          fake1.Executed.SequenceEqual(clearSlide.RequiredEffects) &&
          !controller.States["screen1"].HasSlide &&
          controller.States["screen1"].HasActiveBackground,
        "Acknowledged F2 clears only lyrics and preserves separately playing background");

    var livePreview = FreeShowPreviewControlState.Prepare(
        config, originalOverlays.RootElement, currentNativeStates:controller.States);
    Check(livePreview.Control("slide").Presence == PreviewLayerPresence.Active &&
          livePreview.Control("background").Presence == PreviewLayerPresence.Active,
        "Preview sees other active output still displaying lyrics and background");

    fake2.AddCapabilities([
        FreeShowSourceEffect.CaptureLastSlidePositionIfEligible,
        FreeShowSourceEffect.CancelNextSlideTimers,
        FreeShowSourceEffect.PropagateOutputChanged,
        FreeShowSourceEffect.NotifySlideCleared,
        FreeShowSourceEffect.RecordTimelineClearSlide]);
    var screen2Slide = FreeShowOutputCommandPlanner.Prepare(
        new FreeShowShortcutAction(NativeShortcutCommand.ClearSlide, "screen2"), config);
    Check((await controller.DispatchPreparedAsync([screen2Slide])).Succeeded &&
          !controller.States["screen2"].HasSlide,
        "Second screen clear only affects its own Native OutData state");

    livePreview = FreeShowPreviewControlState.Prepare(
        config, originalOverlays.RootElement, currentNativeStates:controller.States);
    Check(livePreview.Control("slide").Presence == PreviewLayerPresence.Cleared &&
          livePreview.Control("background").Presence == PreviewLayerPresence.Active,
        "Preview reads committed in-memory output state, not stale source SETTINGS");

    var f3 = FreeShowOutputCommandPlanner.Prepare(
        new FreeShowShortcutAction(NativeShortcutCommand.ClearOverlays, "screen1"),
        config, originalOverlays.RootElement);
    Check(f3.CanPlan && f3.LockedOverlayIds.SequenceEqual(["pinned"]),
        "Preview clear preflight retains the original locked overlay metadata");
    var f3Denied = FreeShowOutputCommandPlanner.Prepare(
        new FreeShowShortcutAction(NativeShortcutCommand.ClearOverlays, "screen1"),
        config);
    Check(!f3Denied.CanPlan && f3Denied.Warnings.Count > 0 &&
          controller.States["screen1"].Overlays.Count == 2,
        "Preview clear refuses missing locked overlay definitions");
    Check((await controller.DispatchPreparedAsync([f3])).Succeeded &&
          controller.States["screen1"].Overlays.SequenceEqual(["pinned"]) &&
          controller.States["screen1"].Effects.Count == 0,
        "Preview clear preserves pinned overlay, clears unlocked overlay and effects");

    var shortcutClearOverlays = FreeShowOutputCommandPlanner.Prepare(
        new FreeShowShortcutAction(
            NativeShortcutCommand.ClearAllOverlaysForShortcut, "screen1"), config);
    var shortcutClearEffects = FreeShowOutputCommandPlanner.Prepare(
        new FreeShowShortcutAction(NativeShortcutCommand.ClearEffects, "screen1"), config);
    Check(shortcutClearOverlays.CanPlan &&
          shortcutClearOverlays.RequiredEffects.SequenceEqual([
              FreeShowSourceEffect.ClearAllOverlays]) &&
          shortcutClearEffects.RequiredEffects.Contains(
              FreeShowSourceEffect.RecordTimelineClearOverlays),
        "Keyboard F3 is source-distinct from locked-overlay-preserving Preview clear");
    Check((await controller.DispatchPreparedAsync(
              [shortcutClearOverlays, shortcutClearEffects])).Succeeded &&
          controller.States["screen1"].Overlays.Count == 0,
        "Keyboard F3 clears even originally locked overlays as shortcuts.ts specifies");

    var previewButtonPlans = controller.PlanPreviewClearAction("overlays");
    Check(previewButtonPlans.Count == 2 &&
          previewButtonPlans.All(p => p.SourceAction.Command ==
              NativeShortcutCommand.ClearOverlays),
        "Original Preview overlay button is distinct from keyboard F3 clear-all overlays");
    Check(controller.CheckReadiness(previewButtonPlans).Count > 0,
        "Preview action remains blocked if any active output lacks complete overlay backend");
    Check(controller.PlanPreviewClearAction("all").Count == 0 &&
          controller.PlanPreviewClearAction("nextTimer").Count == 0,
        "Unported Clear All and timer actions have no fake native output plans");

    var untouchedController = new FreeShowOutputCommandCoordinator(
        config, originalOverlays.RootElement);
    var hookedCache = new FreeShowOutputSlidePositionCache();
    var hooks = FreeShowSlideCacheSourceEffects.Create(
        hookedCache, untouchedController,
        id => id == show.ShowId ? show : null, () => false);
    var originalF2 = untouchedController.PlanPreviewClearAction("slide")[0];
    await hooks[FreeShowSourceEffect.CaptureLastSlidePositionIfEligible](
        originalF2, CancellationToken.None);
    Check(hookedCache.Get("screen1").HasValue &&
          ShowDocument.String(hookedCache.Get("screen1")!.Value, "id") == show.ShowId,
        "F2 source-effect hook captures actual original OutSlide before it is cleared");
    await hooks[FreeShowSourceEffect.ClearSlideCache](originalF2, CancellationToken.None);
    Check(hookedCache.Get("screen1") == null,
        "F2 double-press source-effect hook clears the shared slide cache");

    var globalAudio = FreeShowOutputCommandPlanner.Prepare(
        new FreeShowShortcutAction(NativeShortcutCommand.ClearAudioAndPlaylist, ""), config);
    Check(globalAudio.CanPlan &&
          (await controller.DispatchPreparedAsync([globalAudio])).Status ==
              FreeShowCommandResultStatus.Refused,
        "F4 remains blocked until a source-complete global audio backend is registered");

    var cache = new FreeShowOutputSlidePositionCache();
    using var positioned = JsonDocument.Parse("""
{"outputs":{"screen1":{"name":"Primary","enabled":true,"active":true,
"bounds":{"width":1920,"height":1080},
"out":{"slide":{"id":"original-show-id","layout":"main","index":1,
"line":0,"unknownOriginal":"retained"}}}}}
""");
    var positionState = new FreeShowOutputState(
        FreeShowOutputConfiguration.Parse(positioned.RootElement).Find("screen1")!);
    Check(cache.TryCapture(positionState, show, focusMode:false) &&
          ShowDocument.String(cache.Get("screen1")!.Value, "unknownOriginal") == "retained",
        "F2 slide cache retains original OutSlide fields without rewriting .show");
    using var lastSlideState = JsonDocument.Parse("""
{"outputs":{"screen1":{"name":"Primary","enabled":true,"active":true,
"bounds":{"width":1920,"height":1080},
"out":{"slide":{"id":"original-show-id","layout":"main","index":3}}}}
""");
    var last = new FreeShowOutputState(
        FreeShowOutputConfiguration.Parse(lastSlideState.RootElement).Find("screen1")!);
    Check(!cache.TryCapture(last, show, focusMode:false) &&
          cache.TryCapture(last, show, focusMode:true),
        "Original FreeShow excludes final slide unless focus mode preserves it");
    cache.Clear();
    Check(cache.Snapshot().Count == 0, "Double F2 erases cached original output positions");
}

// 0.0.16 — original FreeShow outputCache, restoreOutput and clearTimers.
// Synthetic only: never touches a real FreeShow settings/store or output.
using (var restoreSettings = JsonDocument.Parse("""
{
  "outLocked": false,
  "outputs": {
    "display-a": {"name":"Main","enabled":true,"active":true,
      "bounds":{"width":1920,"height":1080},
      "out":{"slide":{"id":"original-show-id","layout":"main","index":1},
             "background":{"path":"loop.mp4"},"overlays":[],
             "effects":[],"unknownNativeMustKeep":{"original":"yes"}}},
    "display-b": {"name":"Online","enabled":true,"active":true,
      "bounds":{"width":1920,"height":1080},
      "out":{"slide":{"id":"original-show-id","layout":"main","index":0},
             "background":null,"effects":[],"overlays":[]}},
    "disabled-display": {"name":"Disabled","enabled":false,"active":false,
      "bounds":{"width":1920,"height":1080},
      "out":{"slide":{"id":"old","index":2}}}
  }
}
"""))
using (var originalAudio = JsonDocument.Parse("""
[{"path":"file.mp3","metadata":{"name":"Prelude"},"options":{"startAt":12.3}}]
"""))
{
    var outputConfig = FreeShowOutputConfiguration.Parse(restoreSettings.RootElement);
    var coordinator = new FreeShowOutputCommandCoordinator(outputConfig);
    var ids = outputConfig.GetActiveNormalOutputs().Select(x => x.Id).ToArray();
    var restoreCache = new FreeShowOutputRestoreCache();

    restoreCache.Capture(ids, coordinator.States);
    Check(restoreCache.HasSnapshot && !restoreCache.HasCompleteAudioSnapshot &&
        !restoreCache.PlanRestore(ids, coordinator.States).IsSourceComplete,
        "Unknown original global audio prevents fake complete Restore");

    restoreCache.Capture(ids, coordinator.States, originalAudio.RootElement, false);
    var baseline = restoreCache.PlanRestore(ids, coordinator.States);
    Check(baseline.IsSourceComplete && baseline.OutputSnapshots.Count == 2 &&
          baseline.PlayingAudioData.GetArrayLength() == 1 &&
          baseline.PlayingMetronome == false,
        "Clear All stores full active output data plus original audio and metronome state");

    var clearA = new FreeShowShortcutAction(NativeShortcutCommand.ClearSlide, "display-a");
    var clearB = new FreeShowShortcutAction(NativeShortcutCommand.ClearSlide, "display-b");
    Check(coordinator.States["display-a"].TryApply(clearA) &&
          coordinator.States["display-b"].TryApply(clearB),
        "Synthetic outputs can be independently cleared in memory");
    Check(restoreCache.PlanRestore(ids, coordinator.States).IsSourceComplete,
        "Output cache survives visual clear and retains original slide data");

    var noAudio = new RecordingOutputRestoreRuntime(["display-a","display-b"], audioAvailable:false);
    var blockedRestore = await coordinator.RestoreAsync(restoreCache, noAudio);
    Check(blockedRestore.Status == FreeShowRestoreStatus.Refused &&
          restoreCache.HasSnapshot &&
          !coordinator.States["display-a"].HasSlide,
        "Missing full audio restore backend blocks all physical output changes");

    var partialRestore = new RecordingOutputRestoreRuntime(["display-a","display-b"], true)
    {
        FailForOutput = "display-b"
    };
    var partialResult = await coordinator.RestoreAsync(restoreCache, partialRestore);
    Check(partialResult.Status == FreeShowRestoreStatus.PartialExecution &&
          partialResult.OutputsRestored == 1 &&
          coordinator.States["display-a"].HasSlide &&
          !coordinator.States["display-b"].HasSlide &&
          restoreCache.HasSnapshot,
        "Partial restore reports failure and preserves backup for reconciliation");

    var fullRestore = new RecordingOutputRestoreRuntime(["display-a","display-b"], true);
    var restored = await coordinator.RestoreAsync(restoreCache, fullRestore);
    Check(restored.Succeeded && restored.OutputsRestored == 2 &&
          fullRestore.RestoredAudio && fullRestore.RestoredMetronome == false &&
          !restoreCache.HasSnapshot && coordinator.States["display-b"].HasSlide,
        "Acknowledged full restore returns both outputs and global playback then clears cache");
    Check(ShowDocument.Property(coordinator.States["display-a"].Snapshot(),
        "unknownNativeMustKeep").ValueKind == JsonValueKind.Object,
        "Restore retains original unknown OutData fields without rewriting show or settings");

    long revision = coordinator.States["display-a"].Revision;
    var snapshot = coordinator.States["display-a"].Snapshot();
    Check(!await coordinator.SynchronizeAcknowledgedOutputAsync(
            "display-a", snapshot, revision - 1),
        "Late external output event cannot overwrite newer acknowledged state");
    Check(await coordinator.SynchronizeAcknowledgedOutputAsync(
            "display-a", snapshot, revision) &&
          coordinator.States["display-a"].Revision == revision + 1,
        "Matching source revision synchronizes the original complete OutData");

    var restoredAgain = restoreCache.PlanRestore(ids, coordinator.States);
    Check(!restoredAgain.IsSourceComplete,
        "Restore is single-use after complete acknowledgement");

    // Commands prepared against one OutData revision cannot silently run
    // after another controller changes that output.
    var stalePlan = coordinator.PlanPreviewClearAction("background")[0];
    Check(stalePlan.ExpectedNativeRevision.HasValue &&
          coordinator.CheckReadiness([stalePlan]).Count > 0,
        "Prepared action without a full runtime is not executable");
    coordinator.States["display-a"].TryApply(
        new FreeShowShortcutAction(NativeShortcutCommand.ClearBackground, "display-a"));
    Check(coordinator.CheckReadiness([stalePlan]).Any(
          x => x.Contains("changed since action preparation")),
        "Outdated operator command is rejected after newer source state revision");
    Check((await coordinator.DispatchPreparedAsync(
        Array.Empty<FreeShowOutputCommandPlan>())).Status ==
            FreeShowCommandResultStatus.Refused,
        "Empty or unported command batch cannot falsely report completed");

    var zeroAudio = JsonDocument.Parse("[]");
    restoreCache.Capture(ids, coordinator.States, zeroAudio.RootElement, false);
    using var fullyCleared = JsonDocument.Parse("""
{"outLocked":false,"outputs":{"display-a":{"name":"Main","enabled":true,"active":true,
"bounds":{"width":1920,"height":1080},
"out":{"slide":null,"background":null,"overlays":[],"effects":[]}}}}
""");
    var preview = FreeShowPreviewControlState.Prepare(
        FreeShowOutputConfiguration.Parse(fullyCleared.RootElement),
        runtime:new FreeShowAuxiliaryPreviewState(
            PreviewLayerPresence.Cleared, PreviewLayerPresence.Cleared));
    var savedAt = DateTimeOffset.UnixEpoch;
    Check(!FreeShowRestoreEligibility.CanOffer(
            restoreCache, preview, savedAt, savedAt.AddMilliseconds(999)) &&
          FreeShowRestoreEligibility.CanOffer(
            restoreCache, preview, savedAt, savedAt.AddMilliseconds(1000)),
        "Original ClearButtons Restore waits 1000 ms after a complete clear");
    var unknownAudioPreview = FreeShowPreviewControlState.Prepare(
        FreeShowOutputConfiguration.Parse(fullyCleared.RootElement));
    Check(!FreeShowRestoreEligibility.CanOffer(
            restoreCache, unknownAudioPreview, savedAt, savedAt.AddSeconds(2)),
        "Original global audio playback unknown cannot be treated as cleared");
    zeroAudio.Dispose();
}

{
    var timers = new FreeShowOutputTimerRegistry();
    var slideA = new RecordingSourceTimer();
    var overlayA = new RecordingSourceTimer();
    var slideB = new RecordingSourceTimer();
    timers.RegisterSlide("display-a", slideA);
    timers.RegisterOverlay("display-a", "announcement", overlayA);
    timers.RegisterSlide("display-b", slideB);
    bool preventedReplacement = false;
    try { timers.RegisterSlide("display-a", new RecordingSourceTimer()); }
    catch (InvalidOperationException) { preventedReplacement = true; }
    Check(preventedReplacement && timers.SlideTimerCount == 2,
        "Native timer registry cannot silently orphan a live slide timer handle");
    var f2Only = await timers.CancelAsync(["display-a"], cancelOverlayTimers:false);
    Check(f2Only.Succeeded && f2Only.CancelledSlideTimers == 1 &&
          f2Only.CancelledOverlayTimers == 0 &&
          slideA.Cancelled && !overlayA.Cancelled && !slideB.Cancelled,
        "F2 clearTimers('', false) cancels next slide timer but keeps overlays and other displays");
    var fullTimerClear = await timers.CancelAsync(["display-a","display-b"], cancelOverlayTimers:true);
    Check(fullTimerClear.Succeeded && fullTimerClear.CancelledSlideTimers == 1 &&
          fullTimerClear.CancelledOverlayTimers == 1 &&
          overlayA.Cancelled && slideB.Cancelled &&
          timers.SlideTimerCount == 0 && timers.OverlayTimerCount == 0,
        "Clear All cancels independent source slide and overlay timers for active outputs");
    var failingTimer = new RecordingSourceTimer { FailToCancel = true };
    timers.RegisterSlide("display-a", failingTimer);
    var failure = await timers.CancelAsync(["display-a"], cancelOverlayTimers:false);
    Check(!failure.Succeeded && timers.SlideTimerCount == 1 && !failingTimer.Cancelled,
        "Failed physical timer cancellation stays registered for explicit retry");
}

// 0.0.17 — FreeShow original clearAll(button): source-ordered execution
// with all-capability gate, full cache, locked overlays, focus-mode slide
// positions and partial failure reporting. These are synthetic fixtures.
using (var originalDisplays = JsonDocument.Parse("""
{
 "outLocked":false,
 "outputs":{
   "main-id":{"name":"Main","enabled":true,"active":true,
     "bounds":{"width":1920,"height":1080},
     "out":{"slide":{"id":"original-show-id","layout":"main","index":1,"line":0},
       "background":{"path":"loop.mp4"},"overlays":["pinned","temporary"],
       "effects":["glow"],"transition":{"next":500},
       "scene":{"id":"stage-scene"},"refresh":2}},
   "online-id":{"name":"Online","enabled":true,"active":true,
     "bounds":{"width":1920,"height":1080},
     "out":{"slide":{"id":"original-show-id","layout":"main","index":0},
       "background":{"path":"secondary.png"},"overlays":[],
       "effects":[],"transition":{"next":250}}}
 }
}
"""))
using (var overlayDefinitions = JsonDocument.Parse("""
{"pinned":{"locked":true},"temporary":{"locked":false}}
"""))
using (var liveAudio = JsonDocument.Parse("""
[{"path":"music.mp3","metadata":{"name":"Original"},"options":{"startAt":15.25}}]
"""))
{
    var sourceConfig = FreeShowOutputConfiguration.Parse(originalDisplays.RootElement);
    var sourceClear = new FreeShowOutputCommandCoordinator(
        sourceConfig, overlayDefinitions.RootElement);
    var context = new FreeShowClearAllContext(
        FromButton:true, FocusMode:false, liveAudio.RootElement, true);
    var restoreCache = new FreeShowOutputRestoreCache();
    var slideCache = new FreeShowOutputSlidePositionCache();
    var sourcePlan = sourceClear.PlanClearAll(context);

    Check(sourcePlan.IsReady && !sourcePlan.AlreadyCleared &&
          sourcePlan.ActiveOutputIds.SequenceEqual(["main-id","online-id"]) &&
          sourcePlan.LockedOverlayIds["main-id"].SetEquals(["pinned"]),
        "Source Clear All plans original output IDs and locked overlay metadata");
    Check(FreeShowClearAllSteps.ExecuteInOriginalOrder.SequenceEqual([
          FreeShowClearAllStep.StopTimelinePlayback,
          FreeShowClearAllStep.ClearBackgrounds,
          FreeShowClearAllStep.ClearNextSlideTimers,
          FreeShowClearAllStep.ClearSlides,
          FreeShowClearAllStep.ClearOverlaysAndEffects,
          FreeShowClearAllStep.ClearGlobalAudio,
          FreeShowClearAllStep.ClearAllRemainingTimers]),
        "Source timeline/background/F2/overlays/audio/final-timers order preserved");

    var blocked = await sourceClear.ClearAllAsync(
        context, restoreCache, slideCache, runtime:null);
    Check(blocked.Status == FreeShowClearAllStatus.Refused &&
          !restoreCache.HasSnapshot &&
          sourceClear.States["main-id"].HasSlide &&
          sourceClear.States["main-id"].HasActiveBackground,
        "Without full physical/audio backend Clear All refuses before changing output");

    var limited = new RecordingSourceClearAllRuntime(
        Enum.GetValues<FreeShowClearAllStep>().Where(
            x => x != FreeShowClearAllStep.ClearGlobalAudio));
    Check(sourceClear.CheckClearAllReadiness(sourcePlan, limited).Any(
            x => x.Contains("ClearGlobalAudio")) &&
          (await sourceClear.ClearAllAsync(
            context, restoreCache, slideCache, limited)).Status ==
                FreeShowClearAllStatus.Refused,
        "Missing just global audio backend rejects entire multi-output clear");

    var unsafeContext = context with {
        FromButton = false, PopupActive = true
    };
    Check(!sourceClear.PlanClearAll(unsafeContext).IsReady &&
          (await sourceClear.ClearAllAsync(
            unsafeContext, restoreCache, slideCache,
            new RecordingSourceClearAllRuntime(
                Enum.GetValues<FreeShowClearAllStep>()))).Status ==
                FreeShowClearAllStatus.Refused,
        "Escape-origin Clear All obeys original active popup/selection guard");
    Check(sourceClear.PlanClearAll(
        unsafeContext with { FromButton = true }).IsReady,
        "Source explicit Clear All button may bypass Escape UI selection guard");

    var noOverlayInfo = new FreeShowOutputCommandCoordinator(sourceConfig);
    Check(!noOverlayInfo.PlanClearAll(context).IsReady,
        "Unknown locked-overlay definitions cannot trigger destructive Clear All");

    var partial = new RecordingSourceClearAllRuntime(
        Enum.GetValues<FreeShowClearAllStep>())
    {
        FailAtStep = FreeShowClearAllStep.ClearBackgrounds
    };
    var partialResult = await sourceClear.ClearAllAsync(
        context, restoreCache, slideCache, partial);
    Check(partialResult.Status == FreeShowClearAllStatus.PartialExecution &&
          partialResult.CompletedSteps == 1 &&
          restoreCache.HasSnapshot &&
          sourceClear.States["main-id"].HasActiveBackground,
        "Partial runtime Clear All preserves full output/audio recovery snapshot");
    Check((await sourceClear.ClearAllAsync(context, restoreCache, slideCache,
          new RecordingSourceClearAllRuntime(
              Enum.GetValues<FreeShowClearAllStep>()))).Status ==
              FreeShowClearAllStatus.Refused,
        "Unreconciled Clear All cache cannot be overwritten by half-cleared state");

    var sourceClearSuccess = new FreeShowOutputCommandCoordinator(
        sourceConfig, overlayDefinitions.RootElement);
    var successCache = new FreeShowOutputRestoreCache();
    var successSlideCache = new FreeShowOutputSlidePositionCache();
    var runtime = new RecordingSourceClearAllRuntime(
        Enum.GetValues<FreeShowClearAllStep>());
    var result = await sourceClearSuccess.ClearAllAsync(
        context, successCache, successSlideCache, runtime);
    Check(result.Succeeded && result.CompletedSteps == 7 &&
          runtime.Executed.SequenceEqual(FreeShowClearAllSteps.ExecuteInOriginalOrder),
        "Original complete Clear All effects execute once and in order");
    Check(successCache.HasSnapshot &&
          successCache.PlanRestore(
            sourcePlan.ActiveOutputIds, sourceClearSuccess.States).IsSourceComplete &&
          ShowDocument.Property(
            successCache.PlanRestore(
              sourcePlan.ActiveOutputIds,
              sourceClearSuccess.States).OutputSnapshots["main-id"], "scene")
            .ValueKind == JsonValueKind.Object,
        "Entire original OutData and playback metadata saved before source clear");
    Check(!sourceClearSuccess.States["main-id"].HasSlide &&
          !sourceClearSuccess.States["main-id"].HasActiveBackground &&
          sourceClearSuccess.States["main-id"].Overlays.SequenceEqual(["pinned"]) &&
          sourceClearSuccess.States["main-id"].Effects.Count == 0 &&
          ShowDocument.Property(
            sourceClearSuccess.States["main-id"].Snapshot(), "transition")
            .ValueKind == JsonValueKind.Null &&
          ShowDocument.Property(
            sourceClearSuccess.States["main-id"].Snapshot(), "scene")
            .ValueKind == JsonValueKind.Object,
        "Clear All changes lyrics/background/transition/unlocked overlays but retains scene and pinned overlays");

    var freshPlan = sourceClearSuccess.PlanClearAll(context);
    Check(sourceClearSuccess.CheckClearAllReadiness(
          sourcePlan, runtime).Any(x => x.Contains("changed since")) &&
          freshPlan.AlreadyCleared == false,
        "Prior source Clear All plan becomes stale after acknowledged output changes");

    var focusController = new FreeShowOutputCommandCoordinator(
        sourceConfig, overlayDefinitions.RootElement);
    var focusCtx = context with { FocusMode = true };
    var focusCache = new FreeShowOutputRestoreCache();
    var focusSlides = new FreeShowOutputSlidePositionCache();
    var focusRuntime = new RecordingSourceClearAllRuntime(
        Enum.GetValues<FreeShowClearAllStep>());
    Check((await focusController.ClearAllAsync(
        focusCtx, focusCache, focusSlides, focusRuntime)).Status ==
            FreeShowClearAllStatus.Refused &&
          !focusCache.HasSnapshot && focusRuntime.Executed.Count == 0,
        "Focus mode refuses missing source Show lookup before touching live output");
    Check((await focusController.ClearAllAsync(
        focusCtx, focusCache, focusSlides, focusRuntime,
        id => id == show.ShowId ? show : null)).Succeeded &&
          focusSlides.Get("main-id").HasValue,
        "Focus mode retains eligible source slide cache after Clear All");

    using var alreadyEmpty = JsonDocument.Parse("""
{"outLocked":false,"outputs":{
  "main-id":{"name":"Main","enabled":true,"active":true,
    "bounds":{"width":1920,"height":1080},
    "out":{"slide":null,"background":null,"transition":null,
           "effects":[],"overlays":[],"scene":{"id":"leave-scene"}}}}}
""");
    using var audioEmpty = JsonDocument.Parse("[]");
    var alreadyConfig = FreeShowOutputConfiguration.Parse(alreadyEmpty.RootElement);
    var emptyCoordinator = new FreeShowOutputCommandCoordinator(alreadyConfig);
    var emptyCtx = context with {
        PlayingAudioData = audioEmpty.RootElement, PlayingMetronome = false
    };
    var emptyState = new FreeShowOutputSlidePositionCache();
    var emptyResult = await emptyCoordinator.ClearAllAsync(
        emptyCtx, new FreeShowOutputRestoreCache(), emptyState, runtime:null);
    Check(emptyResult.Status == FreeShowClearAllStatus.AlreadyCleared &&
          emptyResult.CompletedSteps == 0 &&
          emptyCoordinator.States["main-id"].Revision == 0,
        "FreeShow already-cleared output is a no-op except reset of slide cache");
}

// 0.0.18 — runtime-bound timer cancellation and background-video
// continuity. Core-only synthetic fixtures: NOT executed until Beta 2.
{
    var timers = new FreeShowOutputTimerRegistry();
    var slide1 = new RecordingSourceTimer();
    var slide2 = new RecordingSourceTimer();
    var overlay1 = new RecordingSourceTimer();
    timers.RegisterSlide("screen1", slide1);
    timers.RegisterSlide("screen2", slide2);
    timers.RegisterOverlay("screen1", "announcement", overlay1);

    var inventory = new RecordingFreeShowTimerInventory();
    inventory.Allow("screen1", false);
    var bridge = new FreeShowTimerSourceEffects(timers, inventory);
    Check(!bridge.CanCancel(["screen1","screen2"], false) &&
          !bridge.CanCancel(["screen1"], true),
        "No native timer is declared cleared without authoritative scheduler inventory");

    bool missingAuthorityRefused = false;
    try { await bridge.CancelAsync(["screen1","screen2"], false); }
    catch (NotSupportedException) { missingAuthorityRefused = true; }
    Check(missingAuthorityRefused && !slide1.Cancelled && !slide2.Cancelled &&
          !overlay1.Cancelled,
        "Incomplete multi-screen inventory prevents touching any registered timer");

    inventory.Allow("screen2", false);
    using var settingsForTimer = JsonDocument.Parse("""
{"outputs":{"screen1":{"name":"Main","enabled":true,"active":true,
"bounds":{"width":1920,"height":1080},"out":{"slide":{"id":"test"}}}}}
""");
    var configuration = FreeShowOutputConfiguration.Parse(settingsForTimer.RootElement);
    var f2Plan = FreeShowOutputCommandPlanner.Prepare(
        new FreeShowShortcutAction(NativeShortcutCommand.ClearSlide, "screen1"),
        configuration);
    var hooks = bridge.CreateSlideClearHooks("screen1");
    Check(hooks.ContainsKey(FreeShowSourceEffect.CancelNextSlideTimers),
        "Native source F2 effect is connected to registered real timer cancellation handle");
    await hooks[FreeShowSourceEffect.CancelNextSlideTimers](
        f2Plan, CancellationToken.None);
    Check(slide1.Cancelled && !slide2.Cancelled && !overlay1.Cancelled &&
          timers.SlideTimerCount == 1 && timers.OverlayTimerCount == 1,
        "F2 cancels exactly its output's next-slide timer without stopping overlay or another output");

    // Before the source overlay timer inventory is complete, even a
    // registered timer hook must NOT claim the final Clear All phase ready.
    var timerOnlyBefore = bridge.CreatePartialClearAllRuntime();
    using (var earlyAudio = JsonDocument.Parse("[]"))
    {
        var earlyConfig = FreeShowOutputConfiguration.Parse(settingsForTimer.RootElement);
        var earlyController = new FreeShowOutputCommandCoordinator(earlyConfig);
        var earlyPlan = earlyController.PlanClearAll(
            new FreeShowClearAllContext(true, false, earlyAudio.RootElement, false));
        Check(!timerOnlyBefore.CanExecuteSourceStep(
              FreeShowClearAllStep.ClearAllRemainingTimers, earlyPlan),
            "Per-plan source timer readiness rejects unknown overlay timer inventory");
    }
    inventory.Allow("screen1", true);
    inventory.Allow("screen2", true);
    var timerPhases = bridge.CreateClearAllTimerHooks();
    var partialClearAll = bridge.CreatePartialClearAllRuntime();
    Check(partialClearAll.SupportsCompleteSourceStep(
          FreeShowClearAllStep.ClearNextSlideTimers) &&
          partialClearAll.SupportsCompleteSourceStep(
          FreeShowClearAllStep.ClearAllRemainingTimers) &&
          !partialClearAll.SupportsCompleteSourceStep(
          FreeShowClearAllStep.ClearGlobalAudio) &&
          !partialClearAll.SupportsCompleteSourceStep(
          FreeShowClearAllStep.ClearBackgrounds),
        "Registering timer hooks never falsely enables audio or media Clear All stages");

    using var originalTimerAudio = JsonDocument.Parse("[]");
    var planContext = new FreeShowClearAllContext(
        true, false, originalTimerAudio.RootElement, false);
    var sourceForTimer = new FreeShowOutputCommandCoordinator(configuration);
    // Manually supply original IDs to exercise the full timer phase factory;
    // the actual Clear All plan comes from a real source configuration.
    var clearPlan = sourceForTimer.PlanClearAll(planContext);
    Check(clearPlan.IsReady, "Synthetic Clear All has a recognized output and authoritative audio state");
    Check(partialClearAll.CanExecuteSourceStep(
          FreeShowClearAllStep.ClearAllRemainingTimers, clearPlan) &&
          !partialClearAll.CanExecuteSourceStep(
          FreeShowClearAllStep.ClearGlobalAudio, clearPlan),
        "Source timer-only runtime exposes no fake media/audio phase");
    await partialClearAll.ExecuteCompleteSourceStepAsync(
        FreeShowClearAllStep.ClearAllRemainingTimers, clearPlan);
    Check(overlay1.Cancelled && timers.SlideTimerCount == 1 &&
          timers.OverlayTimerCount == 0,
        "Final Clear All timer phase cancels overlay timer without affecting an unrelated output");
    Check((await bridge.CancelAsync(["screen2"], false)).CancelledSlideTimers == 1 &&
          slide2.Cancelled, "Second output timer can be cancelled independently");
}

{
    var outputVideo = new FreeShowMediaReference(
        "loop-bg", "loop-bg.mp4", NativeMediaKind.Video,
        true, true, default);
    var layer = new NativeVideoLayer("output-background", outputVideo);
    var cue = new NativeVideoCue(0, "verse", [layer], layer, []);
    var ready = FreeShowBackgroundContinuity.Plan(
        cue, "loop-bg", "loop-bg", media => "/copied/" + media.Path);
    Check(ready.CanApply &&
          ready.Decision == NativeBackgroundClearDecision.StopCurrentOutputVideo,
        "Source F1 can stop only the exact, already playing output video");
    Check(!FreeShowBackgroundContinuity.Plan(
        cue, "loop-bg", "different-player", media => "/copied/" + media.Path).CanApply,
        "Background clear never disposes an unowned persistent video decoder");
    Check(!FreeShowBackgroundContinuity.Plan(
        cue, "stale-output", "loop-bg", media => "/copied/" + media.Path).CanApply,
        "Stale source background IDs refuse media clear instead of changing wrong output");

    var styleVideo = new FreeShowMediaReference(
        "style", "style.mp4", NativeMediaKind.Video, true, true, default);
    var styleLayer = new NativeVideoLayer("style-background", styleVideo);
    var styleOnly = new NativeVideoCue(0, "verse", [styleLayer], styleLayer, []);
    var leaveStyle = FreeShowBackgroundContinuity.Plan(
        styleOnly, "", "style", media => "/copied/" + media.Path);
    Check(leaveStyle.CanApply &&
          leaveStyle.Decision == NativeBackgroundClearDecision.NoChange,
        "F1 on empty active background must not restart or stop independent style video");

    var twoVideos = new NativeVideoCue(
        0, "verse", [styleLayer, layer], null, []);
    Check(!FreeShowBackgroundContinuity.Plan(
        twoVideos, "loop-bg", "loop-bg", media => "/copied/" + media.Path).CanApply,
        "Single-decoder native output rejects simultaneous style and active video layers");

    var imageLayer = new NativeVideoLayer("output-background",
        new FreeShowMediaReference(
            "image-bg", "image-bg.png", NativeMediaKind.Image, false, false, default));
    var imageCue = new NativeVideoCue(0, "verse", [imageLayer], null, []);
    Check(!FreeShowBackgroundContinuity.Plan(
        imageCue, "image-bg", "", media => null).CanApply,
        "Incomplete still-background compositor cannot claim source F1 media parity");
    var unsupportedOptions = new FreeShowMediaReference(
        "video-no-loop", "one-shot.mp4", NativeMediaKind.Video,
        Loop:false, Muted:true, default);
    var optionsCue = new NativeVideoCue(0, "verse",
        [new NativeVideoLayer("output-background", unsupportedOptions)], null, []);
    Check(!FreeShowBackgroundContinuity.Plan(
        optionsCue, "video-no-loop", "video-no-loop", media => "/copied/one-shot.mp4").CanApply,
        "Fixed-loop prototype cannot falsely implement original one-shot video playback");
}

// 0.0.19 — Original OutputHelper.ts keyboard navigation source fixtures.
// These checks are deliberately authored but NOT executed until Beta 2.
using (var navShowJson = JsonDocument.Parse("""
["navigation-show-id",{
 "name":"Fixture for upstream FreeShow next/previous",
 "settings":{"activeLayout":"main"},
 "slides":{
   "verse":{"items":[{"type":"text","clickReveal":true,"lineReveal":true,
       "lines":[{"text":[{"value":"A"}]},{"text":[{"value":"B"}]},{"text":[{"value":"C"}]}]}]},
   "second":{"items":[{"type":"text","lines":[{"text":[{"value":"Next"}]}]}]},
   "disabled":{"items":[{"type":"text","lines":[{"text":[{"value":"Skip"}]}]}]},
   "last":{"items":[{"type":"text","lines":[{"text":[{"value":"Final"}]}]}]}
 },
 "layouts":{
   "main":{"slides":[
      {"id":"verse"},{"id":"second","bindings":["screen2"]},
      {"id":"disabled","disabled":true},{"id":"last"}]},
   "loop":{"slides":[
      {"id":"verse"},{"id":"last","end":true}]}
 }
}]
"""))
{
    var source = ShowDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
        navShowJson.RootElement.GetRawText()));
    // 0.0.20 — original output.getOutputLines and TextboxLines projection.
    // No Avalonia controls or decoder are executed here.
    var sourceFrame = NativeRenderPlanner.Prepare(source, "main", includeDisabled:true)
        .First(x => x.Source.LayoutIndex == 0);
    using (var initiallyHidden = JsonDocument.Parse("""
{"id":"navigation-show-id","layout":"main","index":0,
 "line":0,"revealCount":0,"itemClickReveal":false}
"""))
    using (var firstRevealed = JsonDocument.Parse("""
{"id":"navigation-show-id","layout":"main","index":0,
 "line":0,"revealCount":1,"itemClickReveal":true}
"""))
    using (var secondRevealed = JsonDocument.Parse("""
{"id":"navigation-show-id","layout":"main","index":0,
 "line":1,"revealCount":2,"itemClickReveal":true}
"""))
    {
        var style = new FreeShowRevealStyleContext(
            StyleLines:1, FewestActiveStyleLines:1, HighestActiveLinePosition:0);
        var hidden = FreeShowRevealRenderProjection.Prepare(
            sourceFrame, initiallyHidden.RootElement, style);
        Check(hidden.IsSupported && hidden.Visible.TextItems.Count == 0,
            "Original clickReveal initially hides its text item without clearing the slide background");

        // 0.0.23 — a visual template which removes original reveal flags
        // cannot silently expose the currently hidden source lyric.
        using (var changedTemplateFlags = JsonDocument.Parse("""
{"type":"text","clickReveal":false,"lineReveal":false}
"""))
        {
            var changedSource = sourceFrame.Source with
            {
                Items = [
                    sourceFrame.Source.Items[0] with
                    { Raw = changedTemplateFlags.RootElement.Clone() }
                ]
            };
            var inconsistent = sourceFrame with { Source = changedSource };
            var blockedReveal = FreeShowRevealRenderProjection.Prepare(
                inconsistent, initiallyHidden.RootElement, style);
            Check(!blockedReveal.IsSupported &&
                  blockedReveal.Unported.Any(x => x.Contains("template")),
                "Template-changed original click/line flags cannot reveal hidden source lyrics");
        }

        var firstVisible = FreeShowRevealRenderProjection.Prepare(
            sourceFrame, firstRevealed.RootElement, style);
        Check(firstVisible.IsSupported &&
              firstVisible.Visible.TextItems.Count == 1 &&
              firstVisible.Visible.TextItems[0].Source.Lines.Count == 1 &&
              firstVisible.Visible.TextItems[0].Source.Lines[0].Text == "A" &&
              firstVisible.Window.RevealStart == 0 &&
              firstVisible.Window.RevealEnd == 1,
            "First clicked and line-revealed original lyric includes only first visible text line");

        var secondVisible = FreeShowRevealRenderProjection.Prepare(
            sourceFrame, secondRevealed.RootElement, style);
        Check(secondVisible.IsSupported &&
              secondVisible.Visible.TextItems.Count == 1 &&
              secondVisible.Visible.TextItems[0].Source.Lines[0].Text == "B" &&
              secondVisible.Visible.TextItems[0].Source.Lines.Count == 1,
            "Increasing original revealCount advances the one-line window without duplicating text");

        using (var sourceAtEnd = JsonDocument.Parse("""
{"id":"navigation-show-id","layout":"main","index":0,
 "line":2,"revealCount":3,"itemClickReveal":true}
"""))
        {
            var fullItem = sourceFrame.TextItems[0];
            var shortItem = fullItem with
            {
                Source = fullItem.Source with
                {
                    Lines = fullItem.Source.Lines.Take(1).ToArray()
                }
            };
            var mixedLengths = sourceFrame with
            {
                TextItems = new[] { fullItem, shortItem }
            };
            var finalWindow = FreeShowRevealRenderProjection.Prepare(
                mixedLengths, sourceAtEnd.RootElement,
                style with { HighestActiveLinePosition = 2 });
            Check(finalWindow.IsSupported &&
                  finalWindow.Visible.TextItems.Count == 2 &&
                  finalWindow.Visible.TextItems[0].Source.Lines[0].Text == "C" &&
                  finalWindow.Visible.TextItems[1].Source.Lines[0].Text == "A",
                "Original TextboxLines falls back to last possible window for shorter text items");
        }

        var unsupportedFaded = FreeShowRevealRenderProjection.Prepare(
            sourceFrame, firstRevealed.RootElement,
            style with { ShowAsFaded = true });
        Check(!unsupportedFaded.IsSupported &&
              unsupportedFaded.Unported.Count > 0,
            "Unported FreeShow showAsFaded alpha composition never presents full reveal parity");

        var unrelatedSourceIndex = JsonDocument.Parse("""
{"id":"navigation-show-id","layout":"main","index":3,
 "line":0,"revealCount":1,"itemClickReveal":true}
""");
        Check(!FreeShowRevealRenderProjection.Prepare(
                sourceFrame, unrelatedSourceIndex.RootElement, style).IsSupported,
            "Reveal projection refuses a state from another source slide index");
        unrelatedSourceIndex.Dispose();
    }
    using (var prewarmInitial = JsonDocument.Parse("""
{"id":"navigation-show-id","layout":"main","index":0,
 "line":0,"revealCount":0,"itemClickReveal":false}
"""))
    {
        var timeline = FreeShowRevealPrewarmSequence.PrepareSingleOutput(
            source, sourceFrame, prewarmInitial.RootElement, "screen1",
            new FreeShowRevealStyleContext(1, 1, 0),
            activeNormalOutputCount:1,
            matchesSourceOutput:(_, _) => false);
        Check(timeline.CanPrewarmCompleteSlide &&
              timeline.States.Count >= 4 &&
              timeline.States[0].OutSlide.ValueKind == JsonValueKind.Object,
            "Single-output FreeShow reveal stages can be prepared entirely before pressing Next");
        Check(!FreeShowRevealPrewarmSequence.PrepareSingleOutput(
            source, sourceFrame, prewarmInitial.RootElement, "screen1",
            new FreeShowRevealStyleContext(1, 1, 0),
            activeNormalOutputCount:2).CanPrewarmCompleteSlide,
            "Multi-output line synchronization is not guessed during one-screen reveal prewarm");
        Check(!FreeShowRevealPrewarmSequence.PrepareSingleOutput(
            source, sourceFrame, prewarmInitial.RootElement, "screen1",
            new FreeShowRevealStyleContext(1, 1, 0),
            activeNormalOutputCount:1, maximumStates:2,
            matchesSourceOutput:(_, _) => false).CanPrewarmCompleteSlide,
            "Incomplete bounded reveal prewarm queue never enables an uncached live transition");
    }
    var noSlide = JsonDocument.Parse("{}");
    var first = FreeShowSlideAdvancePlanner.Prepare(
        source, noSlide.RootElement, "screen1",
        FreeShowSlideAdvanceDirection.First, 0);
    Check(first.HasTarget && first.LayoutIndex == 0 &&
          !first.CanDispatchLive && first.NextOutSlide.ValueKind == JsonValueKind.Object,
        "Home chooses the source first slide, but never sends a partial live action");
    var last = FreeShowSlideAdvancePlanner.Prepare(
        source, noSlide.RootElement, "screen1",
        FreeShowSlideAdvanceDirection.Last, 0);
    Check(last.HasTarget && last.LayoutIndex == 3,
        "End selects the last eligible expanded source layout index");

    using var currentVerse = JsonDocument.Parse("""
{"id":"navigation-show-id","layout":"main","index":0,"line":0}
""");
    var revealFirst = FreeShowSlideAdvancePlanner.Prepare(
        source, currentVerse.RootElement, "screen1",
        FreeShowSlideAdvanceDirection.Next, 1);
    Check(revealFirst.Kind == FreeShowSlideAdvanceKind.RevealWithinSlide &&
          ShowDocument.Property(revealFirst.NextOutSlide, "line").GetInt32() == 1 &&
          ShowDocument.IsTrue(revealFirst.NextOutSlide, "itemClickReveal") &&
          ShowDocument.Property(revealFirst.NextOutSlide, "revealCount").ValueKind ==
              JsonValueKind.Undefined,
        "Next first prioritizes style line, then click reveal before line reveal");

    using var visibleVerse = JsonDocument.Parse("""
{"id":"navigation-show-id","layout":"main","index":0,
 "line":2,"itemClickReveal":true,"revealCount":1}
""");
    var nextReveal = FreeShowSlideAdvancePlanner.Prepare(
        source, visibleVerse.RootElement, "screen1",
        FreeShowSlideAdvanceDirection.Next, 1);
    Check(nextReveal.Kind == FreeShowSlideAdvanceKind.RevealWithinSlide &&
          ShowDocument.Property(nextReveal.NextOutSlide, "line").GetInt32() == 3 &&
          ShowDocument.Property(nextReveal.NextOutSlide, "revealCount").GetInt32() == 2,
        "Original Next advances both style line and revealCount as OutputHelper specifies");

    var prevReveal = FreeShowSlideAdvancePlanner.Prepare(
        source, visibleVerse.RootElement, "screen1",
        FreeShowSlideAdvanceDirection.Previous, 1);
    Check(prevReveal.Kind == FreeShowSlideAdvanceKind.RevealWithinSlide &&
          ShowDocument.Property(prevReveal.NextOutSlide, "line").GetInt32() == 1 &&
          ShowDocument.Property(prevReveal.NextOutSlide, "revealCount").GetInt32() == 0,
        "Previous reverses a reveal step before leaving the current slide");

    using var completeVerse = JsonDocument.Parse("""
{"id":"navigation-show-id","layout":"main","index":0,
 "itemClickReveal":true,"revealCount":3}
""");
    var nextDisplay1 = FreeShowSlideAdvancePlanner.Prepare(
        source, completeVerse.RootElement, "screen1",
        FreeShowSlideAdvanceDirection.Next, 0, (binding, outputId) =>
            binding == "screen2" && outputId == "screen1" ? false : null);
    Check(nextDisplay1.Kind == FreeShowSlideAdvanceKind.ChangeSlide &&
          nextDisplay1.LayoutIndex == 3,
        "Source next skips disabled slides and slides bound to other outputs");

    var nextDisplay2 = FreeShowSlideAdvancePlanner.Prepare(
        source, completeVerse.RootElement, "screen2",
        FreeShowSlideAdvanceDirection.Next, 0);
    Check(nextDisplay2.LayoutIndex == 1,
        "Bound slide remains in the layout for its intended second output");

    var unresolvedBindings = FreeShowSlideAdvancePlanner.Prepare(
        source, completeVerse.RootElement, "screen1",
        FreeShowSlideAdvanceDirection.Next, 0);
    Check(unresolvedBindings.Kind == FreeShowSlideAdvanceKind.Refused &&
          unresolvedBindings.Reason?.Contains("binding") == true,
        "Unknown original synced-output binding cannot be silently skipped");

    using var previousLast = JsonDocument.Parse("""
{"id":"navigation-show-id","layout":"main","index":3}
""");
    var previous = FreeShowSlideAdvancePlanner.Prepare(
        source, previousLast.RootElement, "screen1",
        FreeShowSlideAdvanceDirection.Previous, 0, (binding, outputId) => false);
    Check(previous.LayoutIndex == 0 &&
          ShowDocument.IsTrue(previous.NextOutSlide, "itemClickReveal") &&
          ShowDocument.Property(previous.NextOutSlide, "revealCount").GetInt32() == 3,
        "Previous skips bound and disabled slides, restoring earlier slide's final reveal");

    using var lastSlide = JsonDocument.Parse("""
{"id":"navigation-show-id","layout":"main","index":3,"projectIndex":0}
""");
    var boundary = FreeShowSlideAdvancePlanner.Prepare(
        source, lastSlide.RootElement, "screen1",
        FreeShowSlideAdvanceDirection.Next, 0);
    Check(boundary.Kind == FreeShowSlideAdvanceKind.ProjectBoundary,
        "At final slide, normal Next yields a project item boundary instead of wrapping");

    using var loopLast = JsonDocument.Parse("""
{"id":"navigation-show-id","layout":"loop","index":1}
""");
    var loop = FreeShowSlideAdvancePlanner.Prepare(
        source, loopLast.RootElement, "screen1",
        FreeShowSlideAdvanceDirection.Next, 0);
    Check(loop.LayoutIndex == 0 &&
          loop.Kind == FreeShowSlideAdvanceKind.ChangeSlide,
        "Original layoutRef[index].data.end loops to the first slide");

    using var wrongShow = JsonDocument.Parse("""
{"id":"different-show","layout":"main","index":0}
""");
    Check(FreeShowSlideAdvancePlanner.Prepare(
        source, wrongShow.RootElement, "screen1",
        FreeShowSlideAdvanceDirection.Next, 0).Kind ==
            FreeShowSlideAdvanceKind.Refused,
        "Output navigation will not switch to the selected song when current show differs");

    using var projectDoc = JsonDocument.Parse("""
{
 "folders":{},
 "projects":{"sunday":{"name":"Sunday", "parent":"/",
 "shows":[
    {"id":"navigation-show-id","type":"show","name":"Praise"},
    {"id":"section-b","type":"section","name":"Worship","notes":""},
    {"id":"navigation-show-id","type":"show","name":"Praise Again","layout":"loop"},
    {"id":"video-b","type":"video","name":"Background"}
 ]}}
}
""");
    var library = FreeShowProjectLibrary.Parse(projectDoc.RootElement);
    var projectContent = FreeShowProjectContentProjection.Prepare(
        library.Find("sunday")!);
    var projectNext = FreeShowProjectAdvancePlanner.Prepare(
        projectContent, lastSlide.RootElement, projectContent.ItemAtOriginalIndex(0),
        FreeShowSlideAdvanceDirection.Next, false, true);
    Check(projectNext.Status == FreeShowProjectAdvanceStatus.SelectProjectItem &&
          projectNext.Target?.SourceIndex == 2 &&
          projectNext.Target.LayoutId == "loop" &&
          projectNext.SourceSectionActions.Count == 1 &&
          !projectNext.CanDispatchLive,
        "Project movement retains repeated Show ID and layout override, skipping empty section");
    Check(FreeShowProjectAdvancePlanner.ResolveSourceIndex(
          projectContent, lastSlide.RootElement,
          projectContent.ItemAtOriginalIndex(2)) == 0,
        "Source projectIndex outranks duplicated Show ID or active selection");
    var blockedProject = FreeShowProjectAdvancePlanner.Prepare(
        projectContent, lastSlide.RootElement, projectContent.ItemAtOriginalIndex(0),
        FreeShowSlideAdvanceDirection.Next, false, false);
    Check(blockedProject.Status == FreeShowProjectAdvanceStatus.Refused,
        "Non-focus project advance respects nextItemOnLastSlide source setting");

    var root = Path.Combine(Path.GetTempPath(),
        "freeshow-native-navigation-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "Config"));
    try
    {
        await File.WriteAllTextAsync(Path.Combine(root, "Config", "settings_synced.json"), """
{"styles":{"styleA":{"lines":2,"layers":["slide","background"]},
           "styleB":{"lines":1,"layers":["slide"]}}}
""");
        var styles = await FreeShowVisualSettings.OpenAsync(root);
        using var twoOutputs = JsonDocument.Parse("""
{"outputs":{
 "screen1":{"name":"Main","enabled":true,"active":true,"style":"styleA",
  "bounds":{"width":1920,"height":1080},
  "out":{"slide":{"id":"navigation-show-id","layout":"main",
                   "index":3,"projectIndex":0}}},
 "screen2":{"name":"Online","enabled":true,"active":true,"style":"styleB",
  "bounds":{"width":1920,"height":1080},
  "out":{"slide":{"id":"navigation-show-id","layout":"main",
                   "index":0,"line":0,"projectIndex":0}}}
}}
""");
        var config = FreeShowOutputConfiguration.Parse(twoOutputs.RootElement);
        var context = new FreeShowNavigationSourceContext(config, styles);
        Check(context.GetStyleLimits().IsAuthoritative &&
              context.GetStyleLimits().FewestStyleLines == 1,
            "Original getFewestOutputLines chooses minimum over active output styles");

        var state = config.All.ToDictionary(x => x.Id, x => new FreeShowOutputState(x));
        var revealStyle = FreeShowRevealStyleResolver.Resolve(
            "screen2", config, styles, state);
        Check(revealStyle.CanPrepare &&
              revealStyle.Context?.StyleLines == 1 &&
              revealStyle.Context.FewestActiveStyleLines == 1 &&
              revealStyle.Context.HighestActiveLinePosition == 0,
            "Source Output.svelte reveal window uses the selected output style and shared active output line progress");
        Check(!FreeShowRevealStyleResolver.Resolve(
            "unregistered-output", config, styles, state).CanPrepare,
            "Unknown FreeShow physical output is not assigned a fabricated text style");
        var batch = FreeShowMultiOutputAdvanceInspection.Prepare(
            config, state, context,
            id => id == source.ShowId ? source : null,
            FreeShowSlideAdvanceDirection.Next,
            projectContent, projectContent.ItemAtOriginalIndex(0));
        Check(batch.Status == FreeShowMultiOutputAdvanceStatus.SlideOrRevealTargets &&
              batch.OutputPlans["screen1"].Kind == FreeShowSlideAdvanceKind.ProjectBoundary &&
              batch.OutputPlans["screen2"].Kind == FreeShowSlideAdvanceKind.RevealWithinSlide &&
              !batch.CanDispatchLive,
            "One active output at final slide cannot force another output to leave its current reveal");
        // 0.0.24 — original playSlide lifecycle. These fake runtime
        // adapters are CHECKS-ONLY: no real output action is installed.
        var transitionController = new FreeShowOutputCommandCoordinator(config);
        var transitionCapture = FreeShowOutputSnapshotBatch.Capture(
            config, transitionController.States);
        var transitionInspect = FreeShowMultiOutputAdvanceInspection.Prepare(
            config, transitionController.States, context,
            id => id == source.ShowId ? source : null,
            FreeShowSlideAdvanceDirection.Next,
            projectContent, projectContent.ItemAtOriginalIndex(0));
        var transitionPlan = FreeShowSlideTransitionPlanner.Prepare(
            config, transitionCapture, transitionInspect,
            id => id == source.ShowId ? source : null);
        Check(transitionPlan.IsSourcePrepared &&
              transitionPlan.Targets.Count == 1 &&
              transitionPlan.Targets[0].OutputId == "screen2" &&
              transitionPlan.Targets[0].NavigationKind ==
                  FreeShowSlideAdvanceKind.RevealWithinSlide,
            "Original multi-output intent changes only output still revealing lyrics");

        var noSlideRuntimes =
            new Dictionary<string, IFreeShowSlideTransitionRuntime>();
        var refusedTransition = await transitionController.DispatchSlideTransitionAsync(
            transitionPlan, noSlideRuntimes);
        Check(refusedTransition.Status == FreeShowSlideTransitionStatus.Refused &&
              refusedTransition.OutputsAcknowledged == 0 &&
              transitionController.States["screen2"].Revision == 0,
            "No full FreeShow action/setOutput/updateOut runtime refuses before changing OutData");

        var incompleteTransition = new RecordingCompleteSlideTransitionRuntime(
            "screen2", FreeShowSlideTransitionOrder.Phases.Take(2));
        var missingUpdateOut = await transitionController.DispatchSlideTransitionAsync(
            transitionPlan, new Dictionary<string, IFreeShowSlideTransitionRuntime>
            { ["screen2"] = incompleteTransition });
        Check(missingUpdateOut.Status == FreeShowSlideTransitionStatus.Refused &&
              incompleteTransition.Executed.Count == 0,
            "Missing updateOut source phase blocks even a cached lyric-only transition");

        var completeTransition = new RecordingCompleteSlideTransitionRuntime(
            "screen2", FreeShowSlideTransitionOrder.Phases);
        var completeTransitionAdapters =
            new Dictionary<string, IFreeShowSlideTransitionRuntime>
            { ["screen2"] = completeTransition };
        var completeResult = await transitionController.DispatchSlideTransitionAsync(
            transitionPlan, completeTransitionAdapters);
        var acknowledgedSlide = ShowDocument.Property(
            transitionController.States["screen2"].Snapshot(), "slide");
        Check(completeResult.Succeeded &&
              completeResult.OutputsAcknowledged == 1 &&
              completeTransition.Executed.SequenceEqual(
                  FreeShowSlideTransitionOrder.Phases) &&
              ShowDocument.Property(acknowledgedSlide, "line").GetInt32() == 1 &&
              ShowDocument.IsTrue(acknowledgedSlide, "itemClickReveal") &&
              transitionController.States["screen2"].Revision == 1,
            "Only all-acknowledged FreeShow phases commit complete output state once");
        Check(transitionController.CheckSlideTransitionReadiness(
              transitionPlan, completeTransitionAdapters).Any(
                  x => x.Contains("changed")),
            "Original slide plan from earlier OutData revision cannot be reused");

        var afterFirstCapture = FreeShowOutputSnapshotBatch.Capture(
            config, transitionController.States);
        var afterFirstInspect = FreeShowMultiOutputAdvanceInspection.Prepare(
            config, transitionController.States, context,
            id => id == source.ShowId ? source : null,
            FreeShowSlideAdvanceDirection.Next,
            projectContent, projectContent.ItemAtOriginalIndex(0));
        var afterFirstPlan = FreeShowSlideTransitionPlanner.Prepare(
            config, afterFirstCapture, afterFirstInspect,
            id => id == source.ShowId ? source : null);

        var brokenTransition = new RecordingCompleteSlideTransitionRuntime(
            "screen2", FreeShowSlideTransitionOrder.Phases)
        {
            FailAtPhase =
                FreeShowSlideTransitionPhase.SetOriginalSlideWithFullOutputLifecycle
        };
        var brokenAdapters =
            new Dictionary<string, IFreeShowSlideTransitionRuntime>
            { ["screen2"] = brokenTransition };
        var brokenResult = await transitionController.DispatchSlideTransitionAsync(
            afterFirstPlan, brokenAdapters);
        Check(brokenResult.Status == FreeShowSlideTransitionStatus.PartialExecution &&
              brokenResult.OutputsAcknowledged == 0 &&
              transitionController.UnreconciledSlideOutputs.Contains("screen2") &&
              transitionController.States["screen2"].Revision == 1,
            "A physical failure quarantines output without committing fictional state");

        var replayAttempt = await transitionController.DispatchSlideTransitionAsync(
            afterFirstPlan, completeTransitionAdapters);
        Check(replayAttempt.Status == FreeShowSlideTransitionStatus.Refused &&
              replayAttempt.Problems.Any(x => x.Contains("reconciliation")),
            "Unreconciled physical slide output blocks replay of stale command");

        var snapshotToReconcile = transitionController.States["screen2"].Snapshot();
        var revisionToReconcile = transitionController.States["screen2"].Revision;
        Check(await transitionController.SynchronizeAcknowledgedOutputAsync(
              "screen2", snapshotToReconcile, revisionToReconcile) &&
              transitionController.UnreconciledSlideOutputs.Count == 0 &&
              transitionController.States["screen2"].Revision ==
                  revisionToReconcile + 1,
            "Only an authoritative full OutData sync removes physical output quarantine");

        var keyboardNavigation = new FreeShowKeyboardAdvanceInspector();
        var eventAt = DateTimeOffset.UnixEpoch.AddSeconds(30);
        var routed = keyboardNavigation.Inspect(
            new NativeShortcutInput("ArrowRight", eventAt,
                NativeShortcutOrigin.Keyboard),
            config, state, context, id => id == source.ShowId ? source : null,
            projectContent, projectContent.ItemAtOriginalIndex(0));
        Check(routed?.Status ==
              FreeShowMultiOutputAdvanceStatus.SlideOrRevealTargets &&
              routed.OutputPlans.Count == 2,
            "Existing FreeShow keyboard router feeds the native output navigation inspector");
        var duplicate = keyboardNavigation.Inspect(
            new NativeShortcutInput("ArrowRight", eventAt.AddMilliseconds(50),
                NativeShortcutOrigin.ExternalController),
            config, state, context, id => id == source.ShowId ? source : null);
        Check(duplicate == null,
            "Source 80ms different-input duplicate suppression applies before navigation");
        var ctrlKey = keyboardNavigation.Inspect(
            new NativeShortcutInput("ArrowRight", eventAt.AddMilliseconds(150),
                NativeShortcutOrigin.Keyboard, Ctrl:true),
            config, state, context, id => id == source.ShowId ? source : null);
        Check(ctrlKey == null,
            "Original Ctrl+ArrowRight remains reserved for editor/navigation context");

        // 0.0.22: source-consistent multi-output Preview snapshots and
        // per-screen reveal state. This code is authored, NOT executed.
        var capturedBatch = FreeShowOutputSnapshotBatch.Capture(config, state);
        Check(capturedBatch.IsCoherent &&
              capturedBatch.Outputs.Count == 2 &&
              capturedBatch.StillMatches(state),
            "Two original outputs are captured from one verified OutData revision set");

        var previewFromCurrent = FreeShowOutputPreviewProjection.Prepare(
            config, source, capturedBatch);
        Check(previewFromCurrent.Count == 2 &&
              previewFromCurrent.Any(x =>
                x.Output.Id == "screen1" && x.LayoutIndex == 3) &&
              previewFromCurrent.Any(x =>
                x.Output.Id == "screen2" && x.LayoutIndex == 0),
            "Read-only Preview uses captured actual slide indexes, not inferred selected show");

        var frameCache = NativeRenderPlanner.Prepare(
            source, "main", includeDisabled:true);
        var outputRevealBatch = FreeShowOutputRevealBatchPlanner.Prepare(
            config, styles, capturedBatch, (_, id, arrangement, slideIndex) =>
                id == source.ShowId && arrangement == "main"
                    ? frameCache.FirstOrDefault(x =>
                        x.Source.LayoutIndex == slideIndex)
                    : null);
        Check(outputRevealBatch.CanPrewarmAll &&
              outputRevealBatch.Targets.Count == 2 &&
              outputRevealBatch.Targets["screen1"].Style.StyleLines == 2 &&
              outputRevealBatch.Targets["screen2"].Style.StyleLines == 1 &&
              !outputRevealBatch.CanDispatchLive,
            "Multiscreen reveal preparation keeps distinct original style lines without output side effects");

        var unavailableSourceFrame = FreeShowOutputRevealBatchPlanner.Prepare(
            config, styles, capturedBatch, (outputId, _, _, _) =>
                outputId == "screen1" ? null : frameCache[0]);
        Check(!unavailableSourceFrame.CanPrewarmAll,
            "One unavailable source renderer blocks the entire synchronized reveal prewarm");

        // A new acknowledged command must invalidate the previous snapshot.
        Check(state["screen2"].TryApply(new FreeShowShortcutAction(
            NativeShortcutCommand.ClearSlide, "screen2")) &&
              !capturedBatch.StillMatches(state),
            "A later acknowledged slide clear invalidates old multiscreen preview batch");
        var afterClear = FreeShowOutputSnapshotBatch.Capture(config, state);
        var previewAfterClear = FreeShowOutputPreviewProjection.Prepare(
            config, source, afterClear);
        Check(afterClear.IsCoherent &&
              previewAfterClear.Any(x =>
                x.Output.Id == "screen2" && x.SourceFrame == null) &&
              !FreeShowOutputRevealBatchPlanner.Prepare(
                config, styles, afterClear, (_, id, arrangement, slideIndex) =>
                    id == source.ShowId && arrangement == "main"
                        ? frameCache.FirstOrDefault(x =>
                            x.Source.LayoutIndex == slideIndex)
                        : null).CanPrewarmAll,
            "Cleared output remains blank and no stale lyric reveal is prepared for it");


        using var bothAtEnd = JsonDocument.Parse("""
{"outputs":{
 "screen1":{"name":"Main","enabled":true,"active":true,"style":"styleA",
  "bounds":{"width":1920,"height":1080},
  "out":{"slide":{"id":"navigation-show-id","layout":"main","index":3,"projectIndex":0}}},
 "screen2":{"name":"Online","enabled":true,"active":true,"style":"styleB",
  "bounds":{"width":1920,"height":1080},
  "out":{"slide":{"id":"navigation-show-id","layout":"main","index":3,"projectIndex":0}}}
}}
""");
        var ended = FreeShowOutputConfiguration.Parse(bothAtEnd.RootElement);
        var allReady = FreeShowMultiOutputAdvanceInspection.Prepare(
            ended, ended.All.ToDictionary(x => x.Id, x => new FreeShowOutputState(x)),
            new FreeShowNavigationSourceContext(ended, styles),
            id => id == source.ShowId ? source : null,
            FreeShowSlideAdvanceDirection.Next,
            projectContent, projectContent.ItemAtOriginalIndex(0));
        Check(allReady.Status == FreeShowMultiOutputAdvanceStatus.ProjectSelectionOnly &&
              allReady.ProjectPlan?.Target?.SourceIndex == 2,
            "Project transition is offered only when EVERY active output reaches show boundary");
    }
    finally { Directory.Delete(root, recursive: true); }
    noSlide.Dispose();
}

Console.WriteLine("Native FreeShow source-compatibility fixture checks passed.");

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
    Console.WriteLine("OK: " + label);
}


/// <summary>
/// Only the synthetic compatibility checks can construct this recorder.
/// Production output adapters must actually perform the requested operations.
/// </summary>
internal sealed class RecordingOutputCommandRuntime : IFreeShowOutputCommandRuntime
{
    private readonly HashSet<FreeShowSourceEffect> capabilities;
    public string OutputId { get; }
    public List<FreeShowSourceEffect> Executed { get; } = new();

    public RecordingOutputCommandRuntime(string outputId,
        IEnumerable<FreeShowSourceEffect> supported)
    {
        OutputId = outputId;
        capabilities = new HashSet<FreeShowSourceEffect>(supported);
    }

    public bool SupportsSourceEffect(FreeShowSourceEffect effect) =>
        capabilities.Contains(effect);

    public void AddCapabilities(IEnumerable<FreeShowSourceEffect> effects)
    {
        foreach (var effect in effects) capabilities.Add(effect);
    }

    public Task ExecuteSourceEffectsAsync(
        FreeShowOutputCommandPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan.OutputId != OutputId ||
            plan.RequiredEffects.Any(effect => !SupportsSourceEffect(effect)))
            throw new NotSupportedException("Incomplete source-effect runtime");
        cancellationToken.ThrowIfCancellationRequested();
        Executed.AddRange(plan.RequiredEffects);
        return Task.CompletedTask;
    }
}


internal sealed class RecordingOutputRestoreRuntime : IFreeShowOutputRestoreRuntime
{
    private readonly HashSet<string> allowedOutputs;
    private readonly bool audioAvailable;
    public string? FailForOutput { get; set; }
    public List<string> RestoredOutputIds { get; } = new();
    public bool RestoredAudio { get; private set; }
    public bool? RestoredMetronome { get; private set; }

    public RecordingOutputRestoreRuntime(IEnumerable<string> ids, bool audioAvailable)
    {
        allowedOutputs = new HashSet<string>(ids, StringComparer.Ordinal);
        this.audioAvailable = audioAvailable;
    }

    public bool CanRestoreOutput(string originalOutputId, JsonElement originalOutData) =>
        allowedOutputs.Contains(originalOutputId) &&
        originalOutData.ValueKind == JsonValueKind.Object;

    public bool CanRestoreGlobalAudio(JsonElement playingAudioData, bool playingMetronome) =>
        audioAvailable && playingAudioData.ValueKind == JsonValueKind.Array;

    public Task RestoreOutputAsync(string originalOutputId, JsonElement originalOutData,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (originalOutputId == FailForOutput)
            throw new IOException("Synthetic restore failure.");
        RestoredOutputIds.Add(originalOutputId);
        return Task.CompletedTask;
    }

    public Task RestoreGlobalAudioAsync(JsonElement playingAudioData, bool playingMetronome,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RestoredAudio = true;
        RestoredMetronome = playingMetronome;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingSourceTimer : IFreeShowTimerCancellation
{
    public bool FailToCancel { get; set; }
    public bool Cancelled { get; private set; }
    public Task CancelAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailToCancel) throw new IOException("Synthetic timer failure.");
        Cancelled = true;
        return Task.CompletedTask;
    }
}


internal sealed class RecordingSourceClearAllRuntime : IFreeShowClearAllRuntime
{
    private readonly HashSet<FreeShowClearAllStep> supported;
    public FreeShowClearAllStep? FailAtStep { get; set; }
    public List<FreeShowClearAllStep> Executed { get; } = new();

    public RecordingSourceClearAllRuntime(IEnumerable<FreeShowClearAllStep> supported)
    {
        this.supported = new HashSet<FreeShowClearAllStep>(supported);
    }

    public bool SupportsCompleteSourceStep(FreeShowClearAllStep step) =>
        supported.Contains(step);

    public Task ExecuteCompleteSourceStepAsync(
        FreeShowClearAllStep step, FreeShowClearAllPlan plan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SupportsCompleteSourceStep(step))
            throw new NotSupportedException("Missing source Clear All backend step");
        if (FailAtStep == step)
            throw new IOException("Synthetic source Clear All step failure");
        if (!plan.IsReady)
            throw new InvalidOperationException("Invalid original Clear All plan");
        Executed.Add(step);
        return Task.CompletedTask;
    }
}


internal sealed class RecordingFreeShowTimerInventory : IFreeShowTimerInventoryAuthority
{
    private readonly HashSet<string> verifiedSlides = new(StringComparer.Ordinal);
    private readonly HashSet<string> verifiedOverlays = new(StringComparer.Ordinal);

    public void Allow(string outputId, bool includeOverlays)
    {
        verifiedSlides.Add(outputId);
        if (includeOverlays) verifiedOverlays.Add(outputId);
    }

    public bool HasCompleteInventory(string originalOutputId, bool includeOverlayTimers) =>
        verifiedSlides.Contains(originalOutputId) &&
        (!includeOverlayTimers || verifiedOverlays.Contains(originalOutputId));
}


/// <summary>
/// Synthetic FreeShow output backend used ONLY by the unexecuted fixture
/// suite. Never register this fake backend with an operator app.
/// </summary>
internal sealed class RecordingCompleteSlideTransitionRuntime :
    IFreeShowSlideTransitionRuntime
{
    private readonly HashSet<FreeShowSlideTransitionPhase> supported;
    public string OutputId { get; }
    public FreeShowSlideTransitionPhase? FailAtPhase { get; set; }
    public bool ReturnWrongSlide { get; set; }
    public List<FreeShowSlideTransitionPhase> Executed { get; } = new();

    public RecordingCompleteSlideTransitionRuntime(string outputId,
        IEnumerable<FreeShowSlideTransitionPhase> phases)
    {
        OutputId = outputId;
        supported = new HashSet<FreeShowSlideTransitionPhase>(phases);
    }

    public bool SupportsCompletePhase(
        FreeShowSlideTransitionPhase phase,
        FreeShowSlideTransitionTarget target) =>
        target.OutputId == OutputId && supported.Contains(phase);

    public Task ExecuteCompletePhaseAsync(
        FreeShowSlideTransitionPhase phase,
        FreeShowSlideTransitionTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SupportsCompletePhase(phase, target))
            throw new NotSupportedException("Synthetic source phase missing.");
        if (FailAtPhase == phase)
            throw new IOException("Synthetic physical slide phase failure");
        Executed.Add(phase);
        return Task.CompletedTask;
    }

    public Task<JsonElement> CaptureAcknowledgedOutDataAsync(
        FreeShowSlideTransitionTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var original = System.Text.Json.Nodes.JsonNode.Parse(
            target.PreviousOutData.GetRawText()) as System.Text.Json.Nodes.JsonObject ??
            throw new InvalidDataException("Synthetic original OutData missing.");
        original["slide"] = System.Text.Json.Nodes.JsonNode.Parse(
            target.NextOutSlide.GetRawText());
        if (ReturnWrongSlide)
            ((System.Text.Json.Nodes.JsonObject)original["slide"]!)["index"] = 999;
        return Task.FromResult(JsonSerializer.SerializeToElement(original));
    }
}
