# Getting started

Installing MarqueeManager is a single **installer**: download, run, activate.

## Before you begin

- a working **RetroBat** installation;
- the **[APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose/releases/latest/download/APIExpose-Cabinet-Setup.exe)** plugin installed and running — it feeds media and data to MarqueeManager;
- the **[.NET 8 Desktop runtime](https://dotnet.microsoft.com/download/dotnet/8.0)**;
- at least one secondary screen (marquee, topper…) or a DMD, physical or virtual.

## Installation

1. Download **[`MarqueeManager-Setup.exe`](https://github.com/Nelfe80/RetroBat-Marquee-Manager/releases/latest/download/MarqueeManager-Setup.exe)** from the releases page.
2. Run the installer: it installs the plugin into `RetroBat\plugins\` and registers the EmulationStation start hook — you get:

    ```text
    RetroBat\plugins\MarqueeManager\
    ```

3. Start RetroBat again: MarqueeManager starts automatically with EmulationStation.

!!! note "What does the hook do?"
    The installer registers a startup script on the EmulationStation side, without touching anything else in RetroBat. Uninstalling removes it just as cleanly.

## First setting: your screens

Launch `MarqueeManagerSetup.exe`: on first start, a **three-step wizard** detects your screens, suggests a type for each and lays a working configuration — under three minutes to your first marquee. Everything stays tweakable later in [My setup](mon-setup.md).

## Check that it works

Browse EmulationStation: the marquee should follow the system, then the selected game. Launch a game: the game's media displays, and at the end of the session the surface returns to selection.

!!! tip "Updating"
    Just run the new installer: it updates the plugin in place. Back up your `config.ini` first — configuration migration is automatic, but your customized file remains your reference.
