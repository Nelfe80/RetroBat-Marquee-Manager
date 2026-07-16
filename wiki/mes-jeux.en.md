# My components

The **My components** tab is MarqueeManagerSetup's composition library: automatic templates, **per-system source priorities**, your **personal media folder**, and original compositions per game or per system — still with light effects, lamps and light profiles.

![My components tab](assets/setup/setup-games.png)

## Composition templates

Four automatic recipes mirror APIExpose's own (fanart background, black or white gradient picked by luminance, logo): three horizontal at the generated-marquee proportions — **1920×360, 1280×400, 920×360** — and one vertical **1080×1920**. Assign a template to a system in the priorities: every game gets its composition, rendered in the background on first display then cached.

!!! tip "Instant ES navigation"
    **Pre-generate this system** (or all of them) renders every templated composition ahead of time: no wait at game selection, ever.

## Per-system priorities

For each **category** (marquee, topper, DMD) then each system, an ordered chain of sources: the runtime shows the **first available** one. Sources: manual composition, my folder, template, scraped marquee, screen-marquee, APIExpose generated, logo, fanart… (and for the DMD: your animated GIFs, the pack's dmd*.gif, dmd.png).

A typical arcade chain: *composition > my folder > scraped marquee > generated* — your images first, the scrape next, the auto-generated as last resort. And when one game displeases you, its manual composition overrides it individually.

### My folder

Drop your own **images or videos** into `media\marquees\user\<system>\` (same for `media\toppers\user\`, `media\dmd\user\` — your animated DMD GIFs cycle there). File names are **alias-resolved** through APIExpose's gamelist index: `Metal Slug (World).png`, `metalslug.png` or the exact set name all point to the same game. Drag & drop straight onto the card (or onto a game sheet) copies and renames automatically.

**Test the chain** shows, for a few games of the system, which source wins — the chain is never a black box.

## Original compositions

The search (game name, rom name or alias) opens the game sheet:

- **Composer**: layers from the game's media, canvas locked to the real marquee resolution. An inserted logo takes **50 % of the width** by default; the "Auto recipe" button lays fanart + logo in one click.
- **Fetch media online**: Arcade Database (no key), SteamGridDB, TheGamesDB — and ScreenScraper as a fallback (APIExpose scrapes it already). Keys in Options → Online sources. Clicking a result downloads it and adds it as a layer.
- **Light effects** (.MEM signals), rbmarquee **scene & lamps** and **light profile**: unchanged, see their dedicated sections.

Per-SYSTEM compositions (a system selected in ES) are stored in `media\marquees\systems\<system>.png`.

## Live video on a surface

A surface's video component can follow a **live Twitch stream > YouTube > local video** chain: when someone streams the displayed game, the live takes the video's place. Twitch/YouTube credentials in Options → Online sources; without keys, the local video simply plays.
