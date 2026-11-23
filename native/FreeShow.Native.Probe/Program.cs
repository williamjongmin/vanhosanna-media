using System.Text;
using FreeShow.Native.Core;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length != 2 || args[0] is not ("--show" or "--folder"))
{
    Console.Error.WriteLine("Read-only FreeShow Native compatibility probe");
    Console.Error.WriteLine("  --show <path-to-original.show>");
    Console.Error.WriteLine("  --folder <path-to-FreeShow-Shows-folder>");
    return 2;
}

try
{
    if (args[0] == "--show")
    {
        var show = await ShowDocument.ReadAsync(args[1]);
        Console.WriteLine("ID: " + show.ShowId);
        Console.WriteLine("Name: " + show.Name);
        Console.WriteLine("Active layout: " + show.ActiveLayoutId);
        Console.WriteLine("Wrapped FreeShow .show file: " + show.IsFreeShowFileEnvelope);
        var frames = SlideProjection.ReadFrames(show, includeDisabled: true);
        foreach (var slide in frames)
        {
            Console.WriteLine("[" + slide.LayoutIndex + "] " +
                (slide.IsChild ? "  child " : "parent ") +
                slide.SlideId + (slide.Disabled ? " (disabled)" : "") +
                (slide.Group.Length > 0 ? " (" + slide.Group + ")" : ""));
            foreach (var line in slide.Items.SelectMany(item => item.Lines))
                if (!string.IsNullOrWhiteSpace(line.Text)) Console.WriteLine("  " + line.Text);
        }
        foreach (var problem in show.ValidateLayout())
            Console.Error.WriteLine("WARNING: " + problem);
    }
    else
    {
        var shows = await ShowLibrary.InspectFolderAsync(args[1]);
        Console.WriteLine("Shows checked: " + shows.Count);
        foreach (var show in shows)
        {
            Console.WriteLine(show.FileName + ": " +
                (show.Error ?? show.ShowId + " | " + show.Name + " | " + show.Slides + " slides"));
            foreach (var warning in show.Warnings) Console.Error.WriteLine("WARNING: " + warning);
        }
        if (shows.Any(show => show.Error != null)) return 1;
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
    return 1;
}
