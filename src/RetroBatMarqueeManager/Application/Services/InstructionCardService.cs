using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Processes;

namespace RetroBatMarqueeManager.Application.Services;

/// <summary>
/// Instruction card catalog + touch interaction. APIExpose sends the full Cards
/// array for the current game; this service keeps the whole list (the legacy path
/// only showed the first one) and, when state\surfaces.profile.json enables touch
/// on the iccard surface, maps taps to zone actions: cycle-card, show-card,
/// show-player-card, default-card. Written by MarqueeManagerSetup.
///
/// Media naming (APIExpose artwork\ic): `ic.png` or `ic-N[-variant].png` — e.g.
/// mercs ships ic-1-left … ic-5-right. Files sharing the same N are ONE logical
/// card in two panel positions: left (player 1 side) and right (player 2 side).
/// Cycling and "icN" ids work on logical cards; show-player-card picks the side.
/// </summary>
public sealed class InstructionCardService : IDisposable
{
    private readonly IConfigService _config;
    private readonly MarqueeController _surfaces;
    private readonly ILogger<InstructionCardService> _logger;
    private readonly object _lock = new();
    private readonly TouchSettings? _touch;
    private List<InstructionCardCatalog.CardGroup> _groups = new();
    private int _groupIndex;
    private string? _sidePreference;
    private System.Threading.Timer? _revertTimer;

    public InstructionCardService(IConfigService config, MarqueeController surfaces, ILogger<InstructionCardService> logger)
    {
        _config = config;
        _surfaces = surfaces;
        _logger = logger;
        _touch = LoadTouchProfile();
        if (_touch is { Enabled: true })
        {
            _surfaces.IcCardTapped += OnTap;
            _logger.LogInformation("Instruction card touch enabled: mode={Mode}, {ZoneCount} zone(s)",
                _touch.Mode, _touch.Zones.Count);
        }
    }

    /// <summary>New game selected: replace the catalog and show the default card.</summary>
    public async Task SetCardsAsync(IReadOnlyList<string> cards, CancellationToken cancellationToken)
    {
        string? path;
        lock (_lock)
        {
            _groups = InstructionCardCatalog.BuildGroups(cards);
            _groupIndex = DefaultGroupIndex();
            _sidePreference = null;
            CancelRevert();
            path = _groups.Count > 0 ? _groups[_groupIndex].PathFor(null) : null;
        }

        if (path != null)
        {
            await DisplayAsync(path, cancellationToken);
        }
    }

    private int DefaultGroupIndex()
    {
        var byId = _touch?.DefaultCard is { Length: > 0 } id ? ResolveGroupIndex(id) : null;
        return byId is { } index && index >= 0 && index < _groups.Count ? index : 0;
    }

    private void OnTap(double fx, double fy)
    {
        TouchZone? hit = null;
        lock (_lock)
        {
            if (_touch is not { Enabled: true } || _groups.Count == 0) return;
            // first matching zone wins: generated profiles list the specific zone
            // (e.g. center) before the catch-all
            foreach (var zone in _touch.Zones)
            {
                if (zone.Contains(fx, fy))
                {
                    hit = zone;
                    break;
                }
            }
        }

        if (hit?.Tap == null) return;
        _logger.LogDebug("Instruction card tap ({Fx:0.##},{Fy:0.##}) -> zone {Zone}, action {Action}",
            fx, fy, hit.Id, hit.Tap.Action);
        _ = ExecuteAsync(hit.Tap, CancellationToken.None);
    }

    private async Task ExecuteAsync(TouchAction tap, CancellationToken cancellationToken)
    {
        string? path = null;
        var revertMs = 0;
        lock (_lock)
        {
            if (_groups.Count == 0) return;
            switch (tap.Action.ToLowerInvariant())
            {
                case "cycle-card":
                    _groupIndex = (_groupIndex + 1) % _groups.Count;
                    break;

                case "show-card":
                    if (tap.Card is not { Length: > 0 } card || ResolveGroupIndex(card) is not { } found)
                    {
                        _logger.LogDebug("show-card resolved no card for id {Card} ({Count} groups)", tap.Card, _groups.Count);
                        return;
                    }

                    _groupIndex = found;
                    break;

                case "show-player-card":
                    if (tap.Player is not { } player) return;
                    // a file explicitly named for the player wins; otherwise the side
                    // convention: left holder = player 1, right holder = player 2
                    if (ResolvePlayerGroupIndex(player) is { } playerGroup)
                    {
                        _groupIndex = playerGroup;
                    }
                    else
                    {
                        _sidePreference = player switch { 1 => "left", 2 => "right", _ => null };
                    }

                    break;

                case "default-card":
                    _groupIndex = DefaultGroupIndex();
                    _sidePreference = null;
                    break;

                default:
                    return;
            }

            path = _groups[_groupIndex].PathFor(_sidePreference);

            // temporary card: come back to the default one after the delay
            var isDefault = _groupIndex == DefaultGroupIndex() && _sidePreference is null;
            revertMs = !isDefault ? tap.DurationMs ?? _touch!.ReturnToDefaultMs : 0;
            CancelRevert();
            if (revertMs > 0)
            {
                _revertTimer = new System.Threading.Timer(_ => RevertToDefault(), null, revertMs, Timeout.Infinite);
            }
        }

        await DisplayAsync(path!, cancellationToken);
    }

    private void RevertToDefault()
    {
        string? path;
        lock (_lock)
        {
            CancelRevert();
            if (_groups.Count == 0) return;
            var index = DefaultGroupIndex();
            if (index == _groupIndex && _sidePreference is null) return;
            _groupIndex = index;
            _sidePreference = null;
            path = _groups[index].PathFor(null);
        }

        _ = DisplayAsync(path!, CancellationToken.None);
    }

    private void CancelRevert()
    {
        _revertTimer?.Dispose();
        _revertTimer = null;
    }

    private async Task DisplayAsync(string path, CancellationToken cancellationToken)
    {
        foreach (var target in _config.GetTargetsForContent("iccard"))
        {
            await _surfaces.DisplayMediaAsync(path, target, cancellationToken);
        }
    }

    /// <summary>"ic2" / "2" → logical card n°2; otherwise match by file name fragment.</summary>
    private int? ResolveGroupIndex(string card)
    {
        var match = Regex.Match(card, "^(?:ic)?-?([0-9]+)$", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var number) && number >= 1)
        {
            var byNumber = _groups.FindIndex(g => g.Number == number);
            return byNumber >= 0 ? byNumber : null;
        }

        for (var i = 0; i < _groups.Count; i++)
        {
            if (_groups[i].Variants.Any(v =>
                    Path.GetFileNameWithoutExtension(v.Path).Contains(card, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Group holding a file explicitly named for the player (p1/player1), if any.</summary>
    private int? ResolvePlayerGroupIndex(int player)
    {
        var pattern = new Regex($@"(?:^|[^a-z0-9])p(?:layer)?{player}(?:[^0-9]|$)", RegexOptions.IgnoreCase);
        for (var i = 0; i < _groups.Count; i++)
        {
            if (_groups[i].Variants.Any(v => pattern.IsMatch(Path.GetFileNameWithoutExtension(v.Path))))
            {
                return i;
            }
        }

        return null;
    }

    private TouchSettings? LoadTouchProfile()
    {
        var path = Path.Combine(_config.BaseDirectory, "state", "surfaces.profile.json");
        try
        {
            if (!File.Exists(path)) return null;
            var document = JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var touch = document?.Surfaces?
                .FirstOrDefault(s => string.Equals(s.Kind, "iccard", StringComparison.OrdinalIgnoreCase))?.Touch;
            return touch;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read touch profile {Path}: {Message}", path, ex.Message);
            return null;
        }
    }

    public void Dispose() => CancelRevert();

    // --- profile model (subset written by MarqueeManagerSetup) ---

    private sealed class ProfileDocument
    {
        [JsonPropertyName("surfaces")]
        public List<SurfaceProfile>? Surfaces { get; set; }
    }

    private sealed class SurfaceProfile
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("touch")]
        public TouchSettings? Touch { get; set; }
    }

    public sealed class TouchSettings
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "simple";

        [JsonPropertyName("defaultCard")]
        public string? DefaultCard { get; set; }

        [JsonPropertyName("returnToDefaultMs")]
        public int ReturnToDefaultMs { get; set; }

        [JsonPropertyName("zones")]
        public List<TouchZone> Zones { get; set; } = new();
    }

    public sealed class TouchZone
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>"x,y,w,h" in percent of the surface, e.g. "0,0,50%,100%".</summary>
        [JsonPropertyName("rect")]
        public string Rect { get; set; } = "0,0,100%,100%";

        [JsonPropertyName("tap")]
        public TouchAction? Tap { get; set; }

        public bool Contains(double fx, double fy)
        {
            var parts = Rect.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) return false;
            var values = new double[4];
            for (var i = 0; i < 4; i++)
            {
                if (!double.TryParse(parts[i].TrimEnd('%').Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                {
                    return false;
                }
            }

            double x = values[0] / 100.0, y = values[1] / 100.0, w = values[2] / 100.0, h = values[3] / 100.0;
            return fx >= x && fx <= x + w && fy >= y && fy <= y + h;
        }
    }

    public sealed class TouchAction
    {
        /// <summary>cycle-card | show-card | show-player-card | default-card</summary>
        [JsonPropertyName("action")]
        public string Action { get; set; } = "cycle-card";

        [JsonPropertyName("card")]
        public string? Card { get; set; }

        [JsonPropertyName("player")]
        public int? Player { get; set; }

        [JsonPropertyName("durationMs")]
        public int? DurationMs { get; set; }
    }
}
