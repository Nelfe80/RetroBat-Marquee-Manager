namespace RetroBatMarqueeManager.Application.Services;

/// <summary>
/// Pure logic of the instruction card catalog: groups the raw APIExpose file list
/// into logical cards. Naming convention (artwork\ic): `ic.png` or `ic-N[-variant].png`
/// — e.g. mercs ships ic-1-left … ic-5-right. Files sharing the same N are ONE
/// logical card in two panel positions: left (player 1 side) and right (player 2
/// side). Kept free of dependencies so it stays trivially testable.
/// </summary>
public static class InstructionCardCatalog
{
    public sealed record CardVariant(string Path, string Variant);

    public sealed record CardGroup(int Number, List<CardVariant> Variants)
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
    {
        var groups = new List<CardGroup>();
        foreach (var path in cards)
        {
            var (number, variant) = ParseStem(System.IO.Path.GetFileNameWithoutExtension(path));
            var group = groups.FirstOrDefault(g => g.Number == number);
            if (group == null)
            {
                groups.Add(group = new CardGroup(number, new List<CardVariant>()));
            }

            group.Variants.Add(new CardVariant(path, variant));
        }

        return groups;
    }

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
}
