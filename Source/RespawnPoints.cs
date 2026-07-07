using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapWarp.Source;

// Safe respawn points (transition / hazard-respawn markers) per scene, stored as normalized [0,1] positions
// within the scene — the same convention MapTeleport uses (world = normalized * sceneSize). The map hover draws
// these on the hovered room's sprite.
//
// The full dataset is meant to be generated offline (all rooms, even unvisited ones) into
// `BepInEx/config/mapwarp_respawns.json`; until then CaptureCurrentScene fills in rooms as the player visits
// them (which also validates the coordinate math against the live markers). Both write the same file/format.
internal static class RespawnPoints {
    // sceneName -> normalized [0,1] positions.
    private static readonly Dictionary<string, List<Vector2>> Data = new();
    private static bool loaded;

    private static string FilePath => Path.Combine(Paths.ConfigPath, "mapwarp_respawns.json");

    // JSON shape: { "Scene_01": [[0.5, 0.2], [0.9, 0.5]], ... }
    private static void EnsureLoaded() {
        if (loaded) return;
        loaded = true;
        try {
            if (!File.Exists(FilePath)) return;
            var raw = JsonConvert.DeserializeObject<Dictionary<string, List<float[]>>>(File.ReadAllText(FilePath));
            if (raw == null) return;
            foreach (var (scene, pts) in raw) {
                var list = new List<Vector2>(pts.Count);
                foreach (var p in pts)
                    if (p.Length >= 2)
                        list.Add(new Vector2(p[0], p[1]));
                Data[scene] = list;
            }

            Log.Info($"Loaded respawn points for {Data.Count} scenes");
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    private static void Save() {
        try {
            var raw = new Dictionary<string, List<float[]>>(Data.Count);
            foreach (var (scene, pts) in Data) {
                var list = new List<float[]>(pts.Count);
                foreach (var p in pts) list.Add(new[] { p.x, p.y });
                raw[scene] = list;
            }

            File.WriteAllText(FilePath, JsonConvert.SerializeObject(raw));
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    internal static IReadOnlyList<Vector2>? Get(string scene) {
        EnsureLoaded();
        return Data.TryGetValue(scene, out var list) ? list : null;
    }

    // Scan the currently loaded scene for safe respawn spots and record them (normalized by the scene size).
    // Mirrors MapTeleport.PlaceAtNearestSafeSpot: every HazardRespawnMarker plus each TransitionPoint's respawn
    // position. Deduplicated because a TransitionPoint's respawnMarker is usually itself a HazardRespawnMarker.
    internal static void CaptureCurrentScene(string scene, float width, float height) {
        EnsureLoaded();
        if (width <= 0 || height <= 0) return;

        // Dedup in world space, rounded to whole units, so overlapping markers collapse to one.
        var seen = new HashSet<(int, int)>();
        var points = new List<Vector2>();

        void Add(Vector2 world) {
            var key = (Mathf.RoundToInt(world.x), Mathf.RoundToInt(world.y));
            if (!seen.Add(key)) return;
            points.Add(new Vector2(world.x / width, world.y / height));
        }

        foreach (var m in Object.FindObjectsByType<HazardRespawnMarker>(FindObjectsSortMode.None))
            Add(m.transform.position);

        foreach (var tp in Object.FindObjectsByType<TransitionPoint>(FindObjectsSortMode.None)) {
            var pos = tp.respawnMarker != null ? tp.respawnMarker.transform.position : tp.transform.position;
            Add(pos);
        }

        if (points.Count == 0) return;

        Data[scene] = points;
        Save();
    }
}
