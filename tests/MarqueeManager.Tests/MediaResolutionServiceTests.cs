using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Geometry;
using MarqueeManager.Compositions.Core.Policy;
using MarqueeManager.Compositions.Core.Resolution;
using Xunit;

namespace MarqueeManager.Tests;

/// <summary>Fixed-chain resolution (spec §6, matrix §28.1/§28.2) with in-memory
/// fakes for the injected discovery / generation / policy ports.</summary>
public sealed class MediaResolutionServiceTests
{
    private static readonly BackgroundSpec Bg = new(BackgroundKinds.Solid, "#000000");

    private static ScopePolicy Full(bool game, params SourceKind[] disabled)
    {
        var kinds = game
            ? new[] { SourceKind.Personal, SourceKind.UserDrop, SourceKind.Generated, SourceKind.Scraped, SourceKind.Logo, SourceKind.SystemFallback }
            : new[] { SourceKind.Personal, SourceKind.UserDrop, SourceKind.Generated, SourceKind.Scraped, SourceKind.Logo };
        var set = new HashSet<SourceKind>(disabled);
        var sources = kinds.ToDictionary(k => k, k => new SourceSettings(!set.Contains(k), new FitPolicy(FitMode.Contain)));
        return new ScopePolicy(null, false, sources, Bg);
    }

    private sealed class Policies : IPresentationPolicyProvider
    {
        public ScopePolicy System = Full(game: false);
        public ScopePolicy Game = Full(game: true);
        public ScopePolicy PolicyFor(ResolutionContext c) => c.Scope == MediaScope.System ? System : Game;
    }

    private sealed class Assets : IMediaAssetResolver
    {
        public Dictionary<(MediaScope, SourceKind), AssetLookup> Map = new();
        public AssetLookup Resolve(SourceKind kind, ResolutionContext c)
            => Map.GetValueOrDefault((c.Scope, kind), AssetLookup.Missing);
        public void Have(MediaScope scope, SourceKind kind)
            => Map[(scope, kind)] = AssetLookup.Found(new MediaAsset($"{scope}/{kind}.png", new PixelSize(1920, 360), Provenance: kind.ToString()));
    }

    private sealed class Planner : IGenerationPlanner
    {
        public Dictionary<SourceKind, GenerationPlan> Override = new();
        public GenerationPlan Plan(SourceKind kind, MediaAsset a, FitDecision? f, ResolutionContext c)
            => Override.GetValueOrDefault(kind, GenerationPlan.Live);
    }

    private static ResolutionContext SysCtx() =>
        new("marquee-2", "marquee", 1920, 360, MediaScope.System, "megadrive", "megadrive", null, null, "navigation");
    private static ResolutionContext GameCtx() =>
        new("marquee-2", "marquee", 1920, 360, MediaScope.Game, "megadrive", "megadrive", "path:abc", "sonic", "navigation");

    private static (MediaResolutionService svc, Assets assets, Planner planner, Policies policies) Build()
    {
        var assets = new Assets();
        var planner = new Planner();
        var policies = new Policies();
        return (new MediaResolutionService(policies, assets, planner, new FitCalculator()), assets, planner, policies);
    }

    private static bool Traced(ResolvedMedia r, ResolutionSource link, string code)
        => r.Trace.Any(t => t.Link == link && t.Code == code);

    // SYS-01..05 — the system chain in order.
    [Fact]
    public void System_AllPresent_PersonalWins()
    {
        var (svc, assets, _, _) = Build();
        foreach (var k in new[] { SourceKind.Personal, SourceKind.Generated, SourceKind.Scraped, SourceKind.Logo })
            assets.Have(MediaScope.System, k);

        var r = svc.Resolve(SysCtx());

        Assert.Equal(ResolutionSource.Personal, r.Source);
        Assert.True(Traced(r, ResolutionSource.Personal, TraceCodes.SourceSelected));
    }

    [Fact]
    public void System_PersonalAbsent_GeneratedWins()
    {
        var (svc, assets, _, _) = Build();
        assets.Have(MediaScope.System, SourceKind.Generated);
        assets.Have(MediaScope.System, SourceKind.Scraped);

        Assert.Equal(ResolutionSource.Generated, svc.Resolve(SysCtx()).Source);
    }

    // The user drop folder ("Mon dossier médias") sits between the personal creation
    // and the generated tile: it beats generated but yields to a personal creation.
    [Fact]
    public void System_PersonalAbsent_UserDropBeatsGenerated()
    {
        var (svc, assets, _, _) = Build();
        assets.Have(MediaScope.System, SourceKind.UserDrop);
        assets.Have(MediaScope.System, SourceKind.Generated);

        var r = svc.Resolve(SysCtx());

        Assert.Equal(ResolutionSource.UserDrop, r.Source);
        Assert.True(Traced(r, ResolutionSource.UserDrop, TraceCodes.SourceSelected));
    }

    [Fact]
    public void System_PersonalPresent_BeatsUserDrop()
    {
        var (svc, assets, _, _) = Build();
        assets.Have(MediaScope.System, SourceKind.Personal);
        assets.Have(MediaScope.System, SourceKind.UserDrop);

        Assert.Equal(ResolutionSource.Personal, svc.Resolve(SysCtx()).Source);
    }

    [Fact]
    public void System_OnlyScraped_ScrapedWins()
    {
        var (svc, assets, _, _) = Build();
        assets.Have(MediaScope.System, SourceKind.Scraped);

        Assert.Equal(ResolutionSource.Scraped, svc.Resolve(SysCtx()).Source);
    }

    [Fact]
    public void System_OnlyLogo_LogoWins()
    {
        var (svc, assets, _, _) = Build();
        assets.Have(MediaScope.System, SourceKind.Logo);

        Assert.Equal(ResolutionSource.Logo, svc.Resolve(SysCtx()).Source);
    }

    [Fact]
    public void System_NoMedia_FallsBackToNeutral()
    {
        var (svc, _, _, _) = Build();

        var r = svc.Resolve(SysCtx());

        Assert.Equal(ResolutionSource.Neutral, r.Source);
        Assert.True(Traced(r, ResolutionSource.Neutral, TraceCodes.FallbackNeutral));
        Assert.Null(r.Fit);
    }

    // SYS-06 — a disabled link is skipped even though its media exists.
    [Fact]
    public void System_GeneratedDisabled_SkippedDespiteMedia()
    {
        var (svc, assets, _, policies) = Build();
        policies.System = Full(game: false, SourceKind.Generated);
        assets.Have(MediaScope.System, SourceKind.Generated);
        assets.Have(MediaScope.System, SourceKind.Scraped);

        var r = svc.Resolve(SysCtx());

        Assert.Equal(ResolutionSource.Scraped, r.Source);
        Assert.True(Traced(r, ResolutionSource.Generated, TraceCodes.SourceDisabled));
    }

    // A link needing a not-yet-generated derivative is skipped (spec §17.4).
    [Fact]
    public void System_RequiredDerivative_SkippedToNextLink()
    {
        var (svc, assets, planner, _) = Build();
        assets.Have(MediaScope.System, SourceKind.Generated);
        assets.Have(MediaScope.System, SourceKind.Scraped);
        planner.Override[SourceKind.Generated] = GenerationPlan.Required;

        var r = svc.Resolve(SysCtx());

        Assert.Equal(ResolutionSource.Scraped, r.Source);
        Assert.True(Traced(r, ResolutionSource.Generated, TraceCodes.AdaptationRequired));
    }

    // A ready cached derivative wins and its path is the effective one.
    [Fact]
    public void System_ReadyDerivative_UsesDerivativePath()
    {
        var (svc, assets, planner, _) = Build();
        assets.Have(MediaScope.System, SourceKind.Generated);
        planner.Override[SourceKind.Generated] = GenerationPlan.Ready("cache/gen.png");

        var r = svc.Resolve(SysCtx());

        Assert.Equal(ResolutionSource.Generated, r.Source);
        Assert.Equal("cache/gen.png", r.EffectivePath);
    }

    // GAME-01 — the game's own personal wins first.
    [Fact]
    public void Game_PersonalPresent_Wins()
    {
        var (svc, assets, _, _) = Build();
        assets.Have(MediaScope.Game, SourceKind.Personal);

        Assert.Equal(ResolutionSource.Personal, svc.Resolve(GameCtx()).Source);
    }

    // GAME-05 — no game media: the FULL system chain runs as fallback.
    [Fact]
    public void Game_NoGameMedia_RunsSystemChain()
    {
        var (svc, assets, _, _) = Build();
        assets.Have(MediaScope.System, SourceKind.Generated); // system has a generated tile

        var r = svc.Resolve(GameCtx());

        Assert.Equal(ResolutionSource.Generated, r.Source); // shows the system's result
        Assert.True(Traced(r, ResolutionSource.SystemFallback, TraceCodes.FallbackSystem));
        Assert.True(Traced(r, ResolutionSource.Generated, TraceCodes.SourceSelected));
    }

    // GAME-06 — fallback disabled: straight to neutral.
    [Fact]
    public void Game_FallbackDisabled_Neutral()
    {
        var (svc, assets, _, policies) = Build();
        policies.Game = Full(game: true, SourceKind.SystemFallback);
        assets.Have(MediaScope.System, SourceKind.Generated); // present but must NOT be reached

        var r = svc.Resolve(GameCtx());

        Assert.Equal(ResolutionSource.Neutral, r.Source);
        Assert.True(Traced(r, ResolutionSource.SystemFallback, TraceCodes.SourceDisabled));
        Assert.True(Traced(r, ResolutionSource.Neutral, TraceCodes.FallbackNeutral));
    }

    // §19 — a payload with no frontend system is traced (but still resolves).
    [Fact]
    public void MissingFrontendSystem_IsTraced()
    {
        var (svc, assets, _, _) = Build();
        assets.Have(MediaScope.System, SourceKind.Scraped);
        var ctx = SysCtx() with { FrontendSystem = null };

        var r = svc.Resolve(ctx);

        Assert.True(Traced(r, ResolutionSource.None, TraceCodes.IdentityFrontendMissing));
        Assert.Equal(ResolutionSource.Scraped, r.Source);
    }

    // Decision 2: a lighting-pinned target frames every link with the pinned policy,
    // ignoring the per-source fit, so lamp coordinates stay aligned.
    [Fact]
    public void PinnedFit_OverridesPerSourceFit()
    {
        var (svc, assets, _, _) = Build();
        assets.Have(MediaScope.System, SourceKind.Scraped); // source policy is Contain
        var ctx = SysCtx() with { PinnedFit = new FitPolicy(FitMode.FillWidth) };

        var r = svc.Resolve(ctx);

        Assert.Equal(ResolutionSource.Scraped, r.Source);
        Assert.Equal(FitMode.FillWidth, r.Fit!.RequestedMode); // pinned, not the source's Contain
    }
}
