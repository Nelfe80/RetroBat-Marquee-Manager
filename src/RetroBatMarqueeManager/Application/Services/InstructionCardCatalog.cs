namespace RetroBatMarqueeManager.Application.Services;

/// <summary>
/// Pure logic of the instruction card catalog: turns what APIExpose publishes into
/// logical cards, grouped by ROLE.
///
/// A role is the folder the card sits in (`artwork\ic\cody\ic-1.png`), and it IS what
/// the card is about: a character, a topic (`items-and-weaponry`), a stage. Cards at
/// the root of `artwork\ic` carry the empty role — the game's general card.
///
/// Naming inside a role: `ic.png` or `ic-N[-variant].png` — e.g. mercs ships
/// ic-1-left … ic-5-right. Files sharing the same N are ONE logical card in two panel
/// positions: left (player 1 side) and right (player 2 side).
///
/// Kept free of dependencies so it stays trivially testable.
/// </summary>
public static class InstructionCardCatalog
{
    /// <summary>Where one entry sits INSIDE a card, as fractions of the drawing. Named
    /// entries are the ones a companion file could put a name on — a character, a
    /// weapon — and they are what an event can point at.</summary>
    public sealed record CardPanel(string Role, string Kind, bool Named, string? Label,
        double X, double Y, double W, double H);

    /// <summary>A card as the stream describes it: the file, the role it belongs to,
    /// and what it holds.</summary>
    public sealed record CardSource(string Path, string Role, IReadOnlyList<CardPanel> Panels)
    {
        /// <summary>A bare path, no role: what the older flat list amounted to.</summary>
        public static CardSource Of(string path) => new(path, string.Empty, Array.Empty<CardPanel>());
    }

    public sealed record CardVariant(string Path, string Variant, IReadOnlyList<CardPanel> Panels);

    public sealed record CardGroup(int Number, string Role, List<CardVariant> Variants)
    {
        /// <summary>Preferred side first, then the side-less file, then left, then anything.</summary>
        public string PathFor(string? preference)
        {
            return (Find(preference) ?? Find("") ?? Find("left") ?? Variants[0]).Path;

            CardVariant? Find(string? variant) => variant is null
                ? null
                : Variants.FirstOrDefault(v => v.Variant.Equals(variant, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Groups the raw file list into logical cards, keeping APIExpose's order.</summary>
    public static List<CardGroup> BuildGroups(IReadOnlyList<string> cards)
        => BuildGroups(cards.Select(CardSource.Of).ToList());

    /// <summary>
    /// Groups the published cards into logical cards. Two cards belong to the same
    /// group when they share a role AND a number: `cody\ic-1` and `haggar\ic-1` are two
    /// different cards, and the role is what tells them apart — before roles existed,
    /// the number alone merged them into one.
    /// </summary>
    public static List<CardGroup> BuildGroups(IReadOnlyList<CardSource> cards)
    {
        var groups = new List<CardGroup>();
        foreach (var card in cards)
        {
            var role = card.Role ?? string.Empty;
            var (number, variant) = ParseStem(System.IO.Path.GetFileNameWithoutExtension(card.Path));
            var group = groups.FirstOrDefault(g => g.Number == number
                                                   && g.Role.Equals(role, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                groups.Add(group = new CardGroup(number, role, new List<CardVariant>()));
            }

            group.Variants.Add(new CardVariant(card.Path, variant, card.Panels ?? Array.Empty<CardPanel>()));
        }

        return groups;
    }

    /// <summary>The roles present in the catalog, in the order the stream sent them.
    /// The empty role — the cards at the root — is not one: it has no name.</summary>
    public static List<string> Roles(IReadOnlyList<CardGroup> groups)
    {
        var roles = new List<string>();
        foreach (var group in groups)
        {
            if (group.Role.Length == 0) continue;
            if (!roles.Any(r => r.Equals(group.Role, StringComparison.OrdinalIgnoreCase))) roles.Add(group.Role);
        }

        return roles;
    }

    /// <summary>
    /// The cards a viewer set to this role must cycle through. No role = the whole
    /// catalog, which is the honest answer when nothing says which character is being
    /// played: the viewer shows everything the game has rather than nothing.
    /// </summary>
    public static List<CardGroup> ForRole(IReadOnlyList<CardGroup> groups, string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return groups.ToList();
        var wanted = groups.Where(g => g.Role.Equals(role, StringComparison.OrdinalIgnoreCase)).ToList();
        // A role nobody has cards for shows nothing — never someone else's card.
        return wanted;
    }

    /// <summary>The role whose name matches what an event announced ("Cody" → `cody`),
    /// or null when the game ships no card for it.</summary>
    public static string? MatchRole(IReadOnlyList<CardGroup> groups, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var slug = Slug(name);
        if (slug.Length == 0) return null;
        foreach (var role in Roles(groups))
        {
            if (Slug(role).Equals(slug, StringComparison.OrdinalIgnoreCase)) return role;
        }

        return null;
    }

    /// <summary>"Fire Water" → "fire-water": the nomenclature of the folders, so a name
    /// coming from a .MEM entry can be compared to a role.</summary>
    public static string Slug(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        var pendingSeparator = false;
        foreach (var raw in value.Trim().ToLowerInvariant())
        {
            var c = Deaccent(raw);
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSeparator && builder.Length > 0) builder.Append('-');
                pendingSeparator = false;
                builder.Append(c);
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.ToString();
    }

    private static char Deaccent(char c) => c switch
    {
        'á' or 'à' or 'â' or 'ä' or 'ã' or 'å' => 'a',
        'é' or 'è' or 'ê' or 'ë' => 'e',
        'í' or 'ì' or 'î' or 'ï' => 'i',
        'ó' or 'ò' or 'ô' or 'ö' or 'õ' => 'o',
        'ú' or 'ù' or 'û' or 'ü' => 'u',
        'ç' => 'c',
        'ñ' => 'n',
        _ => c
    };

    /// <summary>"ic" → (1, ""); "ic-3" → (3, ""); "ic-3-left" → (3, "left").</summary>
    public static (int Number, string Variant) ParseStem(string stem)
    {
        if (!stem.StartsWith("ic", StringComparison.OrdinalIgnoreCase))
        {
            return (1, stem.ToLowerInvariant());
        }

        var rest = stem[2..].TrimStart('-', '_');
        if (rest.Length == 0)
        {
            return (1, "");
        }

        var parts = rest.Split('-', '_');
        if (int.TryParse(parts[0], out var number) && number >= 1)
        {
            return (number, string.Join("-", parts.Skip(1)).ToLowerInvariant());
        }

        return (1, rest.ToLowerInvariant());
    }

    /// <summary>
    /// The name that binds a touch zone to a viewer. Explicit when the user named it,
    /// otherwise derived so that the common cases need no naming at all: a viewer set to
    /// player 2 answers on `p2`, a viewer set to a role answers on that role, and a lone
    /// viewer answers on `main` — which is also what an unconfigured zone targets.
    /// </summary>
    public static string ChannelOf(string? channel, string? role, string? player)
    {
        if (!string.IsNullOrWhiteSpace(channel)) return channel.Trim().ToLowerInvariant();
        if (int.TryParse(player, out var number) && number >= 1) return "p" + number;
        if (!string.IsNullOrWhiteSpace(role)) return Slug(role);
        return "main";
    }
}
