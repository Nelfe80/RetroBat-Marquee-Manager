using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MarqueeManager.Setup.Data;

/// <summary>
/// ScreenScraper credential split — same doctrine as APIExpose:
///
/// DEV credentials (the developer's, never in the repo, never in the UI):
///  1) environment variables APIEXPOSE_SCREENSCRAPER_DEV_ID / _DEV_PASSWORD,
///  2) ..\APIExpose\.env (untracked, format KEY = 'value') — the dev machine,
///  3) EmbeddedSecretDefaults.g.cs, GENERATED at build into obj\ from that same
///     .env (empty constants otherwise) — distributed binaries, nothing committed.
/// No dev credentials → the ScreenScraper source simply doesn't show up.
///
/// USER credentials (ssid/sspassword, the only ones visible in Options):
///  config.ini [Scraper] first, else picked up from EmulationStation's
///  es_settings.cfg (ScreenScraperUser/ScreenScraperPass and aliases).
/// </summary>
public static class ScreenScraperCredentials
{
    public static (string DevId, string DevPassword) ResolveDev(string pluginRoot)
    {
        var id = Environment.GetEnvironmentVariable("APIEXPOSE_SCREENSCRAPER_DEV_ID")?.Trim() ?? "";
        var password = Environment.GetEnvironmentVariable("APIEXPOSE_SCREENSCRAPER_DEV_PASSWORD")?.Trim() ?? "";
        if (id.Length > 0 && password.Length > 0) return (id, password);

        var (envId, envPassword) = ReadApiExposeEnv(pluginRoot);
        if (envId.Length > 0 && envPassword.Length > 0) return (envId, envPassword);

        return (Infrastructure.EmbeddedSecretDefaults.ScreenScraperDevId,
            Infrastructure.EmbeddedSecretDefaults.ScreenScraperDevPassword);
    }

    public static bool HasDev(string pluginRoot)
    {
        var (id, password) = ResolveDev(pluginRoot);
        return id.Length > 0 && password.Length > 0;
    }

    /// <summary>User ssid/sspassword: explicit [Scraper] values win, otherwise
    /// EmulationStation's own scraper account is reused silently.</summary>
    public static (string User, string Password) ResolveUser(string pluginRoot, Func<string, string> credential)
    {
        var user = credential("ScreenScraperUser").Trim();
        var password = credential("ScreenScraperPass").Trim();
        if (user.Length > 0 && password.Length > 0) return (user, password);

        var (esUser, esPassword) = ReadEsSettings(pluginRoot);
        return (user.Length > 0 ? user : esUser, password.Length > 0 ? password : esPassword);
    }

    // ================= sources =================

    private static (string Id, string Password) ReadApiExposeEnv(string pluginRoot)
    {
        try
        {
            var path = Path.Combine(pluginRoot, "..", "APIExpose", ".env");
            if (!File.Exists(path)) return ("", "");
            var text = File.ReadAllText(path);
            return (MatchEnv(text, "SCREENSCRAPER_DEVID"), MatchEnv(text, "SCREENSCRAPER_DEVPASSWORD"));
        }
        catch
        {
            return ("", "");
        }
    }

    private static string MatchEnv(string text, string key)
    {
        var match = Regex.Match(text, key + @"\s*=\s*'([^']*)'");
        if (match.Success) return match.Groups[1].Value.Trim();
        match = Regex.Match(text, key + @"\s*=\s*""?([^""\r\n]*)""?");
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static (string User, string Password) ReadEsSettings(string pluginRoot)
    {
        try
        {
            var path = Path.Combine(pluginRoot, "..", "..", "emulationstation", ".emulationstation", "es_settings.cfg");
            if (!File.Exists(path)) return ("", "");
            var document = XDocument.Load(path);
            string Read(params string[] names)
            {
                foreach (var name in names)
                {
                    var value = document.Root?.Elements("string")
                        .FirstOrDefault(e => string.Equals((string?)e.Attribute("name"), name, StringComparison.OrdinalIgnoreCase))
                        ?.Attribute("value")?.Value;
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                }
                return "";
            }
            return (
                Read("ScreenScraperUser", "ScraperUser", "scraper_user"),
                Read("ScreenScraperPass", "ScreenScraperPassword", "ScraperPassword", "scraper_password"));
        }
        catch
        {
            return ("", "");
        }
    }

    // ================= system → ScreenScraper systemeid =================

    private static Dictionary<string, string>? _systemMap;

    /// <summary>APIExpose ships the full frontend→systemeid table
    /// (resources\scraping\reference\systems_screenscraper.json); a small
    /// built-in map covers the common systems if APIExpose is absent.</summary>
    public static string ResolveSystemId(string pluginRoot, string system)
    {
        _systemMap ??= LoadSystemMap(pluginRoot);
        var key = (system ?? "").Trim().Replace(' ', '_').ToLowerInvariant();
        return _systemMap.TryGetValue(key, out var id) ? id : "";
    }

    private static Dictionary<string, string> LoadSystemMap(string pluginRoot)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["arcade"] = "75", ["mame"] = "75", ["fbneo"] = "75", ["hbmame"] = "75", ["neogeo"] = "142",
            ["nes"] = "3", ["snes"] = "4", ["n64"] = "14", ["gb"] = "9", ["gbc"] = "10", ["gba"] = "12",
            ["megadrive"] = "1", ["mastersystem"] = "2", ["gamegear"] = "21", ["saturn"] = "22", ["dreamcast"] = "23",
            ["psx"] = "57", ["ps2"] = "58", ["psp"] = "61", ["pcengine"] = "31", ["atari2600"] = "26", ["amiga"] = "64"
        };
        try
        {
            var path = Path.Combine(pluginRoot, "..", "APIExpose", "resources", "scraping", "reference", "systems_screenscraper.json");
            if (File.Exists(path))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (raw != null)
                {
                    foreach (var (key, value) in raw)
                    {
                        var normalized = key.Trim().Replace(' ', '_').ToLowerInvariant();
                        if (normalized.Length > 0 && value is { Length: > 0 }) map[normalized] = value.Trim();
                    }
                }
            }
        }
        catch
        {
            // built-in map remains
        }
        return map;
    }
}
