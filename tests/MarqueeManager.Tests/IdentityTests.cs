using MarqueeManager.Compositions.Core.Resolution;
using Xunit;

namespace MarqueeManager.Tests;

/// <summary>Identity rules from spec §19 — the fallback StableGameId and the
/// frontend/canonical distinction.</summary>
public sealed class IdentityTests
{
    [Fact]
    public void FromRomPath_IsDeterministicAndPrefixed()
    {
        var a = StableGameIds.FromRomPath("megadrive/sonic.zip");
        var b = StableGameIds.FromRomPath("megadrive/sonic.zip");

        Assert.Equal(a, b);
        Assert.StartsWith("path:", a);
        Assert.Equal(5 + 8, a.Length); // "path:" + 8 hex chars
    }

    // §19: the extension is part of the identity, so archives never collide.
    [Fact]
    public void FromRomPath_DifferentExtensions_DoNotCollide()
    {
        Assert.NotEqual(
            StableGameIds.FromRomPath("megadrive/sonic.zip"),
            StableGameIds.FromRomPath("megadrive/sonic.7z"));
    }

    [Fact]
    public void FromRomPath_NormalizesSeparatorsCaseAndLeadingSlash()
    {
        var reference = StableGameIds.FromRomPath("megadrive/sonic.zip");

        Assert.Equal(reference, StableGameIds.FromRomPath("megadrive\\sonic.zip"));
        Assert.Equal(reference, StableGameIds.FromRomPath("/megadrive/sonic.zip"));
        Assert.Equal(reference, StableGameIds.FromRomPath("MegaDrive/Sonic.ZIP"));
        Assert.Equal(reference, StableGameIds.FromRomPath("  megadrive/sonic.zip  "));
    }

    [Fact]
    public void Resolve_PrefersApiExposeIdWhenPresent()
    {
        Assert.Equal("ss-12345", StableGameIds.Resolve("ss-12345", "megadrive/sonic.zip"));
        Assert.StartsWith("path:", StableGameIds.Resolve(null, "megadrive/sonic.zip"));
        Assert.StartsWith("path:", StableGameIds.Resolve("   ", "megadrive/sonic.zip"));
    }

    [Fact]
    public void FromRomPath_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => StableGameIds.FromRomPath(""));
        Assert.Throws<ArgumentException>(() => StableGameIds.FromRomPath("   "));
    }

    // §19: MAME and FBNeo are distinct frontend systems; the system key never
    // silently uses the canonical one when the frontend is present.
    [Fact]
    public void SystemKey_UsesFrontendWhenPresent()
    {
        var mame = Context("mame", "arcade");
        var fbneo = Context("fbneo", "arcade");

        Assert.Equal("mame", mame.SystemKey);
        Assert.Equal("fbneo", fbneo.SystemKey);
        Assert.NotEqual(mame.SystemKey, fbneo.SystemKey);
        Assert.True(mame.HasFrontendSystem);
    }

    [Fact]
    public void SystemKey_FallsBackToCanonicalWhenFrontendMissing()
    {
        var ctx = Context(null, "arcade");

        Assert.Equal("arcade", ctx.SystemKey);
        Assert.False(ctx.HasFrontendSystem);
    }

    private static ResolutionContext Context(string? frontend, string? canonical)
        => new("marquee-2", "marquee", 1920, 360, MediaScope.System,
               frontend, canonical, null, null, "navigation");
}
