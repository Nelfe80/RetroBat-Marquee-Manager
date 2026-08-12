namespace MarqueeManager.Compositions.Core.Composition;

/// <summary>
/// The visual assets a composition layer can be bound to, and the shape of the paths
/// they are stored under. A gabarit layer carries an
/// <see cref="MarqueeLayer.AssetKey"/>; rendering it for a given game or system means
/// resolving that key against THAT entry's media.
///
/// This table describes stored paths so they can be INTERPRETED — it is never used to
/// go looking through APIExpose's folders. APIExpose is the single source of media and
/// serves it over its streams; MarqueeManager resolves nothing on its own.
/// </summary>
public static class GabaritAssets
{
    /// <summary>Key → the tail a GAME asset's path ends with.</summary>
    public static readonly IReadOnlyList<(string Key, string[] Relatives)> Table = new[]
    {
        ("fanart", new[] { @"artwork\fanart.jpg", @"artwork\fanart.png" }),
        ("mix", new[] { @"artwork\mix\mixrbv2.png", @"artwork\mix\mixrbv1.png" }),
        ("wheel", new[] { @"ui\wheels\wheel.png" }),
        ("marquee", new[] { @"artwork\marquee\marquee.png", @"artwork\marquee\marquee.jpg" }),
        ("screenmarquee", new[] { @"artwork\marquee\screenmarquee.png" }),
        ("generated", new[] { @"artwork\marquee\generated-marquee.png" }),
        ("generateddmd", new[] { @"artwork\marquee\generated-dmd.png" }),
        ("flyer", new[] { @"artwork\flyer.jpg", @"artwork\flyer.png" }),
        ("screentitle", new[] { @"artwork\screentitle.png" }),
        ("screenshot", new[] { @"artwork\screenshot.png" }),
        ("box3d", new[] { @"artwork\box\3d.png" }),
        ("boxfront", new[] { @"artwork\box\front.png" }),
        ("bezel", new[] { @"artwork\bezels\bezel.png" })
    };

    /// <summary>Key → the tail a SYSTEM asset's path ends with.</summary>
    public static readonly IReadOnlyList<(string Key, string[] Relatives)> SystemTable = new[]
    {
        ("fanart", new[] { @"artwork\fanart.jpg", @"artwork\fanart.png" }),
        ("wheel", new[] { @"ui\wheels\wheel.png" }),
        ("marquee", new[] { @"artwork\marquee\generated-system-marquee.png" }),
        ("dmd", new[] { @"artwork\marquee\generated-system-dmd.png" })
    };

    /// <summary>
    /// Infers the asset key a stored path stands for, by matching its tail against the
    /// tables. A gabarit BACKGROUND carries no AssetKey — only the concrete path picked
    /// while composing — so without this a general template stays soldered to the media
    /// of the entry it was previewed on (the Jaguar template wearing the Mega Drive
    /// fanart). Null when the path is a genuine one-off, e.g. a downloaded image.
    /// </summary>
    public static string? KeyFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var normalized = path!.Replace('/', '\\');
        foreach (var (key, relatives) in Table.Concat(SystemTable))
            foreach (var relative in relatives)
                if (normalized.EndsWith(relative, StringComparison.OrdinalIgnoreCase))
                    return key;
        return null;
    }
}
