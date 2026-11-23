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

Console.WriteLine("Native FreeShow source-compatibility fixture checks passed.");

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
    Console.WriteLine("OK: " + label);
}
