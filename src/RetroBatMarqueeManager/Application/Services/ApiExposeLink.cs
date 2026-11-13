using System.Net;
using System.Text.Json;

namespace RetroBatMarqueeManager.Application.Services;

/// <summary>
/// What it takes to talk to APIExpose, kept free of any dependency so the test project
/// can compile this file directly: which calls need the cabinet key, where the key is
/// found, how long to wait before the next attempt, and how to read what the API sends
/// without assuming more than it promises.
/// </summary>
public static class ApiExposeLink
{
    public const string ApiKeyHeader = "X-Api-Key";

    /// <summary>
    /// APIExpose only asks for the cabinet key when the caller is not on the loopback.
    /// The LAN address of this very machine counts as "not loopback": the API sees the
    /// source address, not the intent.
    /// </summary>
    public static bool IsLoopback(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return true;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
        if (uri.IsLoopback) return true;
        return IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// The key to present, empty when none is needed or none is known. The configured
    /// value wins; otherwise the key APIExpose generated for this cabinet is read from
    /// the sibling plugin folder, which covers the common case of a LAN address that
    /// points back to this machine.
    /// </summary>
    public static string ApiKeyFor(string? baseUrl, string? configuredKey, string baseDirectory)
    {
        if (IsLoopback(baseUrl)) return string.Empty;
        var configured = (configuredKey ?? string.Empty).Trim();
        if (configured.Length > 0) return configured;
        return ReadLocalCabinetKey(baseDirectory);
    }

    public static string ReadLocalCabinetKey(string baseDirectory)
    {
        try
        {
            var path = Path.Combine(baseDirectory, "..", "APIExpose", "state", "cabinet-api-key.txt");
            if (!File.Exists(path)) return string.Empty;
            var key = File.ReadAllText(path).Trim();
            return key.Length >= 16 ? key : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Wait before reconnection attempt number <paramref name="attempt"/> (1-based):
    /// 1 s, 2 s, 4 s, 8 s, 16 s then 30 s, spread by ±20 % so the eleven streams do not
    /// all knock at the same instant. <paramref name="jitter"/> is a draw in [0, 1).
    /// </summary>
    public static TimeSpan ReconnectDelay(int attempt, double jitter)
    {
        var step = Math.Clamp(attempt, 1, 6) - 1;
        var seconds = Math.Min(30d, Math.Pow(2, step));
        var spread = 0.8 + 0.4 * Math.Clamp(jitter, 0d, 1d);
        return TimeSpan.FromSeconds(seconds * spread);
    }

    /// <summary>
    /// Says in plain words why a connection failed, so the log tells the user what to
    /// do. Never contains the key.
    /// </summary>
    public static string DescribeFailure(string? message, bool loopback, bool keyPresented)
    {
        var text = message ?? string.Empty;
        if (text.Contains("401", StringComparison.Ordinal))
        {
            if (loopback) return "APIExpose refused the connection (401)";
            return keyPresented
                ? "APIExpose refused the cabinet key (401): check [Settings] ApiExposeApiKey against the key of that cabinet"
                : "APIExpose asks for the cabinet key (401): set [Settings] ApiExposeApiKey to the key of that cabinet (its APIExpose\\state\\cabinet-api-key.txt)";
        }
        if (text.Contains("403", StringComparison.Ordinal)) return "APIExpose refused the connection (403)";
        if (text.Contains("404", StringComparison.Ordinal)) return "this APIExpose does not publish that stream (404): update APIExpose";
        return text.Length > 0 ? text : "connection closed";
    }

    /// <summary>
    /// The hiscore stream carries every event whose type starts with "hiscore.": the
    /// score tables, but also the console capture diagnostics (captured, ignored, write
    /// skipped), whose payload holds a Rom and a Score too. Only a table is a table.
    /// </summary>
    public static bool IsHiscoreTable(string? type)
    {
        if (string.IsNullOrEmpty(type)) return true; // legacy envelope without a type
        return !type.StartsWith("hiscore.console.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A score as text, whatever its size: scores beyond 2 147 483 647 exist, and a
    /// number the API wrote with a decimal point is still a score.
    /// </summary>
    public static string ScoreText(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var whole)) return whole.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (value.TryGetDecimal(out var dec)) return decimal.Truncate(dec).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return value.GetRawText();
            case JsonValueKind.String:
                return value.GetString() ?? string.Empty;
            default:
                return string.Empty;
        }
    }

    /// <summary>A rank or a head count: 0 when absent, unreadable or out of range.</summary>
    public static int Count(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var whole)) return 0;
        return whole is > 0 and <= int.MaxValue ? (int)whole : 0;
    }
}
