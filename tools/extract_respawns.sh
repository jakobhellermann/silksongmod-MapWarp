#!/usr/bin/env bash
# Regenerate the embedded map datasets:
#   mapwarp_respawns.json    — every safe respawn point (HazardRespawnMarker + TransitionPoint respawn) of every
#                              room, normalized to [0,1] within its scene (RespawnPoints embeds this).
#   mapwarp_scene_sizes.json — each room's tile-map size in game units ({scene: [w, h]}); SceneSizes embeds this
#                              to turn a normalized map position into exact world coordinates.
#
# Requires `rabex` (https://github.com/jakobhellermann/rabex-cli) with `references cat --jq` and the
# `world_position` builtin, plus `jq` and `python3`. Run after a game update to refresh the data.
#
#   tools/extract_respawns.sh [steam-game-name]      # default: silksong
set -euo pipefail

GAME="${1:-silksong}"
RABEX="${RABEX:-rabex}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/mapwarp_respawns.json"
OUT_SIZES="$ROOT/mapwarp_scene_sizes.json"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# The MonoScripts bundle name is version-specific; discover it.
MONO="$("$RABEX" --steam-game "$GAME" bundles 2>/dev/null \
    | grep -oE '[^[:space:]]*monoscripts\.bundle' | head -1)"
[ -n "$MONO" ] || { echo "no monoscripts bundle found" >&2; exit 1; }

ref() { "$RABEX" --steam-game "$GAME" bundle "$MONO" file object "$1" references cat --jq "$2"; }

echo "extracting (this scans every scene, ~1min)..." >&2
# A marker's world position is its GameObject's Transform world position.
ref HazardRespawnMarker '{scene:._scene, p:(go|transform|world_position)}' | jq -sc . > "$TMP/hrm.json"
# A TransitionPoint's respawn is its respawnMarker's position, or its own when it has none.
ref TransitionPoint '{scene:._scene, p:(if (.respawnMarker.path_id // 0) == 0 then go else (.respawnMarker|deref|go) end | transform | world_position)}' | jq -sc . > "$TMP/tp.json"
# Per-scene size to normalize against (= GameManager.GetSceneWidth/Height).
ref tk2dTileMap '{scene:._scene, w:.width, h:.height}' | jq -sc . > "$TMP/tm.json"

python3 - "$TMP" "$OUT" "$OUT_SIZES" <<'PY'
import json, sys, collections
tmp, out, out_sizes = sys.argv[1], sys.argv[2], sys.argv[3]
hrm = json.load(open(f"{tmp}/hrm.json"))
tp  = json.load(open(f"{tmp}/tp.json"))
dims = {d["scene"]: (d["w"], d["h"]) for d in json.load(open(f"{tmp}/tm.json")) if d.get("scene")}

pts = collections.defaultdict(list)
for row in hrm + tp:
    if row.get("scene") and row.get("p") is not None:
        pts[row["scene"]].append((row["p"]["x"], row["p"]["y"]))

data, skipped = {}, 0
for scene, world in sorted(pts.items()):
    wh = dims.get(scene)
    if not wh or wh[0] <= 0 or wh[1] <= 0:
        skipped += 1
        continue
    w, h = wh
    seen, norm = set(), []
    for x, y in world:
        key = (round(x), round(y))  # dedup overlapping markers in world space
        if key in seen:
            continue
        seen.add(key)
        norm.append([round(x / w, 6), round(y / h, 6)])
    data[scene] = norm

json.dump(data, open(out, "w"), separators=(",", ":"), sort_keys=True)
print(f"scenes={len(data)} points={sum(len(v) for v in data.values())} skipped_no_dims={skipped} -> {out}", file=sys.stderr)

sizes = {s: [w, h] for s, (w, h) in dims.items() if w > 0 and h > 0}
json.dump(sizes, open(out_sizes, "w"), separators=(",", ":"), sort_keys=True)
print(f"scene sizes={len(sizes)} -> {out_sizes}", file=sys.stderr)
PY
