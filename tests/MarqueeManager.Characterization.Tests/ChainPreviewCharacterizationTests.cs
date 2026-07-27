using System.IO;
using MarqueeManager.Setup.Data;
using Xunit;

namespace MarqueeManager.Characterization.Tests;

/// <summary>
/// Pins TODAY's marquee resolution (the current ChainPreview / CompositionChainResolver
/// behavior) against temp fixtures, so the rework (lot 3) can prove it either
/// preserves each case or diverges on PURPOSE. Default marquee chain today:
/// composition &gt; user &gt; marquee &gt; generated &gt; logo.
/// </summary>
public sealed class ChainPreviewCharacterizationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mm-char-" + Guid.NewGuid().ToString("N"));
    private readonly string _pluginRoot;
    private const string System = "megadrive";
    private const string Rom = "sonic";

    public ChainPreviewCharacterizationTests()
    {
        _pluginRoot = Path.Combine(_root, "MarqueeManager");
        // GameMediaCatalog reads <pluginRoot>\..\APIExpose\media\systems
        Directory.CreateDirectory(Path.Combine(_root, "APIExpose", "media", "systems", System, "games", Rom));
    }

    private ChainPreview.Result Resolve()
        => ChainPreview.Resolve(_pluginRoot, new GameMediaCatalog(_pluginRoot),
            new CompositionAssignments(_pluginRoot), "marquee", System, Rom);

    private void GameFile(string relative)
        => Write(Path.Combine(_root, "APIExpose", "media", "systems", System, "games", Rom, relative));

    private void Composition()
        => Write(Path.Combine(_pluginRoot, "media", "marquees", System, Rom + ".png"));

    private void UserFile()
        => Write(Path.Combine(_pluginRoot, "media", "marquees", "user", System, Rom + ".png"));

    private static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 0 }); // existence is all the chain checks
    }

    [Fact]
    public void AllPresent_CompositionWins()
    {
        Composition();
        UserFile();
        GameFile(@"artwork\marquee\marquee.png");
        GameFile(@"artwork\marquee\generated-marquee.png");
        GameFile(@"ui\wheels\wheel.png");

        Assert.Equal("composition", Resolve().Source);
    }

    [Fact]
    public void NoComposition_UserFolderWins()
    {
        UserFile();
        GameFile(@"artwork\marquee\marquee.png");

        Assert.Equal("user", Resolve().Source);
    }

    // Documents TODAY's order: the scraped marquee beats the generated one.
    // The rework INTENTIONALLY flips this (generated is tried first but only wins
    // when valid) — this test is the before-picture for the migration diff.
    [Fact]
    public void ScrapedMarquee_BeatsGenerated_Today()
    {
        GameFile(@"artwork\marquee\marquee.png");
        GameFile(@"artwork\marquee\generated-marquee.png");

        Assert.Equal("marquee", Resolve().Source);
    }

    [Fact]
    public void OnlyGenerated_GeneratedWins()
    {
        GameFile(@"artwork\marquee\generated-marquee.png");

        Assert.Equal("generated", Resolve().Source);
    }

    [Fact]
    public void OnlyLogo_LogoWins()
    {
        GameFile(@"ui\wheels\wheel.png");

        Assert.Equal("logo", Resolve().Source);
    }

    [Fact]
    public void MarqueeJpgFallback_Wins()
    {
        GameFile(@"artwork\marquee\marquee.jpg");

        Assert.Equal("marquee", Resolve().Source);
    }

    [Fact]
    public void NothingPresent_ResolvesToStreamDefault()
    {
        var result = Resolve();

        Assert.Null(result.Path);
        Assert.Equal("", result.Source);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
