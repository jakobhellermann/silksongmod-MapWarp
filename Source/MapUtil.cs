using UnityEngine;

namespace MapWarp.Source;

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

    // The on-screen rectangle the map render occupies. The game renders at cam.aspect into a texture shown
    // centered on screen with letterbox/pillarbox bars whenever the window aspect differs. Returned as
    // (dx, dy, dw, dh) in screen pixels, bottom-left origin. Reduces to the full screen when aspects match.
    private static (float dx, float dy, float dw, float dh) MapRect(Camera cam) {
        float sw = Screen.width, sh = Screen.height, a = cam.aspect;
        if (sw / sh > a) {
            var dw = sh * a;
            return ((sw - dw) * 0.5f, 0f, dw, sh);
        }

        var dh = sw / a;
        return (0f, (sh - dh) * 0.5f, sw, dh);
    }

    // Camera viewport point (0..1, bottom-left) to on-screen pixels (bottom-left origin), letterbox-corrected.
    internal static Vector2 ViewportToScreen(Camera cam, Vector3 vp) {
        var (dx, dy, dw, dh) = MapRect(cam);
        return new Vector2(dx + vp.x * dw, dy + vp.y * dh);
    }

    // World point to on-screen pixels (bottom-left), for hit-testing against Input.mousePosition.
    // cam.WorldToScreenPoint would return render-texture pixels — wrong under letterboxing / render scale.
    internal static Vector2 WorldToScreen(Camera cam, Vector3 world) =>
        ViewportToScreen(cam, cam.WorldToViewportPoint(world));

    // World point to OnGUI coordinates (top-left origin), letterbox-corrected.
    internal static Vector2 WorldToGui(Camera cam, Vector3 world) {
        var s = WorldToScreen(cam, world);
        return new Vector2(s.x, Screen.height - s.y);
    }
}
