namespace RetroBatMarqueeManager.Core.Surfaces;

/// <summary>
/// What the cabinet's control panel looks like, as APIExpose describes it on
/// /ws/panel. Two messages, two lifetimes:
///
///   • <see cref="PanelBoardConfig"/> comes from `panel.config.changed`: how many
///     panels the cabinet has, how many buttons each carries and where they sit.
///     Static — it only moves when the user reconfigures the cabinet.
///   • <see cref="PanelBoardButton"/> comes from `panel.state`: what each button DOES
///     in the game currently selected. It changes with every selection.
///
/// Both are read from the stream, never from APIExpose's folders: the panel drawn on
/// a marquee has to be the panel the API is publishing, and a second source would
/// eventually disagree with it.
/// </summary>
public sealed record PanelBoardConfig(
    int PlayerCount,
    int ButtonsPerPlayer,
    IReadOnlyList<IReadOnlyList<int>> Rows,
    bool HasStick,
    string StickColor)
{
    /// <summary>The arrangement to fall back on when the cabinet has not been described
    /// yet: one player, six buttons, a stick. Drawing SOMETHING beats drawing nothing —
    /// a panel that never appears reads as a broken component, and the first
    /// `panel.config.changed` (retained, so it arrives on connection) corrects it.</summary>
    public static readonly PanelBoardConfig Unknown = new(
        1, 6,
        new IReadOnlyList<int>[] { new[] { 4, 3, 5 }, new[] { 1, 2, 6 } },
        true, string.Empty);
}

/// <summary>
/// The panel as APIExpose DREW it — the same SVG it writes for EmulationStation themes,
/// plus where each button landed inside that drawing.
///
/// The coordinates travel with the file on purpose: the light of a press has to sit
/// exactly on the button the artwork drew. Recomputing that layout here would be the
/// renderer's geometry copied into a second place, and the first time a row moves, the
/// lights would land beside the buttons with nothing to explain why.
/// </summary>
public sealed record PanelBoardArt(
    string Path,
    double Width,
    double Height,
    IReadOnlyList<PanelArtButton> Buttons);

/// <summary>Where one button sits in the drawing, in the drawing's own units.</summary>
public sealed record PanelArtButton(int Slot, double Cx, double Cy, double R);

/// <summary>One physical place on the panel: what the selected game makes of it.
/// <paramref name="Used"/> false means the cabinet has this button but the game does
/// not speak to it — it is still drawn, faded, because the panel must tell the truth
/// about the CABINET.</summary>
public sealed record PanelBoardButton(int Slot, string Label, string Function, string Color, bool Used);
