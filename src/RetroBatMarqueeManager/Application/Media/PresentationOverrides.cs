using System.Text.Json;

namespace RetroBatMarqueeManager.Application.Media;

/// <summary>
/// Reads state\media-presentation.json (schema marqueemanager.media-presentation.v1),
/// the per-target "use this source" overrides the Setup's resolution cards write.
/// A target policy enables/disables each fixed-chain source for one surface + scope +
/// system/game; a DISABLED source is skipped when the runtime resolves that target, so
/// clicking a card in the Setup changes what shows on screen. Absent file / absent
/// target = no override, and the default chain runs untouched (no regression).
/// Reloaded on file change.
/// </summary>
public sealed class PresentationOverrides
{
    public enum Source { Personal, UserDrop, Generated, Scraped, Logo, SystemFallback }

    public sealed class TargetPolicy
    {
        private readonly IReadOnlyDictionary<Source, bool> _enabled;
        public TargetPolicy(IReadOnlyDictionary<Source, bool> enabled) => _enabled = enabled;

        /// <summary>A source absent from the document defaults to ENABLED, matching the
        /// Setup's policy defaults (older docs predate the user-drop link).</summary>
        public bool IsEnabled(Source s) => !_enabled.TryGetValue(s, out var e) || e;
    }

    private sealed record Entry(string Scope, string SurfaceId, string? Frontend, string? Canonical, string? Rom,
        IReadOnlyDictionary<Source, bool> Enabled);

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private DateTime _stamp;
    private List<Entry>? _entries;

    public PresentationOverrides(string baseDirectory, ILogger logger)
    {
        _path = Path.Combine(baseDirectory, "state", "media-presentation.json");
        _logger = logger;
    }

    /// <summary>The policy the user set for this exact target, or null when none —
    /// then the caller keeps its default resolution.</summary>
    public TargetPolicy? For(string surfaceId, bool systemScope, string? system, string? rom)
    {
        if (string.IsNullOrEmpty(surfaceId) || string.IsNullOrEmpty(system)) return null;
        var entries = Load();
        if (entries.Count == 0) return null;
        var wantScope = systemScope || string.IsNullOrEmpty(rom) ? "system" : "game";
        foreach (var e in entries)
        {
            if (!e.SurfaceId.Equals(surfaceId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!e.Scope.Equals(wantScope, StringComparison.OrdinalIgnoreCase)) continue;
            if (!SystemMatches(e, system!)) continue;
            if (wantScope == "game" && !RomMatches(e.Rom, rom)) continue;
            return new TargetPolicy(e.Enabled);
        }
        return null;
    }

    private static bool SystemMatches(Entry e, string system)
        => Spelled(e.Frontend, system) || Spelled(e.Canonical, system);

    // mame ↔ arcade tolerance, mirroring the resolver's SystemSpellings: the Setup
    // stores the frontend name (mame) while the runtime is fed the canonical (arcade).
    private static bool Spelled(string? stored, string system)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        if (stored.Equals(system, StringComparison.OrdinalIgnoreCase)) return true;
        if (stored.Equals("mame", StringComparison.OrdinalIgnoreCase) && system.Equals("arcade", StringComparison.OrdinalIgnoreCase)) return true;
        if (stored.Equals("arcade", StringComparison.OrdinalIgnoreCase) && system.Equals("mame", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool RomMatches(string? stored, string? rom)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(rom))
            return string.Equals(stored, rom, StringComparison.OrdinalIgnoreCase);
        return stored.Equals(rom, StringComparison.OrdinalIgnoreCase) || Normalize(stored) == Normalize(rom);
    }

    /// <summary>"Sonic The Hedgehog (USA, Europe)" and the slug "sonic_the_hedgehog"
    /// both reduce to "sonicthehedgehog" — dump tags stripped, then letters/digits only —
    /// so the Setup's physical rom name matches the runtime's slug.</summary>
    private static string Normalize(string s)
    {
        var cut = s.IndexOfAny(new[] { '(', '[' });
        if (cut > 0) s = s[..cut];
        return new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private List<Entry> Load()
    {
        lock (_sync)
        {
            var stamp = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
            if (_entries != null && stamp == _stamp) return _entries;
            _stamp = stamp;
            _entries = new List<Entry>();
            try
            {
                if (File.Exists(_path))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(_path));
                    if (doc.RootElement.TryGetProperty("targetPolicies", out var policies)
                        && policies.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in policies.EnumerateArray())
                            if (Parse(p) is { } entry) _entries.Add(entry);
                    }
                    _logger.LogInformation("Presentation overrides loaded: {Count} target(s)", _entries.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Invalid media-presentation.json ignored: {Message}", ex.Message);
            }
            return _entries;
        }
    }

    private static Entry? Parse(JsonElement p)
    {
        var surfaceId = Str(p, "surfaceId");
        var scope = Str(p, "scope");
        if (surfaceId.Length == 0 || scope.Length == 0) return null;
        var enabled = new Dictionary<Source, bool>();
        if (p.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Object)
        {
            foreach (var s in sources.EnumerateObject())
            {
                if (!TryParseSource(s.Name, out var kind)) continue;
                enabled[kind] = !(s.Value.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False);
            }
        }
        return new Entry(scope, surfaceId,
            NullIfEmpty(Str(p, "frontendSystem")), NullIfEmpty(Str(p, "canonicalSystem")),
            NullIfEmpty(Str(p, "rom")), enabled);
    }

    private static bool TryParseSource(string name, out Source kind)
    {
        switch (name.ToLowerInvariant())
        {
            case "personal": kind = Source.Personal; return true;
            case "userdrop":
            case "user-drop": kind = Source.UserDrop; return true;
            case "generated": kind = Source.Generated; return true;
            case "scraped": kind = Source.Scraped; return true;
            case "logo": kind = Source.Logo; return true;
            case "systemfallback":
            case "system-fallback": kind = Source.SystemFallback; return true;
            default: kind = Source.Personal; return false;
        }
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
