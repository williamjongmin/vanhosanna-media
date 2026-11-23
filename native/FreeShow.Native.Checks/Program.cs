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
Check(f3.Count == 4 && f3.Count(x => x.Command == NativeShortcutCommand.ClearOverlays) == 2
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
    Check(state.TryApply(new FreeShowShortcutAction(NativeShortcutCommand.ClearOverlays, "worship"))
        && state.Overlays.Count == 0 && state.Effects.Count == 1,
        "F3 overlay action leaves effects for its separate action");
    Check(state.TryApply(new FreeShowShortcutAction(NativeShortcutCommand.ClearEffects, "worship"))
        && state.Effects.Count == 0,
        "F3 effect action clears effects");
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

Console.WriteLine("Native FreeShow source-compatibility fixture checks passed.");

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
    Console.WriteLine("OK: " + label);
}
