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

    public IReadOnlyList<string> ListSystems()
    {
        try
        {
            return Directory.EnumerateDirectories(_systemsRoot)
                .Where(dir => Directory.Exists(Path.Combine(dir, "games")))
                .Select(Path.GetFileName)
                .OfType<string>()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
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
