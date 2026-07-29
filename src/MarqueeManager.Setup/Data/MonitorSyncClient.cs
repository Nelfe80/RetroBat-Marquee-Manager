using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace MarqueeManager.Setup.Data;

/// <summary>
/// Tells APIExpose which physical monitor is the RetroBat/game screen so it keeps
/// <c>&lt;system&gt;.MonitorIndex</c> in es_settings.cfg pointing there — the value
/// standalone emulators (MAME/FBNeo…) use to pick their display. APIExpose owns the
/// es_settings write (and only applies the value when it differs); this is a
/// best-effort, fire-and-forget push on the loopback API, so failures (APIExpose not
/// running, feature disabled) are swallowed and simply retried on the next save.
/// </summary>
public static class MonitorSyncClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private static string? _lastPushed;

    /// <summary>Pushes <paramref name="deviceName"/> (a Windows GDI device name, e.g.
    /// <c>\\.\DISPLAY1</c>) as the game screen. No-op when the name is empty, unchanged
    /// since the last successful push, or when the base URL is unusable.</summary>
    public static void PushGameScreen(string? apiBaseUrl, string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return;
        if (string.Equals(_lastPushed, deviceName, StringComparison.OrdinalIgnoreCase)) return;

        var baseUrl = ToHttpBase(apiBaseUrl);
        if (baseUrl == null) return;

        _lastPushed = deviceName;
        var url = $"{baseUrl}/api/v1/system/monitor-index/sync?deviceName={Uri.EscapeDataString(deviceName)}";
        _ = Task.Run(async () =>
        {
            try
            {
                using var response = await Http.PostAsync(url, content: null).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }
            catch
            {
                _lastPushed = null; // let the next save retry
            }
        });
    }

    private static string? ToHttpBase(string? apiBaseUrl)
    {
        var url = (apiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (url.Length == 0) return null;

        if (url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url["ws://".Length..];
        else if (url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url["wss://".Length..];
        else if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url;

        return url;
    }
}
