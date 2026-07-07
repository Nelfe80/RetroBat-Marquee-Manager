using RetroBatMarqueeManager.Application.Services;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Rendering.Skia;
using RetroBatMarqueeManager.Infrastructure.UI;

namespace RetroBatMarqueeManager.Infrastructure.Processes;

public sealed class MarqueeController : IDisposable
{
    private static readonly string[] Targets = { "marquee", "topper", "dmd", "iccard", "lcd" };
    private readonly IConfigService _config;
    private readonly IDmdService _dmd;
    private readonly ILogger<MarqueeController> _logger;
    private readonly Dictionary<string, List<MarqueeWindow>> _windows = new(StringComparer.OrdinalIgnoreCase);
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

            foreach (var target in Targets)
            foreach (var screen in _config.GetScreenIndices(target))
            {
                var isMarquee = target.Equals("marquee", StringComparison.OrdinalIgnoreCase);
                var window = new MarqueeWindow(screen, _logger,
                    isMarquee ? lightingOptions : null,
                    _config.GetTargetBounds(target),
                    isMarquee && _config.LightingDmdMirror && _config.DmdEnabled ? _dmd : null,
                    _config.DmdWidth, _config.DmdHeight);
                if (!_windows.TryGetValue(target, out var list)) _windows[target] = list = new();
                list.Add(window);
                if (target.Equals("iccard", StringComparison.OrdinalIgnoreCase))
                    window.SurfaceTapped += (fx, fy) => IcCardTapped?.Invoke(fx, fy);
                window.Show();
                _logger.LogInformation("Surface {Target} opened on screen {Screen}", target, screen);
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
        Application.Lighting.LightingSceneMeta? lightingMeta = null)
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
        if (!video && target.Equals("marquee", StringComparison.OrdinalIgnoreCase))
            path = PreferRealMarquee(path);
        _logger.LogInformation("Displaying {Kind} on target {Target} ({WindowCount} window(s)): {Path}", video ? "video" : "image", target, windows.Count, path);
        foreach (var window in windows)
        {
            if (video) window.DisplayVideo(path); else window.DisplayImage(path, lightingMeta);
        }
        return Task.CompletedTask;
    }

    public void SetInformation(string owner, string title, string detail, string? badgePath, bool persistent, int durationMs, string target = "marquee")
    {
        foreach (var window in GetWindows(target)) window.SetInformationOverlay(owner, title, detail, badgePath, persistent, durationMs);
    }

    public void ClearInformation(string owner, string target = "marquee")
    {
        foreach (var window in GetWindows(target)) window.RemoveInformationOverlay(owner);
    }

    public void ClearAllInformation()
    {
        foreach (var window in AllWindows()) window.ClearAllOverlays();
    }

    public void UpdateSpeedrunDisplay(string title, string detail, string? badgePath,
        double elapsedSeconds = 0, double? recordSeconds = null, double? userRecordSeconds = null, string? currentRank = null,
        int? leaderboardId = null, string? leaderboardTitle = null)
    {
        foreach (var window in GetWindows("marquee")) window.UpdateSpeedrunDisplay(title, detail, badgePath, elapsedSeconds, recordSeconds, userRecordSeconds, currentRank, leaderboardId, leaderboardTitle);
    }

    public void UpdateBadgeTray(IReadOnlyList<(int Id, string Path, bool Unlocked)> badges)
    {
        foreach (var window in GetWindows("marquee")) window.UpdateBadgeTray(badges);
    }

    public void ClearBadgeTray()
    {
        foreach (var window in GetWindows("marquee")) window.ClearBadgeTray();
    }

    public void ShowAchievementTakeover(string title, string detail, int points, string? badgePath, int durationMs)
    {
        foreach (var window in GetWindows("marquee")) window.ShowAchievementTakeover(title, detail, points, badgePath, durationMs);
    }

    public void ShowLeaderboardResult(string time, string rank, string diff, bool isRecord, int durationMs, string? badgePath = null)
    {
        foreach (var window in GetWindows("marquee")) window.ShowLeaderboardResult(time, rank, diff, isRecord, durationMs, badgePath);
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
        foreach (var window in GetWindows("marquee")) window.SetLayDmdActive(active);
    }

    /// <summary>Game launch / return-to-frontend: the marquee lighting re-ignites.</summary>
    public void PowerCycleLighting()
    {
        foreach (var window in GetWindows("marquee")) window.PowerCycleLighting();
    }

    /// <summary>Ingame = clean session: lighting sounds muted, attract paused.</summary>
    public void SetLightingIngame(bool ingame)
    {
        foreach (var window in GetWindows("marquee")) window.SetLightingIngame(ingame);
    }

    /// <summary>Live MAME output → mapped scene lamp (ws/arcade).</summary>
    public void SetLightingOutput(string output, int value)
    {
        foreach (var window in GetWindows("marquee")) window.SetLightingOutput(output, value);
    }

    /// <summary>Semantic ingame event → light effect (ws/ingame via the effects library).</summary>
    public void TriggerLightingEffect(Application.Lighting.IngameEffectRule rule)
    {
        foreach (var window in GetWindows("marquee")) window.TriggerLightingEffect(rule);
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

    public void Dispose()
    {
        foreach (var window in AllWindows()) window.Dispatcher.BeginInvoke(new Action(window.Close));
        _windows.Clear();
    }
}
