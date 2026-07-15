# My games

The **My games** tab of MarqueeManagerSetup is the per-game workshop: compose your own marquee, wire in-game signals to light effects, retouch the scene lamps and pin a light profile.

![My games tab](assets/setup/setup-games.png)

Pick a game first: filter by system then type a few letters in the search box (the APIExpose media library is indexed automatically).

## Compose the marquee

The composer assembles a marquee from the game's media: fanart, logo (wheel), scraped marquee, flyer, box, screenshot… Click a thumbnail to add it as a layer, then:

- **drag** to move, **wheel** to resize, **Shift+wheel** to rotate;
- fine tuning (opacity, mirror, layer order) under the preview;
- three backgrounds: black, dark gradient or blurred fanart.

The canvas is **locked to the real resolution** of your marquee (the `MarqueeBounds` area when set, otherwise the marquee screen). On save:

- the flattened PNG goes to `media\marquees\<system>\<rom>.png` — it **replaces the scraped or generated marquee** for this game, with absolute priority;
- a `.project.json` file sits next to it: reopen the game and the composition is editable again, layer by layer.

"Delete my composition" hands control back to the original marquee.

## Light effects (game signals)

Every game with a `.MEM` definition exposes its **semantic signals** (LOSE_LIFE, BOSS_DEFEATED, COIN_GAIN…). The card shows them as readable sentences:

> **When** `HIT` — Player 1 Health decreased **then** Colored flash

Click a row to open the editor: effect kind (flash, pulse, tint, shake, strobe, blackout, sprites), color, duration, animated sprite, cooldown… The **▶ Preview the effect** button replays the effect in a mini marquee band, without launching the game.

### Tweak scope

The "My tweaks apply to" selector picks the write layer:

| Scope | File |
|---|---|
| this game only | `overrides\effects\<system>\<rom>.json` |
| the whole system | `overrides\effects\<system>.json` |
| every game of the genre | `overrides\effects\genres\<slug>.json` |

The runtime resolves in order **game → system → genre → genre defaults → generic defaults**, and reloads the layers at every game change. "Silence this signal" mutes one signal without touching the others.

### Genre drives the style

The game's scraped genre (shmup, beat'em up, racing…) is normalized by `resources\lighting\genres.map.xml`: a `HIT` in a shmup blows bombs across the marquee, the same `HIT` in a beat'em up throws an impact burst. Genre rules live in `resources\lighting\ingame.effects.xml` (`genre=` attribute) and stay editable.

### Live monitor

Start listening, launch the game and play: firing signals scroll by live (name, family, time). Click one to tune its effect right away — the most natural way to discover what a game emits.

## Scene & lamps

For arcade games, the light scene (`resources\rbmarquee\<rom>.xml`) places **lamps driven by the game outputs** (APB's beacons, Chase H.Q.'s lamps…). The editor shows the marquee as background:

- drag a lamp, resize with the wheel;
- color and **output wiring** (list of the game's known outputs) under the preview;
- attract mode: none, chase or alternate.

Saving stamps the scene `generated="false"`: it is **curated**, the automatic generator will never overwrite it again (a `.bak` backup is kept).

## Light profile

By default the lighting engine picks the period bulb through its grammar (year, manufacturer, publisher…). You can **pin** a library bulb (39 profiles: fluorescent tubes, incandescent, neon, LED…) and/or a cabinet profile for a specific game. The choice is stored with the game's effects and beats the grammar.
