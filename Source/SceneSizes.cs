using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace MapWarp.Source;

// Each room's tile-map size in game units ({scene: [w, h]} = GameManager.GetSceneWidth/Height). Generated
// offline for every room and embedded as `mapwarp_scene_sizes.json` (see `tools/extract_respawns.sh`), so a
// normalized [0,1] map position can be turned into exact world coordinates without loading the scene.
internal static class SceneSizes {
    private const string ResourceName = "mapwarp_scene_sizes.json";

    private static Dictionary<string, Vector2> Data => field ??= LoadEmbedded();

    private static Dictionary<string, Vector2> LoadEmbedded() {
        try {
            var asm = typeof(SceneSizes).Assembly;
            var name = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith(ResourceName, StringComparison.Ordinal));
            if (name == null) return new Dictionary<string, Vector2>();

            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null) return new Dictionary<string, Vector2>();
            using var reader = new StreamReader(stream);
            var raw = JsonConvert.DeserializeObject<Dictionary<string, float[]>>(reader.ReadToEnd());
            var result = new Dictionary<string, Vector2>(raw?.Count ?? 0);
            if (raw != null)
                foreach (var (scene, wh) in raw)
                    if (wh.Length >= 2)
                        result[scene] = new Vector2(wh[0], wh[1]);
            Log.Info($"Scene sizes: {result.Count} embedded scenes");
            return result;
        } catch (Exception e) {
            Log.Error(e);
            return new Dictionary<string, Vector2>();
        }
    }

    internal static Vector2? Get(string scene) => Data.TryGetValue(scene, out var size) ? size : null;
}
