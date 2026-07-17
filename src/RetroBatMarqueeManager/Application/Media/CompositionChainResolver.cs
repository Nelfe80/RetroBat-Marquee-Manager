using System.Text.Json;
using RetroBatMarqueeManager.Application.Lighting;

namespace RetroBatMarqueeManager.Application.Media;

/// <summary>
/// Walks the per-system source chains of media\assignments.json (schema
/// marqueemanager.compositions.v1, written by the Setup) for the three media
/// categories — marquee, topper, dmd. First existing source wins:
///   composition   media\&lt;cat&gt;s\&lt;sys&gt;\&lt;rom&gt;.png (game) / systems\&lt;sys&gt;.png (system scope)
///   user          media\&lt;cat&gt;s\user\&lt;sys&gt;\&lt;rom&gt;.* — the drop folder; file names
///                 resolve through the Setup-built sidecar .index.json (aliases),
///                 falling back to the exact rom name
///   template:&lt;id&gt; media\&lt;cat&gt;s\.cache\&lt;sys&gt;\&lt;rom&gt;-&lt;id&gt;.png (lazy Skia render —
///                 a miss fires the renderer callback and the chain continues)
///   anything else = the matching ws snapshot asset (marquee, screenmarquee,
///                 generated, logo, fanart, topper, animations, still…)
/// No file / no chain → historical defaults (2.2.0 behavior + user first).
/// </summary>
public sealed class CompositionChainResolver
{
    private static readonly string[] MediaExtensions =
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".mp4", ".webm", ".mkv", ".avi", ".mov" };

    private static readonly IReadOnlyDictionary<string, string[]> DefaultChains =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["marquee"] = new[] { "composition", "user", "marquee", "generated", "logo" },
            ["topper"] = new[] { "composition", "user", "topper" },
            ["dmd"] = new[] { "user", "animations", "still", "generated" }
        };

    private readonly string _baseDirectory;
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private Dictionary<string, (string[] Global, Dictionary<string, string[]> Systems)>? _chains;
    private DateTime _chainsStamp;
    private readonly Dictionary<string, (DateTime Stamp, Dictionary<string, string> Map)> _sidecars = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fired when a template PNG is missing: (templateId, system, rom,
    /// systemScope). The host renders it in the background then re-displays.</summary>
    public Action<string, string, string, bool>? TemplateMissing;

    private readonly bool _preferGeneratedMarquee;

    public CompositionChainResolver(string baseDirectory, ILogger logger, bool preferGeneratedMarquee = false)
    {
        _baseDirectory = baseDirectory;
        _logger = logger;
        _preferGeneratedMarquee = preferGeneratedMarquee;
    }

    /// <summary>Resolves the path a category should display, or null to keep the
    /// stream's own offer. snapshotAsset maps a chain source name to the ws
    /// snapshot path (null when the snapshot lacks it).</summary>
    public string? Resolve(string category, LightingSceneMeta? meta, bool systemScope,
        Func<string, string?> snapshotAsset)
    {
        var system = meta?.System;
        var rom = meta?.Rom;
        if (string.IsNullOrEmpty(system)) return null;

        foreach (var source in ChainFor(category, system))
        {
            string? path = null;
            if (source.Equals("composition", StringComparison.OrdinalIgnoreCase))
            {
                path = systemScope || string.IsNullOrEmpty(rom)
                    ? FirstExisting(CategoryRoot(category), "systems", SafeName(system!))
                    : FirstExisting(CategoryRoot(category), SafeName(system!), SafeName(rom!));
            }
            else if (source.Equals("user", StringComparison.OrdinalIgnoreCase))
            {
                path = systemScope || string.IsNullOrEmpty(rom)
                    ? FirstExisting(Path.Combine(CategoryRoot(category), "user"), "systems", SafeName(system!))
                    : ResolveUserFile(category, system!, rom!);
            }
            else if (source.StartsWith("template:", StringComparison.OrdinalIgnoreCase))
            {
                if (systemScope || string.IsNullOrEmpty(rom)) continue;
                var templateId = source["template:".Length..];
                var cached = Path.Combine(CategoryRoot(category), ".cache", SafeName(system!),
                    SafeName(rom!) + "-" + SafeName(templateId) + ".png");
                if (File.Exists(cached))
                {
                    path = cached;
                }
                else
                {
                    TemplateMissing?.Invoke(templateId, system!, rom!, systemScope);
                    continue; // next source keeps the surface fed while it renders
                }
            }
            else
            {
                path = snapshotAsset(source);
            }

            if (path != null && File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>A graphic creation saved for ONE SPECIFIC SURFACE
    /// (media\&lt;cat&gt;s\surfaces\&lt;surfaceId&gt;\…): each surface can carry its own
    /// creation for the same game or system, independent of the category-level
    /// file which stays the default for the other surfaces.</summary>
    public string? SurfaceCreation(string category, string surfaceId, LightingSceneMeta? meta, bool systemScope)
    {
        var system = meta?.System;
        var rom = meta?.Rom;
        if (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(surfaceId)) return null;
        var root = Path.Combine(CategoryRoot(category), "surfaces", SafeName(surfaceId));
        return systemScope || string.IsNullOrEmpty(rom)
            ? FirstExisting(root, "systems", SafeName(system))
            : FirstExisting(root, SafeName(system), SafeName(rom!));
    }

    // ================= chains =================

    private IReadOnlyList<string> ChainFor(string category, string system)
    {
        var chains = LoadChains();
        if (chains.TryGetValue(category, out var entry))
        {
            foreach (var candidate in SystemSpellings(system))
            {
                if (entry.Systems.TryGetValue(candidate, out var chain)) return chain;
            }
            if (entry.Global.Length > 0) return entry.Global;
        }
        // historical option: the generated composite beats the (bad) scan
        if (category.Equals("marquee", StringComparison.OrdinalIgnoreCase) && _preferGeneratedMarquee)
            return new[] { "composition", "user", "generated", "marquee", "logo" };
        return DefaultChains.TryGetValue(category, out var fallback) ? fallback : Array.Empty<string>();
    }

    private Dictionary<string, (string[] Global, Dictionary<string, string[]> Systems)> LoadChains()
    {
        var path = Path.Combine(_baseDirectory, "media", "assignments.json");
        lock (_sync)
        {
            var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (_chains != null && stamp == _chainsStamp) return _chains;
            _chainsStamp = stamp;
            _chains = new Dictionary<string, (string[], Dictionary<string, string[]>)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(path))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.TryGetProperty("categories", out var categories)
                        && categories.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var category in categories.EnumerateObject())
                        {
                            var global = ReadChain(category.Value, "global");
                            var systems = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                            if (category.Value.TryGetProperty("systems", out var systemsElement)
                                && systemsElement.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var system in systemsElement.EnumerateObject())
                                {
                                    var chain = system.Value.ValueKind == JsonValueKind.Array
                                        ? system.Value.EnumerateArray()
                                            .Where(v => v.ValueKind == JsonValueKind.String)
                                            .Select(v => v.GetString() ?? "")
                                            .Where(v => v.Length > 0)
                                            .ToArray()
                                        : Array.Empty<string>();
                                    if (chain.Length > 0) systems[system.Name] = chain;
                                }
                            }
                            _chains[category.Name] = (global, systems);
                        }
                        _logger.LogInformation("Composition chains loaded: {Count} categorie(s)", _chains.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Invalid assignments.json ignored: {Message}", ex.Message);
            }
            return _chains;
        }
    }

    private static string[] ReadChain(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString() ?? "")
                .Where(v => v.Length > 0)
                .ToArray()
            : Array.Empty<string>();

    // ================= user drop folder =================

    private string? ResolveUserFile(string category, string system, string rom)
    {
        foreach (var candidateSystem in SystemSpellings(system))
        {
            var folder = Path.Combine(CategoryRoot(category), "user", SafeName(candidateSystem));
            if (!Directory.Exists(folder)) continue;

            // sidecar written by the Setup: file name → canonical rom (alias-aware)
            var sidecar = LoadSidecar(folder);
            foreach (var (file, mappedRom) in sidecar)
            {
                if (mappedRom.Equals(rom, StringComparison.OrdinalIgnoreCase))
                {
                    var mapped = Path.Combine(folder, file);
                    if (File.Exists(mapped)) return mapped;
                }
            }

            // exact rom name fallback (no Setup indexing needed)
            var exact = FirstExisting(folder, SafeName(rom));
            if (exact != null) return exact;
        }
        return null;
    }

    private Dictionary<string, string> LoadSidecar(string folder)
    {
        var path = Path.Combine(folder, ".index.json");
        lock (_sync)
        {
            var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (_sidecars.TryGetValue(folder, out var cached) && cached.Stamp == stamp) return cached.Map;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(path))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var property in doc.RootElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                            map[property.Name] = property.Value.GetString() ?? "";
                    }
                }
            }
            catch
            {
                // corrupt sidecar: exact-name fallback still works
            }
            _sidecars[folder] = (stamp, map);
            return map;
        }
    }

    // ================= helpers =================

    private string CategoryRoot(string category)
        => Path.Combine(_baseDirectory, "media", category.ToLowerInvariant() + "s");

    /// <summary>media\marquees\a\b.(png|jpg|…) — first existing extension.</summary>
    private static string? FirstExisting(params string[] segments)
    {
        var stem = Path.Combine(segments);
        foreach (var extension in MediaExtensions)
        {
            var path = stem + extension;
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static IEnumerable<string> SystemSpellings(string system)
    {
        yield return system;
        if (system.Equals("mame", StringComparison.OrdinalIgnoreCase)) yield return "arcade";
        else if (system.Equals("arcade", StringComparison.OrdinalIgnoreCase)) yield return "mame";
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
