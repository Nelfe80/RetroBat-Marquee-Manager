# DMD and ZeDMD

MarqueeManager drives a **physical DMD** (ZeDMD and compatibles) through the private DLLs in `tools\dmd` and `tools\zedmd`, and uses `dmdext` only to pass through compatible video media.

## Enabling the DMD

In `config.ini`, section `[DMD]`: activation, model, resolution and port. For a standard ZeDMD:

```ini
[DMD]
Enabled=true
Model=zedmd
Width=128
Height=32
ZeDmdPort=
OptimizeZeDmd=true
```

An empty `ZeDmdPort=` lets auto-detection do its job; setting `COMx` speeds up startup if you know the port.

## ZeDMD optimization

With `OptimizeZeDmd=true`, MarqueeManager prepares the panel before opening it: firmware and panel-size read, USB/refresh/brightness calibration when needed, settings saved only if something changed. Related settings:

| Key | Neutral value | Effect |
|---|---|---|
| `Brightness` | `-1` | Leaves firmware brightness untouched |
| `UsbPackageSize` | `0` | Auto: 512 at 128×32, 1024 in HD |
| `PanelMinRefreshRate` | `0` | Leaves minimum refresh rate untouched |

## Crisp rendering at 128×32

MarqueeManager always renders at `Width`×`Height`. Media already at 128×32 display pixel-perfect; others are resized with nearest-neighbor filtering (no blur, but native remains best).

!!! tip "The best setting: generate at the right size"
    On the APIExpose side, request DMDs generated at 128×32:

    ```text
    global.apiexpose.marquee_manager.dmd_autogen_profile=128x32
    ```

    After changing the profile, delete the old 256×64 `generated-dmd.png` / `generated-system-dmd.png` files so APIExpose regenerates them.

## The display rotation

The DMD alternates blocks by clear priority: **notification > challenge/leaderboard > timer/score > RA state > MAME `.lay` > base media**. A persistent block stays readable at least 3 seconds (`MinimumBlockDisplayMs=3000`); a notification is locked for its whole duration, then persistent values return.

Up to two active challenge/leaderboard badges sit on the right side of the panel.

## Pinballs: handing over

Pinball games drive their own DMD. The `ActiveSystemsDMD` list triggers the "external control" mode:

```ini
ActiveSystemsDMD=fpinball,pinballfx,pinballfx2,pinballfx3,pinballfm,vpinball,zaccariapinball
```

When a game from these systems starts, MarqueeManager releases the DMD (and only stops the `dmdext` processes it launched itself), then takes over again at the end of the game.

## MAME `.lay`

`.lay` views never release the physical DMD: the `DMD_Only` view is rendered offscreen and sent to the DMD like any other media. Only `ActiveSystemsDMD` grants external control.
