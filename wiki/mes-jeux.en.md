# My games

**My games** is a game's full sheet: the marquee it displays, its graphic creations, its online media, its **ingame effects**, its lamps and lighting.

![My games view](assets/setup/setup-games.png)

## Finding a game

Pick a system (only systems with **installed games** in `roms\` show up — the arcade family is grouped), then type a game or rom name: “lunar” finds *Lunar Lander* (`llander`), even without scraped media. Names come from your gamelist, completed by the APIExpose library.

## The displayed marquee

The sheet shows **the marquee currently displayed** for this game and its **source** (your creation, your folder, scraped, generated…), resolved through the system's priority rule — the link opens My systems to change it. When the source is yours (creation or a file from your folder), a button **deletes** it: the next source in the chain takes over.

## Graphic creations — one per surface

Every **surface carries its own creation**: creation A on the marquee surface and creation B on the topper can coexist for the same game. The sheet lists the existing creations (click = edit that one); “**Open the graphic creation interface**” builds yours for the surface picked in the selector.

The interface: target (screen/surface) at the top, **media by type** on the left (click = pick the version in a per-source modal, static gradients included), canvas in the middle (drag, wheel = size, Shift+wheel = rotate), **layers** on the right (eye, padlock, drag & drop for z-order) with the selected layer's inspector (size, rotation, opacity, text, mirror).

## Fetch media online

Arcade Database (no key), SteamGridDB, TheGamesDB — keys in Options → Online sources. Click a media to import it: it becomes available in the graphic creation interface (downloaded media).

??? note "What about ScreenScraper?"
    The ScreenScraper source only appears when the **developer** credentials are available (never shipped in the code); your **user** account is picked up from EmulationStation or typed in Options. APIExpose already scrapes ScreenScraper locally — this direct source is a complement, unchecked by default.

## Ingame effects management

Games with a `.MEM` definition ( MEM badge on the sheet) emit **semantic signals** (HIT, LOSE_LIFE, BOSS_DEFEATED…). Each row reads “When [signal] then [effect]”, with a status dot: **gray** = no effect, **orange** = default effect, **green** = your setting. Clicking a row (or “Link an effect to a signal…”) opens the dedicated editor: signal, simple effect or one of **My effects**, preview, save.

### My effects

A named effect = a **stack of sequenced actions** (veil, flash, shake, strobe, sprites, your webm/gif media) with start and duration. Sprites tune their **size (up to 1000 %, crisp pixels)**, **growth** and **position** (well-spaced random, centered, evenly spread); `full_*` sprites are unique full-width backdrops. The library ships official effects (★, not deletable — duplicate them) plus yours in `media\effects\library.json`.

### Per-game policy

**Inherit** (genre/system defaults + your settings), **Only my effects**, or **Disable everything**. The live monitor shows firing signals while you play.

## My dynamic Arcade marquee

This is the marquee shown **while playing**: the game's MAME outputs light the lamps you place, like the original cabinet's illuminated header. Background of your choice (generated marquee first), circle/rectangle lamps with precise position and dimensions, wiring to the game's outputs, a detailed list, and an **attract mode test** button (chase, alternate). The scene saves to `resources\rbmarquee\<rom>.xml` and the generator never overwrites it again.
