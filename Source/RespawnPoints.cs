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
// The full dataset is generated offline for every room and embedded as `mapwarp_respawns.json` (see
// `tools/extract_respawns.sh`). A file at `BepInEx/config/mapwarp_respawns.json` overrides/augments it, and
// CaptureCurrentScene records any scene missing from the embedded set as the player enters it (a safety net for
// game updates before the data is regenerated). Format: `{ "Scene_01": [[nx, ny], ...] }`.
internal static class RespawnPoints {
    private const string ResourceName = "mapwarp_respawns.json";

    // Merged view (embedded base with config overrides on top), used by Get.
    private static readonly Dictionary<string, List<Vector2>> Data = new();
    // Only the entries persisted to the config file (overrides + runtime-captured scenes).
    private static readonly Dictionary<string, List<Vector2>> Overrides = new();
    private static bool loaded;

    private static string OverridePath => Path.Combine(Paths.ConfigPath, ResourceName);

    private static void EnsureLoaded() {
        if (loaded) return;
        loaded = true;
        try {
            var embedded = LoadEmbedded();
            foreach (var (scene, pts) in embedded) Data[scene] = pts;

            if (File.Exists(OverridePath))
                foreach (var (scene, pts) in Parse(File.ReadAllText(OverridePath))) {
                    Overrides[scene] = pts;
                    Data[scene] = pts;
                }

            Log.Info($"Respawn points: {embedded.Count} embedded scenes, {Overrides.Count} overrides");
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    // The embedded dataset shipped in the DLL. Matched by suffix so the exact resource namespace doesn't matter.
    private static Dictionary<string, List<Vector2>> LoadEmbedded() {
        var asm = typeof(RespawnPoints).Assembly;
        var name = Array.Find(asm.GetManifestResourceNames(),
            n => n.EndsWith(ResourceName, StringComparison.Ordinal));
        if (name == null) return new Dictionary<string, List<Vector2>>();

        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null) return new Dictionary<string, List<Vector2>>();
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static Dictionary<string, List<Vector2>> Parse(string json) {
        var result = new Dictionary<string, List<Vector2>>();
        var raw = JsonConvert.DeserializeObject<Dictionary<string, List<float[]>>>(json);
        if (raw == null) return result;
        foreach (var (scene, pts) in raw) {
            var list = new List<Vector2>(pts.Count);
            foreach (var p in pts)
                if (p.Length >= 2)
                    list.Add(new Vector2(p[0], p[1]));
            result[scene] = list;
        }

        return result;
    }

    internal static IReadOnlyList<Vector2>? Get(string scene) {
        EnsureLoaded();
        return Data.TryGetValue(scene, out var list) ? list : null;
    }

    // Record the current scene's safe respawn spots — but only when it's missing from the loaded data, so the
    // embedded dataset stays authoritative and we don't rescan every visited room. Mirrors
    // MapTeleport.PlaceAtNearestSafeSpot: every HazardRespawnMarker plus each TransitionPoint's respawn position,
    // deduplicated (a TransitionPoint's respawnMarker is usually itself a HazardRespawnMarker).
    internal static void CaptureCurrentScene(string scene, float width, float height) {
        EnsureLoaded();
        if (width <= 0 || height <= 0 || Data.ContainsKey(scene)) return;

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
        Overrides[scene] = points;
        SaveOverrides();
    }

    private static void SaveOverrides() {
        try {
            var raw = new Dictionary<string, List<float[]>>(Overrides.Count);
            foreach (var (scene, pts) in Overrides) {
                var list = new List<float[]>(pts.Count);
                foreach (var p in pts) list.Add(new[] { p.x, p.y });
                raw[scene] = list;
            }

            File.WriteAllText(OverridePath, JsonConvert.SerializeObject(raw));
        } catch (Exception e) {
            Log.Error(e);
        }
    }
}
