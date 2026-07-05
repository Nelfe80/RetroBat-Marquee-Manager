using System.Xml.Linq;
using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Lighting;

public enum IngameEffectKind { Flash, Pulse, PowerCycle, Blackout, Sprite, Shake, Strobe, Tint }

/// <summary>One rule of the ingame effects library (resources/lighting/ingame.effects.xml).
/// A rule can combine a glass flash AND sprites (sprite attr on any kind).</summary>
public sealed record IngameEffectRule(
    IngameEffectKind Kind,
    SKColor Color,
    int DurationMs,
    float Dip,
    int ThrottleMs,
    string Label,
    string? Sprite = null,
    int Count = 1,
    string Motion = "pop",
    SKColor? TrailColor = null);

/// <summary>
/// Maps semantic .mem actions from the ws/ingame stream to light effects (CDC §18).
/// Editable XML; action match is a case-insensitive contains on alternatives,
/// family match is a prefix; the first matching rule in file order wins.
/// Missing/invalid file → built-in defaults, never fatal (§20.6).
/// </summary>
public sealed class IngameEffectLibrary
{
    private sealed record Rule(string[]? Actions, string? FamilyPrefix, IngameEffectRule Effect);

    private readonly List<Rule> _rules = new();
    private readonly Dictionary<string, long> _lastFired = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public static IngameEffectLibrary Load(string directory, ILogger logger)
    {
        var library = new IngameEffectLibrary();
        var path = Path.Combine(directory, "ingame.effects.xml");
        try
        {
            if (File.Exists(path))
            {
                library.LoadRules(XDocument.Load(path));
                logger.LogInformation("Ingame effects library loaded: {Count} rule(s) from {Path}", library._rules.Count, path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid ingame effects library {Path}; built-in defaults kept", path);
        }
        if (library._rules.Count == 0) library.LoadRules(XDocument.Parse(BuiltInXml));
        return library;
    }

    /// <summary>Resolve an action/family to a rule, applying per-rule throttling. Null = no effect.</summary>
    public IngameEffectRule? Resolve(string action, string? family)
    {
        foreach (var rule in _rules)
        {
            var matches = rule.Actions != null
                ? rule.Actions.Any(a => action.Contains(a, StringComparison.OrdinalIgnoreCase))
                : family != null && rule.FamilyPrefix != null &&
                  family.StartsWith(rule.FamilyPrefix, StringComparison.OrdinalIgnoreCase);
            if (!matches) continue;

            var now = _clock.ElapsedMilliseconds;
            lock (_lastFired)
            {
                if (_lastFired.TryGetValue(rule.Effect.Label, out var last) && now - last < rule.Effect.ThrottleMs)
                    return null;
                _lastFired[rule.Effect.Label] = now;
            }
            return rule.Effect;
        }
        return null;
    }

    private void LoadRules(XDocument document)
    {
        foreach (var element in document.Descendants("effect"))
        {
            var kind = ((string?)element.Attribute("kind"))?.ToLowerInvariant() switch
            {
                "pulse" => IngameEffectKind.Pulse,
                "powercycle" => IngameEffectKind.PowerCycle,
                "blackout" => IngameEffectKind.Blackout,
                "sprite" => IngameEffectKind.Sprite,
                "shake" => IngameEffectKind.Shake,
                "strobe" => IngameEffectKind.Strobe,
                "tint" => IngameEffectKind.Tint,
                _ => IngameEffectKind.Flash
            };
            var actionsRaw = (string?)element.Attribute("action");
            var family = (string?)element.Attribute("family");
            if (actionsRaw == null && family == null) continue;
            var actions = actionsRaw?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var trailRaw = (string?)element.Attribute("trail");
            var effect = new IngameEffectRule(kind,
                ParseColor((string?)element.Attribute("color")),
                (int?)element.Attribute("durationMs") ?? 300,
                (float)((double?)element.Attribute("dip") ?? 0.0),
                (int?)element.Attribute("throttleMs") ?? 400,
                actionsRaw ?? $"family:{family}",
                (string?)element.Attribute("sprite"),
                Math.Clamp((int?)element.Attribute("count") ?? 1, 1, 8),
                ((string?)element.Attribute("motion") ?? "pop").ToLowerInvariant(),
                trailRaw != null ? ParseColor(trailRaw) : null);
            _rules.Add(new Rule(actions, family, effect));
        }
    }

    private static SKColor ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return new SKColor(255, 40, 24);
        hex = hex.TrimStart('#');
        return hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? new SKColor((byte)(v >> 16), (byte)(v >> 8 & 0xFF), (byte)(v & 0xFF))
            : new SKColor(255, 40, 24);
    }

    private const string BuiltInXml = @"
<ingameEffects version='1.0'>
  <effect action='LOSE_LIFE|KO|CRASH|HIT|DAMAGE' kind='flash' color='#ff2015' durationMs='300' dip='0.4' throttleMs='400' />
  <effect action='GAME_OVER' kind='blackout' durationMs='1600' throttleMs='4000' />
  <effect action='GAIN_LIFE|HEAL|COIN_GAIN|KEY_GET' kind='pulse' color='#ffffff' durationMs='200' throttleMs='500' />
</ingameEffects>";
}
