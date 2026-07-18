using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Processes;

namespace RetroBatMarqueeManager.Application.Services;

public sealed class WebSocketListenerService : BackgroundService
{
    private static readonly string[] Streams =
    {
        "arcade", "frontend", "marquee", "topper", "instruction-card", "panel", "hiscore",
        "retroachievements", "score", "timer", "ingame"
    };

    // Streams whose messages describe the CURRENT state (snapshots): during fast
    // ES navigation only the most recent one matters — replaying the backlog one
    // by one is what made the marquee lag tens of seconds behind the frontend.
    // Event-like streams (hiscore, retroachievements, ingame…) keep strict FIFO.
    private static readonly HashSet<string> StateStreams = new(StringComparer.OrdinalIgnoreCase)
    {
        "marquee", "topper", "instruction-card", "panel", "frontend"
    };

    private readonly Dictionary<string, Channel<JsonDocument>> _mailboxes = new(StringComparer.OrdinalIgnoreCase);

    private readonly IConfigService _config;
    private readonly MarqueeController _surfaces;
    private readonly IDmdService _dmd;
    private readonly LayManager _lay;
    private readonly SurfacePresentationService _presentation;
    private readonly InstructionCardService _instructionCards;
    private readonly ILogger<WebSocketListenerService> _logger;
    private string? _selectedSystem;
    private string? _selectedRom;
    private string? _runningRom;
    private bool _pinballDmdActive;
    private readonly Application.Lighting.IngameEffectLibrary _ingameEffects;
    private readonly Application.Lighting.GenreMap _genreMap;
    private readonly string _effectOverridesRoot;
    private readonly Application.Media.CompositionChainResolver _compositionChains;
    private readonly Application.Media.CompositionTemplateRenderer _templateRenderer;

    public WebSocketListenerService(
        IConfigService config,
        MarqueeController surfaces,
        IDmdService dmd,
        LayManager lay,
        SurfacePresentationService presentation,
        InstructionCardService instructionCards,
        ILogger<WebSocketListenerService> logger)
    {
        _config = config;
        _surfaces = surfaces;
        _dmd = dmd;
        _lay = lay;
        _presentation = presentation;
        _instructionCards = instructionCards;
        _logger = logger;
        _ingameEffects = Application.Lighting.IngameEffectLibrary.Load(
            Path.Combine(config.BaseDirectory, "resources", "lighting"), logger);
        _genreMap = Application.Lighting.GenreMap.Load(
            Path.Combine(config.BaseDirectory, "resources", "lighting"), logger);
        _effectOverridesRoot = Path.Combine(config.BaseDirectory, "overrides", "effects");
        _compositionChains = new Application.Media.CompositionChainResolver(
            config.BaseDirectory, logger, config.LightingPreferGeneratedMarquee);
        _templateRenderer = new Application.Media.CompositionTemplateRenderer(config.BaseDirectory, logger);
        _compositionChains.TemplateMissing = OnTemplateMissing;
    }

    private readonly Dictionary<string, string?> _lastMarqueeKinds = new(StringComparer.OrdinalIgnoreCase);
    private Application.Lighting.LightingSceneMeta? _lastMarqueeMeta;

    /// <summary>A chain asked for a template PNG not yet cached: render it in the
    /// background, then re-display if the selection did not move on (the
    /// "pending → updated" pattern of APIExpose's own generation).</summary>
    private void OnTemplateMissing(string templateId, string system, string rom, bool systemScope)
    {
        string? fanart, logo;
        lock (_lastMarqueeKinds)
        {
            _lastMarqueeKinds.TryGetValue("fanart", out fanart);
            _lastMarqueeKinds.TryGetValue("logo", out logo);
        }
        _templateRenderer.RenderInBackground("marquee", templateId, system, rom, fanart, logo, path =>
        {
            var meta = _lastMarqueeMeta;
            if (meta?.Rom == null || !meta.Rom.Equals(rom, StringComparison.OrdinalIgnoreCase)) return;
            foreach (var target in _config.GetTargetsForContent("marquee"))
            {
                // never stomp a surface that displays its own graphic creation
                if (_compositionChains.SurfaceCreation("marquee", target, meta, systemScope) != null) continue;
                _ = _surfaces.DisplayMediaAsync(path, target, CancellationToken.None, meta, resolved: true);
            }
        });
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // rollback switch: [Settings] CoalesceStateStreams=false restores the
        // historical inline processing (strict FIFO on every stream)
        var coalesce = !_config.GetValue("Settings", "CoalesceStateStreams", "true")
            .Equals("false", StringComparison.OrdinalIgnoreCase);
        var tasks = new List<Task>();
        foreach (var stream in Streams)
        {
            if (coalesce && StateStreams.Contains(stream))
            {
                var mailbox = Channel.CreateUnbounded<JsonDocument>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
                _mailboxes[stream] = mailbox;
                tasks.Add(DrainLatestAsync(stream, mailbox.Reader, stoppingToken));
            }
            tasks.Add(ListenAsync(stream, stoppingToken));
        }
        return Task.WhenAll(tasks);
    }

    private async Task ListenAsync(string stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                var uri = new Uri($"{_config.ApiExposeWebSocketBaseUrl}/ws/{stream}");
                await socket.ConnectAsync(uri, cancellationToken);
                _logger.LogInformation("Connected to APIExpose {Stream} stream", stream);
                await ReceiveAsync(socket, stream, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning("APIExpose {Stream} stream disconnected: {Message}; retrying in 5 seconds", stream, ex.Message);
                try { await Task.Delay(5000, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, string stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) return;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text) continue;
            try
            {
                if (_mailboxes.TryGetValue(stream, out var mailbox))
                {
                    // never block the socket drain on processing: hand the
                    // snapshot to the stream worker (which owns its disposal)
                    mailbox.Writer.TryWrite(JsonDocument.Parse(message.ToArray()));
                    continue;
                }
                using var document = JsonDocument.Parse(message.ToArray());
                await ProcessAsync(stream, document.RootElement, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Invalid JSON received on {Stream}: {Message}", stream, ex.Message);
            }
        }
    }

    /// <summary>
    /// Worker for a state stream: drains everything already received and only
    /// processes the most recent snapshot — older ones describe selections the
    /// user has already scrolled past. On the frontend stream, lifecycle events
    /// (game started/ended) are never skipped; only `*.selected*` messages
    /// coalesce between themselves, in arrival order.
    /// </summary>
    private async Task DrainLatestAsync(string stream, ChannelReader<JsonDocument> reader, CancellationToken cancellationToken)
    {
        var batch = new List<JsonDocument>();
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                batch.Clear();
                while (reader.TryRead(out var pending)) batch.Add(pending);

                var lastSnapshot = -1;
                for (var i = batch.Count - 1; i >= 0; i--)
                {
                    if (!IsSnapshotMessage(stream, batch[i])) continue;
                    lastSnapshot = i;
                    break;
                }

                var skipped = 0;
                for (var i = 0; i < batch.Count; i++)
                {
                    var document = batch[i];
                    try
                    {
                        if (i < lastSnapshot && IsSnapshotMessage(stream, document))
                        {
                            skipped++;
                            continue;
                        }
                        await ProcessAsync(stream, document.RootElement, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Processing {Stream} message failed", stream);
                    }
                    finally
                    {
                        document.Dispose();
                    }
                }
                if (skipped > 0)
                    _logger.LogDebug("{Stream}: coalesced {Count} stale snapshot(s)", stream, skipped);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            while (reader.TryRead(out var leftover)) leftover.Dispose();
        }
    }

    /// <summary>frontend carries both state (`*.selected*`) and lifecycle events;
    /// the other state streams are pure snapshots.</summary>
    private static bool IsSnapshotMessage(string stream, JsonDocument document)
    {
        if (!stream.Equals("frontend", StringComparison.OrdinalIgnoreCase)) return true;
        return Text(document.RootElement, "Type", "type").Contains(".selected", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ProcessAsync(string stream, JsonElement root, CancellationToken cancellationToken)
    {
        switch (stream)
        {
            case "retroachievements":
                await _presentation.HandleRetroAchievementsAsync(root, cancellationToken);
                return;
            case "score":
                await _presentation.HandleScoreAsync(root, cancellationToken);
                return;
            case "timer":
                await _presentation.HandleTimerAsync(root, cancellationToken);
                return;
            case "arcade":
                await HandleArcadeAsync(root, cancellationToken);
                return;
            case "ingame":
                HandleIngame(root);
                return;
            case "frontend":
                await HandleFrontendAsync(root, cancellationToken);
                return;
            case "marquee":
                await HandleMarqueeAsync(root, cancellationToken);
                return;
            case "topper":
                await HandleTopperAsync(root, cancellationToken);
                return;
            case "instruction-card":
                await HandleInstructionCardAsync(root, cancellationToken);
                return;
            case "panel":
                await HandleSimpleMediaAsync(root, "lcd", cancellationToken);
                return;
            case "hiscore":
                HandleHiscore(root, cancellationToken);
                return;
        }
    }

    private async Task HandleMarqueeAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var payload = Payload(root);
        var media = Child(payload, "Media", "media");
        var snapshotMeta = ExtractLightingMeta(payload);
        var selection = Child(payload, "Selection", "selection");
        var systemScope = Text(selection, "Scope", "scope").Equals("system", StringComparison.OrdinalIgnoreCase);

        // remember the snapshot kinds: template renders and component feeds use them
        lock (_lastMarqueeKinds)
        {
            _lastMarqueeKinds["logo"] = MediaPath(media, "Logo");
            _lastMarqueeKinds["fanart"] = MediaPath(media, "Fanart");
            _lastMarqueeKinds["marquee"] = MediaPath(media, "Marquee");
            _lastMarqueeKinds["generated"] = MediaPath(media, "GeneratedMarquee");
            _lastMarqueeKinds["screenmarquee"] = MediaPath(media, "ScreenMarquee");
            _lastMarqueeKinds["screenmarquee-small"] = MediaPath(media, "ScreenMarqueeSmall");
            _lastMarqueeKinds["topper"] = MediaPath(media, "Topper");
        }
        _lastMarqueeMeta = snapshotMeta;

        // the per-system priority chain decides the marquee source; the stream's
        // own priority (marquee > generated > logo) stays the last resort
        var chained = _compositionChains.Resolve("marquee", snapshotMeta, systemScope, SnapshotKind);
        var marquee = chained
                      ?? MediaPath(media, "Marquee") ?? MediaPath(media, "GeneratedMarquee") ?? MediaPath(media, "Logo");
        if (marquee != null)
        {
            if (snapshotMeta != null)
            {
                // the ingame effect layers (game > system > genre) follow the displayed game
                _ingameEffects.SetContext(snapshotMeta.System ?? _selectedSystem, snapshotMeta.Rom ?? _selectedRom,
                    _genreMap.Resolve(snapshotMeta.Genre, snapshotMeta.GenreIds), _effectOverridesRoot, _logger);
            }
            foreach (var target in _config.GetTargetsForContent("marquee"))
            {
                // a graphic creation saved for THIS surface wins over the
                // category-level resolution (creations are per-surface)
                var surfaceCreation = _compositionChains.SurfaceCreation("marquee", target, snapshotMeta, systemScope);
                await _surfaces.DisplayMediaAsync(surfaceCreation ?? marquee, target, cancellationToken, snapshotMeta,
                    resolved: surfaceCreation != null || chained != null);
            }
        }

        FeedSurfaceComponents(media, snapshotMeta);

        var dmd = Child(media, "Dmd", "dmd");
        var generatedDmdPath = MediaPath(dmd, "Generated");
        var stillDmdPath = MediaPath(dmd, "Still");
        var chainedDmd = _compositionChains.Resolve("dmd", snapshotMeta, systemScope, source => source.ToLowerInvariant() switch
        {
            "animations" => FirstAnimation(dmd),
            "still" => stillDmdPath,
            "generated" => generatedDmdPath,
            _ => null
        });
        var dmdPath = chainedDmd ?? FirstAnimation(dmd) ?? stillDmdPath ?? generatedDmdPath;
        if (dmdPath == null) return;
        // Keep the generated game DMD behind text even when an animation is preferred while idle.
        await _dmd.SetBaseMediaAsync(dmdPath, cancellationToken, generatedDmdPath ?? stillDmdPath ?? dmdPath);
        foreach (var target in _config.GetTargetsForContent("dmd"))
        {
            var surfaceCreation = _compositionChains.SurfaceCreation("dmd", target, snapshotMeta, systemScope);
            await _surfaces.DisplayMediaAsync(surfaceCreation ?? dmdPath, target, cancellationToken);
        }
    }

    /// <summary>
    /// The dynamic surface components eat the whole snapshot: every media kind
    /// (logo, fanart, screenmarquee…) plus the game video resolved on disk (the
    /// snapshot does not carry it), and the selection meta for text.meta.
    /// Cheap no-op when no surface declares dynamic components.
    /// </summary>
    private void FeedSurfaceComponents(JsonElement media, Application.Lighting.LightingSceneMeta? meta)
    {
        if (!_surfaces.HasComponent("media.logo") && !_surfaces.HasComponent("media.fanart")
            && !_surfaces.HasComponent("media.image") && !_surfaces.HasComponent("media.video")
            && !_surfaces.HasComponent("text.meta"))
            return;

        var kinds = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["logo"] = MediaPath(media, "Logo"),
            ["fanart"] = MediaPath(media, "Fanart"),
            ["marquee"] = MediaPath(media, "Marquee"),
            ["generated"] = MediaPath(media, "GeneratedMarquee"),
            ["screenmarquee"] = MediaPath(media, "ScreenMarquee"),
            ["screenmarquee-small"] = MediaPath(media, "ScreenMarqueeSmall"),
            ["topper"] = MediaPath(media, "Topper"),
            // APIExpose 1.3.5+ carries Media.Video in the snapshot; the disk
            // walk stays as fallback for older APIs.
            ["video"] = MediaPath(media, "Video") ?? ResolveGameVideo(meta)
        };

        var metaValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = meta?.GameName ?? "",
            ["year"] = meta?.Year?.ToString() ?? "",
            ["developer"] = meta?.Developer ?? "",
            ["publisher"] = meta?.Publisher ?? "",
            ["system"] = meta?.System ?? ""
        };

        _surfaces.UpdateComponentMedia(kinds, metaValues);
        _ = ResolveLiveVideoAsync(meta);
    }

    private static readonly HttpClient VideoHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
    private (string Token, DateTime Expires) _twitchToken;

    /// <summary>
    /// media.video source chain (user rule: live stream &gt; YouTube &gt; local video).
    /// The local file is already pushed with the snapshot; when a live Twitch
    /// stream (or a YouTube video) is found for the game, the component swaps to
    /// its embed. Every lookup failure silently keeps the previous source.
    /// </summary>
    private async Task ResolveLiveVideoAsync(Application.Lighting.LightingSceneMeta? meta)
    {
        if (meta?.GameName is not { Length: > 0 } gameName) return;
        var sources = _config.GetSurfaces()
            .SelectMany(surface => surface.Components)
            .FirstOrDefault(component => component.Type.Equals("media.video", StringComparison.OrdinalIgnoreCase))
            ?.Option("sources", "local")
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (sources == null || !sources.Any(s => s is "twitch-live" or "youtube")) return;

        var rom = meta.Rom;
        foreach (var source in sources)
        {
            string? url = null;
            try
            {
                if (source.Equals("twitch-live", StringComparison.OrdinalIgnoreCase))
                    url = await TwitchLiveUrlAsync(gameName).ConfigureAwait(false);
                else if (source.Equals("youtube", StringComparison.OrdinalIgnoreCase))
                    url = await YouTubeEmbedUrlAsync(gameName).ConfigureAwait(false);
                else if (source.Equals("local", StringComparison.OrdinalIgnoreCase))
                    return; // the snapshot feed already pushed the local file
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Video source {Source} lookup failed: {Message}", source, ex.Message);
            }

            if (url != null)
            {
                // the selection may have moved on during the lookup
                if (_lastMarqueeMeta?.Rom != rom) return;
                _logger.LogInformation("media.video: {Source} found for {Game}", source, gameName);
                _surfaces.SetComponentSource("media.video", url);
                return;
            }
        }
    }

    /// <summary>Live Twitch stream on the game, via Helix (client credentials from
    /// config [Scraper] TwitchClientId/TwitchClientSecret).</summary>
    private async Task<string?> TwitchLiveUrlAsync(string gameName)
    {
        var clientId = _config.GetValue("Scraper", "TwitchClientId");
        var secret = _config.GetValue("Scraper", "TwitchClientSecret");
        if (clientId.Length == 0 || secret.Length == 0) return null;

        if (_twitchToken.Token is not { Length: > 0 } || DateTime.UtcNow >= _twitchToken.Expires)
        {
            using var tokenResponse = await VideoHttp.PostAsync(
                $"https://id.twitch.tv/oauth2/token?client_id={Uri.EscapeDataString(clientId)}&client_secret={Uri.EscapeDataString(secret)}&grant_type=client_credentials",
                null).ConfigureAwait(false);
            if (!tokenResponse.IsSuccessStatusCode) return null;
            using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
            var token = tokenDoc.RootElement.GetProperty("access_token").GetString() ?? "";
            var expires = tokenDoc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
            _twitchToken = (token, DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 120)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.twitch.tv/helix/games?name={Uri.EscapeDataString(gameName)}");
        request.Headers.Add("Client-Id", clientId);
        request.Headers.Add("Authorization", "Bearer " + _twitchToken.Token);
        using var gameResponse = await VideoHttp.SendAsync(request).ConfigureAwait(false);
        if (!gameResponse.IsSuccessStatusCode) return null;
        using var gameDoc = JsonDocument.Parse(await gameResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
        var gameId = gameDoc.RootElement.TryGetProperty("data", out var games) && games.GetArrayLength() > 0
            ? games[0].GetProperty("id").GetString()
            : null;
        if (gameId == null) return null;

        using var streamsRequest = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.twitch.tv/helix/streams?game_id={gameId}&first=1");
        streamsRequest.Headers.Add("Client-Id", clientId);
        streamsRequest.Headers.Add("Authorization", "Bearer " + _twitchToken.Token);
        using var streamsResponse = await VideoHttp.SendAsync(streamsRequest).ConfigureAwait(false);
        if (!streamsResponse.IsSuccessStatusCode) return null;
        using var streamsDoc = JsonDocument.Parse(await streamsResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
        var login = streamsDoc.RootElement.TryGetProperty("data", out var streams) && streams.GetArrayLength() > 0
            ? streams[0].GetProperty("user_login").GetString()
            : null;
        return login == null ? null : $"https://www.twitch.tv/{login}";
    }

    /// <summary>First embeddable YouTube video on the game (Data API key in
    /// config [Scraper] YouTubeApiKey).</summary>
    private async Task<string?> YouTubeEmbedUrlAsync(string gameName)
    {
        var key = _config.GetValue("Scraper", "YouTubeApiKey");
        if (key.Length == 0) return null;
        var query = Uri.EscapeDataString(gameName + " arcade gameplay");
        var json = await VideoHttp.GetStringAsync(
            $"https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=1&type=video&videoEmbeddable=true&q={query}&key={Uri.EscapeDataString(key)}")
            .ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0
            ? items[0].GetProperty("id").GetProperty("videoId").GetString()
            : null;
        return id == null ? null : $"https://www.youtube.com/embed/{id}?autoplay=1&mute=1&controls=0&loop=1&playlist={id}";
    }

    /// <summary>Fallback for APIExpose &lt; 1.3.5 (snapshot without Media.Video):
    /// games\&lt;rom&gt;\video.mp4 lives in the APIExpose media library (sibling
    /// plugin) and is walked on disk.</summary>
    private string? ResolveGameVideo(Application.Lighting.LightingSceneMeta? meta)
    {
        if (meta?.System is not { Length: > 0 } || meta.Rom is not { Length: > 0 }) return null;
        foreach (var system in meta.System.Equals("mame", StringComparison.OrdinalIgnoreCase)
                     ? new[] { meta.System, "arcade" } : new[] { meta.System })
        {
            var path = Path.Combine(_config.BaseDirectory, "..", "APIExpose", "media", "systems",
                system, "games", meta.Rom, "video.mp4");
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        return null;
    }

    /// <summary>
    /// Metadata carried by the enriched marquee stream (Selection.Releasedate /
    /// Developer / Publisher / System) — input of the §15 lighting profile resolver.
    /// </summary>
    private static Application.Lighting.LightingSceneMeta? ExtractLightingMeta(JsonElement payload)
    {
        var selection = Child(payload, "Selection", "selection");
        if (selection.ValueKind != JsonValueKind.Object) return null;

        int? year = null;
        var releasedate = Text(selection, "Releasedate", "releasedate", "ReleaseDate");
        if (releasedate.Length >= 4 && int.TryParse(releasedate[..4], out var parsed) && parsed is > 1950 and < 2100)
            year = parsed;

        var developer = Text(selection, "Developer", "developer");
        var publisher = Text(selection, "Publisher", "publisher");
        var gameName = Text(selection, "GameName", "gameName", "Name", "name");
        var system = Text(selection, "System", "system");
        var gamePath = Text(selection, "GamePath", "gamePath");
        var rom = gamePath.Length > 0 ? Path.GetFileNameWithoutExtension(gamePath) : Text(selection, "Game", "game");
        var genre = Text(selection, "Genre", "genre");
        var genreIds = Text(selection, "Genres", "genres");

        if (year == null && developer.Length == 0 && publisher.Length == 0 && gameName.Length == 0 && system.Length == 0)
            return null;
        return new Application.Lighting.LightingSceneMeta(year,
            developer.Length > 0 ? developer : null,
            publisher.Length > 0 ? publisher : null,
            gameName.Length > 0 ? gameName : null,
            system.Length > 0 ? system : null,
            rom.Length > 0 ? rom : null,
            genre.Length > 0 ? genre : null,
            genreIds.Length > 0 ? genreIds : null);
    }

    private async Task HandleTopperAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var payload = Payload(root);
        var media = Child(payload, "Media", "media");
        var meta = ExtractLightingMeta(payload) ?? _lastMarqueeMeta;
        var systemScope = Text(Child(payload, "Selection", "selection"), "Scope", "scope")
            .Equals("system", StringComparison.OrdinalIgnoreCase);
        var chained = _compositionChains.Resolve("topper", meta, systemScope, source =>
            source.Equals("topper", StringComparison.OrdinalIgnoreCase) ? MediaPath(media, "Topper")
            : source.Equals("fanart", StringComparison.OrdinalIgnoreCase) ? MediaPath(media, "Fanart")
            : source.Equals("logo", StringComparison.OrdinalIgnoreCase) ? MediaPath(media, "Logo")
            : null);
        var path = chained ?? MediaPath(media, "Topper");
        if (path != null)
        {
            foreach (var target in _config.GetTargetsForContent("topper"))
            {
                var surfaceCreation = _compositionChains.SurfaceCreation("topper", target, meta, systemScope);
                await _surfaces.DisplayMediaAsync(surfaceCreation ?? path, target, cancellationToken,
                    resolved: surfaceCreation != null || chained != null);
            }
        }
    }

    /// <summary>Chain source name → the last marquee snapshot asset.</summary>
    private string? SnapshotKind(string source)
    {
        lock (_lastMarqueeKinds)
        {
            return _lastMarqueeKinds.TryGetValue(source, out var path) ? path : null;
        }
    }

    private async Task HandleInstructionCardAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var payload = Payload(root);
        var cards = Child(payload, "Cards", "cards");
        if (cards.ValueKind != JsonValueKind.Array) return;
        // keep the whole catalog: the touch profile can cycle/show any of them
        var paths = new List<string>();
        foreach (var card in cards.EnumerateArray())
        {
            var path = ResolveLocal(Text(card, "Path", "path"));
            if (path != null) paths.Add(path);
        }

        if (paths.Count == 0) return;
        await _instructionCards.SetCardsAsync(paths, cancellationToken);
    }

    private async Task HandleSimpleMediaAsync(JsonElement root, string defaultTarget, CancellationToken cancellationToken)
    {
        var payload = Payload(root);
        var path = ResolveLocal(Text(payload, "Path", "path", "Value", "value"));
        var target = Text(payload, "Target", "target");
        if (path != null) await _surfaces.DisplayMediaAsync(path, target.Length == 0 ? defaultTarget : target.ToLowerInvariant(), cancellationToken);
    }

    private void HandleHiscore(JsonElement root, CancellationToken cancellationToken)
    {
        var payload = Payload(root);
        var rom = NormalizeRom(Text(payload, "RomName", "romName", "Rom", "rom"));
        if (rom.Length == 0) rom = NormalizeRom(Text(payload, "GamePath", "gamePath", "RomPath", "romPath"));
        if (rom.Length == 0 || !rom.Equals(_selectedRom, StringComparison.OrdinalIgnoreCase) && !rom.Equals(_runningRom, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Ignoring hiscore event outside selected/running game context: rom={Rom}, selected={Selected}, running={Running}", rom, _selectedRom, _runningRom);
            return;
        }

        var score = Text(payload, "Score", "score", "Value", "value");
        var player = Text(payload, "Player", "player", "Name", "name");
        if (score.Length == 0)
        {
            var scores = Child(payload, "Scores", "scores");
            if (scores.ValueKind == JsonValueKind.Array)
            {
                var first = scores.EnumerateArray().FirstOrDefault();
                score = Text(first, "Score", "score", "Value", "value");
                player = Text(first, "Name", "name", "Player", "player");
            }
        }
        if (score.Length == 0) return;
        _surfaces.SetInformation("hiscore", "HIGH SCORE", $"{player} {score}".Trim(), null, true, 0);
    }

    private async Task HandleFrontendAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var type = Text(root, "Type", "type");
        var payload = Payload(root);
        if (type.Equals("ui.game.selected", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("ui.game.selected.raw", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("ui.system.selected", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("ui.system.selected.raw", StringComparison.OrdinalIgnoreCase))
        {
            if (_pinballDmdActive) ReleasePinballDmd("selection changed");
            _surfaces.SetDisplayScene("navigation");
            var selectedSystem = ExtractSystem(payload);
            var selectedRom = ExtractRom(payload);
            if (selectedSystem.Length > 0)
            {
                _selectedSystem = selectedSystem;
                _logger.LogInformation("Frontend selected system: {System}", selectedSystem);
            }
            if (selectedRom.Length > 0)
            {
                if (!string.Equals(_selectedRom, selectedRom, StringComparison.OrdinalIgnoreCase))
                    _surfaces.ClearInformation("hiscore");
                _selectedRom = selectedRom;
            }
            else
            {
                _selectedRom = null;
                _surfaces.ClearInformation("hiscore");
            }
            return;
        }

        if (type.Equals("ui.game.ended", StringComparison.OrdinalIgnoreCase) || type.Equals("ui.game.ended.raw", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Frontend game ended event received: {Type}", type);
            // scene FIRST: ingame-only surfaces must leave the game screen even
            // if a later step throws — ES does not always re-select afterwards
            _surfaces.SetDisplayScene("navigation");
            _lay.Clear();
            _presentation.MarkGameEnded();
            _runningRom = null;
            if (_pinballDmdActive) ReleasePinballDmd("game ended");
            // back to the frontend: sounds return, audible re-ignition
            _surfaces.SetLightingIngame(false);
            _surfaces.PowerCycleLighting();
            return;
        }
        if (!type.Equals("ui.game.started", StringComparison.OrdinalIgnoreCase) && !type.Equals("ui.game.started.raw", StringComparison.OrdinalIgnoreCase)) return;
        _surfaces.SetDisplayScene("ingame");
        _presentation.MarkGameStarted();
        // game launch drama: silent power cycle — the play session stays clean
        _surfaces.SetLightingIngame(true);
        _surfaces.PowerCycleLighting();
        var system = ExtractSystem(payload);
        if (system.Length == 0) system = _selectedSystem ?? string.Empty;
        if (system.Length > 0) _selectedSystem = system;
        var rom = ExtractRom(payload);
        if (rom.Length > 0) _runningRom = rom;
        _logger.LogInformation("Frontend game started event received: {Type}, system={System}, rom={Rom}", type, system, rom);
        if (system.Length > 0 && _config.ActiveSystemsDmd.Contains(system))
        {
            ActivatePinballDmd(system);
            return;
        }
        if (rom.Length > 0) await LoadLayoutAsync(rom, cancellationToken);
    }

    private void ActivatePinballDmd(string system)
    {
        if (_pinballDmdActive) return;
        _pinballDmdActive = true;
        _logger.LogInformation("System {System} is configured in ActiveSystemsDMD; private DMD is released for pinball.", system);
        _lay.Clear();
        _presentation.ClearGameState();
        _dmd.SetExternalControl(true);
    }

    private void ReleasePinballDmd(string reason)
    {
        if (!_pinballDmdActive) return;
        _pinballDmdActive = false;
        _logger.LogInformation("Pinball DMD external control released ({Reason}); private DMD will resume.", reason);
        _dmd.SetExternalControl(false);
    }

    private async Task HandleArcadeAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var type = Text(root, "Type", "type");
        var payload = Payload(root);
        var signals = Child(payload, "Signals", "signals");
        if (signals.ValueKind == JsonValueKind.Undefined) signals = Child(root, "Signals", "signals");
        if (signals.ValueKind == JsonValueKind.Array)
        {
            foreach (var signal in signals.EnumerateArray())
            {
                var key = Text(signal, "Key", "key");
                var value = Integer(signal, "Value", "value");
                if (key.Length > 0 && value != null)
                {
                    _lay.SetLampState(key, value.Value);
                    _surfaces.SetLightingOutput(key, value.Value);
                }
            }
        }
        if (type.Equals("mame.session.started", StringComparison.OrdinalIgnoreCase))
        {
            var rom = Text(payload, "MachineName", "machineName", "Rom", "rom");
            if (rom.Length > 0) await LoadLayoutAsync(rom, cancellationToken);
        }
    }

    /// <summary>
    /// ws/ingame: semantic .mem actions (CDC §9). The action is already semantic —
    /// resolve it through the ingame effects library and fire the light effect.
    /// </summary>
    private void HandleIngame(JsonElement root)
    {
        // APIExpose wrapper events are EventEnvelopes: the semantic action lives in
        // Payload.actionType or Payload.signal.Name (same extraction as LedManager)
        var payload = Payload(root);
        var signal = Child(payload, "Signal", "signal");

        var action = Text(root, "Action", "action");
        if (action.Length == 0) action = Text(payload, "ActionType", "actionType", "Action", "action");
        if (action.Length == 0 && signal.ValueKind == JsonValueKind.Object) action = Text(signal, "Name", "name");
        if (action.Length == 0) return;

        var family = Text(root, "Family", "family");
        if (family.Length == 0) family = Text(payload, "Family", "family");
        if (family.Length == 0 && signal.ValueKind == JsonValueKind.Object) family = Text(signal, "Family", "family");

        var color = Text(root, "Color", "color");
        if (color.Length == 0) color = Text(payload, "Color", "color");
        if (color.Length == 0 && signal.ValueKind == JsonValueKind.Object) color = Text(signal, "Color", "color");

        // flow lifecycle changes gate the speedrun leaderboard (no timer during demos)
        _presentation.OnGameplayFlow(action);

        var sequence = _ingameEffects.Resolve(action, family.Length > 0 ? family : null);
        if (sequence.Count == 0) return;

        // la couleur portee par l'evenement (deltas score arcade) prime sur la
        // couleur de la regle : l'effet prend la teinte de la cible du jeu.
        var eventColor = Application.Lighting.IngameEffectLibrary.TryParseEventColor(color);

        _logger.LogInformation("Ingame action {Action} → {Count} effect action(s) ({Label})",
            action, sequence.Count, sequence[0].Label);
        foreach (var step in sequence)
        {
            var rule = eventColor is { } overrideColor ? step with { Color = overrideColor } : step;
            if (rule.DelayMs <= 0)
            {
                FireEffect(rule);
            }
            else
            {
                // sequenced action ("flash PUIS nuée de sprites"): fire after its delay
                _ = Task.Delay(rule.DelayMs).ContinueWith(_ => FireEffect(rule), TaskScheduler.Default);
            }
        }
    }

    private void FireEffect(Application.Lighting.IngameEffectRule rule)
    {
        if (rule.MediaPath is { Length: > 0 })
        {
            _surfaces.PlayMediaEffect(rule.MediaPath, rule.MediaFullscreen, rule.DurationMs);
            if (rule.Kind == Application.Lighting.IngameEffectKind.Sprite && rule.Sprite == null) return;
        }
        _surfaces.TriggerLightingEffect(rule);
    }

    private Task LoadLayoutAsync(string rom, CancellationToken cancellationToken)
    {
        if (!_config.LayEnabled || cancellationToken.IsCancellationRequested) return Task.CompletedTask;

        // CDC §26.3: the rbmarquee lighting scene owns the marquee, but the .lay
        // keeps driving the DMD with its purpose-built 128x32 view
        var lightingOwnsMarquee = _config.LightingEnabled &&
            File.Exists(Path.Combine(_config.BaseDirectory, "resources", "rbmarquee", rom + ".xml"));

        var path = Path.Combine(_config.LayDofPath, rom, "default.lay");
        if (!File.Exists(path)) path = ResolveAliasLayout(rom) ?? path;
        if (!File.Exists(path)) return Task.CompletedTask;
        var layout = MameLayParser.Parse(path);
        foreach (var warning in layout.Warnings) _logger.LogWarning("MAME layout {Path}: {Warning}", path, warning);
        if (layout.Views.Count == 0)
        {
            _logger.LogWarning("MAME layout contains no supported views: {Path}", path);
            return Task.CompletedTask;
        }
        if (lightingOwnsMarquee)
            _logger.LogInformation("Legacy .lay for {Rom}: DMD view only (rbmarquee scene owns the marquee)", rom);
        _lay.LoadMameLayout(layout, Path.GetDirectoryName(path)!, rom, dmdOnly: lightingOwnsMarquee);
        return Task.CompletedTask;
    }

    private string? ResolveAliasLayout(string rom)
    {
        var path = Path.Combine(_config.LayDofPath, "aliases.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Name.Equals(rom, StringComparison.OrdinalIgnoreCase))
                        return Path.Combine(_config.LayDofPath, property.Value.GetString() ?? string.Empty, "default.lay");
        }
        catch (Exception ex) { _logger.LogWarning("Invalid DOF aliases.json: {Message}", ex.Message); }
        return null;
    }

    private string ExtractRom(JsonElement payload)
    {
        var direct = Text(payload, "Rom", "rom", "MachineName", "machineName");
        if (direct.Length > 0) return Path.GetFileNameWithoutExtension(direct);
        foreach (var name in new[] { "Selection", "selection", "Running", "running" })
        {
            var child = Child(payload, name);
            var longName = Text(child, "LongName", "longName");
            if (longName.Length > 0) return longName;
            var path = Text(child, "GamePath", "gamePath");
            if (path.Length > 0) return Path.GetFileNameWithoutExtension(path);
        }
        var context = Child(payload, "Context", "context");
        var selected = Child(context, "Selected", "selected");
        if (selected.ValueKind == JsonValueKind.Undefined)
        {
            var ui = Child(context, "Ui", "ui");
            selected = Child(ui, "Selected", "selected");
        }
        var selectedPath = Text(selected, "GamePath", "gamePath");
        if (selectedPath.Length > 0) return Path.GetFileNameWithoutExtension(selectedPath);
        return string.Empty;
    }

    private string ExtractSystem(JsonElement payload)
    {
        var direct = Text(payload, "System", "system", "SystemName", "systemName", "SystemId", "systemId", "Platform", "platform", "Collection", "collection");
        if (direct.Length > 0) return NormalizeSystem(direct);
        foreach (var name in new[] { "Selection", "selection", "Selected", "selected", "Running", "running", "Game", "game" })
        {
            var child = Child(payload, name);
            var system = Text(child, "System", "system", "SystemName", "systemName", "SystemId", "systemId", "Platform", "platform");
            if (system.Length > 0) return NormalizeSystem(system);
            var path = Text(child, "GamePath", "gamePath", "Path", "path");
            var fromPath = SystemFromPath(path);
            if (fromPath.Length > 0) return fromPath;
        }
        var context = Child(payload, "Context", "context");
        foreach (var name in new[] { "Selected", "selected", "Running", "running", "Ui", "ui" })
        {
            var child = Child(context, name);
            var system = ExtractSystemFromObject(child);
            if (system.Length > 0) return system;
        }
        return SystemFromPath(Text(payload, "GamePath", "gamePath", "Path", "path"));
    }

    private static string ExtractSystemFromObject(JsonElement source)
    {
        var system = Text(source, "System", "system", "SystemName", "systemName", "SystemId", "systemId", "Platform", "platform");
        if (system.Length > 0) return NormalizeSystem(system);
        return SystemFromPath(Text(source, "GamePath", "gamePath", "Path", "path"));
    }

    private static string SystemFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var parts = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
            if (parts[i].Equals("roms", StringComparison.OrdinalIgnoreCase))
                return NormalizeSystem(parts[i + 1]);
        return string.Empty;
    }

    private static string NormalizeSystem(string value)
        => value.Trim().Trim('"').ToLowerInvariant();

    private static string NormalizeRom(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFileNameWithoutExtension(value.Trim().Trim('"')).ToLowerInvariant();

    private string? MediaPath(JsonElement source, string name)
    {
        var node = Child(source, name, name.ToLowerInvariant());
        return ResolveLocal(node.ValueKind == JsonValueKind.String ? node.GetString() ?? string.Empty : Text(node, "Path", "path"));
    }

    private string? FirstAnimation(JsonElement dmd)
    {
        var animations = Child(dmd, "Animations", "animations");
        if (animations.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in animations.EnumerateArray())
        {
            var path = ResolveLocal(item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : Text(item, "Path", "path"));
            if (path != null) return path;
        }
        return null;
    }

    private string? ResolveLocal(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile) return null;
        var path = Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(_config.BaseDirectory, "..", "APIExpose", value));
        return File.Exists(path) ? path : null;
    }

    private static JsonElement Payload(JsonElement root)
    {
        var payload = Child(root, "Payload", "payload");
        return payload.ValueKind == JsonValueKind.Undefined ? root : payload;
    }
    private static JsonElement Child(JsonElement source, params string[] names)
    {
        if (source.ValueKind != JsonValueKind.Object) return default;
        foreach (var property in source.EnumerateObject())
            if (names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return property.Value;
        return default;
    }
    private static string Text(JsonElement source, params string[] names)
    {
        var value = Child(source, names);
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? string.Empty : value.ToString();
    }
    private static int? Integer(JsonElement source, params string[] names)
    {
        var value = Child(source, names);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)) return result;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result) ? result : null;
    }
}
