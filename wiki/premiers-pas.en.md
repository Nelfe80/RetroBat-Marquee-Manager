# Getting started

Installing MarqueeManager requires **no installer**: download, extract, activate.

## Before you begin

- a working **RetroBat** installation;
- the **[APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose/releases)** plugin installed and running — it feeds media and data to MarqueeManager;
- the **[.NET 8 Desktop runtime](https://dotnet.microsoft.com/download/dotnet/8.0)**;
- at least one secondary screen (marquee, topper…) or a DMD, physical or virtual.

## Installation

1. Download **`MarqueeManager-x.y.z-full.7z`** from the [releases page](https://github.com/Nelfe80/RetroBat-Marquee-Manager/releases).
2. Extract the archive into your `RetroBat\plugins\` folder — you get:

    ```text
    RetroBat\plugins\MarqueeManager\
    ```

3. Close RetroBat if it is running, then double-click **`install-es-start-hook.bat`**.
4. Start RetroBat again: MarqueeManager starts automatically with EmulationStation.

!!! note "What does the hook do?"
    It installs a startup script on the EmulationStation side, without touching anything else in RetroBat. `uninstall-es-start-hook.bat` removes it just as cleanly.

## First setting: your screens

On first launch, open `config.ini` and set in `[Screens]` which Windows screen carries each surface — it is the only truly required setting. Everything is explained in [Screens and surfaces](ecrans.md).

## Check that it works

Browse EmulationStation: the marquee should follow the system, then the selected game. Launch a game: the game's media displays, and at the end of the session the surface returns to selection.

!!! tip "Updating"
    Replace the folder contents with the new archive. Back up your `config.ini` first — configuration migration is automatic, but your customized file remains your reference.
