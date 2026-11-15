using System.Globalization;
using System.Text.Json;

namespace RetroBatMarqueeManager.Application.Services;

// What APIExpose 1.9.185 added for the plugins, read here with no dependency on WPF, on
// the configuration or on the network - the test project compiles this very file.

/// <summary>What <c>records/leaderboard</c> says about the board it returned, beyond the
/// rows: which mode it is, and why that one.</summary>
public sealed record WorldBoardInfo(
    string Ruleset, string Label, string Source, string System, string RomGroup, string PlayedRuleset, string Difficulty)
{
    public static readonly WorldBoardInfo Unknown = new("", "", "", "", "", "", "");
}

public static class WorldBoard
{
    /// <summary>Reads the mode of a world board. An older APIExpose sends none of these
    /// fields: the label is then empty and the title stays as it always was.</summary>
    public static WorldBoardInfo Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return WorldBoardInfo.Unknown;
        var ruleset = Str(root, "ruleset");
        var label = Str(root, "ruleset_label");
        // the label of the selected mode in modes[] is the same text; it covers a reply
        // that carries the list but not the shortcut
        if (label.Length == 0 && root.TryGetProperty("modes", out var modes) && modes.ValueKind == JsonValueKind.Array)
        {
            foreach (var mode in modes.EnumerateArray())
            {
                if (mode.ValueKind != JsonValueKind.Object) continue;
                var selected = mode.TryGetProperty("selected", out var s) && s.ValueKind == JsonValueKind.True;
                if (selected || (ruleset.Length > 0 && Str(mode, "ruleset").Equals(ruleset, StringComparison.OrdinalIgnoreCase)))
                {
                    label = Str(mode, "label");
                    if (label.Length > 0) break;
                }
            }
        }

        return new WorldBoardInfo(
            ruleset, label.Trim(), Str(root, "ruleset_source"), Str(root, "system"), Str(root, "rom_group"),
            Str(root, "played_ruleset"), Difficulty(root));
    }

    /// <summary>The reply describes APIExpose's CURRENT game, not the one we asked about:
    /// when it names another system than the selection, it answers a selection the user
    /// has already left. An older APIExpose names no system - nothing to compare.</summary>
    public static bool IsForSelection(WorldBoardInfo info, string? selectedSystem)
        => info.System.Length == 0 || string.IsNullOrEmpty(selectedSystem)
           || info.System.Equals(selectedSystem, StringComparison.OrdinalIgnoreCase);

    /// <summary>Only a difficulty READ in the running game is shown. The profile's factory
    /// value is what the game would have without anyone touching it - displaying it would
    /// claim a measurement that was never made.</summary>
    private static string Difficulty(JsonElement root)
    {
        if (!root.TryGetProperty("difficulty", out var list) || list.ValueKind != JsonValueKind.Array) return string.Empty;
        var parts = new List<string>();
        foreach (var field in list.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object) continue;
            if (!(field.TryGetProperty("measured", out var m) && m.ValueKind == JsonValueKind.True)) continue;
            var name = Str(field, "name");
            if (name.Length == 0 || !field.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.Number) continue;
            parts.Add(name + " " + v.GetRawText());
        }
        return string.Join(" · ", parts);
    }

    /// <summary>Puts the mode in a title. <c>{mode}</c> is replaced where the operator
    /// placed it; a title that never mentions it is left exactly as written. Without a
    /// label the token leaves no hole behind.</summary>
    public static string TitleWithMode(string title, string label)
    {
        if (string.IsNullOrEmpty(title)) return string.Empty;
        var filled = title.Replace("{mode}", label ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        if (ReferenceEquals(filled, title) || filled == title) return title;
        while (filled.Contains("  ", StringComparison.Ordinal)) filled = filled.Replace("  ", " ", StringComparison.Ordinal);
        return filled.Trim().TrimEnd('-', '·', ':').TrimEnd();
    }

    private static string Str(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
}

/// <summary>
/// One start per launch. APIExpose can announce the same start twice - its own launcher,
/// then EmulationStation's hook - and each announcement used to replay the launch effects.
/// Since 1.9.185 both carry the same <c>LaunchId</c>. An older APIExpose sends none: every
/// start is then taken as new, as before.
/// </summary>
public sealed class LaunchTracker
{
    private string _current = string.Empty;

    public string Current => _current;

    /// <summary>True when this start is the one already in progress.</summary>
    public bool IsRepeatedStart(string? launchId)
    {
        var id = (launchId ?? string.Empty).Trim();
        if (id.Length == 0) return false;
        if (id.Equals(_current, StringComparison.OrdinalIgnoreCase)) return true;
        _current = id;
        return false;
    }

    /// <summary>The game is over. A stale end - one that names a launch that is no longer
    /// the current one - still ends the play session (the marquee must never stay on a
    /// game screen), it just does not forget the launch in progress.</summary>
    public void Ended(string? launchId)
    {
        var id = (launchId ?? string.Empty).Trim();
        if (id.Length == 0 || id.Equals(_current, StringComparison.OrdinalIgnoreCase)) _current = string.Empty;
    }
}

/// <summary>One meaning of the running game's memory: "player 1's bombs", "the shot level".</summary>
public sealed record SemanticSignal(
    string Id, int Player, string Kind, string Unit, string Label, string Short, long? Min, long? Max, long? Initial);

/// <summary>The current reading of a signal. <see cref="Text"/> is the game's own word
/// for the value when the semantics give one ("Vulcan"), else empty.</summary>
public sealed record SemanticReading(SemanticSignal Signal, long Value, string Text);

/// <summary>One instrument of the game's dashboard: a named group of signals and the
/// shape the semantics ask for.</summary>
public sealed record SemanticInstrument(string Id, string Label, int Player, string Show, IReadOnlyList<string> Signals);

/// <summary>
/// The meaning of the running game's signals (its .SMEM), as <c>deck.semantics</c> gives
/// it, and the value each one currently holds.
///
/// A memory change names an address, an action and a player; the semantics say which
/// signal those three belong to. Nothing is guessed: a change that matches no line of
/// the semantics is not a signal, and is ignored here.
/// </summary>
public sealed class SignalSemantics
{
    private sealed record Source(long Address, string Action, int? Player);
    private sealed record Mapped(long? Raw, long? RawMin, long? RawMax, long? Value, string Label);
    private sealed record Entry(SemanticSignal Signal, List<Source> From, List<Mapped> Values, long Num, long Den, long Add);

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _signals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemanticReading> _readings = new(StringComparer.OrdinalIgnoreCase);
    private List<SemanticInstrument> _instruments = new();

    public string LaunchId { get; private set; } = string.Empty;
    public string Rom { get; private set; } = string.Empty;

    public bool HasSignals { get { lock (_lock) return _signals.Count > 0; } }

    public IReadOnlyList<SemanticInstrument> Instruments { get { lock (_lock) return _instruments.ToArray(); } }

    public IReadOnlyList<SemanticSignal> Signals { get { lock (_lock) return _signals.Values.Select(e => e.Signal).ToArray(); } }

    /// <summary>Everything known right now, for a surface that opens mid-game.</summary>
    public IReadOnlyList<SemanticReading> Readings { get { lock (_lock) return _readings.Values.ToArray(); } }

    /// <summary>
    /// Takes a <c>deck.semantics</c> payload. Returns true when the set of signals changed
    /// hands (another launch, another game, or semantics that were not there before).
    ///
    /// Readings survive a re-publication for the SAME launch: APIExpose re-reads the
    /// semantics when the first signal names another definition file, and the values
    /// already received are still those of this game.
    /// </summary>
    public bool Load(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return false;
        var status = Str(payload, "Status", "status");
        var launch = Str(payload, "LaunchId", "launchId");
        var rom = Str(payload, "Rom", "rom");

        lock (_lock)
        {
            var sameGame = launch.Length > 0
                ? launch.Equals(LaunchId, StringComparison.OrdinalIgnoreCase)
                : rom.Equals(Rom, StringComparison.OrdinalIgnoreCase) && LaunchId.Length == 0;
            var before = _signals.Count;

            _signals.Clear();
            _instruments = new List<SemanticInstrument>();
            if (!sameGame) _readings.Clear();
            LaunchId = launch;
            Rom = rom;

            if (!status.Equals("loaded", StringComparison.OrdinalIgnoreCase)) return before > 0 || !sameGame;

            foreach (var item in List(payload, "Signals", "signals"))
            {
                var id = Str(item, "Id", "id");
                if (id.Length == 0) continue;
                // a signal the game does not have in this configuration is declared but
                // not available: showing its gauge would show a permanent zero
                if (item.TryGetProperty("Available", out var av) && av.ValueKind == JsonValueKind.False) continue;

                var signal = new SemanticSignal(
                    id,
                    (int)(Num(item, "Player", "player") ?? PlayerOf(id)),
                    Str(item, "Kind", "kind").ToLowerInvariant(),
                    Str(item, "Unit", "unit"), Str(item, "Label", "label"), Str(item, "Short", "short"),
                    Num(item, "Min", "min"), Num(item, "Max", "max"), Num(item, "Initial", "initial"));

                var from = new List<Source>();
                foreach (var line in List(item, "From", "from"))
                {
                    var address = Address(line);
                    var action = Str(line, "Action", "action");
                    if (address is null || action.Length == 0) continue;
                    from.Add(new Source(address.Value, action, (int?)Num(line, "Player", "player")));
                }
                if (from.Count == 0) continue;

                var values = new List<Mapped>();
                foreach (var v in List(item, "Values", "values"))
                    values.Add(new Mapped(Num(v, "Raw", "raw"), Num(v, "RawMin", "rawMin"), Num(v, "RawMax", "rawMax"),
                        Num(v, "Value", "value"), Str(v, "Label", "label")));

                long num = 1, den = 1, add = 0;
                if (TryChild(item, out var scale, "Scale", "scale") && scale.ValueKind == JsonValueKind.Object)
                {
                    num = Num(scale, "Num", "num") ?? 1;
                    den = Num(scale, "Den", "den") ?? 1;
                    add = Num(scale, "Add", "add") ?? 0;
                    if (den == 0) den = 1;
                }

                _signals[id] = new Entry(signal, from, values, num, den, add);
            }

            foreach (var item in List(payload, "Instruments", "instruments"))
            {
                var id = Str(item, "Id", "id");
                if (id.Length == 0) continue;
                var members = List(item, "Signals", "signals")
                    .Where(s => s.ValueKind == JsonValueKind.String)
                    .Select(s => s.GetString() ?? string.Empty)
                    .Where(s => _signals.ContainsKey(s))
                    .ToList();
                if (members.Count == 0) continue;
                _instruments.Add(new SemanticInstrument(
                    id, Str(item, "Label", "label"), (int)(Num(item, "Player", "player") ?? PlayerOf(id)),
                    Str(item, "Show", "show").ToLowerInvariant(), members));
            }

            // a reading whose signal is gone must not outlive it
            foreach (var orphan in _readings.Keys.Where(k => !_signals.ContainsKey(k)).ToList()) _readings.Remove(orphan);
            // what the game starts with is known before the first change is seen
            foreach (var entry in _signals.Values)
            {
                if (_readings.ContainsKey(entry.Signal.Id) || entry.Signal.Initial is not { } initial) continue;
                _readings[entry.Signal.Id] = new SemanticReading(entry.Signal, initial, string.Empty);
            }

            return !sameGame || before != _signals.Count;
        }
    }

    /// <summary>The game is over: its signals mean nothing for the next one.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _signals.Clear();
            _readings.Clear();
            _instruments = new List<SemanticInstrument>();
            LaunchId = string.Empty;
            Rom = string.Empty;
        }
    }

    /// <summary>
    /// One memory change. Returns the readings it moved - usually one, none when the
    /// change is not a signal of the semantics or when the value did not move.
    /// </summary>
    public IReadOnlyList<SemanticReading> Apply(string? address, string? action, int? player, long rawValue)
    {
        var parsed = ParseAddress(address);
        if (parsed is null || string.IsNullOrEmpty(action)) return Array.Empty<SemanticReading>();

        List<SemanticReading>? changed = null;
        lock (_lock)
        {
            foreach (var entry in _signals.Values)
            {
                var matches = false;
                foreach (var source in entry.From)
                {
                    if (source.Address != parsed.Value || !source.Action.Equals(action, StringComparison.OrdinalIgnoreCase)) continue;
                    // a line that names no player takes the change whoever it is for
                    if (source.Player is { } wanted && player is { } given && wanted != given) continue;
                    matches = true;
                    break;
                }
                if (!matches) continue;

                var (value, text) = Resolve(entry, rawValue);
                if (_readings.TryGetValue(entry.Signal.Id, out var known) && known.Value == value && known.Text == text) continue;
                var reading = new SemanticReading(entry.Signal, value, text);
                _readings[entry.Signal.Id] = reading;
                (changed ??= new List<SemanticReading>()).Add(reading);
            }
        }
        return changed ?? (IReadOnlyList<SemanticReading>)Array.Empty<SemanticReading>();
    }

    private static (long Value, string Text) Resolve(Entry entry, long raw)
    {
        foreach (var mapped in entry.Values)
        {
            var hit = mapped.Raw is { } exact
                ? exact == raw
                : mapped.RawMin is { } low && mapped.RawMax is { } high && raw >= low && raw <= high;
            if (hit) return (mapped.Value ?? raw, mapped.Label);
        }
        return (raw * entry.Num / entry.Den + entry.Add, string.Empty);
    }

    /// <summary>"players.2.resources.bombs" is player 2's; anything else is player 1's.</summary>
    private static int PlayerOf(string id)
    {
        var parts = id.Split('.');
        return parts.Length > 1 && parts[0].Equals("players", StringComparison.OrdinalIgnoreCase)
               && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p > 0
            ? p
            : 1;
    }

    private static long? Address(JsonElement line)
    {
        if (!TryChild(line, out var value, "Address", "address")) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var n) => n,
            JsonValueKind.String => ParseAddress(value.GetString()),
            _ => null
        };
    }

    /// <summary>"0xFF8305", "FF8305h", "16745221": the three spellings the streams use.</summary>
    public static long? ParseAddress(string? text)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return null;
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(t.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;
        if (t.EndsWith("h", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(t.AsSpan(0, t.Length - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex2) ? hex2 : null;
        return long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec) ? dec : null;
    }

    private static bool TryChild(JsonElement obj, out JsonElement value, params string[] names)
    {
        if (obj.ValueKind == JsonValueKind.Object)
            foreach (var name in names)
                if (obj.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }

    private static string Str(JsonElement obj, params string[] names)
        => TryChild(obj, out var v, names) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static long? Num(JsonElement obj, params string[] names)
        => TryChild(obj, out var v, names) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    private static IEnumerable<JsonElement> List(JsonElement obj, params string[] names)
        => TryChild(obj, out var v, names) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();
}

/// <summary>
/// What an instrument draws, decided here so the drawing has nothing to decide: which
/// signal, how many places, and whether the player has run dry.
///
/// Generic on purpose: an instrument is "a signal with a role", not "the ammunition of a
/// shooter". Bombs, lives, shield, a shot level and a carried weapon all go through it.
/// </summary>
public static class InstrumentPlan
{
    /// <summary>More places than this cannot be told apart on a marquee: the instrument
    /// becomes one continuous bar with its number.</summary>
    public const int MaxPlaces = 24;

    /// <summary>
    /// The signal a component shows. <paramref name="wanted"/> is what the operator chose:
    /// a full identifier, the end of one ("bombs", "power.level"), a unit, or "auto".
    ///
    /// A role is matched on the END of the identifier: the games' semantics write
    /// "players.1.bombs" as well as "players.1.resources.bombs", and "bombs" must find
    /// both without ever finding "players.1.bombs.used".
    ///
    /// Auto takes what a player looks at first - what can run out - among the roles
    /// below, in that order, and nothing else: a frame counter or a section timer is
    /// also "a counter", and drawing it as a bar of ammunition would be a lie.
    /// </summary>
    public static SemanticSignal? Pick(IReadOnlyList<SemanticSignal> signals, int player, string? wanted)
    {
        var mine = signals.Where(s => s.Player == player).ToList();
        var want = (wanted ?? string.Empty).Trim();
        if (want.Length > 0 && !want.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return signals.FirstOrDefault(s => s.Id.Equals(want, StringComparison.OrdinalIgnoreCase))
                   ?? mine.FirstOrDefault(s => s.Id.EndsWith("." + want, StringComparison.OrdinalIgnoreCase))
                   ?? mine.FirstOrDefault(s => s.Unit.Equals(want, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var role in AutoRoles)
        {
            var byRole = mine.FirstOrDefault(s => Depletes(s) && s.Id.EndsWith("." + role, StringComparison.OrdinalIgnoreCase));
            if (byRole != null) return byRole;
        }
        return null;
    }

    /// <summary>What "auto" may show, most urgent first.</summary>
    public static readonly IReadOnlyList<string> AutoRoles = new[] { "ammo", "bombs", "lives", "health", "energy", "shield" };

    /// <summary>A quantity the player spends: it has a floor, and reaching it means something.</summary>
    public static bool Depletes(SemanticSignal signal) => signal.Kind is "counter" or "gauge";

    /// <summary>
    /// How many places the instrument has. The declared maximum when the semantics give
    /// one; otherwise the highest value seen so far (a game whose maximum was never
    /// reached in the proofs still shows every bomb it hands out). Zero means "too many
    /// to count": draw a continuous bar.
    /// </summary>
    public static int Places(SemanticSignal signal, long value, long highestSeen)
    {
        var places = Top(signal, value, highestSeen) - (signal.Min ?? 0);
        if (places <= 0) return 1;
        return places > MaxPlaces ? 0 : (int)places;
    }

    /// <summary>The share of the bar that is filled, between 0 and 1.</summary>
    public static double Fill(SemanticSignal signal, long value, long highestSeen)
    {
        var floor = signal.Min ?? 0;
        var top = Top(signal, value, highestSeen);
        return top <= floor ? 0 : Math.Clamp((value - floor) / (double)(top - floor), 0, 1);
    }

    private static long Top(SemanticSignal signal, long value, long highestSeen)
        => signal.Max ?? Math.Max(Math.Max(highestSeen, signal.Initial ?? 0), value);

    /// <summary>Run dry: a spendable quantity sitting on its floor.</summary>
    public static bool IsEmpty(SemanticSignal signal, long value) => Depletes(signal) && value <= (signal.Min ?? 0);

    /// <summary>What to tell a player who has run dry. Ammunition is reloaded; anything
    /// else is simply out. The operator's own text wins.</summary>
    public static string EmptyText(SemanticSignal signal, string? custom, bool french)
    {
        if (!string.IsNullOrWhiteSpace(custom)) return custom.Trim();
        var what = (signal.Unit + " " + signal.Id).ToLowerInvariant();
        var reloads = what.Contains("ammo") || what.Contains("bullet") || what.Contains("round")
                      || what.Contains("clip") || what.Contains("shell") || what.Contains("magazine");
        return reloads ? (french ? "RECHARGEZ" : "RELOAD") : (french ? "VIDE" : "EMPTY");
    }

    /// <summary>The short name drawn next to the bar.</summary>
    public static string Caption(SemanticSignal signal)
    {
        var text = signal.Short.Length > 0 ? signal.Short : signal.Label.Length > 0 ? signal.Label : signal.Unit;
        return text.ToUpperInvariant();
    }
}

/// <summary>What the marquee says when a certified score comes back from the site.</summary>
public static class VerdictBanner
{
    /// <summary>Title and detail for a <c>scoring.verdict</c>. Null for a status the
    /// marquee has nothing to say about - an unknown word is not turned into a claim.</summary>
    public static (string Title, string Detail)? For(string? status, long score, long? rank, string? modeLabel, bool french)
    {
        var title = (status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "published" => french ? "SCORE CERTIFIÉ" : "CERTIFIED SCORE",
            "held" => french ? "SCORE EN ATTENTE DE CONTRÔLE" : "SCORE HELD FOR REVIEW",
            "quarantined" => french ? "SCORE EN QUARANTAINE" : "SCORE QUARANTINED",
            "rejected" => french ? "SCORE NON CERTIFIÉ" : "SCORE NOT CERTIFIED",
            _ => null
        };
        if (title is null) return null;

        var parts = new List<string>();
        if (score > 0) parts.Add(score.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " "));
        if (!string.IsNullOrWhiteSpace(modeLabel)) parts.Add(modeLabel.Trim());
        // a rank only means something for a score that entered the board
        if (status!.Trim().Equals("published", StringComparison.OrdinalIgnoreCase) && rank is > 0)
            parts.Add((french ? "RANG MONDIAL " : "WORLD RANK ") + rank.Value.ToString(CultureInfo.InvariantCulture));
        return (title, string.Join(" · ", parts));
    }
}

/// <summary>Which named streams this APIExpose serves (<c>GET /api/v1/ws/streams</c>).</summary>
public static class NamedStreams
{
    /// <summary>The names announced, whatever the shape of the listing: an array of
    /// names, an array of objects carrying a name, or the same under a "streams" field.
    /// Empty when the reply is not understood - the caller then opens nothing more.</summary>
    public static IReadOnlySet<string> Announced(string? json)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return names;
        try
        {
            using var document = JsonDocument.Parse(json);
            Collect(document.RootElement, names, 0);
        }
        catch (JsonException)
        {
            names.Clear();
        }
        return names;
    }

    private static void Collect(JsonElement node, HashSet<string> names, int depth)
    {
        if (depth > 3) return;
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) Add(item.GetString(), names);
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in new[] { "Name", "name", "Stream", "stream", "Id", "id" })
                    {
                        if (item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                        {
                            Add(v.GetString(), names);
                            break;
                        }
                    }
                }
            }
            return;
        }
        if (node.ValueKind != JsonValueKind.Object) return;
        foreach (var property in node.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array
                && property.Name.Contains("stream", StringComparison.OrdinalIgnoreCase))
                Collect(property.Value, names, depth + 1);
            else if (property.Value.ValueKind == JsonValueKind.Object)
                Collect(property.Value, names, depth + 1);
        }
    }

    private static void Add(string? name, HashSet<string> names)
    {
        var n = (name ?? string.Empty).Trim().Trim('/');
        if (n.StartsWith("ws/", StringComparison.OrdinalIgnoreCase)) n = n[3..];
        if (n.Length > 0) names.Add(n);
    }
}
