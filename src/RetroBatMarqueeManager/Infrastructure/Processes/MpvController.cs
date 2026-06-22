using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.UI;

namespace RetroBatMarqueeManager.Infrastructure.Processes
{
    public class MpvController : IDisposable
    {
        private readonly IConfigService _config;
        private readonly ILogger<MpvController> _logger;

        private Thread? _uiThread;
        // One target can drive multiple physical screens — e.g. MarqueeScreen=1,3
        private readonly Dictionary<string, List<MarqueeWindow>> _windows = new(StringComparer.OrdinalIgnoreCase);
        private bool _isInitialized = false;

        public MarqueeWindow? Window => GetWindow("marquee");

        public MpvController(IConfigService config, IProcessService processService, ILogger<MpvController> logger)
        {
            _config = config;
            _logger = logger;
        }

        public bool IsMpvEnabled => _config.IsMpvEnabled;

        public void StartMpv()
        {
            if (_isInitialized) return;

            _logger.LogInformation("[WPF Player] Starting native WPF player thread for all enabled screens...");
            _uiThread = new Thread(UiThreadEntry);
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.IsBackground = true;
            _uiThread.Start();
            _isInitialized = true;
        }

        private void UiThreadEntry()
        {
            try
            {
                // Each config key can have comma-separated screen indices: MarqueeScreen=1,3
                var targets = new[]
                {
                    ("marquee", "MarqueeScreen", "ScreenNumber", "2"),
                    ("topper",  "TopperScreen",  "",             "-1"),
                    ("dmd",     "DmdScreen",     "",             "-1"),
                    ("iccard",  "IcCardScreen",  "",             "-1"),
                    ("lcd",     "LcdScreen",     "",             "-1"),
                };

                foreach (var (target, key, aliasKey, defaultVal) in targets)
                {
                    var indices = GetScreenIndices(key, aliasKey, defaultVal);
                    foreach (var idx in indices)
                    {
                        _logger.LogInformation($"[WPF Player] Initializing '{target}' window on Screen {idx}");
                        var window = new MarqueeWindow(idx, _logger);
                        if (!_windows.TryGetValue(target, out var list))
                        {
                            list = new List<MarqueeWindow>();
                            _windows[target] = list;
                        }
                        list.Add(window);
                        window.Show();
                    }
                }

                if (_windows.Count == 0)
                {
                    _logger.LogWarning("[WPF Player] No screens enabled in configuration. Thread exiting.");
                    return;
                }

                System.Windows.Threading.Dispatcher.Run();
            }
            catch (Exception ex)
            {
                _logger.LogError($"[WPF Player Thread] Exception occurred: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // Parses "1" or "1,3" or "false"/"-1" into a list of enabled screen indices
        private List<int> GetScreenIndices(string key, string aliasKey, string defaultValue)
        {
            var val = _config.GetSetting(key, "");
            if (string.IsNullOrEmpty(val) && !string.IsNullOrEmpty(aliasKey))
                val = _config.GetSetting(aliasKey, "");
            if (string.IsNullOrEmpty(val))
                val = defaultValue;

            var result = new List<int>();
            foreach (var part in val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Equals("false", StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(part, out int idx) && idx >= 0)
                    result.Add(idx);
            }
            return result;
        }

        // Returns all windows for a target (empty list if not configured)
        public List<MarqueeWindow> GetWindows(string target)
        {
            if (string.IsNullOrEmpty(target)) target = "marquee";
            return _windows.TryGetValue(target, out var list) ? list : new List<MarqueeWindow>();
        }

        // Returns first window for a target (backward compat)
        public MarqueeWindow? GetWindow(string target)
        {
            var list = GetWindows(target);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task Stop()
        {
            foreach (var window in AllWindows()) window.StopPlayback();
            await Task.CompletedTask;
        }

        public async Task DisplayImage(string imagePath, bool loop = true, CancellationToken token = default)
            => await DisplayImageToTarget(imagePath, "marquee", loop, token);

        public async Task DisplayImageToTarget(string imagePath, string target, bool loop = true, CancellationToken token = default)
        {
            var windows = GetWindows(target);
            if (windows.Count == 0)
            {
                _logger.LogWarning($"[WPF Player] Target '{target}' window not initialized. Ignoring display request.");
                return;
            }
            if (token.IsCancellationRequested) return;

            try
            {
                var ext = Path.GetExtension(imagePath).ToLowerInvariant();
                bool isVideo = new[] { ".mp4", ".webm", ".avi", ".mkv", ".mov" }.Contains(ext);

                if (isVideo)
                {
                    _logger.LogInformation($"[WPF Player] Loading video for target {target}: {imagePath}");
                    foreach (var w in windows) w.DisplayVideo(imagePath);
                }
                else
                {
                    _logger.LogInformation($"[WPF Player] Loading image for target {target}: {imagePath}");
                    foreach (var w in windows) w.DisplayImage(imagePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[WPF Player] Error in DisplayImageToTarget for {target}: {ex.Message}");
            }
            await Task.CompletedTask;
        }

        public async Task OverlayImage(string imagePath, int slot, string position = "0:0", bool isPersistent = true, int loopCount = 0)
            => await OverlayImageToTarget(imagePath, slot, "marquee", position, isPersistent, loopCount);

        public async Task OverlayImageToTarget(string imagePath, int slot, string target, string position = "0:0", bool isPersistent = true, int loopCount = 0)
        {
            foreach (var w in GetWindows(target)) w.SetOverlayImage(slot, imagePath, position);
            await Task.CompletedTask;
        }

        public async Task RemoveOverlay(int slot, bool cancelTimer = false)
            => await RemoveOverlayFromTarget(slot, "marquee", cancelTimer);

        public async Task RemoveOverlayFromTarget(int slot, string target, bool cancelTimer = false)
        {
            foreach (var w in GetWindows(target)) w.RemoveOverlayImage(slot);
            await Task.CompletedTask;
        }

        public async Task ClearRetroAchievementData()
        {
            foreach (var w in AllWindows()) w.ClearAllOverlays();
            await Task.CompletedTask;
        }

        public async Task ShowOverlay(string imagePath, int durationMs, string position = "top-right")
            => await ShowOverlayToTarget(imagePath, durationMs, "marquee", position);

        public async Task ShowOverlayToTarget(string imagePath, int durationMs, string target, string position = "top-right")
        {
            foreach (var w in GetWindows(target))
            {
                w.SetOverlayImage(9, imagePath, position);
                _ = Task.Delay(durationMs).ContinueWith(_ => w.RemoveOverlayImage(9));
            }
            await Task.CompletedTask;
        }

        public async Task ShowAchievementNotification(string cupPath, string finalOverlayPath, int cupDuration = 2000, int finalDuration = 8000)
            => await ShowAchievementNotificationToTarget(cupPath, finalOverlayPath, "marquee", cupDuration, finalDuration);

        public async Task ShowAchievementNotificationToTarget(string cupPath, string finalOverlayPath, string target, int cupDuration = 2000, int finalDuration = 8000)
        {
            foreach (var w in GetWindows(target))
            {
                if (!string.IsNullOrEmpty(cupPath) && File.Exists(cupPath))
                {
                    w.SetOverlayImage(10, cupPath, "center");
                    await Task.Delay(cupDuration);
                    w.RemoveOverlayImage(10);
                }
                if (!string.IsNullOrEmpty(finalOverlayPath) && File.Exists(finalOverlayPath))
                {
                    w.SetOverlayImage(11, finalOverlayPath, "0:0");
                    await Task.Delay(finalDuration);
                    w.RemoveOverlayImage(11);
                }
            }
        }

        public Task<double> GetCurrentTime() => GetCurrentTimeForTarget("marquee");

        public Task<double> GetCurrentTimeForTarget(string target)
        {
            var w = GetWindow(target);
            return Task.FromResult(w?.GetVideoCurrentTime() ?? 0.0);
        }

        public async Task SendCommandAsync(string command, bool retry = true)
            => await SendCommandToTargetAsync(command, "marquee", retry);

        public async Task SendCommandToTargetAsync(string command, string target, bool retry = true)
        {
            var windows = GetWindows(target);
            if (windows.Count == 0 || string.IsNullOrWhiteSpace(command)) return;

            try
            {
                using var doc = JsonDocument.Parse(command);
                if (doc.RootElement.TryGetProperty("command", out var cmdEl) && cmdEl.ValueKind == JsonValueKind.Array)
                {
                    var args = cmdEl.EnumerateArray().Select(e => e.ToString()).ToArray();
                    if (args.Length >= 2 && args[0].Equals("show-text", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = args[1];
                        int duration = 2000;
                        if (args.Length >= 3 && int.TryParse(args[2], out int dur)) duration = dur;
                        foreach (var w in windows) w.ShowOSDText(text, duration);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[WPF Player] Failed to parse command JSON: {ex.Message}");
            }
            await Task.CompletedTask;
        }

        public void LoadMameLayout(MameLayout layout, string defaultViewName)
        {
            foreach (var (target, windows) in _windows)
            {
                string viewName = target.ToLowerInvariant() switch
                {
                    "dmd"    => "DMD_Only",
                    "topper" => "Topper_Only",
                    "iccard" => "ICCard_Only",
                    _        => "Marquee_Only",
                };
                _logger.LogInformation($"[MAME Layout] Loading layout view '{viewName}' for target '{target}'");
                foreach (var w in windows) w.LoadMameLayout(layout, viewName);
            }
        }

        public void SetLampState(string lampName, int state)
        {
            foreach (var w in AllWindows()) w.SetLampState(lampName, state);
        }

        public void ClearLayout()
        {
            foreach (var w in AllWindows()) w.ClearLayout();
        }

        public void Dispose()
        {
            foreach (var w in AllWindows())
                w.Dispatcher.BeginInvoke(new Action(() => w.Close()));
            _windows.Clear();
            _isInitialized = false;
        }

        private IEnumerable<MarqueeWindow> AllWindows()
            => _windows.Values.SelectMany(list => list);
    }
}
