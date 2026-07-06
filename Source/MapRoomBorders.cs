using System;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace BetterMapControls.Source;

[RequireComponent(typeof(Camera))]
public class MapRoomBorders : MonoBehaviour {
    private Camera cam = null!;

    private GameMap? gameMap;
    private Material mat = null!;
    private (GameMapScene scene, SpriteRenderer sr)[]? scenes;

    private void Awake() {
        cam = GetComponent<Camera>();
        mat = new Material(Shader.Find("Hidden/Internal-Colored")) {
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private void OnEnable() {
        gameMap = FindFirstObjectByType<GameMap>();
        if (gameMap == null) return;

        var raw = gameMap.GetComponentsInChildren<GameMapScene>(true);
        var list = new List<(GameMapScene, SpriteRenderer)>(raw.Length);
        foreach (var s in raw) {
            var sr = s.GetComponent<SpriteRenderer>();
            // Skip pure map segments (e.g. Bonetown_top_right) so the overlay shows one box/name per real
            // scene, matching what's actually teleportable.
            if (sr != null && MapTeleport.IsLoadableScene(s.Name)) list.Add((s, sr));
        }

        scenes = list.ToArray();
    }

    private void OnDestroy() {
        Destroy(mat);
    }

    private void OnGUI() {
        try {
            if (!BetterMapControlsPlugin.ShowRoomBorders.Value) return;
            if (!cam || !cam.enabled || !cam.gameObject.activeInHierarchy) return;
            if (scenes == null) return;

            // Only label rooms when zoomed in at least to the default view; hide the clutter when zoomed out.
            if (cam.orthographicSize > MapNavigation.DefaultOrthoSize) return;

            foreach (var (scene, sr) in scenes) {
                var vp = cam.WorldToViewportPoint(sr.bounds.center);
                if (vp.z < 0 || vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1) continue;
                var guiPos = ViewportToGui(cam.WorldToViewportPoint(
                    new Vector3(sr.bounds.min.x, sr.bounds.max.y, sr.bounds.center.z)));
                var label = new GUIContent(scene.name);
                var size = GUI.skin.label.CalcSize(label);
                GUI.Label(new Rect(guiPos.x, guiPos.y, size.x, size.y), label);
            }
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    // Map a camera viewport point (0..1, bottom-left) to on-screen GUI coords (top-left). The map renders at
    // cam.aspect into a texture shown centered on screen with letterbox/pillarbox bars, so on-screen space
    // differs from the camera's render space whenever the window aspect doesn't match — WorldToScreenPoint
    // (texture pixels) would be wrong and scale with pan. Reduces to a plain map when the aspects match.
    private Vector2 ViewportToGui(Vector3 vp) {
        float sw = Screen.width, sh = Screen.height, a = cam.aspect;
        float dw, dh, dx, dy;
        if (sw / sh > a) {
            dh = sh;
            dw = sh * a;
            dx = (sw - dw) * 0.5f;
            dy = 0f;
        } else {
            dw = sw;
            dh = sw / a;
            dx = 0f;
            dy = (sh - dh) * 0.5f;
        }

        return new Vector2(dx + vp.x * dw, sh - (dy + vp.y * dh));
    }

    private void OnPostRender() {
        if (!BetterMapControlsPlugin.ShowRoomBorders.Value) return;
        if (!cam || !mat) return;
        if (scenes == null || scenes.Length == 0) return;

        GL.PushMatrix();
        try {
            mat.SetPass(0);
            GL.LoadIdentity();
            GL.LoadOrtho();
            GL.Begin(GL.LINES);
            try {
                foreach (var (scene, sr) in scenes) {
                    var b = sr.bounds;
                    var min = cam.WorldToViewportPoint(b.min);
                    var max = cam.WorldToViewportPoint(b.max);
                    if (max.x < 0 || min.x > 1 || max.y < 0 || min.y > 1) continue;
                    var hue = Mathf.Abs(scene.name.GetHashCode()) % 1000 / 1000f;
                    GL.Color(Color.HSVToRGB(hue, 0.6f, 1f) with { a = 0.8f });
                    DrawRect(min.x, min.y, max.x, max.y);
                }
            } catch (Exception e) {
                Log.Error(e);
            } finally {
                GL.End();
            }
        } finally {
            GL.PopMatrix();
        }
    }

    private static void DrawRect(float x0, float y0, float x1, float y1) {
        GL.Vertex3(x0, y0, 0);
        GL.Vertex3(x1, y0, 0);
        GL.Vertex3(x1, y0, 0);
        GL.Vertex3(x1, y1, 0);
        GL.Vertex3(x1, y1, 0);
        GL.Vertex3(x0, y1, 0);
        GL.Vertex3(x0, y1, 0);
        GL.Vertex3(x0, y0, 0);
    }

    public static void Install() {
        foreach (var old in FindObjectsByType<MapRoomBorders>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Destroy(old);
        var camGo = GameObject.Find("Map Camera");
        if (camGo == null) return;
        camGo.AddComponent<MapRoomBorders>();
        MapNavigation.Install();
    }
}

[HarmonyPatch(typeof(GameMap), "Start")]
internal static class MapRoomBordersPatchStart {
#pragma warning disable HARMONIZE001
    [UsedImplicitly]
    private static void Postfix() {
#pragma warning restore HARMONIZE001
        MapRoomBorders.Install();
    }
}

[HarmonyPatch(typeof(GameMap), "OnEnable")]
internal static class MapRoomBordersPatchEnable {
#pragma warning disable HARMONIZE001
    [UsedImplicitly]
    private static void Postfix() {
#pragma warning restore HARMONIZE001
        MapRoomBorders.Install();
    }
}
