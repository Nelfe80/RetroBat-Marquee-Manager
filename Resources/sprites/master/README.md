# Sprite masters

This directory contains the immutable, full-resolution GIF sources used to
generate the runtime sprites in the parent directory.

Run `scripts/optimize-sprite-gifs.ps1` from the repository to regenerate every
runtime GIF. The generator:

- preserves the complete animation duration and loop;
- reproduces the runtime's minimum 20 ms frame delay;
- resamples animations above 24 FPS without truncating their timeline;
- limits ordinary sprites to 96 px high and `full_*` backdrops to 320 px high;
- emits full-canvas frames so the runtime decoder does not rebuild GIF frame
  dependencies.

Existing masters are protected by `manifest.json` SHA-256 hashes. Do not replace
them with generated runtime GIFs.
