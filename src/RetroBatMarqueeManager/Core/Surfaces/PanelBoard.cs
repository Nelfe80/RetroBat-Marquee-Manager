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

/// <summary>One physical place on the panel: what the selected game makes of it.
/// <paramref name="Used"/> false means the cabinet has this button but the game does
/// not speak to it — it is still drawn, faded, because the panel must tell the truth
/// about the CABINET.</summary>
public sealed record PanelBoardButton(int Slot, string Label, string Function, string Color, bool Used);
