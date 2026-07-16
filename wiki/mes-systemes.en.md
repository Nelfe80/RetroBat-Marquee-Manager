# My systems

**My systems** decides, system by system, **what shows up and in which order**: your media, the scraped one, the generated one — plus the automatic template that builds a composition for every game.

![My systems view](assets/setup/setup-systems.png)

## Per-system priorities

For each **category** (marquee, topper, DMD) then each system, an ordered chain of sources: the runtime displays the **first available** one. Sources: manual composition, my folder, template, scraped marquee, screen-marquee, APIExpose generated, logo, fanart… (and for the DMD: your animated GIFs, the pack's `dmd*.gif`, `dmd.png`).

A typical arcade chain: *composition > my folder > scraped marquee > generated* — your images first, scraped next, auto-generated as last resort. And if one game displeases you, its manual composition (game sheet) overrides it individually.

**Test the chain** shows, for a few games of the system, which source wins — the chain is never a black box.

## My folder

Drop your own **images or videos** in `media\marquees\user\<system>\` (same for `media\toppers\user\`, `media\dmd\user\` — your animated DMD GIFs are cycled there). File names are **alias-resolved** through APIExpose's gamelist index: `Metal Slug (World).png`, `metalslug.png` or the exact set name all point to the same game. Dropping files straight onto the card (or a game's sheet) copies and renames automatically.

## Composition templates

Four automatic recipes reuse the APIExpose formula (fanart background, black or white gradient depending on luminance, logo): three horizontal ones matching generated-marquee proportions — **1920×360, 1280×400, 920×360** — and one vertical **1080×1920**. Assign a template to a system in the priorities: every game gets its composition, rendered in the background on first display then cached.

!!! tip "Instant ES browsing"
    **Pre-generate this system** (or all) renders every templated composition ahead of time: no more wait when a game is selected. Command line: `MarqueeManager.exe --render-templates arcade` (or `all`).

## Per-system compositions

Selecting a **system** in ES automatically switches the media components to the system's media (theme logo and fanart) — the same composition serves both game and system. For a custom system render, a manual composition is saved to `media\marquees\systems\<system>.png`.
