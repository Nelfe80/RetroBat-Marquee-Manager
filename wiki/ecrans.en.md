# Screens and surfaces

![The surfaces on the cabinet](assets/surfaces-borne.svg)

MarqueeManager can animate **five surfaces**, each being a WPF window placed on the Windows screen of your choice:

| Surface | Typical use | APIExpose stream |
|---|---|---|
| `marquee` | The light strip above the cabinet | `/ws/marquee` |
| `topper` | The screen at the very top | `/ws/topper` |
| `iccard` | The game's instruction card | `/ws/instruction-card` |
| `dmd` (virtual) | A DMD displayed on a regular screen | `/ws/marquee` (DMD media) |
| `lcd` | Information screen: scores, challenges, leaderboards | `/ws/score`, `/ws/timer`, `/ws/retroachievements` |

## Assigning screens

!!! tip "The easy way: the assistant"
    [`MarqueeManagerSetup.exe`](assistant.md) does all of this visually: screen identification, surface assignment, zone testing, without editing the file.

Everything happens in `config.ini`, section `[Screens]`: each surface receives the **index of the Windows screen** that should display it.

- `-1` disables a surface;
- several comma-separated indices duplicate the surface on several screens.

!!! tip "Finding a screen's index"
    The assistant's "Identify screens" button shows the right number on each display. (In Windows, Settings → Display → "Identify", MarqueeManager indices usually start at 0 — if the result is unexpected, try the Windows number minus one.)

## What displays, layer by layer

The media provided by APIExpose (logo, game marquee, video) remains the **background layer**. On top, MarqueeManager composes native layers:

- MAME `.lay` views with lamps driven by the `/ws/arcade` stream;
- persistent information (RA score, game mode);
- temporary notifications (achievement unlocked, challenge, leaderboard result).

On the **LCD**, active cards spread across a horizontal grid with equal columns; in speedrun mode, the leaderboard card becomes a full-width bottom banner to stay readable.

## Reconnections

If APIExpose restarts, each stream reconnects automatically after five seconds — no action needed.
