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
}
finally
{
    Directory.Delete(visualRoot, recursive: true);
}

Console.WriteLine("Native FreeShow source-compatibility fixture checks passed.");

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
    Console.WriteLine("OK: " + label);
}
