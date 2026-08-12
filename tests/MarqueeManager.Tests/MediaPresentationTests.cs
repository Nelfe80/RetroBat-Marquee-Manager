using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Policy;
using MarqueeManager.Compositions.Core.Presentation;
using MarqueeManager.Compositions.Core.Resolution;
using Xunit;

namespace MarqueeManager.Tests;

/// <summary>Media-presentation document parsing/serialization and the real policy
/// provider (defaults → surface delta → target deltas), spec §20.1.</summary>
public sealed class MediaPresentationTests
{
    // Mirrors the §20.1 example: a surface base plus a system and a game target delta.
    private const string ExampleJson = """
    {
      "schema": "marqueemanager.media-presentation.v1",
      "generatedBy": "MarqueeManagerSetup",
      "surfaces": {
        "marquee-2": {
          "system": {
            "templateId": "system-default",
            "autoGenerate": false,
            "sources": {
              "scraped": { "enabled": true, "fit": { "mode": "dynamic", "maxCrop": 0.30, "alignX": "auto", "alignY": "auto", "fallback": "contain" } },
              "logo": { "enabled": true, "logoLayout": { "paddingX": 0.06, "paddingY": 0.08, "minimumPadding": 0.03, "background": { "kind": "scope-neutral" } } }
            },
            "neutralBackground": { "kind": "solid", "color": "#000000" }
          },
          "game": { "sources": { "systemFallback": { "enabled": true } } }
        }
      },
      "targetPolicies": [
        { "scope": "system", "surfaceId": "marquee-2", "frontendSystem": "megadrive",
          "sources": { "generated": { "enabled": false }, "scraped": { "fit": { "mode": "fill-height", "alignX": "center" } } } },
        { "scope": "game", "surfaceId": "marquee-2", "frontendSystem": "megadrive", "canonicalSystem": "megadrive",
          "gameId": "path:9bf7e0c4", "rom": "sonic",
          "sources": { "generated": { "enabled": false }, "scraped": { "fit": { "mode": "dynamic", "maxCrop": 0.18, "alignX": "auto", "alignY": "auto" } } } }
      ]
    }
    """;

    private static ResolutionContext SystemCtx(string system) =>
        new("marquee-2", "marquee", 1920, 360, MediaScope.System, system, system, null, null, "navigation");
    private static ResolutionContext GameCtx() =>
        new("marquee-2", "marquee", 1920, 360, MediaScope.Game, "megadrive", "megadrive", "path:9bf7e0c4", "sonic", "navigation");

    [Fact]
    public void Parse_ReadsSurfaceAndTargetDeltas()
    {
        var doc = MediaPresentationSerializer.TryParse(ExampleJson);

        Assert.NotNull(doc);
        Assert.NotNull(doc!.Surface("marquee-2")!.System);
        Assert.Equal(2, doc.TargetPolicies.Count);
        Assert.Contains(doc.TargetPolicies, t => t.Scope == MediaScope.Game && t.GameId == "path:9bf7e0c4");
    }

    [Fact]
    public void Provider_SystemTargetDelta_DisablesGeneratedForThatSystemOnly()
    {
        var provider = new MediaPresentationPolicyProvider(MediaPresentationSerializer.TryParse(ExampleJson));

        var megadrive = provider.PolicyFor(SystemCtx("megadrive"));
        Assert.False(megadrive.IsEnabled(SourceKind.Generated));           // target delta
        Assert.Equal(FitMode.FillHeight, megadrive.Source(SourceKind.Scraped)!.Fit!.Mode);
        Assert.Equal(0.30, megadrive.Source(SourceKind.Scraped)!.Fit!.MaxCrop, 4); // inherited from surface

        var other = provider.PolicyFor(SystemCtx("snes"));
        Assert.True(other.IsEnabled(SourceKind.Generated));                // untouched
        Assert.Equal(FitMode.Dynamic, other.Source(SourceKind.Scraped)!.Fit!.Mode);
    }

    [Fact]
    public void Provider_GameTargetDelta_AppliesToThatGame()
    {
        var provider = new MediaPresentationPolicyProvider(MediaPresentationSerializer.TryParse(ExampleJson));

        var game = provider.PolicyFor(GameCtx());

        Assert.False(game.IsEnabled(SourceKind.Generated));
        Assert.Equal(0.18, game.Source(SourceKind.Scraped)!.Fit!.MaxCrop, 4);
    }

    [Fact]
    public void Provider_NoDocument_YieldsPureDefaults()
    {
        var provider = new MediaPresentationPolicyProvider();

        var policy = provider.PolicyFor(SystemCtx("megadrive"));

        Assert.True(policy.IsEnabled(SourceKind.Generated));
        Assert.Equal(FitMode.Dynamic, policy.Source(SourceKind.Scraped)!.Fit!.Mode);
    }

    [Fact]
    public void RoundTrip_PreservesTargetDeltas()
    {
        var doc = MediaPresentationSerializer.TryParse(ExampleJson);
        var reparsed = MediaPresentationSerializer.TryParse(MediaPresentationSerializer.Serialize(doc!));
        var provider = new MediaPresentationPolicyProvider(reparsed);

        var game = provider.PolicyFor(GameCtx());
        Assert.False(game.IsEnabled(SourceKind.Generated));
        Assert.Equal(0.18, game.Source(SourceKind.Scraped)!.Fit!.MaxCrop, 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ \"schema\": \"wrong\" }")]
    [InlineData("not json")]
    public void TryParse_InvalidOrWrongSchema_ReturnsNull(string json)
        => Assert.Null(MediaPresentationSerializer.TryParse(json));

    private static ResolutionContext GameContext(string system, string rom, string gameId)
        => new("marquee-2", "marquee", 1920, 720, MediaScope.Game,
            FrontendSystem: system, CanonicalSystem: system, StableGameId: gameId, Rom: rom,
            DisplayState: "navigation");

    [Fact]
    public void GameEntryWithoutAGamePinnedSpeaksForEverySystemGame()
    {
        var wide = new TargetPolicy(MediaScope.Game, "marquee-2", "lynx", "lynx", null, null,
            new ScopePolicyDelta());

        Assert.True(wide.Matches(GameContext("lynx", "blue_lightning", "path:aa")));
        Assert.True(wide.Matches(GameContext("lynx", "chips_challenge", "path:bb")));
        Assert.False(wide.Matches(GameContext("megadrive", "sonic", "path:cc")));
    }

    [Fact]
    public void AGamesOwnChoiceOutranksTheSystemWideOne()
    {
        // the broad entry is listed AFTER the precise one on purpose: order in the
        // document must not decide who wins
        var document = new MediaPresentationDocument(
            new Dictionary<string, SurfaceScopeDeltas>(StringComparer.OrdinalIgnoreCase),
            new[]
            {
                new TargetPolicy(MediaScope.Game, "marquee-2", "lynx", "lynx", "path:aa", "blue_lightning",
                    new ScopePolicyDelta(Sources: new Dictionary<SourceKind, SourceSettingsDelta>
                    {
                        [SourceKind.Scraped] = new(Enabled: true)
                    })),
                new TargetPolicy(MediaScope.Game, "marquee-2", "lynx", "lynx", null, null,
                    new ScopePolicyDelta(Sources: new Dictionary<SourceKind, SourceSettingsDelta>
                    {
                        [SourceKind.Scraped] = new(Enabled: false)
                    }))
            });

        var provider = new MediaPresentationPolicyProvider(document);
        var pinned = provider.PolicyFor(GameContext("lynx", "blue_lightning", "path:aa"));
        var other = provider.PolicyFor(GameContext("lynx", "chips_challenge", "path:bb"));

        Assert.True(pinned.Sources[SourceKind.Scraped].Enabled);
        Assert.False(other.Sources[SourceKind.Scraped].Enabled);
    }
}
