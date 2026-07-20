<p align="center"><img src="https://raw.githubusercontent.com/jakobhellermann/silksongmod-MapWarp/main/thunderstore/icon.png" alt="MapWarp icon" width="128"></p>

# MapWarp

Right-click anywhere on the map to warp there, with pan/zoom mouse controls. Plus options to reveal
the full map, always show compass and outline rooms.

<p align="center"><img src="https://raw.githubusercontent.com/jakobhellermann/silksongmod-MapWarp/main/docs/demo.png" alt="MapWarp demo" width="640"></p>

## Features

- **Zoom & pan**: scroll to zoom toward the cursor, click-drag to pan.
- **Click to teleport**: right-click a room to warp Hornet there. By default you land at the nearest
  transition / safe respawn point, hold **Shift** to drop at the exact clicked position instead.

## Config

Editable in `BepInEx/config/io.github.jakobhellermann.mapwarp.cfg` (or via
[Configuration Manager](https://thunderstore.io/c/hollow-knight-silksong/p/jakobhellermann/BepInExConfigurationManager/)):

**Teleport**

- **Enable teleport** *(default on)*: right-click a room on the map to warp to the nearest safe spot (hold
  Shift for the exact spot).
- **Show respawn points** *(default on)*: when hovering a room on the map, show safe spawn points.

**Map**

- **Unlock entire map** *(default on)*: show the whole map even if you haven't purchased it
- **Show full map in quickmap** *(default off)*: show the entire map instead of just the current area in the
  quickmap.
- **Instant map open** *(default on)*: open the quick map instantly instead of waiting for the open animation.
- **Always show compass** *(default off)*: always show your position on the map, as if the Compass tool were
  equipped.

**Debug**

- **Show Room Borders** *(default off)*: outline each room on the map and label it with its scene name.
