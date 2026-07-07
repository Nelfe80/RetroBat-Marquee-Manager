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
/// </summary>
public sealed class InstructionCardService : IDisposable
{
    private readonly IConfigService _config;
    private readonly MarqueeController _surfaces;
    private readonly ILogger<InstructionCardService> _logger;
    private readonly object _lock = new();
    private readonly TouchSettings? _touch;
    private IReadOnlyList<string> _cards = Array.Empty<string>();
    private int _currentIndex;
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
        int defaultIndex;
        lock (_lock)
        {
            _cards = cards;
            defaultIndex = DefaultIndex();
            _currentIndex = defaultIndex;
            CancelRevert();
        }

        if (cards.Count > 0)
        {
            await DisplayAsync(cards[defaultIndex], cancellationToken);
        }
    }

    private int DefaultIndex()
    {
        var byId = _touch?.DefaultCard is { Length: > 0 } id ? ResolveCardIndex(id) : null;
        return byId is { } index && index >= 0 && index < _cards.Count ? index : 0;
    }

    private void OnTap(double fx, double fy)
    {
        TouchZone? hit = null;
        lock (_lock)
        {
            if (_touch is not { Enabled: true } || _cards.Count == 0) return;
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
            if (_cards.Count == 0) return;
            int? target = tap.Action.ToLowerInvariant() switch
            {
                "cycle-card" => (_currentIndex + 1) % _cards.Count,
                "show-card" => tap.Card is { Length: > 0 } card ? ResolveCardIndex(card) : null,
                "show-player-card" => tap.Player is { } player ? ResolvePlayerIndex(player) : null,
                "default-card" => DefaultIndex(),
                _ => null
            };
            if (target is not { } index || index < 0 || index >= _cards.Count)
            {
                _logger.LogDebug("Instruction card action {Action} resolved to no card ({Count} available)", tap.Action, _cards.Count);
                return;
            }

            _currentIndex = index;
            path = _cards[index];

            // temporary card: come back to the default one after the delay
            var isDefault = index == DefaultIndex();
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
            if (_cards.Count == 0) return;
            var index = DefaultIndex();
            if (index == _currentIndex) return;
            _currentIndex = index;
            path = _cards[index];
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

    /// <summary>"ic2" / "2" → second card; otherwise match by file name fragment.</summary>
    private int? ResolveCardIndex(string card)
    {
        var match = Regex.Match(card, "^(?:ic)?([0-9]+)$", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var number) && number >= 1)
        {
            return number - 1;
        }

        for (var i = 0; i < _cards.Count; i++)
        {
            if (Path.GetFileNameWithoutExtension(_cards[i]).Contains(card, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Player card: name containing p1/player1 wins, otherwise the Nth card.</summary>
    private int? ResolvePlayerIndex(int player)
    {
        var pattern = new Regex($@"(?:^|[^a-z0-9])p(?:layer)?{player}(?:[^0-9]|$)", RegexOptions.IgnoreCase);
        for (var i = 0; i < _cards.Count; i++)
        {
            if (pattern.IsMatch(Path.GetFileNameWithoutExtension(_cards[i])))
            {
                return i;
            }
        }

        return player - 1;
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
