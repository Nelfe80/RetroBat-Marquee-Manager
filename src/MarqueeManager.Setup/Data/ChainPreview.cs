using System.IO;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Data;

/// <summary>
/// Setup-side mirror of the runtime chain walk for ONE game: returns the media
/// file the marquee currently displays and where it comes from — powers the
/// "marquee affiché" preview of the game sheet. Same file rules as the runtime's
/// CompositionChainResolver, without launching ES.
/// </summary>
public static class ChainPreview
{
    public sealed record Result(string? Path, string Source, string Label, bool Deletable);

    public static Result Resolve(string pluginRoot, GameMediaCatalog media,
        CompositionAssignments assignments, string category, string system, string rom)
    {
        foreach (var source in assignments.ChainFor(category, system))
        {
            string? path = null;
            switch (source)
            {
                case "composition":
                    var composition = assignments.CompositionPath(category, system, rom);
                    if (File.Exists(composition)) path = composition;
                    break;
                case "user":
                    path = UserFile(assignments, category, system, rom);
                    break;
                case var t when t.StartsWith("template:"):
                    var cached = Path.Combine(pluginRoot, "media", category + "s", ".cache",
                        Safe(system), Safe(rom) + "-" + Safe(t["template:".Length..]) + ".png");
                    if (File.Exists(cached)) path = cached;
                    break;
                case "marquee":
                    path = LibraryFile(media, system, rom, @"artwork\marquee\marquee.png", @"artwork\marquee\marquee.jpg");
                    break;
                case "screenmarquee":
                    path = LibraryFile(media, system, rom, @"artwork\marquee\screenmarquee.png");
                    break;
                case "generated":
                    path = LibraryFile(media, system, rom, @"artwork\marquee\generated-marquee.png", @"artwork\marquee\generated-dmd.png");
                    break;
                case "logo":
                    path = LibraryFile(media, system, rom, @"ui\wheels\wheel.png");
                    break;
                case "fanart":
                    path = LibraryFile(media, system, rom, @"artwork\fanart.jpg", @"artwork\fanart.png");
                    break;
                case "topper":
                    path = LibraryFile(media, system, rom, @"artwork\marquee\topper.png");
                    break;
                case "animations":
                    var marqueeDir = Path.Combine(media.GameRoot(system, rom), "artwork", "marquee");
                    if (Directory.Exists(marqueeDir)) path = Directory.EnumerateFiles(marqueeDir, "dmd*.gif").FirstOrDefault();
                    break;
                case "still":
                    path = LibraryFile(media, system, rom, @"artwork\marquee\dmd.png");
                    break;
            }
            if (path != null)
            {
                // creations and personal files can be deleted from the sheet;
                // scraped/generated media belong to the library
                var deletable = source is "composition" or "user";
                return new Result(path, source, LabelOf(source), deletable);
            }
        }
        return new Result(null, "", L.T("rien (flux d'origine)", "nothing (stream default)"), false);
    }

    public static string LabelOf(string source) => source switch
    {
        "composition" => L.T("Ma création graphique", "My graphic creation"),
        "user" => L.T("Mon dossier", "My folder"),
        "marquee" => L.T("Marquee scrapé", "Scraped marquee"),
        "screenmarquee" => "Screen-marquee",
        "generated" => L.T("Généré (APIExpose)", "Generated (APIExpose)"),
        "logo" => "Logo (wheel)",
        "fanart" => "Fanart",
        "topper" => L.T("Topper scrapé", "Scraped topper"),
        "animations" => L.T("Animations dmd*.gif", "dmd*.gif animations"),
        "still" => "dmd.png",
        var t when t.StartsWith("template:") => "Template " + t["template:".Length..],
        _ => source
    };

    private static string? UserFile(CompositionAssignments assignments, string category, string system, string rom)
    {
        var folder = assignments.UserFolder(category, system);
        if (!Directory.Exists(folder)) return null;
        var direct = Directory.EnumerateFiles(folder, Safe(rom) + ".*").FirstOrDefault();
        if (direct != null) return direct;
        var sidecar = Path.Combine(folder, ".index.json");
        if (!File.Exists(sidecar)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(sidecar));
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String
                    && string.Equals(property.Value.GetString(), rom, StringComparison.OrdinalIgnoreCase))
                {
                    var mapped = Path.Combine(folder, property.Name);
                    if (File.Exists(mapped)) return mapped;
                }
            }
        }
        catch
        {
            // unreadable sidecar
        }
        return null;
    }

    private static string? LibraryFile(GameMediaCatalog media, string system, string rom, params string[] relatives)
    {
        foreach (var relative in relatives)
        {
            var path = Path.Combine(media.GameRoot(system, rom), relative);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
