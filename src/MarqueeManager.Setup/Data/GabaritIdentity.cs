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

    /// <summary>What the pickers of "Mes jeux" / "Mes systèmes" carry for their "All"
    /// entry: the level above every entry, where the template of last resort is
    /// composed. Not a system, and never a folder name.</summary>
    public const string AllSentinel = "__all__";
    public const string SystemScope = "system";
    /// <summary>The template of LAST resort for games, across every system: what
    /// "Tous les jeux" composes. A system's own template (GameScopeFor) outranks it;
    /// it exists so a library can be dressed once instead of system by system.</summary>
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
