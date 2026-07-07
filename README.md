# MapWarp

Mouse controls and quality-of-life options for the Hollow Knight: Silksong map.

## Features

- **Zoom & pan** — scroll to zoom toward the cursor, click-drag to pan. Works on both the world map and the
  quick map, and the OS cursor stays visible while a map is open.
- **Click to teleport** — right-click a room to warp Hornet there. By default you land at the nearest
  transition / safe respawn point (so you never end up stuck in terrain or spikes); hold **Shift** to drop at
  the exact clicked position instead. A label next to the cursor shows which scene you're aiming at.

## Config

Editable in `BepInEx/config/io.github.jakobhellermann.mapwarp.cfg` (or via a config-manager mod):

- **RevealEntireMap** *(default off)* — show the whole map as fully explored: every room in every zone, even
  zones whose map you haven't acquired and rooms you haven't visited.
- **ShowRoomBorders** *(default on)* — outline each room on the map and label it with its scene name.
- **ShowAllRoomsInAreaMap** *(default off)* — keep every room in the current area visible, including ones you
  haven't explored yet.
