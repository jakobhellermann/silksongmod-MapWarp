using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapWarp.Source;

[RequireComponent(typeof(Camera))]
public class MapRoomBorders : MonoBehaviour {
    private static MapRoomBorders? active;

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
        active = this;
    }

    private void OnDestroy() {
        if (active == this) active = null;
        Destroy(mat);
    }

    private void OnGUI() {
        try {
            if (!MapWarpPlugin.ShowRoomBorders.Value) return;
            if (!cam || !cam.enabled || !cam.gameObject.activeInHierarchy) return;
            if (scenes == null) return;

            // Only label rooms when zoomed in at least to the default view; hide the clutter when zoomed out.
            if (cam.orthographicSize > MapNavigation.DefaultOrthoSize) return;

            foreach (var (scene, sr) in scenes) {
                // Only rooms the map is actually showing — with "full map in quickmap" off the game leaves other
                // zones' rooms inactive, so this keeps the overlay in sync instead of boxing every room.
                if (!sr.gameObject.activeInHierarchy) continue;
                var vp = cam.WorldToViewportPoint(sr.bounds.center);
                if (vp.z < 0 || vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1) continue;
                // Label at the room's top-left corner, letterbox-corrected (see MapUtil.WorldToGui).
                var guiPos = MapUtil.WorldToGui(cam,
                    new Vector3(sr.bounds.min.x, sr.bounds.max.y, sr.bounds.center.z));
                var label = new GUIContent(scene.name);
                var size = GUI.skin.label.CalcSize(label);
                GUI.Label(new Rect(guiPos.x, guiPos.y, size.x, size.y), label);
            }
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    private void OnPostRender() {
        if (!MapWarpPlugin.ShowRoomBorders.Value) return;
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
                    if (!sr.gameObject.activeInHierarchy) continue;
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

    // The map room's own sprite tint = Silksong's authored area colouring. Used by MapNavigation to colour the
    // hover preview label to match the area it points at. Falls back to white when the room isn't found (map
    // not built yet / non-loadable scene). Alpha forced opaque since the sprite may be mid-fade.
    internal static Color AreaTint(string sceneName) {
        if (active != null && active.scenes != null)
            foreach (var (scene, sr) in active.scenes)
                if (scene.name == sceneName)
                    return sr.color with { a = 1f };
        return Color.white;
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
