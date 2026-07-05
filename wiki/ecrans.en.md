# Screens and surfaces

MarqueeManager can animate **five surfaces**, each being a WPF window placed on the Windows screen of your choice:

| Surface | Typical use | APIExpose stream |
|---|---|---|
| `marquee` | The light strip above the cabinet | `/ws/marquee` |
| `topper` | The screen at the very top | `/ws/topper` |
| `iccard` | The game's instruction card | `/ws/instruction-card` |
| `dmd` (virtual) | A DMD displayed on a regular screen | `/ws/marquee` (DMD media) |
| `lcd` | Information screen: scores, challenges, leaderboards | `/ws/score`, `/ws/timer`, `/ws/retroachievements` |

## Assigning screens

Everything happens in `config.ini`, section `[Screens]`: each surface receives the **index of the Windows screen** that should display it.

- `-1` disables a surface;
- several comma-separated indices duplicate the surface on several screens.

!!! tip "Finding a screen's index"
    In Windows, Settings → Display → "Identify": the displayed numbers map to the indices (note that MarqueeManager indices usually start at 0 — if the result is unexpected, try the Windows number minus one).

## What displays, layer by layer

The media provided by APIExpose (logo, game marquee, video) remains the **background layer**. On top, MarqueeManager composes native layers:

- MAME `.lay` views with lamps driven by the `/ws/arcade` stream;
- persistent information (RA score, game mode);
- temporary notifications (achievement unlocked, challenge, leaderboard result).

On the **LCD**, active cards spread across a horizontal grid with equal columns; in speedrun mode, the leaderboard card becomes a full-width bottom banner to stay readable.

## Reconnections

If APIExpose restarts, each stream reconnects automatically after five seconds — no action needed.
