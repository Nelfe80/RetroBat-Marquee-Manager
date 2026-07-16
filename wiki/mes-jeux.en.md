# My games

**My games** is a game's full sheet: its marquee composition, its online media, its **light effects** driven by the game's signals, its lamps and its lighting profile. Search accepts the game name, the rom name or any alias.

![My games view](assets/setup/setup-games.png)

## Composer

Layers built from the game's media, on a canvas matching the marquee's real resolution. An inserted logo takes **50 % of the width** by default; “Auto recipe” lays fanart + gradient + logo in one click. A saved composition overrides every other source ([My systems](mes-systemes.md)).

## Fetch media online

Arcade Database (no key), SteamGridDB, TheGamesDB — keys in Options → Online sources. Clicking a result downloads it and adds it as a layer.

??? note "What about ScreenScraper?"
    The ScreenScraper source only appears when the **developer** credentials are available (they are never shipped in the code); your ScreenScraper **user** account is picked up automatically from EmulationStation, or typed in Options. Day to day, APIExpose already scrapes ScreenScraper locally — this direct source is an on-demand complement, unchecked by default.

## Light effects: “When [signal] then [effect]”

Games with a `.MEM` definition emit **semantic signals** (HIT, LOSE_LIFE, BOSS_DEFEATED…). Each signal reads like a sentence: *When HIT then red flash*. Tweaks apply to the game, its whole system or its whole **genre** (a HIT in a shmup is not a HIT in a beat 'em up); a badge always shows where the winning rule comes from.

### My effects — the effect composer

**My effects…** opens the library: an effect is a **stack of sequenced actions**, each with its type (colored veil, flash, shake, strobe, sprite swarm, **your webm/gif media**), its parameters, its **start** (ms) and duration. Two actions starting at 0 play together (“red veil + shake + explosions”); staggered starts make a sequence (“red flash THEN a swarm of sprites”). The preview replays the whole sequence; the library is a plain file (`media\effects\library.json`), exportable.

Drop your animations (transparent webm, gif, apng) in `media\effects\user\`: a “My media” action plays them **overlaid** on the composed marquee, or temporarily **fullscreen**. The Lighting Engine's neon tubes stay alive behind.

### Allocate and disengage

On every game signal: **Simple effect** (flash, sprite… as before) or **One of my effects** (the named sequence). And per game, a three-position policy:

- **Inherit** — genre/system defaults + your tweaks (normal behavior);
- **Only my effects** — every default muted, only the signals you allocated react;
- **Disable everything** — no MEM effect on this game.

### Live monitor

Launch the game and play: firing signals show up live; click one to tune its effect. It is the easiest way to discover what a game can emit.

## Scene, lamps and lighting

- **Scene & lamps**: the rbmarquee editor (MAME layout lamps driven by `/ws/arcade`).
- **Lighting profile**: the Lighting Engine's bulbs and cabinet, per game.
