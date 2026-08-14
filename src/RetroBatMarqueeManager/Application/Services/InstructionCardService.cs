using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Core.Surfaces;
using RetroBatMarqueeManager.Infrastructure.Processes;

namespace RetroBatMarqueeManager.Application.Services;

/// <summary>
/// Instruction card catalog + touch interaction. APIExpose sends the full Cards array
/// for the current game, each card carrying the ROLE it belongs to; this service keeps
/// the whole list and shows it through CHANNELS.
///
/// A channel is one reading position: a viewer displays it, a touch zone drives it.
/// They are two different layers on purpose — a cabinet can have its buttons on a
/// touchscreen and its card on the topper, so the finger and the card are not on the
/// same surface. The channel is what ties them together, and it is derived from what
/// the viewer already says (its player, its role) so the common cases need no naming.
///
/// Each channel holds its own place in the catalog: which role it is on, which card of
/// that role, and whether it follows the character the game announces. Cycling stays
/// INSIDE the current role — Cody 01 → 02 → 03 → 01 — which is what makes a card usable
/// mid-game. With no role, it walks the whole catalog: when nothing says who is being
/// played, showing everything is the honest answer.
///
/// Media naming (APIExpose artwork\ic): `ic.png` or `ic-N[-variant].png`, under an
/// optional role folder. Files sharing the same N are ONE logical card in two panel
/// positions: left (player 1 side) and right (player 2 side).
/// </summary>
public sealed class InstructionCardService : IDisposable
{
    private const string MainChannel = "main";

    private readonly IConfigService _config;
    private readonly MarqueeController _surfaces;
    private readonly ILogger<InstructionCardService> _logger;
    private readonly object _lock = new();
    private readonly TouchSettings? _touch;
    private readonly Dictionary<string, Channel> _channels = new(StringComparer.OrdinalIgnoreCase);
    private List<InstructionCardCatalog.CardGroup> _groups = new();
    private bool _channelsResolved;

    public InstructionCardService(IConfigService config, MarqueeController surfaces, ILogger<InstructionCardService> logger)
    {
        _config = config;
        _surfaces = surfaces;
        _logger = logger;
        _touch = LoadTouchProfile();
        _surfaces.SurfaceTapped += OnTap;
        if (_touch is { Enabled: true })
        {
            _logger.LogInformation("Instruction card touch enabled: mode={Mode}, {ZoneCount} zone(s)",
                _touch.Mode, _touch.Zones.Count);
        }
    }

    /// <summary>One reading position in the catalog. Named by the viewer that shows it
    /// and by the zones that drive it.</summary>
    private sealed class Channel
    {
        public required string Name { get; init; }

        /// <summary>The role the user pinned on the viewer. Empty = the whole catalog.</summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>The role an event pinned (the character this player just chose).
        /// Beats the configured one, and only auto mode ever sets it.</summary>
        public string? EventRole { get; set; }

        public int? Player { get; set; }

        /// <summary>Follows what the game announces. Off = the zones drive it alone.</summary>
        public bool Auto { get; set; }

        public int Index { get; set; }
        public string? Side { get; set; }

        /// <summary>The entry framed inside the card, when the game named something the
        /// card holds. Cleared as soon as the player browses: a frame pointing at what he
        /// no longer carries is worse than no frame.</summary>
        public InstructionCardCatalog.CardPanel? Panel { get; set; }
        public System.Threading.Timer? Revert { get; set; }

        public string EffectiveRole => EventRole ?? Role;
    }

    /// <summary>What one channel must show right now: its card, and the entry to frame.</summary>
    private sealed record Shown(string Channel, string? Path, double[]? Panel);

    /// <summary>New game selected: replace the catalog and put every channel back on its
    /// first card.</summary>
    public async Task SetCardsAsync(IReadOnlyList<InstructionCardCatalog.CardSource> cards, CancellationToken cancellationToken)
    {
        List<Shown> updates;
        lock (_lock)
        {
            _groups = InstructionCardCatalog.BuildGroups(cards);
            ResolveChannels();
            foreach (var channel in _channels.Values)
            {
                CancelRevert(channel);
                // A game change forgets what the previous game announced: the character
                // of the game you just left has no card here.
                channel.EventRole = null;
                channel.Side = null;
                channel.Panel = null;
                channel.Index = DefaultIndex(channel);
            }

            updates = _channels.Values.Select(Snapshot).ToList();
        }

        foreach (var shown in updates)
        {
            // A game without an instruction card CLEARS the previous one. Returning early
            // left the last game's card on screen — one card following you across the whole
            // library. Nothing of an entry may survive into the next.
            await DisplayAsync(shown, cancellationToken);
        }

        if (_surfaces.HasComponent("iccard.static"))
        {
            string? staticPath;
            lock (_lock)
            {
                staticPath = StaticCardPath();
            }

            _surfaces.SetComponentSource("iccard.static", staticPath);
        }
    }

    /// <summary>Legacy entry point: a flat list of paths, no roles.</summary>
    public Task SetCardsAsync(IReadOnlyList<string> cards, CancellationToken cancellationToken)
        => SetCardsAsync(cards.Select(InstructionCardCatalog.CardSource.Of).ToList(), cancellationToken);

    /// <summary>
    /// The game announced what a player has in hand (CHARACTER_SELECTED / WEAPON_SELECTED
    /// carry a NAME in their description). Channels in auto mode for that player jump to
    /// the matching role — and stay there, cycling inside it.
    /// </summary>
    public async Task OnNameAnnouncedAsync(int player, string name, CancellationToken cancellationToken)
    {
        List<Shown> updates;
        lock (_lock)
        {
            if (_groups.Count == 0) return;

            // Two ways a name can be carried by the cards: it is a ROLE — a folder of its
            // own, a character with his own pages — or it is one ENTRY inside a shared
            // card, like the weapons of Ghouls'n Ghosts drawn side by side. The folder
            // wins: it is the richer answer, several pages instead of one frame.
            var role = InstructionCardCatalog.MatchRole(_groups, name);
            if (role == null && InstructionCardCatalog.FindPanel(_groups, name) == null)
            {
                // Nothing in this game's cards names it: the viewer keeps what it shows.
                // Blanking it would punish the player for a card the pack does not carry.
                _logger.LogDebug("Announced name {Name} matches no role and no entry ({Count} cards)",
                    name, _groups.Count);
                return;
            }

            updates = new List<Shown>();
            foreach (var channel in _channels.Values)
            {
                if (!channel.Auto || (channel.Player is { } p && p != player)) continue;
                CancelRevert(channel);
                if (role != null)
                {
                    if (string.Equals(channel.EventRole, role, StringComparison.OrdinalIgnoreCase)) continue;
                    channel.EventRole = role;
                    channel.Index = 0;
                    channel.Panel = null;
                }
                else if (!PointAtEntry(channel, name))
                {
                    continue;
                }

                channel.Side = null;
                updates.Add(Snapshot(channel));
            }

            if (updates.Count == 0) return;
            _logger.LogInformation("Player {Player} is on {Name}: {Count} viewer(s) → {What}",
                player, name, updates.Count, role != null ? "role " + role : "entry framed in the card");
        }

        foreach (var shown in updates) await DisplayAsync(shown, cancellationToken);
    }

    /// <summary>
    /// Points this channel at the entry that names what was announced — the weapon just
    /// picked up, drawn among the others on a shared card.
    ///
    /// Looked up in the channel's OWN cards first: an index is only meaningful inside the
    /// list the channel walks, and a viewer pinned to a role walks a shorter one. Found
    /// elsewhere, the channel moves to the card's role — otherwise it would carry the
    /// index of a card it cannot show.
    /// </summary>
    private bool PointAtEntry(Channel channel, string name)
    {
        var own = GroupsOf(channel);
        var hit = InstructionCardCatalog.FindPanel(own, name);
        if (hit != null)
        {
            channel.Index = hit.GroupIndex;
            channel.Panel = hit.Panel;
            return true;
        }

        var wide = InstructionCardCatalog.FindPanel(_groups, name);
        if (wide == null) return false;

        var card = _groups[wide.GroupIndex];
        channel.EventRole = card.Role;
        var index = GroupsOf(channel).IndexOf(card);
        if (index < 0) return false;
        channel.Index = index;
        channel.Panel = wide.Panel;
        return true;
    }

    // ================= channels =================

    /// <summary>Reads the channels off the surfaces: one per viewer, plus the historical
    /// one which the legacy components and the iccard surface still answer on.</summary>
    private void ResolveChannels()
    {
        var viewers = _surfaces.ComponentsOfType("iccard.viewer");
        if (_channelsResolved && viewers.Count == 0) return;
        _channelsResolved = true;

        foreach (var viewer in viewers)
        {
            var player = int.TryParse(viewer.Option("player"), out var parsed) && parsed >= 1 ? parsed : (int?)null;
            var name = InstructionCardCatalog.ChannelOf(viewer.Option("channel"), viewer.Option("role"), viewer.Option("player"));
            var channel = ChannelFor(name);
            channel.Role = viewer.Option("role").Trim();
            channel.Player = player;
            channel.Auto = !viewer.Option("auto", "true").Equals("false", StringComparison.OrdinalIgnoreCase);
        }

        // the historical channel always exists: iccard.cycle, the iccard surface and any
        // zone that names no target answer on it
        ChannelFor(MainChannel);
    }

    private Channel ChannelFor(string name)
    {
        if (!_channels.TryGetValue(name, out var channel))
        {
            _channels[name] = channel = new Channel { Name = name };
        }

        return channel;
    }

    /// <summary>The cards this channel walks: its role's, or all of them.</summary>
    private List<InstructionCardCatalog.CardGroup> GroupsOf(Channel channel)
        => InstructionCardCatalog.ForRole(_groups, channel.EffectiveRole);

    /// <summary>What this channel shows: its card, and the entry to frame inside it. The
    /// frame travels WITH the card — sent apart, it would land on the previous drawing
    /// for one frame, pointing at nothing.</summary>
    private Shown Snapshot(Channel channel)
    {
        var panel = channel.Panel;
        var rect = panel == null ? null : new[] { panel.X, panel.Y, panel.W, panel.H };
        return new Shown(channel.Name, PathOf(channel), rect);
    }

    private string? PathOf(Channel channel)
    {
        var groups = GroupsOf(channel);
        if (groups.Count == 0) return null;
        var index = Math.Clamp(channel.Index, 0, groups.Count - 1);
        return groups[index].PathFor(channel.Side ?? SideOf(channel));
    }

    /// <summary>A card drawn in two panel positions shows the player his own side.</summary>
    private static string? SideOf(Channel channel) => channel.Player switch
    {
        1 => "left",
        2 => "right",
        _ => null
    };

    private int DefaultIndex(Channel channel)
    {
        var groups = GroupsOf(channel);
        if (groups.Count == 0) return 0;
        // the profile's pinned card only applies to the historical channel: it was
        // written when there was a single reading position
        if (channel.Name.Equals(MainChannel, StringComparison.OrdinalIgnoreCase)
            && _touch?.DefaultCard is { Length: > 0 } id
            && ResolveGroupIndex(groups, id) is { } index)
        {
            return index;
        }

        return 0;
    }

    // ================= touch =================

    /// <summary>
    /// A tap on a surface. The zones the user drew in the composition win — they are
    /// visible, they carry their own action, and they can sit on ANY surface. The
    /// historical profile (state\surfaces.profile.json) still answers on the iccard
    /// surface when the composition has no zone under the finger.
    /// </summary>
    private void OnTap(SurfaceDefinition surface, string scene, double fx, double fy)
    {
        var zone = FindTouchLayer(surface, scene, fx, fy);
        if (zone != null)
        {
            var action = zone.Option("action", "next-card");
            var channel = InstructionCardCatalog.ChannelOf(zone.Option("channel"), zone.Option("role"), zone.Option("player"));
            _logger.LogDebug("Tap ({Fx:0.##},{Fy:0.##}) on {Surface} -> {Action} on channel {Channel}",
                fx, fy, surface.Id, action, channel);
            _ = ExecuteAsync(channel, new TouchAction
            {
                Action = action,
                Card = zone.Option("card"),
                Role = zone.Option("role"),
                Player = int.TryParse(zone.Option("player"), out var player) && player >= 1 ? player : null,
                DurationMs = int.TryParse(zone.Option("durationMs"), out var ms) && ms > 0 ? ms : null
            }, CancellationToken.None);
            return;
        }

        if (!surface.Category.Equals("iccard", StringComparison.OrdinalIgnoreCase)) return;

        TouchZone? hit = null;
        lock (_lock)
        {
            if (_touch is not { Enabled: true } || _groups.Count == 0) return;
            // first matching zone wins: generated profiles list the specific zone
            // (e.g. center) before the catch-all
            foreach (var legacy in _touch.Zones)
            {
                if (legacy.Contains(fx, fy))
                {
                    hit = legacy;
                    break;
                }
            }
        }

        if (hit?.Tap == null) return;
        _logger.LogDebug("Instruction card tap ({Fx:0.##},{Fy:0.##}) -> zone {Zone}, action {Action}",
            fx, fy, hit.Id, hit.Tap.Action);
        _ = ExecuteAsync(MainChannel, hit.Tap, CancellationToken.None);
    }

    /// <summary>The front-most touch layer under the finger, among those the current
    /// scene shows. Front-most first: a zone drawn over another one is the one the user
    /// sees, so it is the one he means.</summary>
    private static ComponentDefinition? FindTouchLayer(SurfaceDefinition surface, string scene, double fx, double fy)
    {
        for (var i = surface.Components.Count - 1; i >= 0; i--)
        {
            var component = surface.Components[i];
            if (!component.Type.Equals("iccard.touch", StringComparison.OrdinalIgnoreCase)) continue;
            if (!component.ActiveIn(scene)) continue;
            if (fx >= component.X && fx <= component.X + component.W
                                  && fy >= component.Y && fy <= component.Y + component.H)
            {
                return component;
            }
        }

        return null;
    }

    private async Task ExecuteAsync(string channelName, TouchAction tap, CancellationToken cancellationToken)
    {
        Shown shown;
        lock (_lock)
        {
            if (_groups.Count == 0) return;
            ResolveChannels();
            var channel = ChannelFor(channelName);
            var groups = GroupsOf(channel);
            // a role action is allowed to leave an empty role — that is how you get out
            // of one; everything else needs a card to move to
            if (groups.Count == 0 && !IsRoleAction(tap.Action)) return;

            switch (tap.Action.ToLowerInvariant())
            {
                case "next-card":
                case "cycle-card":
                    channel.Index = (channel.Index + 1) % groups.Count;
                    break;

                case "previous-card":
                    channel.Index = (channel.Index - 1 + groups.Count) % groups.Count;
                    break;

                case "show-card":
                    if (tap.Card is not { Length: > 0 } card || ResolveGroupIndex(groups, card) is not { } found)
                    {
                        _logger.LogDebug("show-card resolved no card for id {Card} ({Count} groups)", tap.Card, groups.Count);
                        return;
                    }

                    channel.Index = found;
                    break;

                case "show-role":
                {
                    // an explicit role stops following the game: the user asked for THIS
                    // card, and an announcement must not take it away under his finger
                    var role = tap.Role?.Trim() ?? string.Empty;
                    channel.EventRole = role.Length > 0 ? role : null;
                    channel.Role = role;
                    channel.Index = 0;
                    channel.Side = null;
                    break;
                }

                case "next-role":
                case "previous-role":
                {
                    var roles = InstructionCardCatalog.Roles(_groups);
                    if (roles.Count == 0) return;
                    var current = roles.FindIndex(r => r.Equals(channel.EffectiveRole, StringComparison.OrdinalIgnoreCase));
                    var step = tap.Action.StartsWith("previous", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
                    // no role yet: the first step lands on the first role, not the second
                    var next = current < 0
                        ? (step > 0 ? 0 : roles.Count - 1)
                        : (current + step + roles.Count) % roles.Count;
                    channel.EventRole = roles[next];
                    channel.Index = 0;
                    channel.Side = null;
                    break;
                }

                case "show-player-card":
                    if (tap.Player is not { } player) return;
                    // a file explicitly named for the player wins; otherwise the side
                    // convention: left holder = player 1, right holder = player 2
                    if (ResolvePlayerGroupIndex(groups, player) is { } playerGroup)
                    {
                        channel.Index = playerGroup;
                    }
                    else
                    {
                        channel.Side = player switch { 1 => "left", 2 => "right", _ => null };
                    }

                    break;

                case "auto":
                case "toggle-auto":
                    channel.Auto = !channel.Auto;
                    if (!channel.Auto) channel.EventRole = null;
                    break;

                case "default-card":
                    channel.Index = DefaultIndex(channel);
                    channel.Side = null;
                    break;

                default:
                    return;
            }

            // The finger takes over: whatever the game had pointed at is no longer what
            // the player is looking at, and a frame left behind would designate an entry of
            // another card.
            channel.Panel = null;
            shown = Snapshot(channel);

            // temporary card: come back to the default one after the delay
            var isDefault = channel.Index == DefaultIndex(channel) && channel.Side is null && channel.EventRole is null;
            var revertMs = !isDefault ? tap.DurationMs ?? _touch?.ReturnToDefaultMs ?? 0 : 0;
            CancelRevert(channel);
            if (revertMs > 0)
            {
                var name = channel.Name;
                channel.Revert = new System.Threading.Timer(_ => RevertToDefault(name), null, revertMs, Timeout.Infinite);
            }
        }

        await DisplayAsync(shown, cancellationToken);
    }

    private static bool IsRoleAction(string action) => action.ToLowerInvariant()
        is "show-role" or "next-role" or "previous-role" or "auto" or "toggle-auto";

    private void RevertToDefault(string channelName)
    {
        Shown shown;
        lock (_lock)
        {
            if (!_channels.TryGetValue(channelName, out var channel)) return;
            CancelRevert(channel);
            if (_groups.Count == 0) return;
            var index = DefaultIndex(channel);
            if (index == channel.Index && channel.Side is null && channel.EventRole is null) return;
            channel.EventRole = null;
            channel.Index = index;
            channel.Side = null;
            channel.Panel = null;
            shown = Snapshot(channel);
        }

        _ = DisplayAsync(shown, CancellationToken.None);
    }

    private static void CancelRevert(Channel channel)
    {
        channel.Revert?.Dispose();
        channel.Revert = null;
    }

    private async Task DisplayAsync(Shown shown, CancellationToken cancellationToken)
    {
        var isMain = shown.Channel.Equals(MainChannel, StringComparison.OrdinalIgnoreCase);
        if (isMain && shown.Path != null)
        {
            foreach (var target in _config.GetTargetsForContent("iccard"))
            {
                await _surfaces.DisplayMediaAsync(shown.Path, target, cancellationToken);
            }
        }

        // split rendering (fixed card + cycling card side by side): the viewers of this
        // channel follow every card change, the static one is pinned on its configured
        // card and only moves on a game change (SetCardsAsync)
        _surfaces.SetCardSource(shown.Channel, shown.Path, shown.Panel);
    }

    /// <summary>Path of the card pinned by an iccard.static component ("card" option,
    /// logical number, default 1).</summary>
    private string? StaticCardPath()
    {
        var option = _surfaces.ComponentOption("iccard.static", "card");
        var number = int.TryParse(option, out var parsed) && parsed >= 1 ? parsed : 1;
        var group = _groups.FirstOrDefault(g => g.Number == number) ?? _groups.FirstOrDefault();
        return group?.PathFor("left");
    }

    /// <summary>"ic2" / "2" → logical card n°2; otherwise match by file name fragment.</summary>
    private static int? ResolveGroupIndex(List<InstructionCardCatalog.CardGroup> groups, string card)
    {
        var match = Regex.Match(card, "^(?:ic)?-?([0-9]+)$", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var number) && number >= 1)
        {
            var byNumber = groups.FindIndex(g => g.Number == number);
            return byNumber >= 0 ? byNumber : null;
        }

        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].Variants.Any(v =>
                    Path.GetFileNameWithoutExtension(v.Path).Contains(card, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Group holding a file explicitly named for the player (p1/player1), if any.</summary>
    private static int? ResolvePlayerGroupIndex(List<InstructionCardCatalog.CardGroup> groups, int player)
    {
        var pattern = new Regex($@"(?:^|[^a-z0-9])p(?:layer)?{player}(?:[^0-9]|$)", RegexOptions.IgnoreCase);
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].Variants.Any(v => pattern.IsMatch(Path.GetFileNameWithoutExtension(v.Path))))
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

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var channel in _channels.Values) CancelRevert(channel);
        }
    }

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
        /// <summary>next-card | previous-card | show-card | show-role | next-role |
        /// previous-role | show-player-card | toggle-auto | default-card</summary>
        [JsonPropertyName("action")]
        public string Action { get; set; } = "next-card";

        [JsonPropertyName("card")]
        public string? Card { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("player")]
        public int? Player { get; set; }

        [JsonPropertyName("durationMs")]
        public int? DurationMs { get; set; }
    }
}
