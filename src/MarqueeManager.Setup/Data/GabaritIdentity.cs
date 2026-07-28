namespace MarqueeManager.Setup.Data;

/// <summary>
/// Identity of a surface's GENERAL TEMPLATE ("gabarit") inside the existing
/// composer/project storage. The gabarit is just a <see cref="MarqueeProject"/>
/// composed with generic layers (each keyed by its AssetKey: fanart, logo…) and
/// saved per surface under a reserved system id, with the scope as its "rom":
/// media\&lt;cat&gt;\surfaces\&lt;surfaceId&gt;\__gabarit__\{system|game}.project.json.
/// At render, each layer's AssetKey resolves to the CURRENT system's media, so one
/// layout serves every system of the surface.
/// </summary>
public static class GabaritIdentity
{
    public const string SystemId = "__gabarit__";
    public const string SystemScope = "system";
    public const string GameScope = "game";
}
