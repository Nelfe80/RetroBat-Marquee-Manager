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
        ("boxback", new[] { @"artwork\box\back.png" }),
        ("boxside", new[] { @"artwork\box\side.png" }),
        ("boxtexture", new[] { @"artwork\box\texture.png" }),
        ("cartridge", new[] { @"artwork\cartridge.png", @"artwork\cartridge.jpg" }),
        ("label", new[] { @"artwork\label.png", @"artwork\label.jpg" }),
        ("figurine", new[] { @"artwork\figurine.png", @"artwork\figurine.jpg" }),
        ("map", new[] { @"documents\maps\map.png", @"documents\maps\map.jpg" }),
        ("wheelcarbon", new[] { @"ui\wheels\wheel-carbon.png" }),
        ("wheelsteel", new[] { @"ui\wheels\wheel-steel.png" }),
        ("steamgrid", new[] { @"ui\steamgrid.png" }),
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
    /// Every media type a composition can bind to, in palette order. This is the
    /// AUTHORITY on what is composable — the palette is built from it, never from what
    /// one sample game happens to own, because a template is generic: opening it on a
    /// media-poor game (005, first of the arcade list) used to offer four buttons for a
    /// whole system. A type with no file behind it is placed as a coloured placeholder.
    ///
    /// Scope: "game" = resolved against the entry, "system" = against its system,
    /// "both" = available on either level. Aspect is the placeholder's shape only; a
    /// real medium always uses its own.
    /// </summary>
    public static readonly IReadOnlyList<PaletteEntry> Palette = new PaletteEntry[]
    {
        new("fanart",         "both",   "Fanart",             "Fanart",             "#3D5A80", 16.0 / 9),
        new("mix",            "game",   "Mix (mixrbv2)",      "Mix (mixrbv2)",      "#4C6E91", 4.0 / 3),
        new("wheel",          "game",   "Logo du jeu",        "Game logo",          "#E0A458", 3.4),
        new("marquee",        "game",   "Marquee",            "Marquee",            "#8E5572", 4.0),
        new("screenmarquee",  "game",   "Screen marquee",     "Screen marquee",     "#A3688A", 4.0),
        new("generated",      "game",   "Marquee généré",     "Generated marquee",  "#6D8B74", 4.0),
        new("generateddmd",   "game",   "DMD généré",         "Generated DMD",      "#5C7A63", 4.0),
        new("flyer",          "game",   "Flyer",              "Flyer",              "#B56576", 0.7),
        new("screentitle",    "game",   "Écran-titre",        "Title screen",       "#6A7FA8", 4.0 / 3),
        new("screenshot",     "game",   "Capture d'écran",    "Screenshot",         "#7B8FB5", 4.0 / 3),
        new("box3d",          "game",   "Boîte 3D",           "3D box",             "#9C6644", 0.75),
        new("boxfront",       "game",   "Jaquette",           "Box front",          "#B08968", 0.72),
        new("boxback",        "game",   "Dos de boîte",       "Box back",           "#A9765A", 0.72),
        new("boxside",        "game",   "Tranche de boîte",   "Box side",           "#8A6244", 0.72),
        new("boxtexture",     "game",   "Texture de boîte",   "Box texture",        "#9C7A5A", 2.0),
        new("cartridge",      "game",   "Cartouche",          "Cartridge",          "#7A9E7E", 1.4),
        new("label",          "game",   "Étiquette",          "Label",              "#C08497", 1.0),
        new("figurine",       "game",   "Figurine",           "Figurine",           "#8367C7", 0.6),
        new("map",            "game",   "Carte",              "Map",                "#4FA3A0", 4.0 / 3),
        new("wheelcarbon",    "game",   "Logo carbone",       "Carbon logo",        "#5E6068", 3.4),
        new("wheelsteel",     "game",   "Logo acier",         "Steel logo",         "#9AA7B3", 3.4),
        new("steamgrid",      "game",   "SteamGrid",          "SteamGrid",          "#4C7EA8", 2.0 / 3),
        new("bezel",          "game",   "Bezel",              "Bezel",              "#4A4E69", 16.0 / 9),
        new("video",          "game",   "Vidéo",              "Video",              "#5F4B8B", 4.0 / 3, false),
        new("systemfanart",   "system", "Fanart du système",  "System fanart",      "#33658A", 16.0 / 9),
        new("systemwheel",    "system", "Logo du système",    "System logo",        "#F6AE2D", 3.4),
        new("systemmarquee",  "system", "Marquee du système", "System marquee",     "#86BBD8", 4.0)
    };

    /// <summary>A composable media type: what the palette offers and what a placeholder
    /// stands for until the entry's real medium is resolved.</summary>
    /// <param name="Served">
    /// Whether the RUNTIME can put this type on a surface. MarqueeManager reads the
    /// APIExpose streams, never the folders, so a type the streams do not carry can be
    /// composed and previewed in the Setup — where the disk is right there — and then
    /// draw NOTHING in front of the cabinet. Saying so in the palette is the whole
    /// point: a layer that will never appear must not look like one that will.
    ///
    /// The streams now publish a canonical asset table per surface, so box art, mix,
    /// flyer, title screen, capture and bezel all travel. Video remains marked: the
    /// stream carries it, but a template bakes to a still image and cannot move.
    /// </param>
    public sealed record PaletteEntry(string Key, string Scope, string Fr, string En, string Color, double Aspect,
        bool Served = true);

    /// <summary>True when the key is a resolvable media type — as opposed to a one-off
    /// (an import, a download, a gradient) that carries its own file and must never be
    /// swapped out for another entry's media.</summary>
    public static bool IsResolvable(string? key) =>
        key is { Length: > 0 } && Palette.Any(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

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
