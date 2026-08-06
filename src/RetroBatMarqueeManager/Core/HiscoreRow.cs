namespace RetroBatMarqueeManager.Core;

/// <summary>One line of a local hiscore leaderboard as provided by APIExpose
/// (its HiscoreExtractionResult.Scores collection). Rank may be empty when the
/// source has no explicit rank — the renderer falls back to the row position.</summary>
public sealed record HiscoreRow(string Rank, string Name, string Score);

/// <summary>"Your rank" line drawn under a leaderboard, carried as raw data so the window
/// formats it with its own (customizable, culture-aware) labels. World: a certified world
/// rank when <see cref="Present"/>, else a paired/unpaired prompt. Local: your best line for
/// the game when Present, else a not-ranked-yet note. Empty strings when a value is absent.</summary>
public sealed record HiscoreMyRank(
    bool World, bool Present, bool Paired,
    string Rank, int Of, string Score, string Pseudo);
