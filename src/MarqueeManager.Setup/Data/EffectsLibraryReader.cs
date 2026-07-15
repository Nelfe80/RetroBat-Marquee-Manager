using System.IO;
using System.Xml.Linq;

namespace MarqueeManager.Setup.Data;

/// <summary>Where a resolved effect comes from — drives the provenance badge.</summary>
public enum EffectOrigin { None, Game, System, GenreOverride, GenreDefault, Default }

/// <summary>
/// Read-only mirror of the runtime's effect resolution, used to show the user
/// what a signal currently does and where that behavior comes from. Parses
/// resources\lighting\ingame.effects.xml (genre= aware) and genres.map.xml with
/// the same matching rules as the runtime's IngameEffectLibrary/GenreMap.
/// </summary>
public sealed class EffectsLibraryReader
{
    private sealed record XmlRule(string[]? Actions, string? FamilyPrefix, string[]? Genres, EffectRule Effect);
    private sealed record GenreEntry(string Slug, HashSet<string> Ids, string[] LabelParts);

    private readonly List<XmlRule> _rules = new();
    private readonly List<GenreEntry> _genres = new();

    public EffectsLibraryReader(string pluginRoot)
    {
        var lighting = Path.Combine(pluginRoot, "resources", "lighting");
        try
        {
            var library = Path.Combine(lighting, "ingame.effects.xml");
            if (File.Exists(library))
            {
                foreach (var element in XDocument.Load(library).Descendants("effect"))
                {
                    var actionsRaw = (string?)element.Attribute("action");
                    var family = (string?)element.Attribute("family");
                    if (actionsRaw == null && family == null) continue;
                    _rules.Add(new XmlRule(
                        actionsRaw?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        family,
                        ((string?)element.Attribute("genre"))?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        new EffectRule
                        {
                            Kind = ((string?)element.Attribute("kind"))?.ToLowerInvariant() ?? "flash",
                            Color = (string?)element.Attribute("color") ?? "#ff2a18",
                            DurationMs = (int?)element.Attribute("durationMs") ?? 300,
                            Dip = (double?)element.Attribute("dip") ?? 0.0,
                            Sprite = (string?)element.Attribute("sprite"),
                            Count = (int?)element.Attribute("count") ?? 1,
                            Motion = ((string?)element.Attribute("motion") ?? "pop").ToLowerInvariant(),
                            ThrottleMs = (int?)element.Attribute("throttleMs") ?? 400
                        }));
                }
            }

            var map = Path.Combine(lighting, "genres.map.xml");
            if (File.Exists(map))
            {
                foreach (var element in XDocument.Load(map).Descendants("genre"))
                {
                    var slug = (string?)element.Attribute("slug");
                    if (string.IsNullOrWhiteSpace(slug)) continue;
                    _genres.Add(new GenreEntry(slug,
                        ((string?)element.Attribute("ids") ?? "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .ToHashSet(StringComparer.Ordinal),
                        ((string?)element.Attribute("labels") ?? "")
                            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                }
            }
        }
        catch
        {
            // unreadable data files: defaults section stays empty, the editor still works
        }
    }

    /// <summary>Genre slugs of a game, same precedence as the runtime.</summary>
    public IReadOnlyList<string> ResolveGenreSlugs(string? genreLabels, string? genreIds)
    {
        var ids = (genreIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var labels = (genreLabels ?? "").ToLowerInvariant();
        var slugs = new List<string>();
        foreach (var entry in _genres)
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

    /// <summary>The library default for an action, genre-scoped rules first.</summary>
    public (EffectRule Rule, bool GenreScoped)? FindDefault(string action, string family, IReadOnlyList<string> slugs)
    {
        foreach (var scoped in new[] { true, false })
        {
            foreach (var rule in _rules)
            {
                if (scoped != rule.Genres is { Length: > 0 }) continue;
                if (scoped && !rule.Genres!.Any(g => slugs.Contains(g, StringComparer.OrdinalIgnoreCase))) continue;

                var matches = rule.Actions != null
                    ? rule.Actions.Any(a => action.Contains(a, StringComparison.OrdinalIgnoreCase))
                    : rule.FamilyPrefix != null && family.StartsWith(rule.FamilyPrefix, StringComparison.OrdinalIgnoreCase);
                if (matches) return (rule.Effect, scoped);
            }
        }
        return null;
    }
}
