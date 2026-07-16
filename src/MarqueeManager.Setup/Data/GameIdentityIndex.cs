using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace MarqueeManager.Setup.Data;

/// <summary>Canonical identity of a game: its rom slug and display name.</summary>
public sealed record GameIdentity(string Rom, string Name);

/// <summary>
/// THE identity service rom↔names↔aliases, per system. Three layered sources
/// (LedManager's cascade), all cached and loaded off the UI thread:
///  1. APIExpose REST GET /api/v1/gamelists/{system}/games — installed games,
///     the names EmulationStation shows;
///  2. roms\&lt;system&gt;\gamelist.xml — same data read locally;
///  3. the APIExpose gamelist pack (resources\gamelist\systems\&lt;sys&gt;_lt.json,
///     JSONL streamed line by line — NEVER loaded whole, files reach 168 MB) —
///     adds `fn` (full file name) and `aka` aliases, so a file the user names
///     "Metal Slug (World).png" still resolves to mslug.
/// Serves the game search AND the user-drop-folder resolution.
/// </summary>
public sealed class GameIdentityIndex
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    private readonly string _pluginRoot;
    private readonly string _apiBaseUrl;
    private readonly object _sync = new();
    private readonly Dictionary<string, IReadOnlyList<GameIdentity>> _namesBySystem = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _aliasesBySystem = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string>? _systemAliases;

    public GameIdentityIndex(string pluginRoot, string apiBaseUrl)
    {
        _pluginRoot = pluginRoot;
        var url = apiBaseUrl.TrimEnd('/');
        if (url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)) url = "http://" + url["ws://".Length..];
        if (url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url["wss://".Length..];
        _apiBaseUrl = url;
    }

    /// <summary>rom → display name pairs of a system (installed first, pack fallback).
    /// Call off the UI thread; result is cached for the session.</summary>
    public async Task<IReadOnlyList<GameIdentity>> NamesAsync(string system)
    {
        lock (_sync)
        {
            if (_namesBySystem.TryGetValue(system, out var cached)) return cached;
        }

        var names = await LoadFromApiAsync(system).ConfigureAwait(false)
                    ?? LoadFromGamelistXml(system)
                    ?? new List<GameIdentity>();

        lock (_sync)
        {
            _namesBySystem[system] = names;
        }
        return names;
    }

    /// <summary>alias (id, set, fn, aka, name — normalized) → canonical rom, from the
    /// gamelist pack JSONL. Heavy (streams the whole file once); cached per system.
    /// Call off the UI thread.</summary>
    public IReadOnlyDictionary<string, string> AliasMap(string system)
    {
        lock (_sync)
        {
            if (_aliasesBySystem.TryGetValue(system, out var cached)) return cached;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = PackPath(system);
        if (path != null)
        {
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (line.Length < 2) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        // the canonical rom slug is `set` (zip base) falling back to `id`
                        var rom = Str(root, "set");
                        if (rom.Length == 0) rom = Str(root, "id");
                        if (rom.Length == 0) continue;

                        AddAlias(map, rom, rom);
                        AddAlias(map, Str(root, "id"), rom);
                        AddAlias(map, Str(root, "fn"), rom);
                        AddAlias(map, Str(root, "n"), rom);
                        if (root.TryGetProperty("aka", out var aka) && aka.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var alias in aka.EnumerateArray())
                            {
                                if (alias.ValueKind == JsonValueKind.String)
                                    AddAlias(map, alias.GetString() ?? "", rom);
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // malformed line: skipped
                    }
                }
            }
            catch
            {
                // unreadable pack: alias resolution degrades to exact match
            }
        }

        lock (_sync)
        {
            _aliasesBySystem[system] = map;
        }
        return map;
    }

    /// <summary>Resolves any user-provided name (file stem, alias, display name)
    /// to the canonical rom. Exact rom match short-circuits without the pack.</summary>
    public string? ResolveRom(string system, string anyName, IReadOnlyList<GameIdentity>? knownRoms = null)
    {
        var normalized = Normalize(anyName);
        if (normalized.Length == 0) return null;
        if (knownRoms != null && knownRoms.Any(g => Normalize(g.Rom) == normalized))
            return knownRoms.First(g => Normalize(g.Rom) == normalized).Rom;
        return AliasMap(system).TryGetValue(normalized, out var rom) ? rom : null;
    }

    /// <summary>Search across rom, display name and pack aliases.</summary>
    public async Task<IReadOnlyList<GameIdentity>> SearchAsync(string system, string query, int limit = 40)
    {
        var names = await NamesAsync(system).ConfigureAwait(false);
        var matches = names
            .Where(g => g.Rom.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || g.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();
        return matches;
    }

    // ================= sources =================

    private async Task<List<GameIdentity>?> LoadFromApiAsync(string system)
    {
        try
        {
            var json = await Http.GetStringAsync($"{_apiBaseUrl}/api/v1/gamelists/{Uri.EscapeDataString(system)}/games")
                .ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var element = doc.RootElement;
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("games", out var games))
                element = games;
            if (element.ValueKind != JsonValueKind.Array) return null;

            var result = new List<GameIdentity>();
            foreach (var game in element.EnumerateArray())
            {
                var rom = Str(game, "rom", "Rom");
                var name = Str(game, "name", "Name");
                if (rom.Length > 0)
                    result.Add(new GameIdentity(rom, name.Length > 0 ? name : rom));
            }
            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null; // APIExpose down: local fallbacks
        }
    }

    private List<GameIdentity>? LoadFromGamelistXml(string system)
    {
        try
        {
            // RetroBat root is two levels above the plugin folder
            var path = Path.GetFullPath(Path.Combine(_pluginRoot, "..", "..", "roms", system, "gamelist.xml"));
            if (!File.Exists(path)) return null;
            var result = new List<GameIdentity>();
            foreach (var game in XDocument.Load(path).Descendants("game"))
            {
                var rom = Path.GetFileNameWithoutExtension(((string?)game.Element("path") ?? "").Trim('.', '/', '\\'));
                var name = (string?)game.Element("name") ?? "";
                if (rom.Length > 0)
                    result.Add(new GameIdentity(rom, name.Length > 0 ? name : rom));
            }
            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private string? PackPath(string system)
    {
        var root = Path.GetFullPath(Path.Combine(_pluginRoot, "..", "APIExpose", "resources", "gamelist", "systems"));
        var direct = Path.Combine(root, system.ToLowerInvariant() + "_lt.json");
        if (File.Exists(direct)) return direct;

        // aliases.json maps e.g. amiga500 → amiga_lt.json ; mame → arcade
        try
        {
            _systemAliases ??= LoadSystemAliases(Path.Combine(root, "aliases.json"));
            if (_systemAliases.TryGetValue(system, out var jsonl))
            {
                var mapped = Path.Combine(root, jsonl);
                if (File.Exists(mapped)) return mapped;
            }
        }
        catch
        {
            // no aliases file
        }

        if (system.Equals("mame", StringComparison.OrdinalIgnoreCase))
        {
            var arcade = Path.Combine(root, "arcade_lt.json");
            if (File.Exists(arcade)) return arcade;
        }
        return null;
    }

    private static Dictionary<string, string> LoadSystemAliases(string path)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return aliases;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object
                && property.Value.TryGetProperty("jsonl", out var jsonl)
                && jsonl.ValueKind == JsonValueKind.String)
            {
                aliases[property.Name] = jsonl.GetString() ?? "";
            }
        }
        return aliases;
    }

    private static void AddAlias(Dictionary<string, string> map, string alias, string rom)
    {
        var normalized = Normalize(alias);
        if (normalized.Length > 0) map.TryAdd(normalized, rom);
    }

    /// <summary>Case/punctuation-tolerant key: "Metal Slug (World)" == "metal slug world".</summary>
    private static string Normalize(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) builder.Append(c);
        }
        return builder.ToString();
    }

    private static string Str(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";
        }
        return "";
    }
}
