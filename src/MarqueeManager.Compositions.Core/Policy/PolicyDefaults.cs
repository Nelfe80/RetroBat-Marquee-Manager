using MarqueeManager.Compositions.Core.Fit;

namespace MarqueeManager.Compositions.Core.Policy;

/// <summary>
/// The §20.1 default surface policies, in the DOMAIN so the Setup and the runtime
/// share one baseline. A media-presentation document is a set of deltas ON TOP of
/// these; when no document exists, these apply verbatim.
/// </summary>
public static class PolicyDefaults
{
    private static readonly FitPolicy Contain = new(FitMode.Contain, HAlign.Center, VAlign.Center);
    private static readonly FitPolicy ScrapDynamic = new(FitMode.Dynamic, HAlign.Auto, VAlign.Auto, 0.30, FitMode.Contain);
    private static readonly LogoLayout Logo = new(0.06, 0.08, 0.03, new BackgroundSpec(BackgroundKinds.ScopeNeutral));
    private static readonly BackgroundSpec Neutral = new(BackgroundKinds.Solid, "#000000");

    public static readonly ScopePolicy System = new("system-default", false,
        new Dictionary<SourceKind, SourceSettings>
        {
            [SourceKind.Personal] = new(true, Contain),
            [SourceKind.UserDrop] = new(true, Contain),
            [SourceKind.Generated] = new(true, Contain),
            [SourceKind.Scraped] = new(true, ScrapDynamic),
            [SourceKind.Logo] = new(true, Contain, Logo),
        }, Neutral);

    public static readonly ScopePolicy Game = new("game-default", false,
        new Dictionary<SourceKind, SourceSettings>
        {
            [SourceKind.Personal] = new(true, Contain),
            [SourceKind.UserDrop] = new(true, Contain),
            [SourceKind.Generated] = new(true, Contain),
            [SourceKind.Scraped] = new(true, ScrapDynamic),
            [SourceKind.Logo] = new(true, Contain, Logo),
            [SourceKind.SystemFallback] = new(true, Contain),
        }, Neutral);
}
