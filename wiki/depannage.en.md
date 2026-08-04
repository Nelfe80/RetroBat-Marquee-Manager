# Troubleshooting

## Nothing shows on the marquee

1. **Is APIExpose running?** MarqueeManager only displays what APIExpose sends. Check that the APIExpose plugin is started.
2. **The right screen?** Open [My setup](mon-setup.md) in the assistant: “Identify screens” shows every screen's number, and the map shows which surface lives on which screen.
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

## The lit marquee stutters (low FPS)

The lighting engine renders on the **CPU** by default, which can saturate on a large surface (1920×360 and up). Two options in **MarqueeManagerSetup → Marquee lighting**:

- **Rasterize the lighting engine on the GPU** (`[Lighting] GpuRaster`): offloads the render to the GPU (Skia OpenGL backend). Holds full resolution at the target frame rate where the CPU had to drop the internal scale. Falls back to CPU automatically if the GL driver fails to initialize. Requires a working OpenGL driver. **Requires a restart.**
- **Internal resolution**: without a GPU, lower it (0.75 → 0.5) to regain smoothness at the cost of slight blur.

## Display artefacts / instability (GPU driver)

If the display flickers or crashes because of the cabinet's GPU driver, uncheck **GPU acceleration (WPF hardware compositing)** (`[Settings] GpuAcceleration`): WPF rendering falls back to software (SoftwareOnly), slower but more stable. **Requires a restart.**

## Where are the logs?

In the plugin's `.log\` folder. For DMD issues, `DmdDevice.log` (at the root) contains the dialogue with the panel. Attach these files to any help request.

## Still stuck?

Open a ticket on the [MarqueeManager issue tracker](https://github.com/Nelfe80/RetroBat-Marquee-Manager/issues), attaching the logs from the `.log\` folder.
