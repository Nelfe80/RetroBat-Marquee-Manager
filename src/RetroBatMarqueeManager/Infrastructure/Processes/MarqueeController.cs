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

    /// <summary>Tap on an iccard surface, as fractions (0..1). Consumed by InstructionCardService.</summary>
    public event Action<double, double>? IcCardTapped;

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
                ? new LightingSurfaceOptions(_config.LightingTestPattern, _config.LightingFpsLimit, _config.LightingShowFps, _config.LightingRenderScale, _config.LightingFillHeightMaxCrop, _config.LightingSoundEnabled, _config.LightingSoundVolume, _config.LightingGlassReflection, _config.LightingTubeVisualOpacity)
                : null;

            // dynamic surfaces (state\surfaces.json) or their legacy [Screens] equivalent
            foreach (var surface in _config.GetSurfaces())
            {
                _surfaces[surface.Id] = surface;
                if (surface.Category.Equals("dmd-physical", StringComparison.OrdinalIgnoreCase))
                    continue; // window-less sink: content routes to IDmdService

                foreach (var screen in surface.Screens)
                {
                    var lighting = surface.HasComponent("lighting.engine") ? lightingOptions : null;
                    var window = new MarqueeWindow(screen, _logger,
                        lighting,
                        surface.Bounds,
                        lighting != null && _config.LightingDmdMirror && _config.DmdEnabled ? _dmd : null,
                        _config.DmdWidth, _config.DmdHeight,
                        surface);
                    if (!_windows.TryGetValue(surface.Id, out var list)) _windows[surface.Id] = list = new();
                    list.Add(window);
                    if (surface.Category.Equals("iccard", StringComparison.OrdinalIgnoreCase))
                        window.SurfaceTapped += (fx, fy) => IcCardTapped?.Invoke(fx, fy);
                    window.Show();
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

    /// <summary>Feeds one component type directly (instruction card split…).</summary>
    public void SetComponentSource(string type, string? path)
    {
        foreach (var window in AllWindows()) window.SetComponentSource(type, path);
    }

    /// <summary>True when at least one surface carries this component.</summary>
    public bool HasComponent(string type)
        => _surfaces.Values.Any(surface => surface.HasComponent(type));

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
        foreach (var window in WindowsWithComponent("lighting.engine")) window.SetLayDmdActive(active);
    }

    /// <summary>Game launch / return-to-frontend: the marquee lighting re-ignites.</summary>
    public void PowerCycleLighting()
    {
        foreach (var window in WindowsWithComponent("lighting.engine")) window.PowerCycleLighting();
    }

    /// <summary>Ingame = clean session: lighting sounds muted, attract paused.</summary>
    public void SetLightingIngame(bool ingame)
    {
        foreach (var window in WindowsWithComponent("lighting.engine")) window.SetLightingIngame(ingame);
    }

    /// <summary>Live MAME output → mapped scene lamp (ws/arcade).</summary>
    public void SetLightingOutput(string output, int value)
    {
        foreach (var window in WindowsWithComponent("lighting.engine")) window.SetLightingOutput(output, value);
    }

    /// <summary>Semantic ingame event → light effect (ws/ingame via the effects library).</summary>
    public void TriggerLightingEffect(Application.Lighting.IngameEffectRule rule)
    {
        foreach (var window in WindowsWithComponent("lighting.engine")) window.TriggerLightingEffect(rule);
    }

    /// <summary>User-dropped effect media (webm/gif) triggered by a signal:
    /// overlay on the marquee or temporary fullscreen takeover.</summary>
    public void PlayMediaEffect(string path, bool fullscreen, int durationMs)
    {
        foreach (var window in WindowsWithComponent("lighting.engine")) window.PlayMediaEffect(path, fullscreen, durationMs);
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
    /// A component scoped by `when` only routes in its display state.</summary>
    private IEnumerable<MarqueeWindow> WindowsWithComponent(string componentType)
        => _surfaces.Values
            .Where(surface => surface.HasComponent(componentType))
            .SelectMany(surface => GetWindows(surface.Id))
            .Where(window => window.IsComponentActive(componentType));

    /// <summary>Display state switch, broadcast to every surface.</summary>
    public void SetDisplayScene(string scene)
    {
        foreach (var window in AllWindows()) window.SetDisplayScene(scene);
    }

    /// <summary>Information overlays are keyed by owner; each owner belongs to a
    /// component type, which decides which surfaces show it.</summary>
    private static string ComponentForOwner(string owner) => owner.ToLowerInvariant() switch
    {
        "hiscore" => "overlay.hiscore",
        "live-score" => "overlay.live.score",
        "live-timer" => "overlay.live.timer",
        _ when owner.StartsWith("ra", StringComparison.OrdinalIgnoreCase) => "overlay.ra.info",
        _ => "overlay.ra.info"
    };

    private Core.Surfaces.SurfaceDefinition? SurfaceOf(string target)
        => _surfaces.TryGetValue(target, out var surface) ? surface : null;

    public void Dispose()
    {
        foreach (var window in AllWindows()) window.Dispatcher.BeginInvoke(new Action(window.Close));
        _windows.Clear();
    }
}
