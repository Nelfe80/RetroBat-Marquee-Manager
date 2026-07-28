using System.IO;
using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Geometry;
using MarqueeManager.Compositions.Core.Policy;
using MarqueeManager.Compositions.Core.Presentation;
using MarqueeManager.Compositions.Core.Resolution;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Data;

/// <summary>The §20.1 default surface policies (no per-target deltas yet — those
/// arrive with the media-presentation store in lot 2).</summary>
public static class PresentationDefaults
{
    private static readonly FitPolicy Contain = new(FitMode.Contain, HAlign.Center, VAlign.Center);
    private static readonly FitPolicy ScrapDynamic = new(FitMode.Dynamic, HAlign.Auto, VAlign.Auto, 0.30, FitMode.Contain);
    private static readonly LogoLayout Logo = new(0.06, 0.08, 0.03, new BackgroundSpec(BackgroundKinds.ScopeNeutral));
    private static readonly BackgroundSpec Neutral = new(BackgroundKinds.Solid, "#000000");

    public static readonly ScopePolicy System = new("system-default", false,
        new Dictionary<SourceKind, SourceSettings>
        {
            [SourceKind.Personal] = new(true, Contain),
            [SourceKind.Generated] = new(true, Contain),
            [SourceKind.Scraped] = new(true, ScrapDynamic),
            [SourceKind.Logo] = new(true, Contain, Logo),
        }, Neutral);

    public static readonly ScopePolicy Game = new("game-default", false,
        new Dictionary<SourceKind, SourceSettings>
        {
            [SourceKind.Personal] = new(true, Contain),
            [SourceKind.Generated] = new(true, Contain),
            [SourceKind.Scraped] = new(true, ScrapDynamic),
            [SourceKind.Logo] = new(true, Contain, Logo),
            [SourceKind.SystemFallback] = new(true, Contain),
        }, Neutral);
}

/// <summary>Serves the default policy for now; the target-delta store lands in lot 2.</summary>
public sealed class DefaultPresentationPolicyProvider : IPresentationPolicyProvider
{
    public ScopePolicy PolicyFor(ResolutionContext context)
        => context.Scope == MediaScope.System ? PresentationDefaults.System : PresentationDefaults.Game;
}

/// <summary>
/// Setup-side media discovery over the EXISTING library (read-only): the same
/// file rules the runtime uses, so the preview shows exactly what would display.
/// Dimensions come from the header reader, never a full decode.
/// </summary>
public sealed class SetupMediaAssetResolver : IMediaAssetResolver
{
    private readonly string _pluginRoot;
    private readonly GameMediaCatalog _media;
    private readonly CompositionAssignments _assignments;

    public SetupMediaAssetResolver(string pluginRoot, GameMediaCatalog media, CompositionAssignments assignments)
    {
        _pluginRoot = pluginRoot;
        _media = media;
        _assignments = assignments;
    }

    public AssetLookup Resolve(SourceKind kind, ResolutionContext context)
    {
        var (category, categoryRoot) = MediaCategory(context.Category);
        return context.Scope == MediaScope.System
            ? ResolveSystem(kind, category, categoryRoot, context)
            : ResolveGame(kind, category, context);
    }

    private AssetLookup ResolveSystem(SourceKind kind, string category, string categoryRoot, ResolutionContext ctx)
    {
        var frontend = ctx.SystemKey ?? "";
        var canonical = ctx.CanonicalSystem ?? frontend;
        switch (kind)
        {
            case SourceKind.Personal:
                var perSurface = new MarqueeProjectStore(_pluginRoot, categoryRoot, ctx.SurfaceId);
                if (perSurface.HasComposition("systems", frontend)) return Found(perSurface.PngPath("systems", frontend), "creation");
                var shared = new MarqueeProjectStore(_pluginRoot, categoryRoot);
                if (shared.HasComposition("systems", frontend)) return Found(shared.PngPath("systems", frontend), "creation");
                return AssetLookup.Missing;
            case SourceKind.Generated:
                // the surface's general template rendered for THIS system wins over
                // the APIExpose autogen when it has been rendered to the cache
                var gabaritCache = GabaritRenderer.CachePath(_pluginRoot, categoryRoot, ctx.SurfaceId, frontend);
                if (File.Exists(gabaritCache)) return Found(gabaritCache, "gabarit");
                return FromRoots(SystemRoots(frontend, canonical), "generated",
                    category == "dmd" ? @"artwork\marquee\generated-system-dmd.png" : @"artwork\marquee\generated-system-marquee.png");
            case SourceKind.Logo:
                return FromRoots(SystemRoots(frontend, canonical), "logo", @"ui\wheels\wheel.png");
            default:
                return AssetLookup.Missing; // no scraped system marquee in the library
        }
    }

    private IEnumerable<string> SystemRoots(string frontend, string canonical)
    {
        yield return SystemMediaRoot(frontend);
        if (!string.Equals(frontend, canonical, StringComparison.OrdinalIgnoreCase))
            yield return SystemMediaRoot(canonical);
    }

    private AssetLookup FromRoots(IEnumerable<string> roots, string provenance, params string[] relatives)
    {
        foreach (var root in roots)
        {
            var found = FromLibrary(root, provenance, relatives);
            if (found.Asset is not null) return found;
        }
        return AssetLookup.Missing;
    }

    private AssetLookup ResolveGame(SourceKind kind, string category, ResolutionContext ctx)
    {
        var system = ctx.CanonicalSystem ?? ctx.SystemKey ?? "";
        var rom = ctx.Rom ?? "";
        var root = _media.GameRoot(system, rom);
        switch (kind)
        {
            case SourceKind.Personal:
                var composition = _assignments.CompositionPath(category, system, rom);
                return File.Exists(composition) ? Found(composition, "creation") : AssetLookup.Missing;
            case SourceKind.Generated:
                return FromLibrary(root, "generated", @"artwork\marquee\generated-marquee.png", @"artwork\marquee\generated-dmd.png");
            case SourceKind.Scraped:
                return FromLibrary(root, "scraped", @"artwork\marquee\marquee.png", @"artwork\marquee\marquee.jpg", @"artwork\marquee\screenmarquee.png");
            case SourceKind.Logo:
                return FromLibrary(root, "logo", @"ui\wheels\wheel.png");
            default:
                return AssetLookup.Missing;
        }
    }

    private AssetLookup FromLibrary(string root, string provenance, params string[] relatives)
    {
        foreach (var relative in relatives)
        {
            var path = Path.Combine(root, relative);
            if (File.Exists(path)) return Found(path, provenance);
        }
        return AssetLookup.Missing;
    }

    private static AssetLookup Found(string path, string provenance)
        => AssetLookup.Found(new MediaAsset(
            path,
            SetupImageDimensions.Read(path),
            IsAnimated: Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase),
            Provenance: provenance));

    private string SystemMediaRoot(string system)
        => Path.GetFullPath(Path.Combine(_pluginRoot, "..", "APIExpose", "media", "systems", system));

    private static (string category, string categoryRoot) MediaCategory(string surfaceCategory)
        => surfaceCategory.ToLowerInvariant() switch
        {
            "topper" => ("topper", "toppers"),
            "dmd-virtual" or "dmd" => ("dmd", "dmd"),
            _ => ("marquee", "marquees")
        };
}

/// <summary>Preview planner: every candidate the resolver returns is an existing,
/// directly displayable file, framed LIVE (user decision). Baked derivatives for
/// blur/gradient/composition come with the generation queue in lot 4.</summary>
public sealed class PreviewGenerationPlanner : IGenerationPlanner
{
    public GenerationPlan Plan(SourceKind kind, MediaAsset asset, FitDecision? fit, ResolutionContext context)
        => GenerationPlan.Live;
}

/// <summary>The result of a preview resolution for one surface + scope + target.</summary>
public sealed record PreviewResult(ResolvedMedia Media, DimensionalReport Dimensions, PixelSize Target);

/// <summary>One chain link's live state for the card UI: enabled for this target,
/// media present, currently displayed, plus its OWN framed preview (path + fit) so
/// each card shows what picking it would give.</summary>
public sealed record ChainLink(
    SourceKind Kind, ResolutionSource Source, bool Enabled, bool Present, bool IsWinner,
    string? Path, MarqueeManager.Compositions.Core.Fit.FitDecision? Fit);

/// <summary>
/// Setup-facing facade: builds the resolution context from a surface and runs the
/// SHARED resolver with the Setup adapters. This is what the new "Mes systèmes /
/// Mes jeux" reads — the exact engine the runtime will use, no ES launch, nothing
/// written or generated.
/// </summary>
public sealed class MediaResolutionPreview
{
    private readonly SetupMediaAssetResolver _assets;
    private readonly PreviewGenerationPlanner _planner = new();
    private readonly IFitCalculator _fit = new FitCalculator();
    private readonly MediaPresentationStore _store;
    private MediaPresentationDocument _document;
    private IPresentationPolicyProvider _policies = null!;
    private IMediaResolutionService _resolver = null!;

    public MediaResolutionPreview(string pluginRoot, GameMediaCatalog media, CompositionAssignments assignments)
    {
        _assets = new SetupMediaAssetResolver(pluginRoot, media, assignments);
        _store = new MediaPresentationStore(pluginRoot);
        _document = _store.Load();
        Rebuild();
    }

    private void Rebuild()
    {
        _policies = new MediaPresentationPolicyProvider(_document);
        _resolver = new MediaResolutionService(_policies, _assets, _planner, _fit);
    }

    public ResolutionContext SystemContext(SurfaceModel surface, IReadOnlyList<ScreenInfo> screens, string system)
        => Context(surface, screens, MediaScope.System, system, null);

    public ResolutionContext GameContext(SurfaceModel surface, IReadOnlyList<ScreenInfo> screens, string system, string rom)
        => Context(surface, screens, MediaScope.Game, system, rom);

    private static ResolutionContext Context(SurfaceModel surface, IReadOnlyList<ScreenInfo> screens, MediaScope scope, string system, string? rom)
    {
        var target = TargetOf(surface, screens);
        // arcade family (mame, fbneo…) keeps its own settings under the frontend key,
        // but its media lives under the canonical "arcade" folder — carry both.
        var canonical = GameMediaCatalog.ArcadeAliases.Contains(system) ? "arcade" : system;
        return new ResolutionContext(surface.Id, surface.Category, target.Width, target.Height, scope,
            FrontendSystem: system, CanonicalSystem: canonical,
            StableGameId: rom is null ? null : StableGameIds.FromRomPath($"{system}/{rom}"),
            Rom: rom, DisplayState: "navigation");
    }

    public PreviewResult Resolve(ResolutionContext context)
    {
        var media = _resolver.Resolve(context);
        var dimensions = DimensionalAnalyzer.Analyze(media.OriginalSize, context.Target, media.Fit, media.Generation);
        return new PreviewResult(media, dimensions, context.Target);
    }

    public PreviewResult ResolveSystem(SurfaceModel surface, IReadOnlyList<ScreenInfo> screens, string system)
        => Resolve(SystemContext(surface, screens, system));

    /// <summary>Per-link state of the whole chain for one target: enabled, present
    /// and which one currently WINS — powers the block UI with its winner check.</summary>
    public IReadOnlyList<ChainLink> DescribeChain(ResolutionContext context)
    {
        var policy = _policies.PolicyFor(context);
        var winner = _resolver.Resolve(context).Source;
        var target = context.Target;
        var links = new List<ChainLink>();
        foreach (var kind in ChainOrder(context.Scope))
        {
            var asset = _assets.Resolve(kind, context).Asset;
            var present = asset != null || kind == SourceKind.SystemFallback;
            MarqueeManager.Compositions.Core.Fit.FitDecision? fit = null;
            if (asset?.Size is { IsValid: true } size && policy.Source(kind) is { } settings)
                fit = LinkFraming.Compute(_fit, kind, size, target, settings, asset.Protected, context.PinnedFit);
            links.Add(new ChainLink(kind, ToSource(kind), policy.IsEnabled(kind), present,
                ToSource(kind) == winner, asset?.Path, fit));
        }
        return links;
    }

    private static SourceKind[] ChainOrder(MediaScope scope) => scope == MediaScope.System
        ? new[] { SourceKind.Personal, SourceKind.Generated, SourceKind.Scraped, SourceKind.Logo }
        : new[] { SourceKind.Personal, SourceKind.Generated, SourceKind.Scraped, SourceKind.Logo, SourceKind.SystemFallback };

    public bool HasOverride(ResolutionContext context) => MediaPresentationEdits.HasOverride(_document, context);

    /// <summary>Cliquer une card = FORCER cette source : on active ce maillon et tout
    /// ce qui est en dessous, on désactive ce qui est au-dessus, donc il gagne s'il
    /// est présent. Persisté puis rechargé.</summary>
    public void SelectSource(ResolutionContext context, SourceKind kind)
    {
        _document = MediaPresentationEdits.SelectSource(_document, context, kind, ChainOrder(context.Scope));
        _store.Save(_document);
        Rebuild();
    }

    /// <summary>Persist a single "use this source" toggle (kept for completeness).</summary>
    public void SetSourceEnabled(ResolutionContext context, SourceKind kind, bool enabled)
    {
        _document = MediaPresentationEdits.SetSourceEnabled(_document, context, kind, enabled);
        _store.Save(_document);
        Rebuild();
    }

    /// <summary>Drop every override for this target (reset to the surface defaults).</summary>
    public void ResetTarget(ResolutionContext context)
    {
        _document = MediaPresentationEdits.ResetTarget(_document, context);
        _store.Save(_document);
        Rebuild();
    }

    private static ResolutionSource ToSource(SourceKind kind) => kind switch
    {
        SourceKind.Personal => ResolutionSource.Personal,
        SourceKind.Generated => ResolutionSource.Generated,
        SourceKind.Scraped => ResolutionSource.Scraped,
        SourceKind.Logo => ResolutionSource.Logo,
        SourceKind.SystemFallback => ResolutionSource.SystemFallback,
        _ => ResolutionSource.None
    };

    /// <summary>The physical target size of a surface: its explicit bounds, else
    /// the targeted Windows screen's dimensions, else a marquee-shaped default.</summary>
    public static PixelSize TargetOf(SurfaceModel surface, IReadOnlyList<ScreenInfo> screens)
    {
        if (!surface.IsFullscreen && surface.Width is > 0 and int w && surface.Height is > 0 and int h)
            return new PixelSize(w, h);
        var index = surface.Screens.Count > 0 ? surface.Screens[0] : -1;
        if (index >= 0 && index < screens.Count)
        {
            var bounds = screens[index].Bounds;
            if (bounds.Width > 0 && bounds.Height > 0) return new PixelSize(bounds.Width, bounds.Height);
        }
        return new PixelSize(1920, 360);
    }
}

/// <summary>Translates the domain's stable, non-localized codes for display (§18.2).</summary>
public static class ResolutionText
{
    public static string Link(ResolutionSource link) => link switch
    {
        ResolutionSource.Personal => L.T("Ma création graphique", "My graphic creation"),
        ResolutionSource.Generated => L.T("Générée (gabarit)", "Generated (template)"),
        ResolutionSource.Scraped => L.T("Marquee scrapé", "Scraped marquee"),
        ResolutionSource.Logo => L.T("Logo mis en page", "Laid-out logo"),
        ResolutionSource.SystemFallback => L.T("Rendu du système", "System render"),
        ResolutionSource.Neutral => L.T("Fond neutre", "Neutral background"),
        _ => ""
    };

    public static string Trace(ResolutionTraceEntry entry) => entry.Code switch
    {
        TraceCodes.SourceDisabled => $"{Link(entry.Link)} — {L.T("désactivée", "disabled")}",
        TraceCodes.SourceMissing => $"{Link(entry.Link)} — {L.T("absente", "absent")}",
        TraceCodes.SourceInvalid => $"{Link(entry.Link)} — {L.T("invalide", "invalid")}",
        TraceCodes.TemplateIngredientsMissing => $"{Link(entry.Link)} — {L.T("ingrédients manquants", "ingredients missing")}",
        TraceCodes.AdaptationRequired => $"{Link(entry.Link)} — {L.T("adaptation à générer", "adaptation to generate")}",
        TraceCodes.AdaptationStale => $"{Link(entry.Link)} — {L.T("adaptation obsolète", "adaptation stale")}",
        TraceCodes.SourceSelected => $"{Link(entry.Link)} — {L.T("utilisée", "used")}",
        TraceCodes.FallbackSystem => "→ " + L.T("chaîne du système", "system chain"),
        TraceCodes.FallbackNeutral => Link(ResolutionSource.Neutral),
        TraceCodes.IdentityFrontendMissing => L.T("(système frontend manquant)", "(frontend system missing)"),
        _ => entry.Code
    };

    public static string Status(DimensionalStatus status) => status switch
    {
        DimensionalStatus.ExactDimensions => L.T("Dimensions exactes", "Exact dimensions"),
        DimensionalStatus.RatioCompatible => L.T("Ratio compatible", "Ratio compatible"),
        DimensionalStatus.Resizable => L.T("Redimensionnable", "Resizable"),
        DimensionalStatus.AdaptationNeeded => L.T("Adaptation nécessaire", "Adaptation needed"),
        DimensionalStatus.Magnified => L.T("Agrandissement", "Magnified"),
        DimensionalStatus.AdaptationToGenerate => L.T("Adaptation à générer", "Adaptation to generate"),
        DimensionalStatus.AdaptationStale => L.T("Adaptation obsolète", "Adaptation stale"),
        DimensionalStatus.UnreadableFormat => L.T("Format illisible", "Unreadable format"),
        DimensionalStatus.UnsupportedFormat => L.T("Format non pris en charge", "Unsupported format"),
        _ => status.ToString()
    };
}
