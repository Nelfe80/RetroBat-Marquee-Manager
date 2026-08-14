using RetroBatMarqueeManager.Application.Services;
using Xunit;

namespace MarqueeManager.Tests;

/// <summary>
/// The catalog of instruction cards: how the published files become logical cards, and
/// how a role — the folder a card sits in — decides what a viewer walks through.
/// </summary>
public sealed class InstructionCardCatalogTests
{
    private static InstructionCardCatalog.CardSource Card(string path, string role = "")
        => new(path, role, Array.Empty<InstructionCardCatalog.CardPanel>());

    [Fact]
    public void Same_number_in_two_roles_stays_two_cards()
    {
        // Before roles existed, grouping was by number alone: Cody's first page and
        // Haggar's first page merged into one card with two "variants", and a viewer
        // showed whichever won the side lookup.
        var groups = InstructionCardCatalog.BuildGroups(new[]
        {
            Card(@"C:\media\ic\cody\ic-1.png", "cody"),
            Card(@"C:\media\ic\cody\ic-2.png", "cody"),
            Card(@"C:\media\ic\haggar\ic-1.png", "haggar")
        });

        Assert.Equal(3, groups.Count);
        Assert.Equal(new[] { "cody", "haggar" }, InstructionCardCatalog.Roles(groups));
    }

    [Fact]
    public void A_role_walks_only_its_own_pages()
    {
        var groups = InstructionCardCatalog.BuildGroups(new[]
        {
            Card(@"C:\media\ic\cody\ic-1.png", "cody"),
            Card(@"C:\media\ic\cody\ic-2.png", "cody"),
            Card(@"C:\media\ic\cody\ic-3.png", "cody"),
            Card(@"C:\media\ic\haggar\ic-1.png", "haggar")
        });

        var cody = InstructionCardCatalog.ForRole(groups, "cody");
        Assert.Equal(3, cody.Count);
        Assert.All(cody, group => Assert.Equal("cody", group.Role));
    }

    [Fact]
    public void No_role_walks_everything()
    {
        // What a viewer must do when nothing says who is playing: show the whole game
        // rather than nothing.
        var groups = InstructionCardCatalog.BuildGroups(new[]
        {
            Card(@"C:\media\ic\ic.png"),
            Card(@"C:\media\ic\cody\ic-1.png", "cody")
        });

        Assert.Equal(2, InstructionCardCatalog.ForRole(groups, null).Count);
        Assert.Equal(2, InstructionCardCatalog.ForRole(groups, "").Count);
    }

    [Fact]
    public void A_role_nobody_has_cards_for_shows_nothing()
    {
        var groups = InstructionCardCatalog.BuildGroups(new[] { Card(@"C:\media\ic\cody\ic-1.png", "cody") });

        Assert.Empty(InstructionCardCatalog.ForRole(groups, "guy"));
    }

    [Fact]
    public void An_announced_name_finds_its_role()
    {
        var groups = InstructionCardCatalog.BuildGroups(new[]
        {
            Card(@"C:\media\ic\fire-water\ic-1.png", "fire-water"),
            Card(@"C:\media\ic\cody\ic-1.png", "cody")
        });

        // the .MEM entry names the weapon as the game's own card writes it
        Assert.Equal("fire-water", InstructionCardCatalog.MatchRole(groups, "Fire Water"));
        Assert.Equal("cody", InstructionCardCatalog.MatchRole(groups, "CODY"));
        Assert.Null(InstructionCardCatalog.MatchRole(groups, "Haggar"));
        Assert.Null(InstructionCardCatalog.MatchRole(groups, "   "));
    }

    [Fact]
    public void Sides_of_one_page_stay_one_card()
    {
        // mercs: the same page drawn twice, once per panel position
        var groups = InstructionCardCatalog.BuildGroups(new[]
        {
            Card(@"C:\media\ic\ic-1-left.png"),
            Card(@"C:\media\ic\ic-1-right.png")
        });

        var group = Assert.Single(groups);
        Assert.Equal(@"C:\media\ic\ic-1-left.png", group.PathFor("left"));
        Assert.Equal(@"C:\media\ic\ic-1-right.png", group.PathFor("right"));
        // a player nobody drew a side for still gets a card
        Assert.Equal(@"C:\media\ic\ic-1-left.png", group.PathFor("middle"));
    }

    [Fact]
    public void An_announced_name_can_be_an_entry_inside_a_card()
    {
        // Ghouls'n Ghosts: one drawing holds every weapon, so the name does not point at a
        // folder — it points INSIDE the card, and only a frame can answer.
        var panels = new[]
        {
            new InstructionCardCatalog.CardPanel("controls", "panel", true, null, 0, 0.30, 1, 0.19),
            new InstructionCardCatalog.CardPanel("normal-armor", "panel", true, null, 0, 0.49, 1, 0.24),
            new InstructionCardCatalog.CardPanel("fire-water", "panel", true, "Fire Water", 0, 0.75, 1, 0.20)
        };
        var groups = InstructionCardCatalog.BuildGroups(new[]
        {
            Card(@"C:\media\ic\bonus-points\ic.png", "bonus-points"),
            new InstructionCardCatalog.CardSource(@"C:\media\ic\items-and-weaponry\ic.png", "items-and-weaponry", panels)
        });

        var hit = InstructionCardCatalog.FindPanel(groups, "Normal Armor");
        Assert.NotNull(hit);
        Assert.Equal(1, hit!.GroupIndex);
        Assert.Equal(0.49, hit.Panel.Y, 3);

        // the label names it too, when the folder name would not
        Assert.Equal("fire-water", InstructionCardCatalog.FindPanel(groups, "FIRE WATER")!.Panel.Role);
        Assert.Null(InstructionCardCatalog.FindPanel(groups, "Excalibur"));
    }

    [Theory]
    // explicit name wins, then the player, then the role, then the historical channel
    [InlineData("cartes", "cody", "1", "cartes")]
    [InlineData("", "cody", "2", "p2")]
    [InlineData("", "items-and-weaponry", "", "items-and-weaponry")]
    [InlineData("", "", "", "main")]
    [InlineData("", "", "0", "main")]
    public void Channel_is_derived_from_what_the_layer_already_says(string channel, string role, string player, string expected)
        => Assert.Equal(expected, InstructionCardCatalog.ChannelOf(channel, role, player));

    [Theory]
    [InlineData("Fire Water", "fire-water")]
    [InlineData("  Guy  ", "guy")]
    [InlineData("Items & Weaponry", "items-weaponry")]
    [InlineData("Perceval", "perceval")]
    public void Names_become_folder_names(string name, string expected)
        => Assert.Equal(expected, InstructionCardCatalog.Slug(name));
}
