using System.Xml.Linq;

namespace RetroBatMarqueeManager.Application.Lighting;

/// <summary>
/// Normalizes the scraped genre metadata into stable slugs (shmup, beatemup,
/// racing…) used by the genre-scoped ingame effect rules. The table lives in
/// resources/lighting/genres.map.xml — per project doctrine no family truth is
/// hard-coded: ScreenScraper numeric ids (language independent, preferred) and
/// label substrings (FR/EN fallback) both live in the data file.
/// </summary>
public sealed class GenreMap
{
    private sealed record Entry(string Slug, HashSet<string> Ids, string[] LabelParts);

    private readonly List<Entry> _entries = new();

    public static GenreMap Load(string directory, ILogger logger)
    {
        var map = new GenreMap();
        var path = Path.Combine(directory, "genres.map.xml");
        try
        {
            if (File.Exists(path))
            {
                foreach (var element in XDocument.Load(path).Descendants("genre"))
                {
                    var slug = (string?)element.Attribute("slug");
                    if (string.IsNullOrWhiteSpace(slug)) continue;
                    var ids = ((string?)element.Attribute("ids") ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.Ordinal);
                    var labels = ((string?)element.Attribute("labels") ?? "")
                        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    map._entries.Add(new Entry(slug, ids, labels));
                }
                logger.LogInformation("Genre map loaded: {Count} slug(s) from {Path}", map._entries.Count, path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid genre map {Path}; genre-scoped effects disabled", path);
        }
        return map;
    }

    /// <summary>Slugs matching the scraped metadata, most specific data first
    /// (numeric ids), label substrings as fallback. File order is preserved.</summary>
    public IReadOnlyList<string> Resolve(string? genreLabels, string? genreIds)
    {
        if (_entries.Count == 0) return Array.Empty<string>();

        var ids = (genreIds ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var labels = (genreLabels ?? "").ToLowerInvariant();

        var slugs = new List<string>();
        foreach (var entry in _entries)
        {
            var matched = ids.Any(entry.Ids.Contains)
                          || entry.LabelParts.Any(part => labels.Contains(part, StringComparison.OrdinalIgnoreCase));
            if (matched && !slugs.Contains(entry.Slug))
            {
                slugs.Add(entry.Slug);
            }
        }
        return slugs;
    }
}
