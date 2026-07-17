using System.IO;
using System.Text.Json;

namespace MarqueeManager.Setup.Data;

/// <summary>One visual asset of a game, offered as a composer layer source.</summary>
public sealed record GameAsset(string Key, string Label, string Path);

/// <summary>A game of the APIExpose media library.</summary>
public sealed record GameEntry(string System, string Rom)
{
    public string? DisplayName { get; init; }
    public string Describe() => DisplayName is { Length: > 0 } && !DisplayName.Equals(Rom, StringComparison.OrdinalIgnoreCase)
        ? $"{DisplayName} ({Rom})"
        : Rom;
}

/// <summary>
/// Read-only view over the APIExpose media library
/// (..\APIExpose\media\systems\&lt;system&gt;\games\&lt;rom&gt;\). The game list is scanned
/// on a background thread (same pattern as LedManagerSetup's GamePanelCatalog);
/// display names, genres and asset inventories are read lazily per game from
/// texts\metadata-&lt;lang&gt;.json.
/// </summary>
public sealed class GameMediaCatalog
{
    private readonly string _systemsRoot;
    private readonly object _sync = new();
    private List<GameEntry>? _games;

    public GameMediaCatalog(string pluginRoot)
    {
        _systemsRoot = Path.GetFullPath(Path.Combine(pluginRoot, "..", "APIExpose", "media", "systems"));
    }

    public bool IsAvailable => Directory.Exists(_systemsRoot);

    /// <summary>The arcade family folders. "Mes jeux" folds them into "arcade"
    /// (one grouped search); "Mes systèmes" keeps them apart — mame/fbneo carry
    /// their own chains and original compositions.</summary>
    public static readonly HashSet<string> ArcadeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "mame", "fbneo", "fba", "hbmame"
    };

    /// <summary>foldArcadeAliases: true collapses mame/fbneo/… into "arcade".</summary>
    public IReadOnlyList<string> ListSystems(bool foldArcadeAliases = false)
    {
        try
        {
            var systems = Directory.EnumerateDirectories(_systemsRoot)
                .Where(dir => Directory.Exists(Path.Combine(dir, "games")))
                .Select(Path.GetFileName)
                .OfType<string>()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (foldArcadeAliases && systems.Contains("arcade", StringComparer.OrdinalIgnoreCase))
            {
                systems.RemoveAll(s => ArcadeAliases.Contains(s));
            }
            return systems;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Rom base names physically present in RetroBat's roms\ folders,
    /// keyed by SYSTEM. The inventory walks roms\ itself (LedManager engine):
    /// a system with installed roms shows up even without an APIExpose media
    /// folder. The arcade family (mame, fbneo…) lands under the "arcade" key.</summary>
    public Dictionary<string, HashSet<string>> ListPresentRoms(string pluginRoot)
    {
        var present = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var romsRoot = Path.GetFullPath(Path.Combine(pluginRoot, "..", "..", "roms"));
        if (!Directory.Exists(romsRoot)) return present;

        foreach (var systemDir in Directory.EnumerateDirectories(romsRoot))
        {
            var folder = Path.GetFileName(systemDir);
            if (folder.Length == 0 || folder.StartsWith('.') || folder.Contains('=')) continue;
            var key = ArcadeAliases.Contains(folder) || folder.Equals("neogeo", StringComparison.OrdinalIgnoreCase)
                ? "arcade"
                : folder;

            HashSet<string>? roms = null;
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(systemDir))
                {
                    var name = Path.GetFileNameWithoutExtension(entry);
                    if (name is not { Length: > 0 } || name.StartsWith('.')) continue;
                    // the roms folder also holds metadata/media noise
                    var extension = Path.GetExtension(entry).ToLowerInvariant();
                    if (extension is ".xml" or ".txt" or ".cfg" or ".dat" or ".ini" or ".bak" or ".srm" or ".sav" or ".nfo") continue;
                    if (Directory.Exists(entry) && name.ToLowerInvariant()
                            is "images" or "videos" or "video" or "manuals" or "media"
                            or "downloaded_images" or "downloaded_videos" or "boxart") continue;
                    roms ??= present.TryGetValue(key, out var existing)
                        ? existing
                        : present[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    roms.Add(name);
                }
            }
            catch
            {
                // unreadable roms folder: skipped
            }
        }
        return present;
    }

    /// <summary>Full rom index, built once on first call (call it off the UI thread).</summary>
    public IReadOnlyList<GameEntry> ListGames()
    {
        lock (_sync)
        {
            if (_games != null)
            {
                return _games;
            }

            var games = new List<GameEntry>();
            foreach (var system in ListSystems())
            {
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(Path.Combine(_systemsRoot, system, "games")))
                    {
                        var rom = Path.GetFileName(dir);
                        if (rom.Length > 0)
                        {
                            games.Add(new GameEntry(system, rom));
                        }
                    }
                }
                catch
                {
                    // unreadable system folder: skipped
                }
            }

            _games = games;
            return games;
        }
    }

    public string GameRoot(string system, string rom)
        => Path.Combine(_systemsRoot, system, "games", rom);

    /// <summary>The SYSTEM marquee currently displayed when this system is
    /// selected in ES: the manual composition wins, else the generated one
    /// (arcade aliases fall back to the arcade render). Null = nothing yet.</summary>
    public string? CurrentSystemMarquee(string pluginRoot, string system)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(system.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
        var composition = Path.Combine(pluginRoot, "media", "marquees", "systems", safe + ".png");
        if (File.Exists(composition)) return composition;

        foreach (var candidate in ArcadeAliases.Contains(system) ? new[] { system, "arcade" } : new[] { system })
        {
            var generated = Path.Combine(_systemsRoot, candidate, "artwork", "marquee", "generated-system-marquee.png");
            if (File.Exists(generated)) return generated;
        }
        return null;
    }

    /// <summary>Display name from texts\metadata-&lt;lang&gt;.json (Fields.name).</summary>
    public string? ReadDisplayName(string system, string rom)
        => ReadMetadataField(system, rom, "name");

    /// <summary>Localized genre labels ("Action, Shooter…"). See also <see cref="ReadGenreIds"/>.</summary>
    public string? ReadGenre(string system, string rom)
        => ReadMetadataField(system, rom, "genre");

    /// <summary>ScreenScraper numeric genre ids ("10, 2844") — stable across languages,
    /// preferred input of the runtime's genre normalization map.</summary>
    public string? ReadGenreIds(string system, string rom)
        => ReadMetadataField(system, rom, "genres");

    /// <summary>
    /// The visual assets a composition can use, in a friendly order: backgrounds
    /// first (fanart, mix), then identity (wheel/logo, marquees), then extras.
    /// </summary>
    public IReadOnlyList<GameAsset> ListAssets(string system, string rom)
    {
        var root = GameRoot(system, rom);
        var assets = new List<GameAsset>();

        void Add(string key, string frLabel, string enLabel, params string[] relative)
        {
            foreach (var rel in relative)
            {
                var path = Path.Combine(root, rel);
                if (File.Exists(path))
                {
                    assets.Add(new GameAsset(key, Localization.L.T(frLabel, enLabel), path));
                    return;
                }
            }
        }

        Add("fanart", "Fanart", "Fanart", @"artwork\fanart.jpg", @"artwork\fanart.png");
        Add("mix", "Mix", "Mix", @"artwork\mix\mixrbv2.png", @"artwork\mix\mixrbv1.png");
        Add("wheel", "Logo (wheel)", "Logo (wheel)", @"ui\wheels\wheel.png");
        Add("marquee", "Marquee scrapé", "Scraped marquee", @"artwork\marquee\marquee.png", @"artwork\marquee\marquee.jpg");
        Add("screenmarquee", "Screen-marquee", "Screen-marquee", @"artwork\marquee\screenmarquee.png");
        Add("flyer", "Flyer", "Flyer", @"artwork\flyer.jpg", @"artwork\flyer.png");
        Add("screentitle", "Écran titre", "Title screen", @"artwork\screentitle.png");
        Add("screenshot", "Capture de jeu", "In-game screenshot", @"artwork\screenshot.png");
        Add("box3d", "Boîte 3D", "3D box", @"artwork\box\3d.png");
        Add("boxfront", "Boîte (face)", "Box (front)", @"artwork\box\front.png");
        Add("bezel", "Bezel", "Bezel", @"artwork\bezels\bezel.png");
        return assets;
    }

    private string? ReadMetadataField(string system, string rom, string field)
    {
        foreach (var lang in Localization.L.French ? new[] { "fr", "en" } : new[] { "en", "fr" })
        {
            try
            {
                var path = Path.Combine(GameRoot(system, rom), "texts", $"metadata-{lang}.json");
                if (!File.Exists(path))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("Fields", out var fields)
                    && fields.TryGetProperty(field, out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
            catch
            {
                // malformed metadata: try the next language
            }
        }

        return null;
    }
}
