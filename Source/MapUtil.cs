using UnityEngine;

namespace BetterMapControls.Source;

internal static class MapUtil {
    // Activate each given GameMapScene and its ancestor chain up to the GameMap root, so rooms the game left
    // inactive (unexplored / locked zones) still render. Takes an already-fetched array (the caller caches it)
    // so this stays allocation-free on per-frame paths — GetComponentsInChildren would allocate every call.
    internal static void ActivateAllRooms(GameMapScene[] scenes, Transform root) {
        foreach (var scene in scenes) {
            var t = scene.transform;
            while (t != null && t != root) {
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                t = t.parent;
            }
        }
    }
}
