using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Processes;
using RetroBatMarqueeManager.Application.Workflows;

namespace RetroBatMarqueeManager.Application.Services
{
    public class WebSocketListenerService : BackgroundService
    {
        private readonly IConfigService _config;
        private readonly MarqueeController _mpv;
        private readonly MarqueeWorkflow _workflow;
        private readonly IDmdService _dmdService;
        private readonly ILogger<WebSocketListenerService> _logger;

        private readonly string _apiExposeBasePath;

        // Latest-wins channels (capacity=1, DropOldest) — always shows the most recent snapshot
        private readonly Channel<MarqueeRequest> _marqueeChannel = Channel.CreateBounded<MarqueeRequest>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        private long _latestMarqueeRequestId;

        // Dedicated DMD channel — decoupled from LCD/marquee so both update simultaneously
        private readonly Channel<DmdRequest> _dmdChannel = Channel.CreateBounded<DmdRequest>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        private long _latestDmdRequestId;

        // Routing table: contentSource → list of window targets that display it
        // e.g. "iccard" → ["iccard", "dmd"] if DmdContent=iccard in config
        private readonly Dictionary<string, List<string>> _routing;

        public WebSocketListenerService(
            IConfigService config,
            MarqueeController mpv,
            MarqueeWorkflow workflow,
            IDmdService dmdService,
            ILogger<WebSocketListenerService> logger)
        {
            _config = config;
            _mpv = mpv;
            _workflow = workflow;
            _dmdService = dmdService;
            _logger = logger;

            // Resolve APIExpose base path: explicit config > sibling directory auto-detect
            string configured = "";
            if (config is Infrastructure.Configuration.IniConfigService ini)
                configured = ini.GetSetting("ApiExposeBasePath", "");

            if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
            {
                _apiExposeBasePath = configured;
            }
            else
            {
                var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                var exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                var pluginsDir = Directory.GetParent(exeDir)?.FullName ?? "";
                _apiExposeBasePath = Path.Combine(pluginsDir, "APIExpose");
            }
            _logger.LogInformation($"[WebSocket] APIExpose base path: {_apiExposeBasePath}");

            // Build routing table from config
            // Default: each target shows its own content
            // Override via config: DmdContent=iccard → dmd screen shows IC card stream
            // Available sources: marquee, topper, dmd, iccard, fanart
            _routing = BuildRoutingTable(config);
            foreach (var (src, targets) in _routing)
                _logger.LogInformation($"[WebSocket] Routing: {src} → [{string.Join(", ", targets)}]");
        }

        private static Dictionary<string, List<string>> BuildRoutingTable(IConfigService config)
        {
            // Default routing: each screen target receives its natural stream content (1:1)
            // Override any mapping via config: TopperContent=fanart, DmdContent=iccard, etc.
            var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["marquee"] = "marquee",
                ["topper"]  = "topper",
                ["dmd"]     = "dmd",
                ["iccard"]  = "iccard",
                ["lcd"]     = "fanart",
            };

            // Read overrides: {TargetName}Content={source} in config
            var targets = new[] { "marquee", "topper", "dmd", "iccard", "lcd" };
            foreach (var target in targets)
            {
                var source = config.GetSetting($"{target}Content", "")
                          ?? config.GetSetting($"{char.ToUpper(target[0])}{target[1..]}Content", "");
                if (!string.IsNullOrEmpty(source))
                    defaults[target] = source.ToLowerInvariant();
            }

            // Invert: source → [targets]
            var routing = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (target, source) in defaults)
            {
                if (!routing.TryGetValue(source, out var list))
                {
                    list = new List<string>();
                    routing[source] = list;
                }
                list.Add(target);
            }
            return routing;
        }

        private List<string> TargetsFor(string contentSource)
            => _routing.TryGetValue(contentSource, out var list) ? list : new List<string>();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var baseUrl = "ws://127.0.0.1:12345";
            if (_config is Infrastructure.Configuration.IniConfigService iniConfig)
            {
                var customUrl = iniConfig.GetSetting("ApiExposeBaseUrl", "ws://127.0.0.1:12345");
                if (!string.IsNullOrEmpty(customUrl))
                    baseUrl = customUrl;
            }

            var streams = new[] { "arcade", "frontend", "ingame", "panel", "marquee", "topper", "instruction-card", "hiscore" };
            _logger.LogInformation($"[WebSocket] Starting concurrent listener tasks for streams: {string.Join(", ", streams)}");

            // Latest-wins workers (independent, both start concurrently)
            var marqueeWorker = RunMarqueeWorkerAsync(stoppingToken);
            var dmdWorker     = RunDmdWorkerAsync(stoppingToken);

            var wsTasks = streams.Select(stream => RunWebSocketClientAsync(baseUrl, stream, stoppingToken));
            await Task.WhenAll(wsTasks.Append(marqueeWorker).Append(dmdWorker));
        }

        // Single worker loop — always processes the LATEST marquee snapshot, skips older ones
        private async Task RunMarqueeWorkerAsync(CancellationToken ct)
        {
            await foreach (var request in _marqueeChannel.Reader.ReadAllAsync(ct))
            {
                if (ct.IsCancellationRequested) break;

                var latest = request;
                while (_marqueeChannel.Reader.TryRead(out var newer))
                    latest = newer;

                if (IsStaleMarqueeRequest(latest.Id))
                    continue;

                await ProcessMarqueeSnapshotAsync(latest, ct);
            }
        }

        // Dedicated DMD worker — independent from marquee worker, no delay between LCD and DMD
        private async Task RunDmdWorkerAsync(CancellationToken ct)
        {
            await foreach (var request in _dmdChannel.Reader.ReadAllAsync(ct))
            {
                if (ct.IsCancellationRequested) break;

                var latest = request;
                while (_dmdChannel.Reader.TryRead(out var newer))
                    latest = newer;

                try
                {
                    await Task.Delay(90, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                while (_dmdChannel.Reader.TryRead(out var newer))
                    latest = newer;

                if (latest.Id != Interlocked.Read(ref _latestDmdRequestId))
                    continue;

                try
                {
                    var started = Stopwatch.GetTimestamp();
                    await _dmdService.PlayAsync(latest.Path);
                    _logger.LogInformation(
                        "[Timing] DMD request={RequestId} completed elapsedMs={ElapsedMs:F1} path={Path}",
                        latest.Id,
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        latest.Path);
                }
                catch (Exception ex) { _logger.LogError($"[DMD Worker] Error: {ex.Message}"); }
            }
        }

        private async Task RunWebSocketClientAsync(string baseUrl, string streamName, CancellationToken stoppingToken)
        {
            var wsUri = new Uri(baseUrl.TrimEnd('/') + "/ws/" + streamName);
            _logger.LogInformation($"[WebSocket] Connecting to {streamName} telemetry stream: {wsUri}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(5));

                    await ws.ConnectAsync(wsUri, cts.Token);
                    _logger.LogInformation($"[WebSocket] Connected to {streamName} stream successfully.");

                    var buffer = new byte[64 * 1024];
                    while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                    {
                        using var ms = new MemoryStream();
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await ws.ReceiveAsync(buffer, stoppingToken);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                break;
                            }
                            ms.Write(buffer, 0, result.Count);
                        } while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogInformation($"[WebSocket] Server closed connection for stream {streamName}");
                            break;
                        }

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            var text = Encoding.UTF8.GetString(ms.ToArray());
                            ProcessMessage(streamName, text);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[WebSocket] {streamName} connection dropped: {ex.Message}. Reconnecting in 5 seconds...");
                    try
                    {
                        await Task.Delay(5000, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private void ProcessMessage(string streamName, string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 1. Arcade stream: mame.output.changed (lamps) + mame.session.started (MAME layout)
                if (streamName.Equals("arcade", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("type", out var typeEl))
                    {
                        var arcadeType = typeEl.GetString() ?? "";
                        if (string.Equals(arcadeType, "mame.output.changed", StringComparison.OrdinalIgnoreCase))
                        {
                            ProcessArcadeOutput(root);
                            return;
                        }
                        if (string.Equals(arcadeType, "mame.session.started", StringComparison.OrdinalIgnoreCase))
                        {
                            _ = HandleMameSessionStarted(json);
                            return;
                        }
                    }
                }

                // Get event Type
                string type = "";
                if (root.TryGetProperty("type", out var tEl) || root.TryGetProperty("Type", out tEl))
                {
                    type = tEl.GetString() ?? "";
                }

                // 2. Specialized Stream Processing — pass raw JSON string to avoid JsonDocument disposal issues
                if (streamName.Equals("marquee", StringComparison.OrdinalIgnoreCase))
                {
                    if (type.Equals("marquee.snapshot", StringComparison.OrdinalIgnoreCase) ||
                        type.Equals("marquee.snapshot.updated", StringComparison.OrdinalIgnoreCase))
                    {
                        HandleMarqueeSnapshot(json); // void — just drops in latest-wins slot
                        return;
                    }
                }
                else if (streamName.Equals("topper", StringComparison.OrdinalIgnoreCase))
                {
                    if (type.Equals("topper.snapshot", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = HandleTopperSnapshot(json);
                        return;
                    }
                }
                else if (streamName.Equals("instruction-card", StringComparison.OrdinalIgnoreCase))
                {
                    if (type.Equals("instruction-card.snapshot", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = HandleInstructionCardSnapshot(json);
                        return;
                    }
                }
                else if (streamName.Equals("hiscore", StringComparison.OrdinalIgnoreCase))
                {
                    _ = HandleHiscoreEvent(type, json);
                    return;
                }

                // 3. Parse into generic ApiExposeEvent
                var ev = ApiExposeEvent.FromElement(root, streamName);
                if (ev == null) return;

                // 4. Direct Target Routing
                if (!string.IsNullOrEmpty(ev.Target))
                {
                    var target = ev.Target.ToLowerInvariant();
                    if (new[] { "marquee", "topper", "iccard", "dmd", "lcd" }.Contains(target))
                    {
                        _ = HandleDirectTargetRouting(target, ev);
                        return;
                    }
                }

                // 5. Default Routing Rules by Stream
                if (streamName.Equals("frontend", StringComparison.OrdinalIgnoreCase))
                {
                    _ = HandleFrontendEvent(ev);
                }
                else if (streamName.Equals("ingame", StringComparison.OrdinalIgnoreCase))
                {
                    _ = HandleIngameEvent(ev);
                }
                else if (streamName.Equals("panel", StringComparison.OrdinalIgnoreCase))
                {
                    _ = HandlePanelEvent(ev);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[WebSocket] Error processing message on stream {streamName}: {ex.Message}");
            }
        }

        private void ProcessArcadeOutput(JsonElement root)
        {
            if (!root.TryGetProperty("payload", out var payload))
            {
                root.TryGetProperty("Payload", out payload);
            }

            if (payload.ValueKind == JsonValueKind.Object)
            {
                JsonElement signals;
                if (payload.TryGetProperty("signals", out signals) || payload.TryGetProperty("Signals", out signals))
                {
                    if (signals.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var signal in signals.EnumerateArray())
                        {
                            string? key = null;
                            if (signal.TryGetProperty("key", out var kEl) || signal.TryGetProperty("Key", out kEl))
                            {
                                key = kEl.GetString();
                            }

                            int? val = null;
                            if (signal.TryGetProperty("value", out var vEl) || signal.TryGetProperty("Value", out vEl))
                            {
                                if (vEl.ValueKind == JsonValueKind.Number)
                                {
                                    val = vEl.GetInt32();
                                }
                                else if (vEl.ValueKind == JsonValueKind.String && int.TryParse(vEl.GetString(), out int parsedVal))
                                {
                                    val = parsedVal;
                                }
                            }

                            if (!string.IsNullOrEmpty(key) && val.HasValue)
                            {
                                _mpv.SetLampState(key, val.Value);
                            }
                        }
                    }
                }
            }
        }

        // Receives new snapshot — just drops it in the latest-wins slot (non-blocking)
        private void HandleMarqueeSnapshot(string json)
        {
            var request = new MarqueeRequest(
                Interlocked.Increment(ref _latestMarqueeRequestId),
                json,
                Stopwatch.GetTimestamp());
            _marqueeChannel.Writer.TryWrite(request); // DropOldest: silently replaces any pending
        }

        // Called by the single worker loop — processes the latest snapshot
        private async Task ProcessMarqueeSnapshotAsync(MarqueeRequest request, CancellationToken ct)
        {
            _logger.LogInformation(
                "[Timing] Marquee request={RequestId} processing queueMs={QueueMs:F1}",
                request.Id,
                Stopwatch.GetElapsedTime(request.ReceivedTimestamp).TotalMilliseconds);

            // /ws/marquee: extract only marquee and dmd content
            // Topper → /ws/topper, IC card → /ws/instruction-card (dedicated streams)
            string? marqueePath = null;
            string? dmdPath     = null;
            var json = request.Json;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                JsonElement payload = root;
                if (root.TryGetProperty("Payload", out var p) || root.TryGetProperty("payload", out p))
                    payload = p;

                if (payload.TryGetProperty("Media", out var media) || payload.TryGetProperty("media", out media))
                {
                    // Marquee for LCD — quality filter: never display DMD-sized images
                    // Priority: Marquee → GeneratedMarquee → Logo (wheel) → nothing
                    if (media.TryGetProperty("Marquee", out var mq) && mq.ValueKind == JsonValueKind.Object)
                        marqueePath = FilterMarqueePath(ReadStringProperty(mq, "Path"));
                    if (string.IsNullOrEmpty(marqueePath) && media.TryGetProperty("GeneratedMarquee", out mq) && mq.ValueKind == JsonValueKind.Object)
                        marqueePath = FilterMarqueePath(ReadStringProperty(mq, "Path"));
                    // Fallback: system Logo (wheel) provided by APIExpose — acceptable quality for LCD
                    if (string.IsNullOrEmpty(marqueePath) && media.TryGetProperty("Logo", out mq) && mq.ValueKind == JsonValueKind.Object)
                        marqueePath = FilterMarqueePath(ReadStringProperty(mq, "Path"));

                    // DMD content
                    if (media.TryGetProperty("Dmd", out var dmd) && dmd.ValueKind == JsonValueKind.Object)
                    {
                        if (dmd.TryGetProperty("Animations", out var anims) && anims.ValueKind == JsonValueKind.Array)
                            foreach (var anim in anims.EnumerateArray())
                            {
                                var ap = anim.ValueKind == JsonValueKind.String ? anim.GetString() : ReadStringProperty(anim, "Path");
                                if (!string.IsNullOrEmpty(ap)) { dmdPath = ap; break; }
                            }
                        if (string.IsNullOrEmpty(dmdPath) && dmd.TryGetProperty("Still", out var still) && still.ValueKind == JsonValueKind.Object)
                            dmdPath = ReadStringProperty(still, "Path");
                        if (string.IsNullOrEmpty(dmdPath) && dmd.TryGetProperty("Generated", out var gen) && gen.ValueKind == JsonValueKind.Object)
                            dmdPath = ReadStringProperty(gen, "Path");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[WebSocket] Error parsing marquee snapshot: {ex.Message}");
                return;
            }

            if (ct.IsCancellationRequested || IsStaleMarqueeRequest(request.Id)) return;

            // /ws/marquee drives the marquee screen and dmd screen only.
            // Topper, IC card and fanart have their own dedicated WS streams.
            if (!string.IsNullOrEmpty(marqueePath))
            {
                var abs = ResolveAbsolutePath(marqueePath);
                if (File.Exists(abs))
                    foreach (var t in TargetsFor("marquee"))
                    {
                        if (ct.IsCancellationRequested || IsStaleMarqueeRequest(request.Id)) return;
                        _logger.LogInformation($"[WebSocket] Routing Marquee → '{t}': {abs}");
                        await _mpv.DisplayImageToTarget(abs, t);
                        _logger.LogInformation(
                            "[Timing] LCD request={RequestId} target={Target} totalMs={TotalMs:F1}",
                            request.Id,
                            t,
                            Stopwatch.GetElapsedTime(request.ReceivedTimestamp).TotalMilliseconds);
                    }
            }

            if (!string.IsNullOrEmpty(dmdPath) && !ct.IsCancellationRequested && !IsStaleMarqueeRequest(request.Id))
            {
                var abs = ResolveAbsolutePath(dmdPath);
                if (File.Exists(abs))
                {
                    // Push to physical DMD via dedicated worker (non-blocking — no delay on LCD)
                    if (_config.DmdEnabled)
                    {
                        var dmdRequest = new DmdRequest(Interlocked.Increment(ref _latestDmdRequestId), abs);
                        _dmdChannel.Writer.TryWrite(dmdRequest);
                        _logger.LogInformation(
                            "[Timing] DMD request={DmdRequestId} queued fromMarqueeRequest={MarqueeRequestId} totalMs={TotalMs:F1}",
                            dmdRequest.Id,
                            request.Id,
                            Stopwatch.GetElapsedTime(request.ReceivedTimestamp).TotalMilliseconds);
                    }

                    // Display on any configured DMD screen target
                    foreach (var t in TargetsFor("dmd"))
                    {
                        if (ct.IsCancellationRequested || IsStaleMarqueeRequest(request.Id)) return;
                        await _mpv.DisplayImageToTarget(abs, t);
                    }
                }
            }
        }

        private bool IsStaleMarqueeRequest(long requestId)
            => requestId != Interlocked.Read(ref _latestMarqueeRequestId);

        private async Task HandleTopperSnapshot(string json)
        {
            _logger.LogInformation("[WebSocket] Processing topper snapshot...");
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                JsonElement payload = root;
                if (root.TryGetProperty("Payload", out var p) || root.TryGetProperty("payload", out p)) payload = p;
                if (payload.TryGetProperty("Media", out var media) || payload.TryGetProperty("media", out media))
                {
                    if (media.TryGetProperty("Topper", out var top) && top.ValueKind == JsonValueKind.Object)
                    {
                        var path = ReadStringProperty(top, "Path");
                        if (!string.IsNullOrEmpty(path))
                        {
                            var abs = ResolveAbsolutePath(path);
                            if (File.Exists(abs))
                                foreach (var t in TargetsFor("topper"))
                                    await _mpv.DisplayImageToTarget(abs, t);
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.LogError($"[WebSocket] HandleTopperSnapshot error: {ex.Message}"); }
        }

        private async Task HandleInstructionCardSnapshot(string json)
        {
            _logger.LogInformation("[WebSocket] Processing instruction-card snapshot...");
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                JsonElement payload = root;
                if (root.TryGetProperty("Payload", out var p) || root.TryGetProperty("payload", out p)) payload = p;
                if ((payload.TryGetProperty("Cards", out var cards) || payload.TryGetProperty("cards", out cards)) && cards.ValueKind == JsonValueKind.Array)
                {
                    foreach (var card in cards.EnumerateArray())
                    {
                        var cardPath = card.ValueKind == JsonValueKind.String ? card.GetString() : ReadStringProperty(card, "Path");
                        if (!string.IsNullOrEmpty(cardPath))
                        {
                            var abs = ResolveAbsolutePath(cardPath);
                            if (File.Exists(abs))
                            {
                                foreach (var t in TargetsFor("iccard"))
                                {
                                    _logger.LogInformation($"[WebSocket] Routing IC Card → '{t}': {abs}");
                                    await _mpv.DisplayImageToTarget(abs, t);
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { _logger.LogError($"[WebSocket] HandleInstructionCardSnapshot error: {ex.Message}"); }
        }

        private async Task HandleHiscoreEvent(string type, string json)
        {
            string? scoreText = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                JsonElement payload = root;
                if (root.TryGetProperty("Payload", out var p) || root.TryGetProperty("payload", out p)) payload = p;

                if (type.Equals("hiscore.score.changed", StringComparison.OrdinalIgnoreCase))
                {
                    var score = ReadStringProperty(payload, "Score") ?? ReadStringProperty(payload, "score");
                    if (!string.IsNullOrEmpty(score)) scoreText = $"SCORE: {score}";
                }
                else if (type.Equals("hiscore.updated", StringComparison.OrdinalIgnoreCase))
                {
                    var hi = ReadStringProperty(payload, "HighScore") ?? ReadStringProperty(payload, "highScore");
                    if (!string.IsNullOrEmpty(hi)) scoreText = $"HI-SCORE: {hi}";
                }
                else if (type.Equals("hiscore.snapshot", StringComparison.OrdinalIgnoreCase))
                {
                    var score = ReadStringProperty(payload, "Score") ?? ReadStringProperty(payload, "score");
                    var hi = ReadStringProperty(payload, "HighScore") ?? ReadStringProperty(payload, "highScore");
                    if (!string.IsNullOrEmpty(score) && !string.IsNullOrEmpty(hi)) scoreText = $"SCORE: {score}  HI: {hi}";
                    else if (!string.IsNullOrEmpty(hi)) scoreText = $"HI-SCORE: {hi}";
                    else if (!string.IsNullOrEmpty(score)) scoreText = $"SCORE: {score}";
                }
            }
            catch (Exception ex) { _logger.LogError($"[WebSocket] HandleHiscoreEvent error: {ex.Message}"); }

            if (!string.IsNullOrEmpty(scoreText))
            {
                _logger.LogInformation($"[WebSocket] Routing Highscore overlay: {scoreText}");
                var cmd = $"{{\"command\": [\"show-text\", \"{scoreText.Replace("\"", "\\\"")}\", \"5000\"]}}";
                await _mpv.SendCommandToTargetAsync(cmd, "dmd");
                await _mpv.SendCommandToTargetAsync(cmd, "lcd");
                await _mpv.SendCommandToTargetAsync(cmd, "topper");
            }
        }

        private string ResolveAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (Path.IsPathRooted(path)) return path;

            // Try APIExpose sibling directory first (media paths are relative to its root)
            if (!string.IsNullOrEmpty(_apiExposeBasePath))
            {
                var fromApiExpose = Path.GetFullPath(Path.Combine(_apiExposeBasePath, path));
                if (File.Exists(fromApiExpose)) return fromApiExpose;
            }

            // Fallback: relative to RetroBat root
            return Path.GetFullPath(Path.Combine(_config.RetroBatPath, path));
        }

        private static string? ReadStringProperty(JsonElement element, string property)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(property, out var val)) return null;

            return val.ValueKind switch
            {
                JsonValueKind.String => val.GetString(),
                JsonValueKind.Number => val.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        private async Task HandleDirectTargetRouting(string target, ApiExposeEvent ev)
        {
            _logger.LogInformation($"[WebSocket] Direct routing to '{target}': Value='{ev.Value}', Text='{ev.Text}'");
            if (!string.IsNullOrEmpty(ev.Value))
            {
                string mediaPath = ev.Value;
                bool isValidMedia = false;
                
                if (mediaPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                    mediaPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    isValidMedia = true;
                }
                else
                {
                    if (!Path.IsPathRooted(mediaPath))
                    {
                        mediaPath = Path.Combine(_config.RetroBatPath, mediaPath);
                    }
                    if (File.Exists(mediaPath))
                    {
                        isValidMedia = true;
                    }
                }

                if (isValidMedia)
                {
                    await _mpv.DisplayImageToTarget(mediaPath, target);
                    return;
                }
            }

            if (!string.IsNullOrEmpty(ev.Text))
            {
                var cmd = $"{{\"command\": [\"show-text\", \"{ev.Text.Replace("\"", "\\\"")}\", \"3000\"]}}";
                await _mpv.SendCommandToTargetAsync(cmd, target);
            }
        }

        private Task HandleFrontendEvent(ApiExposeEvent ev)
        {
            // APIExpose owns media resolution and generation. MarqueeManager only consumes
            // /ws/marquee, /ws/topper and /ws/instruction-card snapshots so fast navigation
            // cannot queue the legacy composer/scraper workflow.
            _logger.LogDebug($"[WebSocket] Frontend event consumed by APIExpose snapshots only: {ev.Type}");
            return Task.CompletedTask;
        }

        // Extract system + gamePath + gameName from APIExpose payload.context.ui.selected
        private static (string system, string gamePath, string? gameName) ExtractGameStartInfo(JsonElement payload)
        {
            string system = "";
            string gamePath = "";
            string? gameName = null;

            if (payload.ValueKind != JsonValueKind.Object) return (system, gamePath, gameName);

            // Try payload.context.ui.selected
            if (payload.TryGetProperty("context", out var ctx) &&
                ctx.TryGetProperty("ui", out var ui))
            {
                // System
                if (ui.TryGetProperty("selectedSystem", out var sys))
                {
                    system = ReadStringProp(sys, "name") ?? ReadStringProp(sys, "id") ?? "";
                }

                // Game path + name from selected
                if (ui.TryGetProperty("selected", out var sel))
                {
                    gamePath = ReadStringProp(sel, "gamePath") ?? ReadStringProp(sel, "romPath") ?? "";
                    gameName = ReadStringProp(sel, "gameName") ?? ReadStringProp(sel, "name");

                    // Fallback system from selected
                    if (string.IsNullOrEmpty(system))
                        system = ReadStringProp(sel, "systemId") ?? "";

                    // Try launch sub-object for romPath
                    if (string.IsNullOrEmpty(gamePath) && sel.TryGetProperty("launch", out var launch))
                    {
                        gamePath = ReadStringProp(launch, "romPath") ?? ReadStringProp(launch, "gamePath") ?? "";
                        if (string.IsNullOrEmpty(system))
                            system = ReadStringProp(launch, "system") ?? "";
                    }
                }
            }

            // Fallback: payload.selection
            if (string.IsNullOrEmpty(gamePath) && payload.TryGetProperty("selection", out var sel2))
            {
                gamePath = ReadStringProp(sel2, "gamePath") ?? ReadStringProp(sel2, "romPath") ?? "";
                gameName ??= ReadStringProp(sel2, "gameName");
                if (string.IsNullOrEmpty(system))
                    system = ReadStringProp(sel2, "system") ?? ReadStringProp(sel2, "frontendSystem") ?? "";
            }

            return (system, gamePath, gameName);
        }

        // Returns null if the path looks like a DMD-quality image (too small for LCD display)
        private static string? FilterMarqueePath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var fileName = Path.GetFileName(path);
            // Reject any file whose name contains "dmd" (generated-dmd.png, generated-system-dmd.png, etc.)
            if (fileName.Contains("dmd", StringComparison.OrdinalIgnoreCase)) return null;
            return path;
        }

        private static string? TryGetSelectionField(JsonElement payload, string field)
        {
            if (payload.ValueKind != JsonValueKind.Object) return null;
            if (!payload.TryGetProperty("selection", out var sel) || sel.ValueKind != JsonValueKind.Object) return null;
            if (!sel.TryGetProperty(field, out var val)) return null;
            return val.ValueKind == JsonValueKind.String ? val.GetString() : null;
        }

        private static string? TryGetNestedString(JsonElement root, params string[] keys)
        {
            var current = root;
            foreach (var key in keys)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out current))
                    return null;
            }
            return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
        }

        private static string? ReadStringProp(JsonElement el, string key)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            if (!el.TryGetProperty(key, out var val)) return null;
            return val.ValueKind == JsonValueKind.String ? val.GetString() : null;
        }

        private Task HandleMameSessionStarted(string json)
        {
            string machineName = "";
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                JsonElement payload = root;
                if (root.TryGetProperty("Payload", out var p) || root.TryGetProperty("payload", out p)) payload = p;
                machineName = ReadStringProperty(payload, "MachineName") ?? ReadStringProperty(payload, "machineName") ?? "";
            }
            catch { }
            if (string.IsNullOrEmpty(machineName)) return Task.CompletedTask;

            _logger.LogInformation($"[WebSocket] MAME session started: {machineName}");
            return Task.CompletedTask;
        }

        private async Task HandleIngameEvent(ApiExposeEvent ev)
        {
            var type = ev.Type.ToLowerInvariant();
            var action = ev.Action?.ToLowerInvariant() ?? "";

            // 1. Score Events
            if (type.Contains("score") || action.Contains("score"))
            {
                if (!string.IsNullOrEmpty(ev.Value))
                {
                    var cmd = $"{{\"command\": [\"show-text\", \"SCORE: {ev.Value}\", \"5000\"]}}";
                    await _mpv.SendCommandToTargetAsync(cmd, "dmd");
                    await _mpv.SendCommandToTargetAsync(cmd, "lcd");
                }
            }
            // 2. IC Card Events
            else if (type.Contains("iccard") || action.Contains("iccard") || action.Contains("card"))
            {
                if (!string.IsNullOrEmpty(ev.Value))
                {
                    string mediaPath = ev.Value;
                    if (!Path.IsPathRooted(mediaPath))
                    {
                        mediaPath = Path.Combine(_config.RetroBatPath, mediaPath);
                    }
                    if (File.Exists(mediaPath))
                    {
                        await _mpv.DisplayImageToTarget(mediaPath, "iccard");
                        return;
                    }
                }

                var text = ev.Text ?? "Card Swiped";
                var cmd = $"{{\"command\": [\"show-text\", \"{text.Replace("\"", "\\\"")}\", \"3000\"]}}";
                await _mpv.SendCommandToTargetAsync(cmd, "iccard");
            }
            // 3. Achievement / RetroAchievements Events
            else if (type.Contains("achievement") || type.Contains("cheevo") || action.Contains("achievement"))
            {
                if (!string.IsNullOrEmpty(ev.Text))
                {
                    var cmd = $"{{\"command\": [\"show-text\", \"🏆 {ev.Text.Replace("\"", "\\\"")}\", \"5000\"]}}";
                    await _mpv.SendCommandToTargetAsync(cmd, "topper");
                    await _mpv.SendCommandToTargetAsync(cmd, "marquee");
                }
            }
        }

        private async Task HandlePanelEvent(ApiExposeEvent ev)
        {
            if (!string.IsNullOrEmpty(ev.Value))
            {
                string mediaPath = ev.Value;
                if (!Path.IsPathRooted(mediaPath))
                {
                    mediaPath = Path.Combine(_config.RetroBatPath, mediaPath);
                }
                if (File.Exists(mediaPath))
                {
                    await _mpv.DisplayImageToTarget(mediaPath, "lcd");
                }
            }
        }

        private sealed record MarqueeRequest(long Id, string Json, long ReceivedTimestamp);
        private sealed record DmdRequest(long Id, string Path);
    }
}
