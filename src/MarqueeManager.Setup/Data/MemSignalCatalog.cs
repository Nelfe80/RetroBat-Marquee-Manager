using System.IO;
using System.Text.RegularExpressions;

namespace MarqueeManager.Setup.Data;

/// <summary>One semantic signal a game's .MEM can fire (ws/ingame action).</summary>
public sealed record MemSignal(string Action, string Family, string Description);

/// <summary>
/// Read-only index of the APIExpose .MEM definitions
/// (..\APIExpose\resources\ram\&lt;system&gt;\*.MEM, Lua tables). Files are named after
/// the friendly game title, so the rom is matched through `rom.name` and the zip
/// base names of `rom.hashes[].label`. The line parser follows RetroCreator's
/// MemSignalsParser rules: `action="X"` plus action_map values, entries flagged
/// no_log/no_survey are dead (the wrapper skips them at load) and IGNORE/UNKNOWN
/// are noise — none of those are offered as bindable signals.
/// </summary>
public sealed partial class MemSignalCatalog
{
    [GeneratedRegex("action\\s*=\\s*\"([A-Z0-9_]+)\"")]
    private static partial Regex ActionRegex();

    [GeneratedRegex("action_map\\s*=\\s*\\{([^}]*)\\}")]
    private static partial Regex ActionMapRegex();

    [GeneratedRegex("=\\s*\"([A-Z0-9_]+)\"")]
    private static partial Regex MapValueRegex();

    [GeneratedRegex("desc\\s*=\\s*\"([^\"]*)\"")]
    private static partial Regex DescRegex();

    [GeneratedRegex("^\\s*([a-zA-Z_][a-zA-Z0-9_]*)\\s*=\\s*\\{")]
    private static partial Regex TableOpenRegex();

    [GeneratedRegex("name\\s*=\\s*\"([^\"]+)\"")]
    private static partial Regex RomNameRegex();

    // full label captured: console labels carry spaces ("Sonic The Hedgehog
    // (USA, Europe).md"); the arcade "<set description>" tail is cut afterwards
    [GeneratedRegex("label\\s*=\\s*\"([^\"]+)\"")]
    private static partial Regex HashLabelRegex();

    private readonly string _ramRoot;
    private readonly object _sync = new();
    private readonly Dictionary<string, SystemIndex> _indexBySystem = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Exact keys (alias.json entries, basenames, rom.name, hash labels)
    /// plus a punctuation-tolerant projection of every key.</summary>
    private sealed record SystemIndex(Dictionary<string, string> Exact, Dictionary<string, string> Normalized);

    public MemSignalCatalog(string pluginRoot)
    {
        _ramRoot = Path.GetFullPath(Path.Combine(pluginRoot, "..", "APIExpose", "resources", "ram"));
    }

    public bool IsAvailable => Directory.Exists(_ramRoot);

    /// <summary>The .MEM file describing this rom, or null when the game has none.
    /// Same resolution order as the runtime wrapper: alias.json exact key first,
    /// then a normalized match, finally the dump tags are stripped
    /// ("Sonic The Hedgehog (Europe)" still finds sonic-the-hedgehog).</summary>
    public string? FindMemFile(string system, string rom)
    {
        var index = IndexFor(system);
        if (index.Exact.TryGetValue(rom, out var path)) return path;
        if (index.Normalized.TryGetValue(NormalizeKey(rom), out path)) return path;
        var stripped = StripDumpTags(rom);
        return !ReferenceEquals(stripped, rom) && index.Normalized.TryGetValue(NormalizeKey(stripped), out path)
            ? path
            : null;
    }

    /// <summary>Signals of a .MEM file, grouped by family path (e.g. "flow.lifecycle").</summary>
    public IReadOnlyList<MemSignal> ReadSignals(string memPath)
    {
        var signals = new List<MemSignal>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // shallow Lua walk: named tables push a family segment, brace balance pops it
            var familyStack = new Stack<(string Name, int Depth)>();
            var depth = 0;
            var inEvents = false;
            var eventsDepth = 0;

            foreach (var raw in File.ReadLines(memPath))
            {
                var line = raw;
                var open = TableOpenRegex().Match(line);
                if (open.Success)
                {
                    var name = open.Groups[1].Value;
                    // only the TOP-LEVEL "events" opens the section: some games
                    // nest a family also named "events" (flow.events in garou) —
                    // it must stack as a family, not reset the section depth
                    if (name.Equals("events", StringComparison.OrdinalIgnoreCase) && !inEvents)
                    {
                        inEvents = true;
                        eventsDepth = depth;
                    }
                    else if (inEvents)
                    {
                        familyStack.Push((name, depth));
                    }
                }

                if (inEvents && !IsDeadEntry(line))
                {
                    var family = string.Join('.', familyStack.Reverse().Select(f => f.Name));
                    var desc = DescRegex().Match(line) is { Success: true } d ? d.Groups[1].Value : "";

                    foreach (Match m in ActionRegex().Matches(line))
                    {
                        AddSignal(signals, seen, m.Groups[1].Value, family, desc);
                    }

                    foreach (Match map in ActionMapRegex().Matches(line))
                    {
                        foreach (Match v in MapValueRegex().Matches(map.Groups[1].Value))
                        {
                            AddSignal(signals, seen, v.Groups[1].Value, family, desc);
                        }
                    }
                }

                depth += line.Count(c => c == '{') - line.Count(c => c == '}');
                while (familyStack.Count > 0 && depth <= familyStack.Peek().Depth)
                {
                    familyStack.Pop();
                }
                if (inEvents && depth <= eventsDepth)
                {
                    inEvents = false;
                }
            }
        }
        catch
        {
            // unreadable .MEM: no signals
        }

        return signals;
    }

    private static bool IsDeadEntry(string line)
        => line.Contains("no_log=true", StringComparison.OrdinalIgnoreCase)
           || line.Contains("no_log = true", StringComparison.OrdinalIgnoreCase)
           || line.Contains("no_survey=true", StringComparison.OrdinalIgnoreCase)
           || line.Contains("no_survey = true", StringComparison.OrdinalIgnoreCase);

    private static void AddSignal(List<MemSignal> signals, HashSet<string> seen, string action, string family, string desc)
    {
        if (action is "IGNORE" or "UNKNOWN" || !seen.Add(action))
        {
            return;
        }

        signals.Add(new MemSignal(action, family, desc));
    }

    /// <summary>rom → .MEM path map for a system, built once per session. Primary
    /// source: the mem-curator alias.json referential (every known dump name,
    /// slug variant and hash of a game → its canonical .MEM basename) — the
    /// exact file the runtime wrapper resolves with. The header scan
    /// (rom.name + hash labels) only complements systems without alias.json.</summary>
    private SystemIndex IndexFor(string system)
    {
        // ES exposes MAME sets under the canonical "arcade" system
        var folder = system.Equals("arcade", StringComparison.OrdinalIgnoreCase) ? "arcade" : system;
        lock (_sync)
        {
            if (_indexBySystem.TryGetValue(folder, out var cached))
            {
                return cached;
            }

            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dir = Path.Combine(_ramRoot, folder);
            if (Directory.Exists(dir))
            {
                var hasAliases = IndexAliases(dir, index);
                foreach (var file in Directory.EnumerateFiles(dir, "*.MEM"))
                {
                    // the file basename (= rom.name slug) is a valid key too
                    index.TryAdd(Path.GetFileNameWithoutExtension(file), file);
                    if (!hasAliases)
                    {
                        IndexHeader(file, index);
                    }
                }
            }

            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in index)
            {
                var key = NormalizeKey(pair.Key);
                if (key.Length > 0) normalized.TryAdd(key, pair.Value);
            }

            var built = new SystemIndex(index, normalized);
            _indexBySystem[folder] = built;
            return built;
        }
    }

    /// <summary>Loads alias.json (raw names, slugs, extensions, hashes → canonical
    /// basename). Duplicate keys differing only by case are legal there — the
    /// case-insensitive index absorbs them. Returns false when the file is
    /// missing or unreadable.</summary>
    private static bool IndexAliases(string dir, Dictionary<string, string> index)
    {
        var path = Path.Combine(dir, "alias.json");
        if (!File.Exists(path)) return false;
        try
        {
            // one directory listing instead of a stat per alias entry (tens of
            // thousands of keys on the big systems)
            var existing = Directory.EnumerateFiles(dir, "*.MEM")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            var count = 0;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                var target = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(target) || !existing.Contains(target)) continue;
                index.TryAdd(property.Name, Path.Combine(dir, target + ".MEM"));
                count++;
            }
            return count > 0;
        }
        catch
        {
            return false; // malformed referential: header scan takes over
        }
    }

    /// <summary>"Sonic The Hedgehog (USA, Europe) [!]" → "Sonic The Hedgehog".</summary>
    private static string StripDumpTags(string rom)
    {
        var cut = rom.IndexOfAny(new[] { '(', '[' });
        return cut > 0 ? rom[..cut].TrimEnd() : rom;
    }

    /// <summary>Case/punctuation-tolerant key: "Streets of Rage 2" == "streets-of-rage-2".</summary>
    private static string NormalizeKey(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static void IndexHeader(string file, Dictionary<string, string> index)
    {
        try
        {
            // rom.name and hash labels sit in the first lines; 80 is generous
            var inRom = false;
            foreach (var line in File.ReadLines(file).Take(80))
            {
                if (line.Contains("rom", StringComparison.OrdinalIgnoreCase) && TableOpenRegex().IsMatch(line))
                {
                    inRom = TableOpenRegex().Match(line).Groups[1].Value
                        .Equals("rom", StringComparison.OrdinalIgnoreCase);
                }

                if (!inRom)
                {
                    continue;
                }

                if (RomNameRegex().Match(line) is { Success: true } name)
                {
                    index.TryAdd(name.Groups[1].Value, file);
                }

                foreach (Match label in HashLabelRegex().Matches(line))
                {
                    var raw = label.Groups[1].Value;
                    var cut = raw.IndexOf('<');       // "garou.7z <Garou - Mark…>"
                    if (cut >= 0) raw = raw[..cut];
                    var zipBase = Path.GetFileNameWithoutExtension(raw.Trim());
                    if (zipBase.Length > 0)
                    {
                        index.TryAdd(zipBase, file);
                    }
                }

                if (line.Contains("events", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }
        catch
        {
            // unreadable header: file skipped
        }
    }
}
