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

    /// <summary>The GAME gabarit is PER SYSTEM (user decision): each system carries
    /// its own general template for its games, stored under the rom key
    /// "game-&lt;system&gt;". A game of megadrive and a game of nes can thus get
    /// different generic layouts, while every game of the SAME system shares one.</summary>
    public static string GameScopeFor(string system)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var safe = new string(system.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
        return "game-" + safe;
    }
}
