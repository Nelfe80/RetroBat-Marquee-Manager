# Troubleshooting

## Nothing shows on the marquee

1. **Is APIExpose running?** MarqueeManager only displays what APIExpose sends. Check that the APIExpose plugin is started.
2. **The right screen?** In `config.ini`, section `[Screens]`, check the screen index (see [Screens and surfaces](ecrans.md#assigning-screens)). `-1` = surface disabled.
3. Is the **.NET 8 Desktop runtime** installed? Without it, the executable will not start.

## The DMD is blurry

Your DMD media are probably generated at 256×64 for a 128×32 panel. Set the generation profile on the APIExpose side and purge the old files — see [DMD — crisp rendering](dmd.md#crisp-rendering-at-12832).

## The ZeDMD is not detected

- Set the port explicitly: `ZeDmdPort=COMx` in `[DMD]` (Device Manager → COM ports).
- Check that no other DMD application (a manually launched dmdext, a pinball game) already holds the panel.

## The DMD does not come back after a pinball game

External-control mode ends at `ui.game.ended`. If a pinball crashed, go back to game selection in EmulationStation — MarqueeManager takes over there. Also check that the system is listed in `ActiveSystemsDMD`.

## My configuration changed after an update

The first V1→V2 migration backs up your old file as `config.ini.v1.bak`, then migrates screens, DMD, DOF and the RA activation. Historical keys (scraping, MPV, ImageMagick, video generation…) are intentionally not carried over: those responsibilities now belong to APIExpose.

## Where are the logs?

In the plugin's `.log\` folder. For DMD issues, `DmdDevice.log` (at the root) contains the dialogue with the panel. Attach these files to any help request.
