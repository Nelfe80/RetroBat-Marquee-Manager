using System.Diagnostics;
using System.Threading.Channels;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Native;

namespace RetroBatMarqueeManager.Application.Services;

public sealed class DmdService : IDmdService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".avi", ".mkv", ".webm", ".mov" };
    private readonly IConfigService _config;
    private readonly DmdDeviceWrapper _wrapper;
    private readonly DmdFrameRenderer _renderer;
    private readonly ILogger<DmdService> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, PersistentContent> _persistent = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _statusBadges = new();
    private readonly Channel<Notification> _notifications = Channel.CreateUnbounded<Notification>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _mediaCancellation;
    private Process? _ownedDmdExt;
    private string? _baseMedia;
    private string? _textBackgroundMedia;
    private byte[]? _layoutFrame;
    private bool _externalControl;
    private bool _initialized;
    private bool _nativeOpen;
    private bool _stopped;
    private Task? _notificationWorker;
    private Task? _rotationWorker;
    private Task? _deferredRenderTask;
    private string? _displayedPersistentOwner;
    private DateTime _displayedPersistentSince;
    private bool _notificationActive;
    private string? _focusedOwner;
    private DateTime _focusedUntil;
    private readonly Dictionary<string, DateTime> _lastFocusByOwner = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _rotationStartedAt = DateTime.UtcNow;
    private static readonly TimeSpan RotationPollInterval = TimeSpan.FromMilliseconds(250);

    public DmdService(IConfigService config, DmdDeviceWrapper wrapper, DmdFrameRenderer renderer, ILogger<DmdService> logger)
    {
        _config = config;
        _wrapper = wrapper;
        _renderer = renderer;
        _logger = logger;
    }

    public string? ActivePersistentOwner
    {
        get
        {
            lock (_sync) return SelectPersistentContent(DateTime.UtcNow)?.Owner;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || !_config.DmdEnabled) return;
        var folder = Path.GetDirectoryName(_config.DmdExePath) ?? Path.Combine(_config.BaseDirectory, "tools", "dmd");
        PrepareDmdDeviceConfig(folder);
        if (_wrapper.Load(folder))
        {
            var optimizationReset = await OptimizeZeDmdAsync(cancellationToken);
            var opened = _wrapper.Open();
            if (opened < 0)
            {
                if (optimizationReset)
                {
                    _logger.LogWarning("DMD open failed after ZeDMD optimization reset; retrying without a second hardware reset.");
                }
                else
                {
                    _logger.LogWarning("DMD open failed; performing one private hardware reset before retry.");
                    _wrapper.HwReset(string.IsNullOrWhiteSpace(_config.ZeDmdPort) ? null : _config.ZeDmdPort);
                }
                await Task.Delay(3000, cancellationToken);
                opened = _wrapper.Open();
            }
            _initialized = opened >= 0;
            _nativeOpen = _initialized;
        }
        if (!_initialized) _logger.LogWarning("Native DMD unavailable; private dmdext fallback remains available for media playback.");
        _notificationWorker = Task.Run(() => RunNotificationsAsync(_lifetime.Token), _lifetime.Token);
        _rotationWorker = Task.Run(() => RunPersistentRotationAsync(_lifetime.Token), _lifetime.Token);
    }

    private async Task<bool> OptimizeZeDmdAsync(CancellationToken cancellationToken)
    {
        if (!_config.DmdOptimizeZeDmd) return false;
        if (!_wrapper.IsZeDmdDllLoaded || !_config.DmdModel.StartsWith("zedmd", StringComparison.OrdinalIgnoreCase)) return false;

        var cachedPort = string.IsNullOrWhiteSpace(_config.ZeDmdPort) ? null : _config.ZeDmdPort;
        try
        {
            _logger.LogInformation("[ZeDMD] Pre-boot hardware optimization starting.");
            if (!_wrapper.HwOpen(cachedPort))
            {
                _logger.LogWarning("[ZeDMD] Hardware optimization skipped: HwOpen failed.");
                return false;
            }

            var isHd = _wrapper.ZeDmdWidth >= 256 ||
                       _config.DmdWidth >= 256 ||
                       _config.DmdModel.Contains("hd", StringComparison.OrdinalIgnoreCase);
            var changed = _wrapper.PushHardwareCalibration(
                isHd,
                _config.DmdBrightness,
                _config.ZeDmdUsbPackageSize > 0 ? _config.ZeDmdUsbPackageSize : -1,
                _config.ZeDmdPanelMinRefreshRate > 0 ? _config.ZeDmdPanelMinRefreshRate : -1);
            _wrapper.HwClose();
            if (changed)
            {
                _logger.LogInformation("[ZeDMD] Hardware optimization changed settings; waiting 2s for firmware restart.");
                await Task.Delay(2000, cancellationToken);
                return true;
            }
            else
            {
                _logger.LogInformation("[ZeDMD] Hardware optimization already current.");
                return false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ZeDMD] Hardware optimization failed; continuing with DmdDevice.Open().");
            try { _wrapper.HwClose(); } catch { }
            return false;
        }
    }

    private void PrepareDmdDeviceConfig(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var selected = NormalizeDmdModel(_config.DmdModel);
            var models = new[]
            {
                "virtual", "virtualdmd", "pin2dmd", "zedmd", "zedmdhd", "zedmdwifi", "zedmdhdwifi",
                "pindmdv1", "pindmdv2", "pindmdv3", "pixelcade", "alphanumeric", "pinup",
                "rawoutput", "networkstream", "browserstream", "vpdbstream"
            };

            var lines = new List<string>
            {
                "; Generated by RetroBatMarqueeManager",
                "[global]",
                "resize = Fit",
                "flip_horizontally = false",
                "flip_vertically = false",
                string.Empty
            };
            foreach (var model in models)
            {
                lines.Add($"[{model}]");
                lines.Add($"enabled = {model.Equals(selected, StringComparison.OrdinalIgnoreCase).ToString().ToLowerInvariant()}");
                lines.Add(string.Empty);
            }

            var path = Path.Combine(folder, "DmdDevice.ini");
            File.WriteAllLines(path, lines);
            Environment.SetEnvironmentVariable("DMDDEVICE_CONFIG", path, EnvironmentVariableTarget.Process);
            _logger.LogInformation("Updated private DmdDevice.ini: {Model}.enabled=true", selected);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to update private DmdDevice.ini for DMD model {Model}", _config.DmdModel);
        }
    }

    private static string NormalizeDmdModel(string model)
    {
        var value = string.IsNullOrWhiteSpace(model) ? "zedmd" : model.Trim().ToLowerInvariant();
        return value switch
        {
            "virtual" => "virtualdmd",
            "ze-dmd" => "zedmd",
            "ze_dmd" => "zedmd",
            "zedmd-hd" => "zedmdhd",
            "ze-dmd-hd" => "zedmdhd",
            _ => value
        };
    }

    public Task SetBaseMediaAsync(string path, CancellationToken cancellationToken = default, string? textBackgroundPath = null)
    {
        if (_externalControl) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Task.CompletedTask;
        lock (_sync)
        {
            _baseMedia = path;
            _textBackgroundMedia = !string.IsNullOrWhiteSpace(textBackgroundPath) && File.Exists(textBackgroundPath)
                ? textBackgroundPath
                : path;
        }
        return RenderTopAsync(cancellationToken);
    }

    public Task SetPersistentTextAsync(string owner, string text, int priority, CancellationToken cancellationToken = default, string? rotationGroup = null, string? detailColor = null, int rotationDurationMs = 4000, bool focusOnChange = false)
    {
        if (!_config.DmdEnabled || _externalControl) return Task.CompletedTask;
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _persistent.Remove(owner);
                if (string.Equals(_focusedOwner, owner, StringComparison.OrdinalIgnoreCase)) _focusedOwner = null;
            }
            else
            {
                var now = DateTime.UtcNow;
                var changed = !_persistent.TryGetValue(owner, out var previous) || !string.Equals(previous.Text, text, StringComparison.Ordinal);
                var groupCountBefore = CountRotationGroup(rotationGroup);
                _persistent[owner] = new PersistentContent(owner, text, priority, now, rotationGroup, detailColor, Math.Max(250, rotationDurationMs));
                if (groupCountBefore < 2 && CountRotationGroup(rotationGroup) >= 2)
                    _rotationStartedAt = now;
                if (changed && focusOnChange && !string.Equals(_displayedPersistentOwner, owner, StringComparison.OrdinalIgnoreCase))
                {
                    var focusDuration = TimeSpan.FromMilliseconds(_config.DmdMinimumBlockDisplayMs);
                    var cooldown = TimeSpan.FromMilliseconds(Math.Max(6000, _config.DmdMinimumBlockDisplayMs * 2));
                    if (!_lastFocusByOwner.TryGetValue(owner, out var lastFocus) || now - lastFocus >= cooldown)
                    {
                        _focusedOwner = owner;
                        _focusedUntil = now + focusDuration;
                        _lastFocusByOwner[owner] = now;
                    }
                }
            }
        }
        return RenderTopAsync(cancellationToken);
    }

    public void SetStatusBadges(IReadOnlyList<string> badgePaths)
    {
        lock (_sync)
        {
            _statusBadges.Clear();
            _statusBadges.AddRange(badgePaths
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2));
        }
        _ = RenderTopAsync(_lifetime.Token);
    }

    public Task ShowNotificationAsync(string owner, string title, string detail, string? badgePath, int durationMs, int priority, CancellationToken cancellationToken = default)
    {
        if (!_config.DmdEnabled || _externalControl || cancellationToken.IsCancellationRequested) return Task.CompletedTask;
        return _notifications.Writer.WriteAsync(new Notification(owner, title, detail, badgePath, Math.Max(250, durationMs), priority), cancellationToken).AsTask();
    }

    public void ClearOwner(string owner)
    {
        lock (_sync) _persistent.Remove(owner);
        _ = RenderTopAsync(_lifetime.Token);
    }

    private int _layoutRenderBusy;

    public void SetLayoutFrame(byte[] pixels)
    {
        if (_externalControl) return;
        lock (_sync) _layoutFrame = pixels.Length == 0 ? null : pixels.ToArray();
        // latest-wins, single render in flight: high-frequency pushes (lighting
        // mirror) must never pile up concurrent renders on the native device
        if (Interlocked.CompareExchange(ref _layoutRenderBusy, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try { await RenderTopAsync(_lifetime.Token); }
            catch (Exception ex) { _logger.LogDebug(ex, "Layout frame render failed"); }
            finally { Volatile.Write(ref _layoutRenderBusy, 0); }
        });
    }

    public void SetExternalControl(bool active)
    {
        if (_stopped || _externalControl == active) return;
        _externalControl = active;
        if (active)
        {
            _logger.LogInformation("DMD external control enabled; releasing private DMD device.");
            StopOwnedPlayback(reopenNative: false);
            ClearPhysicalDmd();
            CloseNative();
            lock (_sync) _layoutFrame = null;
            return;
        }

        _logger.LogInformation("DMD external control disabled; private DMD device will resume.");
        _ = ResumePrivateDmdAsync(_lifetime.Token);
    }

    private async Task ResumePrivateDmdAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(2000, cancellationToken);
            if (_externalControl || _stopped || !_wrapper.IsLoaded) return;
            var opened = _wrapper.Open();
            if (opened < 0)
            {
                _logger.LogWarning("DMD reopen after external control failed; attempting one hardware reset.");
                _wrapper.HwReset(string.IsNullOrWhiteSpace(_config.ZeDmdPort) ? null : _config.ZeDmdPort);
                await Task.Delay(3000, cancellationToken);
                opened = _wrapper.Open();
            }
            _nativeOpen = opened >= 0;
            if (_nativeOpen) await RenderTopAsync(cancellationToken);
            else _logger.LogWarning("DMD reopen after external control failed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task RunNotificationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _notifications.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    StopOwnedPlayback();
                    string? background;
                    string[] statusBadges;
                    lock (_sync)
                    {
                        _notificationActive = true;
                        background = _textBackgroundMedia;
                        statusBadges = _statusBadges.ToArray();
                    }
                    var pixels = _renderer.RenderText(item.Title, item.Detail, item.BadgePath, _config.DmdWidth, _config.DmdHeight, background, statusBadges);
                    RenderPixels(pixels);
                    await Task.Delay(item.DurationMs, cancellationToken);
                }
                finally
                {
                    lock (_sync) _notificationActive = false;
                }
                await RenderTopAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private Task RenderTopAsync(CancellationToken cancellationToken)
    {
        if (!_config.DmdEnabled || _externalControl || cancellationToken.IsCancellationRequested) return Task.CompletedTask;
        PersistentContent? content;
        string? media;
        string? textBackground;
        string[] statusBadges;
        byte[]? layout;
        TimeSpan defer = TimeSpan.Zero;
        lock (_sync)
        {
            if (_notificationActive) return Task.CompletedTask;
            var now = DateTime.UtcNow;
            content = SelectPersistentContent(now);
            media = _baseMedia;
            textBackground = _textBackgroundMedia;
            statusBadges = _statusBadges.ToArray();
            layout = _layoutFrame;
            if (content != null &&
                !string.IsNullOrWhiteSpace(_displayedPersistentOwner) &&
                !string.Equals(_displayedPersistentOwner, content.Owner, StringComparison.OrdinalIgnoreCase))
            {
                var elapsed = now - _displayedPersistentSince;
                var minimumBlockDisplay = TimeSpan.FromMilliseconds(_config.DmdMinimumBlockDisplayMs);
                if (elapsed < minimumBlockDisplay) defer = minimumBlockDisplay - elapsed;
            }

            if (content != null && defer == TimeSpan.Zero &&
                !string.Equals(_displayedPersistentOwner, content.Owner, StringComparison.OrdinalIgnoreCase))
            {
                _displayedPersistentOwner = content.Owner;
                _displayedPersistentSince = now;
            }
        }
        if (defer > TimeSpan.Zero)
        {
            ScheduleDeferredRender(defer);
            return Task.CompletedTask;
        }
        if (content != null)
        {
            StopOwnedPlayback();
            RenderPixels(_renderer.RenderText(content.Owner, content.Text, null, _config.DmdWidth, _config.DmdHeight, textBackground, statusBadges, content.DetailColor));
            return Task.CompletedTask;
        }
        lock (_sync) _displayedPersistentOwner = null;
        if (layout is { Length: > 0 })
        {
            StopOwnedPlayback();
            RenderPixels(layout);
            return Task.CompletedTask;
        }
        return string.IsNullOrWhiteSpace(media) ? Task.CompletedTask : RenderMediaAsync(media, cancellationToken);
    }

    private async Task RunPersistentRotationAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(RotationPollInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                bool shouldRender;
                lock (_sync)
                {
                    var selected = SelectPersistentContent(DateTime.UtcNow);
                    shouldRender = selected != null &&
                                   !string.Equals(selected.Owner, _displayedPersistentOwner, StringComparison.OrdinalIgnoreCase);
                }
                if (shouldRender) await RenderTopAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private PersistentContent? SelectPersistentContent(DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(_focusedOwner))
        {
            if (now < _focusedUntil && _persistent.TryGetValue(_focusedOwner, out var focused)) return focused;
            _focusedOwner = null;
        }
        var candidates = new List<(PersistentContent Content, int EffectivePriority)>();
        candidates.AddRange(_persistent.Values
            .Where(item => string.IsNullOrWhiteSpace(item.RotationGroup))
            .Select(item => (item, item.Priority)));

        foreach (var group in _persistent.Values
                     .Where(item => !string.IsNullOrWhiteSpace(item.RotationGroup))
                     .GroupBy(item => item.RotationGroup!, StringComparer.OrdinalIgnoreCase))
        {
            var items = group.OrderBy(RotationOrder).ThenBy(item => item.Owner, StringComparer.OrdinalIgnoreCase).ToList();
            var totalMs = items.Sum(RotationDurationMs);
            var cursor = Math.Max(0, (now - _rotationStartedAt).TotalMilliseconds) % totalMs;
            var selected = items[0];
            foreach (var item in items)
            {
                selected = item;
                var duration = RotationDurationMs(item);
                if (cursor < duration) break;
                cursor -= duration;
            }
            candidates.Add((selected, items.Max(item => item.Priority)));
        }

        return candidates
            .OrderByDescending(item => item.EffectivePriority)
            .ThenByDescending(item => item.Content.UpdatedAt)
            .Select(item => item.Content)
            .FirstOrDefault();
    }

    private int CountRotationGroup(string? group)
        => string.IsNullOrWhiteSpace(group)
            ? 0
            : _persistent.Values.Count(item => string.Equals(item.RotationGroup, group, StringComparison.OrdinalIgnoreCase));

    private void ScheduleDeferredRender(TimeSpan delay)
    {
        lock (_sync)
        {
            if (_deferredRenderTask is { IsCompleted: false }) return;
            _deferredRenderTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, _lifetime.Token);
                    lock (_sync) _deferredRenderTask = null;
                    await RenderTopAsync(_lifetime.Token);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            }, _lifetime.Token);
        }
    }

    private static int RotationOrder(PersistentContent item)
        => item.Owner.StartsWith("LIVE SCORE", StringComparison.OrdinalIgnoreCase) ? 0
            : item.Owner.StartsWith("RETROACHIEVEMENTS", StringComparison.OrdinalIgnoreCase) ? 1
            : item.Owner.StartsWith("LIVE TIMER", StringComparison.OrdinalIgnoreCase) ? 2
            : 3;

    private static int RotationDurationMs(PersistentContent item) => item.RotationDurationMs;

    private Task RenderMediaAsync(string path, CancellationToken cancellationToken)
    {
        StopOwnedPlayback();
        var extension = Path.GetExtension(path);
        if (VideoExtensions.Contains(extension))
        {
            StartOwnedDmdExt(path);
            return Task.CompletedTask;
        }
        if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
        {
            var frames = _renderer.RenderAnimation(path, _config.DmdWidth, _config.DmdHeight);
            _mediaCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_mediaCancellation.Token.IsCancellationRequested)
                        foreach (var frame in frames)
                        {
                            RenderPixels(frame.Pixels);
                            await Task.Delay(frame.DelayMs, _mediaCancellation.Token);
                        }
                }
                catch (OperationCanceledException) { }
            }, _mediaCancellation.Token);
            return Task.CompletedTask;
        }
        RenderPixels(_renderer.RenderImage(path, _config.DmdWidth, _config.DmdHeight));
        return Task.CompletedTask;
    }

    private readonly object _nativeRenderSync = new();

    private void RenderPixels(byte[] pixels)
    {
        if (_externalControl || pixels.Length == 0 || !_wrapper.IsLoaded || !_nativeOpen) return;
        try
        {
            // the native DMD render is not reentrant: concurrent calls corrupt memory
            lock (_nativeRenderSync)
            {
                if (!_nativeOpen) return;
                _wrapper.Render((ushort)_config.DmdWidth, (ushort)_config.DmdHeight, pixels);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "DMD frame rendering failed"); }
    }

    private void StartOwnedDmdExt(string path)
    {
        if (!File.Exists(_config.DmdExePath))
        {
            _logger.LogWarning("Private dmdext executable unavailable for {Path}", path);
            return;
        }
        try
        {
            CloseNative();
            var info = new ProcessStartInfo
            {
                FileName = _config.DmdExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_config.DmdExePath) ?? _config.BaseDirectory
            };
            info.ArgumentList.Add("play");
            info.ArgumentList.Add("-f");
            info.ArgumentList.Add(path);
            info.ArgumentList.Add("-d");
            info.ArgumentList.Add(_config.DmdModel);
            _ownedDmdExt = Process.Start(info);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to start private dmdext for {Path}", path);
        }
    }

    private void StopOwnedPlayback(bool reopenNative = true)
    {
        _mediaCancellation?.Cancel();
        _mediaCancellation?.Dispose();
        _mediaCancellation = null;
        var process = _ownedDmdExt;
        _ownedDmdExt = null;
        if (process != null)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) { _logger.LogDebug(ex, "Unable to stop owned dmdext process"); }
            process.Dispose();
            if (reopenNative && !_externalControl && _wrapper.IsLoaded)
            {
                var opened = _wrapper.Open();
                _nativeOpen = opened >= 0;
            }
        }
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _lifetime.Cancel();
        StopOwnedPlayback(reopenNative: false);
        ClearPhysicalDmd();
        CloseNative();
        _logger.LogInformation("Private DMD stack stopped.");
    }

    private void CloseNative()
    {
        if (!_wrapper.IsLoaded || !_nativeOpen) return;
        _wrapper.Close();
        _nativeOpen = false;
    }

    private void ClearPhysicalDmd()
    {
        if (!_wrapper.IsLoaded || !_nativeOpen) return;
        try
        {
            var pixels = new byte[Math.Max(1, _config.DmdWidth * _config.DmdHeight * 3)];
            _wrapper.Render((ushort)_config.DmdWidth, (ushort)_config.DmdHeight, pixels);
            _logger.LogInformation("DMD clear frame sent before shutdown.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to send DMD clear frame before shutdown.");
        }
    }

    public void Dispose()
    {
        Stop();
        _lifetime.Dispose();
        _wrapper.Dispose();
    }

    private sealed record PersistentContent(string Owner, string Text, int Priority, DateTime UpdatedAt, string? RotationGroup, string? DetailColor, int RotationDurationMs);
    private sealed record Notification(string Owner, string Title, string Detail, string? BadgePath, int DurationMs, int Priority);
}
