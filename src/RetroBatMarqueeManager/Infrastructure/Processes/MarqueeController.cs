using RetroBatMarqueeManager.Application.Services;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Rendering.Skia;
using RetroBatMarqueeManager.Infrastructure.UI;

namespace RetroBatMarqueeManager.Infrastructure.Processes;

public sealed class MarqueeController : IDisposable
{
    private readonly IConfigService _config;
    private readonly IDmdService _dmd;
    private readonly ILogger<MarqueeController> _logger;
    private readonly Dictionary<string, List<MarqueeWindow>> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Core.Surfaces.SurfaceDefinition> _surfaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _uiThread;

    /// <summary>Tap on a surface that has something to answer — an instruction card
    /// surface, or any surface carrying touch zones — as fractions (0..1), with the
    /// surface itself and the state it is displaying. Consumed by InstructionCardService.
    ///
    /// The surface travels with the tap because the zones live IN the composition now: a
    /// finger on a touchscreen can drive a card shown on the topper, so the surface that
    /// was touched is not the one that changes.</summary>
    public event Action<Core.Surfaces.SurfaceDefinition, string, double, double>? SurfaceTapped;

    public MarqueeController(IConfigService config, IDmdService dmd, ILogger<MarqueeController> logger)
    {
        _config = config;
        _dmd = dmd;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_uiThread != null) return _ready.Task.WaitAsync(cancellationToken);
        _uiThread = new Thread(UiThreadEntry) { IsBackground = true, Name = "MarqueeManager.WPF" };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        return _ready.Task.WaitAsync(cancellationToken);
    }

    private void UiThreadEntry()
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (var i = 0; i < screens.Length; i++)
                _logger.LogInformation("Detected screen {Index}: {DeviceName}, Primary={Primary}, Bounds={Bounds}, WorkingArea={WorkingArea}",
                    i, screens[i].DeviceName, screens[i].Primary, screens[i].Bounds, screens[i].WorkingArea);

            var lightingOptions = _config.LightingEnabled
                ? new LightingSurfaceOptions(_config.LightingTestPattern, _config.LightingFpsLimit, _config.LightingShowFps, _config.LightingRenderScale, _config.LightingFillHeightMaxCrop, _config.LightingSoundEnabled, _config.LightingSoundVolume, _config.LightingGlassReflection, _config.LightingTubeVisualOpacity, _config.LightingTubeThickness, _config.LightingTubeBlur, _config.LightingTubeEndFade, _config.LightingTubeColor, _config.LightingLatestWinsGeneration, _config.LightingMapCache, _config.LightingPresentPipeline, _config.LightingGpuRaster)
                : null;

            // The events layer is NOT gated by [Lighting] Enabled — that switch is
            // about lighting an image, and the two engines are independent now. It
            // only borrows the rendering knobs (cadence, scale, present, GPU); it has
            // no scene, no tubes, no sound.
            var effectsOptions = new LightingSurfaceOptions(
                TestPattern: false,
                FpsLimit: _config.LightingFpsLimit,
                ShowFps: false,
                // Half resolution, deliberately. Sprite GIFs are pre-downscaled to 96 px
                // tall at load (320 px for full_* backdrops) and drawn at ~30 % of the
                // surface height, so a full-resolution overlay upscales a small bitmap
                // and buys nothing visible — while costing 4x the raster, the 5.5 MB/frame
                // WritePixels on the UI thread and the WPF blend of a second full-screen
                // layer. That second present is what made everything crawl as soon as a
                // sprite appeared.
                RenderScale: Math.Min(_config.LightingRenderScale, 0.5),
                FillHeightMaxCrop: 0,
                SoundEnabled: false,
                SoundVolume: 0,
                GlassReflection: 0,
                TubeVisualOpacity: 0,
                PresentPipeline: _config.LightingPresentPipeline,
                // CPU raster on purpose: the events layer has no shader, only sprite
                // blits the sprite budget is already tuned for. Giving it the GPU
                // backend would keep a SECOND WGL/GRContext alive for the lifetime of
                // the window, competing with the lighting engine's raster for nothing.
                GpuRaster: false);

            // screens the user excluded from MarqueeManager (Mon setup → "use this
            // screen" unchecked): no window is created on them, their surfaces stay
            // in the document but are suspended.
            var unmanaged = _config.GetUnmanagedScreenIndices();
            if (unmanaged.Count > 0)
                _logger.LogInformation("Screens excluded from MarqueeManager: {Screens}", string.Join(", ", unmanaged));

            // dynamic surfaces (state\surfaces.json) or their legacy [Screens] equivalent
            foreach (var surface in _config.GetSurfaces())
            {
                _surfaces[surface.Id] = surface;
                if (surface.Category.Equals("dmd-physical", StringComparison.OrdinalIgnoreCase))
                    continue; // window-less sink: content routes to IDmdService

                foreach (var screen in surface.Screens)
                {
                    // an out-of-range index is IGNORED (never silently redirected to
                    // screen 0), and an excluded screen never gets a window
                    if (screen < 0 || screen >= screens.Length)
                    {
                        _logger.LogWarning("Surface {Id}: screen index {Screen} is out of range (0..{Max}); ignored, no window",
                            surface.Id, screen, screens.Length - 1);
                        continue;
                    }
                    if (unmanaged.Contains(screen))
                    {
                        _logger.LogInformation("Surface {Id}: screen {Screen} is excluded from MarqueeManager; suspended, no window",
                            surface.Id, screen);
                        continue;
                    }

                    var lighting = surface.HasVisibleComponent("lighting.engine") ? lightingOptions : null;
                    // the events layer has its own host: it must exist even where no
                    // lighting scene does (fanart surface, video marquee, iccard…)
                    var effects = surface.HasVisibleComponent("effects.engine") ? effectsOptions : null;
                    var window = new MarqueeWindow(screen, _logger,
                        lighting,
                        surface.Bounds,
                        lighting != null && _config.LightingDmdMirror && _config.DmdEnabled ? _dmd : null,
                        _config.DmdWidth, _config.DmdHeight,
                        surface,
                        effects);
                    if (!_windows.TryGetValue(surface.Id, out var list)) _windows[surface.Id] = list = new();
                    list.Add(window);
                    // a surface answers to the finger when it IS an instruction card
                    // surface (historical touch profile) or when the user drew zones on it
                    if (surface.Category.Equals("iccard", StringComparison.OrdinalIgnoreCase)
                        || surface.HasVisibleComponent("iccard.touch"))
                    {
                        var tapped = surface;
                        var source = window;
                        window.SurfaceTapped += (fx, fy) =>
                            SurfaceTapped?.Invoke(tapped, source.ActiveScene, fx, fy);
                        // and it must never come forward: a finger on the marquee sent the
                        // running game behind its launcher
                        window.NeverActivate = true;
                    }
                    window.Show();
                    // apply the INITIAL display state: an ingame-only surface must
                    // not sit over ES from startup until the first scene event
                    window.SetDisplayScene("navigation");
                    // …and whatever the panel stream said before this window existed
                    ReplayPanelState(window);
                    _logger.LogInformation("Surface {Id} ({Category}) opened on screen {Screen}, bounds={Bounds}",
                        surface.Id, surface.Category, screen, surface.Bounds);
                }
            }
            _ready.TrySetResult();
            if (_windows.Count > 0) System.Windows.Threading.Dispatcher.Run();
        }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
            _logger.LogError(ex, "WPF surface thread failed");
        }
    }

    public bool HasTarget(string target) => GetWindows(target).Count > 0;

    public Task DisplayMediaAsync(string path, string target, CancellationToken cancellationToken = default,
        Application.Lighting.LightingSceneMeta? lightingMeta = null, bool resolved = false)
    {
        if (cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(path))
        {
            _logger.LogDebug("Ignoring empty media display request for target {Target}", target);
            return Task.CompletedTask;
        }
        if (!File.Exists(path))
        {
            _logger.LogWarning("Ignoring missing media for target {Target}: {Path}", target, path);
            return Task.CompletedTask;
        }
        var windows = GetWindows(target);
        if (windows.Count == 0)
        {
            _logger.LogDebug("Ignoring media for disabled target {Target}: {Path}", target, path);
            return Task.CompletedTask;
        }
        var video = new[] { ".mp4", ".webm", ".avi", ".mkv", ".mov" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        // the chain resolver already decided? never second-guess it — otherwise
        // the historical preferences stay the safety net
        if (!resolved && !video && SurfaceOf(target)?.Category.Equals("marquee", StringComparison.OrdinalIgnoreCase) == true)
            path = PreferUserComposition(path, lightingMeta) ?? PreferRealMarquee(path);
        _logger.LogInformation("Displaying {Kind} on target {Target} ({WindowCount} window(s)): {Path}", video ? "video" : "image", target, windows.Count, path);
        foreach (var window in windows)
        {
            if (video) window.DisplayVideo(path); else window.DisplayImage(path, lightingMeta);
        }
        return Task.CompletedTask;
    }

    public void SetInformation(string owner, string title, string detail, string? badgePath, bool persistent, int durationMs, string target = "marquee")
    {
        foreach (var window in InformationWindows(owner, target)) window.SetInformationOverlay(owner, title, detail, badgePath, persistent, durationMs);
    }

    public void ClearInformation(string owner, string target = "marquee")
    {
        foreach (var window in InformationWindows(owner, target)) window.RemoveInformationOverlay(owner);
    }

    private string? _lastHiscoreGame;
    private HashSet<string>? _lastHiscoreKeys;
    private static string HiscoreKey(Core.HiscoreRow row) => (row.Name + "|" + row.Score).Trim();

    /// <summary>Renders the full local hiscore Top-N on every surface carrying the
    /// overlay.hiscore component. Rows genuinely new since the last update FOR THE SAME
    /// game are highlighted/animated (a fresh game switch highlights nothing).</summary>
    public void SetHiscoreLeaderboard(string game, string system, IReadOnlyList<Core.HiscoreRow> rows, string source = "local", Core.HiscoreMyRank? myRank = null)
    {
        var highlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // New-score highlight is a LOCAL notion (a run you just set); the world board
        // isn't diffed. Each window only renders the feed matching its own source option.
        if (source.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(game, _lastHiscoreGame, StringComparison.OrdinalIgnoreCase) && _lastHiscoreKeys != null)
            {
                foreach (var row in rows)
                {
                    var key = HiscoreKey(row);
                    if (!_lastHiscoreKeys.Contains(key)) highlight.Add(key);
                }
            }
            _lastHiscoreGame = game;
            _lastHiscoreKeys = new HashSet<string>(rows.Select(HiscoreKey), StringComparer.OrdinalIgnoreCase);
        }

        foreach (var window in WindowsWithComponent("overlay.hiscore"))
            window.SetHiscoreLeaderboard("hiscore", game, system, rows, highlight, source, myRank);
    }

    /// <summary>Explicit non-marquee target = historical direct routing (panel/lcd
    /// messages name their surface); the default goes to the owner's component.</summary>
    private IEnumerable<MarqueeWindow> InformationWindows(string owner, string target)
        => target.Equals("marquee", StringComparison.OrdinalIgnoreCase)
            ? WindowsWithComponent(ComponentForOwner(owner))
            : GetWindows(target);

    public void ClearAllInformation()
    {
        foreach (var window in AllWindows()) window.ClearAllOverlays();
    }

    /// <summary>Selection media kinds + meta → every dynamic component of every
    /// surface (each window keeps only what its components consume).</summary>
    public void UpdateComponentMedia(IReadOnlyDictionary<string, string?> kinds, IReadOnlyDictionary<string, string> meta)
    {
        foreach (var window in AllWindows())
        {
            window.UpdateComponentMedia(kinds);
            window.UpdateComponentMeta(meta);
        }
    }

    // The panel description is RETAINED on the stream: it arrives once, on connection,
    // and the surfaces open two seconds later — so a window born afterwards would never
    // learn what the cabinet looks like and would keep drawing the fallback panel. Kept
    // here and replayed to each window as it opens.
    private readonly object _panelLock = new();
    private Core.Surfaces.PanelBoardConfig? _lastPanelConfig;
    private readonly Dictionary<int, IReadOnlyDictionary<int, Core.Surfaces.PanelBoardButton>> _lastPanelButtons = new();
    private Core.Surfaces.PanelBoardArt? _lastPanelArtTop;
    private Core.Surfaces.PanelBoardArt? _lastPanelArtFront;

    /// <summary>Cabinet panel description (retained on /ws/panel) → every panel
    /// component, whichever surface carries it.</summary>
    public void UpdatePanelConfig(Core.Surfaces.PanelBoardConfig config)
    {
        lock (_panelLock) _lastPanelConfig = config;
        foreach (var window in AllWindows()) window.UpdatePanelConfig(config);
    }

    /// <summary>What the selected game does with each place of one player's panel.</summary>
    public void UpdatePanelButtons(int player, IReadOnlyDictionary<int, Core.Surfaces.PanelBoardButton> buttons)
    {
        lock (_panelLock) _lastPanelButtons[player] = buttons;
        foreach (var window in AllWindows()) window.UpdatePanelButtons(player, buttons);
    }

    /// <summary>Hands a freshly opened window what the stream already said. Without it
    /// the panel would wait for the next reconfiguration — which may never come.</summary>
    private void ReplayPanelState(MarqueeWindow window)
    {
        Core.Surfaces.PanelBoardConfig? config;
        KeyValuePair<int, IReadOnlyDictionary<int, Core.Surfaces.PanelBoardButton>>[] buttons;
        Core.Surfaces.PanelBoardArt? top;
        Core.Surfaces.PanelBoardArt? front;
        lock (_panelLock)
        {
            config = _lastPanelConfig;
            buttons = _lastPanelButtons.ToArray();
            top = _lastPanelArtTop;
            front = _lastPanelArtFront;
        }

        if (config != null) window.UpdatePanelConfig(config);
        foreach (var (player, slots) in buttons) window.UpdatePanelButtons(player, slots);
        if (top != null || front != null) window.UpdatePanelArt(top, front);
    }

    /// <summary>The drawn panel (both views) → the components that asked for artwork.</summary>
    public void UpdatePanelArt(Core.Surfaces.PanelBoardArt? top, Core.Surfaces.PanelBoardArt? front)
    {
        lock (_panelLock) { _lastPanelArtTop = top; _lastPanelArtFront = front; }
        foreach (var window in AllWindows()) window.UpdatePanelArt(top, front);
    }

    /// <summary>A physical press/release resolved to a slot.</summary>
    public void SetPanelInput(int player, int? slot, string? system, bool pressed)
    {
        foreach (var window in AllWindows()) window.SetPanelInput(player, slot, system, pressed);
    }

    /// <summary>Every panel light out.</summary>
    public void ReleasePanelInputs()
    {
        foreach (var window in AllWindows()) window.ReleasePanelInputs();
    }

    /// <summary>Feeds one component type directly (instruction card split…).</summary>
    public void SetComponentSource(string type, string? path)
    {
        foreach (var window in AllWindows()) window.SetComponentSource(type, path);
    }

    /// <summary>Feeds the viewers of ONE channel (instruction cards). The historical
    /// components answer on the main channel, so a composition made before channels
    /// existed keeps working.</summary>
    public void SetCardSource(string channel, string? path, double[]? highlight = null)
    {
        foreach (var window in AllWindows()) window.SetCardSource(channel, path, highlight);
    }

    /// <summary>Every declared component of this type, across all surfaces — with its
    /// options, which is what tells a service how each one is set up.</summary>
    public IReadOnlyList<Core.Surfaces.ComponentDefinition> ComponentsOfType(string type)
        => _surfaces.Values
            .SelectMany(surface => surface.Components)
            .Where(component => component.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>True when at least one surface carries this component.</summary>
    public bool HasComponent(string type)
        => _surfaces.Values.Any(surface => surface.HasComponent(type));

    /// <summary>True when at least one overlay.hiscore surface uses this data source
    /// (local / nelfeplay). Lets the listener fetch the world ranking only when composed.</summary>
    public bool HasHiscoreSource(string source)
        => _surfaces.Values.Any(surface =>
        {
            var options = surface.Component("overlay.hiscore")?.Options;
            var s = options != null && options.TryGetValue("source", out var v) && v.Length > 0 ? v : "local";
            // "dual" consumes both feeds, so it counts as having either source.
            return s.Equals(source, StringComparison.OrdinalIgnoreCase)
                || s.Equals("dual", StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>First surface's option value for a component type (e.g. the pinned
    /// card number of iccard.static).</summary>
    public string? ComponentOption(string type, string option)
        => _surfaces.Values
            .Select(surface => surface.Component(type))
            .FirstOrDefault(component => component != null)?
            .Option(option);

    public void UpdateSpeedrunDisplay(string title, string detail, string? badgePath,
        double elapsedSeconds = 0, double? recordSeconds = null, double? userRecordSeconds = null, string? currentRank = null,
        int? leaderboardId = null, string? leaderboardTitle = null)
    {
        foreach (var window in WindowsWithComponent("overlay.ra.speedrun")) window.UpdateSpeedrunDisplay(title, detail, badgePath, elapsedSeconds, recordSeconds, userRecordSeconds, currentRank, leaderboardId, leaderboardTitle);
    }

    public void UpdateBadgeTray(IReadOnlyList<(int Id, string Path, bool Unlocked)> badges)
    {
        foreach (var window in WindowsWithComponent("overlay.ra.badges")) window.UpdateBadgeTray(badges);
    }

    public void ClearBadgeTray()
    {
        foreach (var window in WindowsWithComponent("overlay.ra.badges")) window.ClearBadgeTray();
    }

    public void ShowAchievementTakeover(string title, string detail, int points, string? badgePath, int durationMs)
    {
        foreach (var window in WindowsWithComponent("overlay.ra.info")) window.ShowAchievementTakeover(title, detail, points, badgePath, durationMs);
    }

    public void ShowLeaderboardResult(string time, string rank, string diff, bool isRecord, int durationMs, string? badgePath = null)
    {
        foreach (var window in WindowsWithComponent("overlay.ra.info")) window.ShowLeaderboardResult(time, rank, diff, isRecord, durationMs, badgePath);
    }

    public void LoadMameLayout(MameLayout layout, string ignoredDefaultView = "Marquee_Only")
    {
        foreach (var pair in _windows)
        {
            var view = pair.Key.ToLowerInvariant() switch
            {
                "dmd" => "DMD_Only",
                "topper" => "Topper_Only",
                "iccard" => "ICCard_Only",
                _ => "Marquee_Only"
            };
            foreach (var window in pair.Value) window.LoadMameLayout(layout, view);
        }
    }

    /// <summary>
    /// A marquee the user composed himself (MarqueeManagerSetup, "Mes jeux") wins
    /// over everything the stream offers — scraped scan and generated composite
    /// alike. Stored in media\marquees\&lt;system&gt;\&lt;rom&gt;.png next to the runtime.
    /// The game identity comes from the enriched stream meta; the media path
    /// layout (…\systems\&lt;system&gt;\games\&lt;rom&gt;\…) is the fallback.
    /// </summary>
    private string? PreferUserComposition(string path, Application.Lighting.LightingSceneMeta? meta)
    {
        var (system, rom) = (meta?.System, meta?.Rom);
        if (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(rom))
        {
            var match = System.Text.RegularExpressions.Regex.Match(path,
                @"[\\/]systems[\\/]([^\\/]+)[\\/]games[\\/]([^\\/]+)[\\/]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            system = match.Groups[1].Value;
            rom = match.Groups[2].Value;
        }

        // ES exposes MAME sets as "arcade"; accept both spellings
        var systems = system!.Equals("mame", StringComparison.OrdinalIgnoreCase)
            ? new[] { system!, "arcade" }
            : new[] { system! };
        foreach (var candidateSystem in systems)
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "media", "marquees",
                SafeFileName(candidateSystem), SafeFileName(rom!) + ".png");
            if (File.Exists(candidate))
            {
                _logger.LogInformation("User-composed marquee preferred: {Path}", candidate);
                return candidate;
            }
        }

        return null;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }

    /// <summary>
    /// Real scan beats upstream-generated composite: if the stream hands us a
    /// "generated-*" file while a real scraped marquee sits next to it on disk,
    /// use the real one — unless the user opted for generated (bad quality scan).
    /// Central chokepoint: every marquee display path goes through here.
    /// </summary>
    private string PreferRealMarquee(string path)
    {
        if (_config.LightingPreferGeneratedMarquee) return path;
        var fileName = Path.GetFileName(path);
        if (!fileName.StartsWith("generated-", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("system-marquee", StringComparison.OrdinalIgnoreCase)) return path;
        var directory = Path.GetDirectoryName(path);
        if (directory == null) return path;
        foreach (var candidate in new[] { "marquee.png", "marquee.jpg" })
        {
            var real = Path.Combine(directory, candidate);
            if (File.Exists(real))
            {
                _logger.LogInformation("Real scraped marquee preferred over generated: {Path}", real);
                return real;
            }
        }
        return path;
    }

    /// <summary>A purpose-built .lay DMD view is active: the lighting mirror yields.</summary>
    public void SetLayDmdActive(bool active)
    {
        foreach (var window in WindowsCarrying("lighting.engine")) window.SetLayDmdActive(active);
    }

    /// <summary>Game launch / return-to-frontend: the marquee lighting re-ignites.</summary>
    public void PowerCycleLighting()
    {
        foreach (var window in WindowsCarrying("lighting.engine")) window.PowerCycleLighting();
    }

    /// <summary>Ingame = clean session: lighting sounds muted, attract paused, and
    /// both engines drop the previous session's state.</summary>
    public void SetLightingIngame(bool ingame)
    {
        foreach (var window in WindowsCarrying("lighting.engine", "effects.engine")) window.SetLightingIngame(ingame);
    }

    /// <summary>Live MAME output → mapped scene lamp (ws/arcade).</summary>
    public void SetLightingOutput(string output, int value)
    {
        foreach (var window in WindowsCarrying("lighting.engine")) window.SetLightingOutput(output, value);
    }

    /// <summary>Kinds a standalone overlay cannot express: they act on tubes the
    /// lighting engine owns, so they are ALSO routed to it. Adding Flash and Strobe
    /// here restores the historical tube dip on those kinds (design note §4a); this
    /// is the same seam a future `lamp="…"` binding will use.</summary>
    private static readonly Application.Lighting.IngameEffectKind[] TubeKinds =
    {
        Application.Lighting.IngameEffectKind.Blackout,
        Application.Lighting.IngameEffectKind.PowerCycle
    };

    /// <summary>
    /// Semantic ingame event (ws/ingame via the effects library). The controller is
    /// the dispatcher: the animated part always goes to the events engine, and the
    /// tube-level part is handed to the lighting engine on top. The two renderers
    /// never talk to each other.
    /// </summary>
    public void TriggerIngameEffect(Application.Lighting.IngameEffectRule rule)
    {
        foreach (var window in WindowsWithComponent("effects.engine")) window.TriggerIngameEffect(rule);
        if (Array.IndexOf(TubeKinds, rule.Kind) < 0) return;
        foreach (var window in WindowsWithComponent("lighting.engine")) window.TriggerTubeEffect(rule);
    }

    /// <summary>User-dropped effect media (webm/gif) triggered by a signal:
    /// overlay on the surface or temporary fullscreen takeover.</summary>
    public void PlayMediaEffect(string path, bool fullscreen, int durationMs)
    {
        foreach (var window in WindowsWithComponent("effects.engine")) window.PlayMediaEffect(path, fullscreen, durationMs);
    }

    public void SetLampState(string lampName, int state)
    {
        foreach (var window in AllWindows()) window.SetLampState(lampName, state);
    }

    public void ClearLayout()
    {
        foreach (var window in AllWindows()) window.ClearLayout();
    }

    public Task StopAsync()
    {
        foreach (var window in AllWindows()) window.StopPlayback();
        return Task.CompletedTask;
    }

    private List<MarqueeWindow> GetWindows(string target)
        => _windows.TryGetValue(target, out var windows) ? windows : new List<MarqueeWindow>();

    private IEnumerable<MarqueeWindow> AllWindows() => _windows.Values.SelectMany(value => value);

    /// <summary>The rich overlays are no longer marquee-only: any surface carrying
    /// the matching component receives them (legacy configs get the historical
    /// component stack on the marquee surface, so behavior is unchanged there).
    /// A component scoped by `when` only routes in its display state.
    ///
    /// VISUAL TRIGGERS ONLY — see <see cref="WindowsCarrying"/> for state signals.</summary>
    private IEnumerable<MarqueeWindow> WindowsWithComponent(string componentType)
        => _surfaces.Values
            .Where(surface => surface.HasComponent(componentType))
            .SelectMany(surface => GetWindows(surface.Id))
            .Where(window => window.IsComponentActive(componentType));

    /// <summary>
    /// STATE signals (design note §4d): routed on declaration alone, never on the
    /// display state. A renderer must keep coherent state while its layer is hidden
    /// — so that it is right when the layer comes back, and so `SetLightingIngame`
    /// still mutes the tube sounds. Routing these through the scope filter is what
    /// silently killed the MAME outputs during play on a `when:navigation` surface.
    /// </summary>
    private IEnumerable<MarqueeWindow> WindowsCarrying(params string[] componentTypes)
        => _surfaces.Values
            .Where(surface => componentTypes.Any(surface.HasComponent))
            .SelectMany(surface => GetWindows(surface.Id));

    /// <summary>The surface now displays its own flattened stack: its layers stop
    /// being drawn live (no unlit copy over the lit one).</summary>
    public void SetDynamicRenderActive(string surfaceId, bool active)
    {
        foreach (var window in GetWindows(surfaceId)) window.SetDynamicRenderActive(active);
    }

    /// <summary>Empties ONE surface's media. Every handler must call it when the new
    /// entry has nothing of its own: keeping the previous entry's media is how one
    /// topper, one card, one fanart ends up following the whole library.</summary>
    public void ClearMedia(string target)
    {
        foreach (var window in GetWindows(target)) window.ClearMedia();
    }

    /// <summary>Pixel size of a surface's window — the dynamic renderer flattens the
    /// layer stack at exactly the size it will be shown at. (0,0) when the surface has
    /// no window (excluded screen, suspended).</summary>
    public (int Width, int Height) SurfacePixelSize(string surfaceId)
    {
        var window = GetWindows(surfaceId).FirstOrDefault();
        return window?.PixelSize ?? (0, 0);
    }

    /// <summary>Display state switch, broadcast to every surface.</summary>
    public void SetDisplayScene(string scene)
    {
        foreach (var window in AllWindows()) window.SetDisplayScene(scene);
    }

    /// <summary>Information overlays are keyed by owner; each owner belongs to a
    /// component type, which decides which surfaces show it.</summary>
    private static string ComponentForOwner(string owner)
    {
        // Owners carry suffixes (live-score-p1, live-timer:default) — match by prefix.
        if (owner.StartsWith("hiscore", StringComparison.OrdinalIgnoreCase)) return "overlay.hiscore";
        if (owner.StartsWith("live-score", StringComparison.OrdinalIgnoreCase)) return "overlay.live.score";
        if (owner.StartsWith("live-timer", StringComparison.OrdinalIgnoreCase)) return "overlay.live.timer";
        return "overlay.ra.info";
    }

    private Core.Surfaces.SurfaceDefinition? SurfaceOf(string target)
        => _surfaces.TryGetValue(target, out var surface) ? surface : null;

    public void Dispose()
    {
        foreach (var window in AllWindows()) window.Dispatcher.BeginInvoke(new Action(window.Close));
        _windows.Clear();
    }
}
