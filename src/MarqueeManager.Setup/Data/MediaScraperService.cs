using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace MarqueeManager.Setup.Data;

/// <summary>A media found online, ready to preview and download.</summary>
public sealed record ScrapeResult(string Source, string Kind, string ThumbUrl, string DownloadUrl);

/// <summary>
/// Online media fetcher for the composer. Sources (all optional, jujuvincebros
/// explicitly excluded by project rule):
///  - Arcade Database (adb.arcadeitalia.net) — no key, MAME sets: marquee, flyer,
///    cabinet, title (mirrors the reference artwork packs). Checked by default.
///  - SteamGridDB — API key: clean logos, grids, heroes.
///  - TheGamesDB — API key: fanarts, banners, clear logos.
///  ScreenScraper is intentionally NOT here: its API needs per-software DEV
///  credentials, and APIExpose already mirrors it locally.
/// Downloads land in media\marquees\downloads\&lt;sys&gt;\&lt;rom&gt;\ and are added as
/// composer layers.
/// </summary>
public sealed class MediaScraperService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MarqueeManagerSetup/2.3");
        return client;
    }

    private readonly string _pluginRoot;
    private readonly Func<string, string> _credential; // key of [Scraper] → value

    public MediaScraperService(string pluginRoot, Func<string, string> credential)
    {
        _pluginRoot = pluginRoot;
        _credential = credential;
    }

    public bool HasKey(string source) => source switch
    {
        "adb" => true,
        "steamgriddb" => _credential("SteamGridDbApiKey").Length > 0,
        "thegamesdb" => _credential("TheGamesDbApiKey").Length > 0,
        _ => false
    };

    public async Task<List<ScrapeResult>> SearchAsync(IReadOnlyList<string> sources,
        string system, string rom, string gameName)
    {
        var results = new List<ScrapeResult>();
        foreach (var source in sources)
        {
            try
            {
                switch (source)
                {
                    case "adb":
                        results.AddRange(await SearchAdbAsync(system, rom).ConfigureAwait(false));
                        break;
                    case "steamgriddb":
                        results.AddRange(await SearchSteamGridDbAsync(gameName).ConfigureAwait(false));
                        break;
                    case "thegamesdb":
                        results.AddRange(await SearchTheGamesDbAsync(gameName).ConfigureAwait(false));
                        break;
                }
            }
            catch
            {
                // a dead source never blocks the others
            }
        }
        return results;
    }

    public async Task<string> DownloadAsync(ScrapeResult result, string system, string rom)
    {
        var folder = Path.Combine(_pluginRoot, "media", "marquees", "downloads", Safe(system), Safe(rom));
        Directory.CreateDirectory(folder);
        var extension = Path.GetExtension(new Uri(result.DownloadUrl).AbsolutePath);
        if (extension.Length is 0 or > 5) extension = ".png";
        var path = Path.Combine(folder, $"{result.Source}-{Safe(result.Kind)}-{DateTime.Now:HHmmss}{extension}");
        var bytes = await Http.GetByteArrayAsync(result.DownloadUrl).ConfigureAwait(false);
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        return path;
    }

    // ================= Arcade Database (no key, MAME sets) =================

    private static async Task<List<ScrapeResult>> SearchAdbAsync(string system, string rom)
    {
        var results = new List<ScrapeResult>();
        if (system is not ("arcade" or "mame" or "hbmame")) return results;
        foreach (var kind in new[] { "marquees", "flyers", "cabinets", "titles" })
        {
            var url = $"https://adb.arcadeitalia.net/media/mame.current/{kind}/{Uri.EscapeDataString(rom)}.png";
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                results.Add(new ScrapeResult("adb", kind.TrimEnd('s'), url, url));
            }
        }
        return results;
    }

    // ================= SteamGridDB =================

    private async Task<List<ScrapeResult>> SearchSteamGridDbAsync(string gameName)
    {
        var results = new List<ScrapeResult>();
        var key = _credential("SteamGridDbApiKey");
        if (key.Length == 0) return results;

        async Task<JsonDocument?> GetAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", "Bearer " + key);
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        using var search = await GetAsync(
            $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(gameName)}").ConfigureAwait(false);
        var id = search?.RootElement.TryGetProperty("data", out var data) == true && data.GetArrayLength() > 0
            ? data[0].GetProperty("id").GetInt32()
            : (int?)null;
        if (id == null) return results;

        foreach (var (endpoint, kind) in new[] { ("logos", "logo"), ("grids", "grid"), ("heroes", "hero") })
        {
            using var doc = await GetAsync($"https://www.steamgriddb.com/api/v2/{endpoint}/game/{id}").ConfigureAwait(false);
            if (doc?.RootElement.TryGetProperty("data", out var items) != true) continue;
            foreach (var item in items.EnumerateArray().Take(6))
            {
                var url = item.GetProperty("url").GetString();
                var thumb = item.TryGetProperty("thumb", out var t) ? t.GetString() : url;
                if (url != null)
                {
                    results.Add(new ScrapeResult("steamgriddb", kind, thumb ?? url, url));
                }
            }
        }
        return results;
    }

    // ================= TheGamesDB =================

    private async Task<List<ScrapeResult>> SearchTheGamesDbAsync(string gameName)
    {
        var results = new List<ScrapeResult>();
        var key = _credential("TheGamesDbApiKey");
        if (key.Length == 0) return results;

        var searchJson = await Http.GetStringAsync(
            $"https://api.thegamesdb.net/v1.1/Games/ByGameName?apikey={Uri.EscapeDataString(key)}&name={Uri.EscapeDataString(gameName)}").ConfigureAwait(false);
        using var search = JsonDocument.Parse(searchJson);
        var id = search.RootElement.TryGetProperty("data", out var data)
                 && data.TryGetProperty("games", out var games) && games.GetArrayLength() > 0
            ? games[0].GetProperty("id").GetInt32()
            : (int?)null;
        if (id == null) return results;

        var imagesJson = await Http.GetStringAsync(
            $"https://api.thegamesdb.net/v1/Games/Images?apikey={Uri.EscapeDataString(key)}&games_id={id}").ConfigureAwait(false);
        using var images = JsonDocument.Parse(imagesJson);
        var root = images.RootElement;
        var baseUrl = root.TryGetProperty("data", out var imageData)
                      && imageData.TryGetProperty("base_url", out var baseUrls)
                      && baseUrls.TryGetProperty("original", out var original)
            ? original.GetString() ?? ""
            : "";
        var thumbBase = imageData.TryGetProperty("base_url", out var b2)
                        && b2.TryGetProperty("thumb", out var thumbEl)
            ? thumbEl.GetString() ?? baseUrl
            : baseUrl;
        if (baseUrl.Length == 0
            || !imageData.TryGetProperty("images", out var perGame)
            || !perGame.TryGetProperty(id.Value.ToString(), out var list)) return results;

        foreach (var image in list.EnumerateArray().Take(12))
        {
            var type = image.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            if (type is not ("fanart" or "banner" or "clearlogo")) continue;
            var filename = image.TryGetProperty("filename", out var f) ? f.GetString() : null;
            if (filename != null)
            {
                results.Add(new ScrapeResult("thegamesdb", type, thumbBase + filename, baseUrl + filename));
            }
        }
        return results;
    }

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
