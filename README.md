<p align="center"><img src="thunderstore/icon.png" alt="MapWarp icon" width="128"></p>

# MapWarp

Right-click anywhere on the map to warp there, plus mouse controls and quality-of-life options for
the map.

## Features

- **Zoom & pan** — scroll to zoom toward the cursor, click-drag to pan. Works on both the world map and the
  quick map, and the cursor stays visible while a map is open.
- **Click to teleport** — right-click a room to warp Hornet there. By default you land at the nearest
  transition / safe respawn point (so you never end up stuck in terrain or spikes); hold **Shift** to drop at
  the exact clicked position instead. A label next to the cursor shows which scene you're aiming at.

## Config

Editable in `BepInEx/config/io.github.jakobhellermann.mapwarp.cfg` (or via
[Configuration Manager](https://thunderstore.io/c/hollow-knight-silksong/p/jakobhellermann/BepInExConfigurationManager/)):

**Teleport**

- **Enable teleport** *(default on)* — right-click a room on the map to warp to the nearest safe spot (hold
  Shift for the exact spot).
- **Show respawn points** *(default on)* — when hovering a room on the map, mark its safe respawn points.

**Map**

- **Unlock entire map** *(default on)* — open and pan the whole map even in zones you haven't acquired it for.
- **Show full map in quickmap** *(default off)* — show the entire map instead of just the current area in the
  quickmap.
- **Instant map open** *(default on)* — open the quick map instantly instead of waiting for the open animation.

**Debug**

- **Show Room Borders** *(default off)* — outline each room on the map and label it with its scene name.
