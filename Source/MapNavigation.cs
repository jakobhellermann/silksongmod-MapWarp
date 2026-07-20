using System;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace MapWarp.Source;

[RequireComponent(typeof(Camera))]
public class MapNavigation : MonoBehaviour {
    private const float ZoomSpeed = 0.15f;
    private const float MinSize = 1f;
    private const float MaxSize = 30f;
    // Map localScale bounds for the zoomed (scene map) view, where zoom scales the map instead of the camera.
    // Floor at the game's smallest zoom scale so you can't zoom out past its default view.
    private static readonly float MinScale = InventoryMapManager.SceneMapStartScale.x;
    private const float MaxScale = 8f;

    // The Map Camera's (and Decorator Camera's) default orthographic size, from the persistent GameCameras
    // rig. Both cameras sit at localPosition (0,0,0); the game pans/zooms by transforming the GameMap object,
    // never these cameras, so this is a fixed constant — no need to capture it at runtime. ResetView restores
    // it on every map open. Recompute (e.g. after a game update) with:
    //   rabex --steam-game silksong bundle coremanagers_assets__gamecameras.bundle \
    //     file object "_GameCameras/HudCamera/In-game/Game Map Rendering/Map Camera@Camera" cat | grep "orthographic size"
    internal const float DefaultOrthoSize = 8.710663795471191f;

    private Camera cam = null!;
    private Camera? decoratorCam;
    private bool dragging;
    private Vector3 dragOrigin;
    private Vector3 dragMapLocal;
    private GUIStyle? previewStyle;
    private float prevSize = -1f;

    // True while a map is open. The InputHandler patch below reads this to keep the OS cursor visible and
    // unlocked on the map. Without it the game locks the cursor when idle (everywhere but menus), which both
    // warps the cursor to screen-center and makes Input.mousePosition read center.
    internal static bool MapOpen;

    private void Awake() {
        cam = GetComponent<Camera>();
        var decoratorGo = GameObject.Find("Decorator Camera");
        decoratorCam = decoratorGo != null ? decoratorGo.GetComponent<Camera>() : null;
        // Also reset here so a hot reload (which re-adds this component) while the map is open and panned
        // snaps back to the default view, not just on the next WorldMap/TryOpenQuickMap.
        ResetThis();
    }

    private void Update() {
        try {
            MapOpen = cam && cam is { enabled: true, gameObject.activeInHierarchy: true };
            if (!MapOpen) {
                prevSize = -1f;
                return;
            }

            // The zoomed scene map when it's big enough to pan, else null (wide map fits on screen). We pan/zoom
            // this instead of the camera so the camera stays at the default pose the game's marker code assumes.
            var map = MapTeleport.Current;
            var pannable = map != null && map.CanStartPan() ? map : null;

            HandleDrag(pannable);
            HandleZoom(pannable);
            AnchorZoomToCursor(pannable);
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    private void OnDisable() => MapOpen = false;

    // Draw the teleport target (the room under the cursor, computed by MapTeleport) next to the cursor,
    // plus the room's known safe respawn points as markers on its map sprite.
    private void OnGUI() {
        try {
            if (!cam || !cam.enabled || !cam.gameObject.activeInHierarchy) return;
            var room = MapTeleport.PreviewRoom;
            if (string.IsNullOrEmpty(room)) return;

            DrawRespawnPoints();

            previewStyle ??= new GUIStyle(GUI.skin.label) {
                fontSize = 13, fontStyle = FontStyle.Bold, padding = new RectOffset(5, 5, 3, 3), richText = true
            };
            // Tint the label with the room's own area colour (its map sprite tint).
            previewStyle.normal.textColor = MapRoomBorders.AreaTint(room);

            // Room name, with the exact teleport target on a smaller second line below it.
            var target = MapTeleport.PreviewTarget;
            var content = new GUIContent(string.IsNullOrEmpty(target) ? room : $"{room}\n<size=11>{target}</size>");
            var size = previewStyle.CalcSize(content);
            // Input.mousePosition (screen space, bottom-left origin) — absolute, so unlike
            // Event.current.mousePosition it isn't affected by GUI-matrix state between OnGUI passes.
            var mp = Input.mousePosition;
            // Place the label up-left of the cursor (offset by its own size) so the cursor never covers it.
            var rect = new Rect(mp.x - size.x, Screen.height - mp.y - size.y, size.x, size.y);

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(rect, content, previewStyle);
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    // Mark the safe respawn points of every room currently under the cursor (MapTeleport.PreviewCandidates) — so
    // where room boxes overlap you see all of their spots, not just the selected room's. Points are stored
    // normalized [0,1] within a scene; each room's own map-sprite bounds map normalized -> world -> screen.
    private void DrawRespawnPoints() {
        if (!MapWarpPlugin.ShowRespawnPoints.Value) return;

        const float s = 12f;
        var prev = GUI.color;
        GUI.color = Color.white;
        foreach (var (room, b) in MapTeleport.PreviewCandidates) {
            if (b.size.x <= 0f || b.size.y <= 0f) continue;
            var points = RespawnPoints.Get(room);
            if (points == null) continue;
            foreach (var p in points) {
                var world = new Vector3(b.min.x + p.x * b.size.x, b.min.y + p.y * b.size.y, 0f);
                // Letterbox-corrected (see MapUtil.WorldToGui) — cam.WorldToScreenPoint returns render-texture
                // pixels, which drift from the on-screen map when the window aspect adds black bars.
                var g = MapUtil.WorldToGui(cam, world);
                GUI.DrawTexture(new Rect(g.x - s / 2f, g.y - s / 2f, s, s), DiamondTexture);
            }
        }

        GUI.color = prev;
    }

    // Diamond marker (dark border + teal fill) baked into one texture. Border width is measured as the
    // perpendicular distance to the fill edge, so it stays uniform along all four sides (a scaled second
    // diamond would bulge at the tips). Built once.
    private static Texture2D DiamondTexture => field ??= BuildDiamond(24, borderPx: 3f);

    private static Texture2D BuildDiamond(int size, float borderPx) {
        var fill = new Color(0.55f, 0.82f, 0.78f, 0.95f);
        var border = new Color(0f, 0f, 0f, 0.8f);
        const float sqrt2 = 1.4142136f;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var c = (size - 1) / 2f;
        var fillR = c - borderPx * sqrt2; // Manhattan radius of the fill; tips reach the texture edge at c.
        var px = new Color[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++) {
            // Perpendicular pixels past the fill edge (negative = inside fill).
            var dp = (Mathf.Abs(x - c) + Mathf.Abs(y - c) - fillR) / sqrt2;
            var fillCov = Mathf.Clamp01(0.5f - dp);          // 1 inside fill, AA across ~1px
            var outerCov = Mathf.Clamp01(0.5f - (dp - borderPx)); // fill + border silhouette
            var rgb = Color.Lerp(border, fill, fillCov);
            px[y * size + x] = new Color(rgb.r, rgb.g, rgb.b, outerCov * Mathf.Lerp(border.a, fill.a, fillCov));
        }

        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // Reset pan/zoom to the camera's known defaults. Called when a map (world or quick) opens, so a map
    // always opens at its default view instead of wherever the user last left it.
    internal static void ResetView() {
        var nav = FindFirstObjectByType<MapNavigation>(FindObjectsInactive.Include);
        if (nav != null) nav.ResetThis();
    }

    private void ResetThis() {
        if (!cam) return;
        cam.orthographicSize = DefaultOrthoSize;
        cam.transform.localPosition = Vector3.zero;
        if (decoratorCam != null) {
            decoratorCam.orthographicSize = DefaultOrthoSize;
            decoratorCam.transform.localPosition = Vector3.zero;
        }
    }

    // The map camera renders to a render-scaled RenderTexture, so cam.ScreenToWorldPoint would
    // divide the mouse pixels by the texture size instead of the screen size. Go through viewport
    // (normalized 0..1) coordinates derived from Screen, which is resolution-independent.
    private Vector3 MouseWorldPoint() {
        var mp = Input.mousePosition;
        var viewport = new Vector3(mp.x / Screen.width, mp.y / Screen.height, 0f);
        return cam.ViewportToWorldPoint(viewport);
    }

    private void HandleDrag(GameMap? map) {
        if (Input.GetMouseButtonDown(0)) {
            dragOrigin = MouseWorldPoint();
            if (map != null) dragMapLocal = map.transform.localPosition;
            dragging = true;
        }

        if (!Input.GetMouseButton(0)) {
            dragging = false;
            return;
        }

        if (!dragging) return;
        var current = MouseWorldPoint();

        if (map != null) {
            // Move the map (camera fixed) via the game's own UpdateMapPosition; absolute from the grab anchor.
            var localDelta = map.transform.parent.InverseTransformVector(current - dragOrigin);
            map.UpdateMapPosition(new Vector2(dragMapLocal.x + localDelta.x, dragMapLocal.y + localDelta.y));
            return;
        }

        var delta = dragOrigin - current;
        cam.transform.position += delta;
        if (decoratorCam) decoratorCam!.transform.position += delta;
    }

    private void HandleZoom(GameMap? map) {
        var scroll = Input.mouseScrollDelta.y;
        if (scroll == 0) return;

        if (map != null) {
            // Zoom by scaling the map (camera fixed), composing on the game's own zoom scale.
            var t = map.transform;
            var cursorWorld = MouseWorldPoint();
            var pivotLocal = t.InverseTransformPoint(cursorWorld);
            var s = Mathf.Clamp(t.localScale.x * (1f + scroll * ZoomSpeed), MinScale, MaxScale);
            t.localScale = new Vector3(s, s, t.localScale.z);
            // Reposition so the map point under the cursor stays put.
            var worldShift = cursorWorld - t.TransformPoint(pivotLocal);
            var localShift = t.parent.InverseTransformVector(worldShift);
            map.UpdateMapPosition(new Vector2(t.localPosition.x + localShift.x, t.localPosition.y + localShift.y));
            return;
        }

        cam.orthographicSize = Mathf.Clamp(cam.orthographicSize * (1f - scroll * ZoomSpeed), MinSize, MaxSize);
    }

    // Wide-map (camera-ortho) zoom only: keep the point under the cursor fixed across any orthographicSize change
    // this frame — ours or the game's zoom smoothing. Works off the per-frame size delta so it's immune to that
    // external driver. Skipped while dragging (which moves the camera and doesn't touch the size).
    private void AnchorZoomToCursor(GameMap? map) {
        // Zoomed view scales the map, not the camera — force the camera back to its default pose (which marker
        // placement assumes, clearing any leftover wide-map pan/zoom) and skip the camera anchor.
        if (map != null) {
            if (!Mathf.Approximately(cam.orthographicSize, DefaultOrthoSize) ||
                cam.transform.localPosition != Vector3.zero) {
                cam.orthographicSize = DefaultOrthoSize;
                cam.transform.localPosition = Vector3.zero;
                if (decoratorCam != null) {
                    decoratorCam.orthographicSize = DefaultOrthoSize;
                    decoratorCam.transform.localPosition = Vector3.zero;
                }
            }
            prevSize = DefaultOrthoSize;
            return;
        }

        var size = cam.orthographicSize;
        if (decoratorCam) decoratorCam!.orthographicSize = size;

        if (!dragging && prevSize > 0f && !Mathf.Approximately(size, prevSize)) {
            // shift = (worldUnderCursor at prevSize) - (worldUnderCursor at size), same camera position.
            var mp = Input.mousePosition;
            var k = 2f * (prevSize - size);
            var shift = new Vector3(k * (mp.x / Screen.width - 0.5f) * cam.aspect,
                k * (mp.y / Screen.height - 0.5f), 0f);
            cam.transform.position += shift;
            if (decoratorCam) decoratorCam!.transform.position += shift;
        }

        prevSize = size;
    }

    public static void Install() {
        foreach (var old in FindObjectsByType<MapNavigation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Destroy(old);
        var camGo = GameObject.Find("Map Camera");
        if (camGo == null) return;
        camGo.AddComponent<MapNavigation>();
    }
}

[HarmonyPatch(typeof(GameMap), "WorldMap")]
internal static class MapNavigationPatchWorldMap {
    [UsedImplicitly]
    private static void Postfix() {
        MapNavigation.ResetView();
    }
}

[HarmonyPatch(typeof(GameMap), "TryOpenQuickMap")]
internal static class MapNavigationPatchQuickMap {
    [UsedImplicitly]
    private static void Postfix() {
        MapNavigation.ResetView();
    }
}

// While a map is open, force the game's own cursor handling to keep the OS cursor enabled (visible +
// unlocked). InputHandler otherwise locks the cursor whenever there's no mouse movement (everywhere but
// menus), which warps it to screen-center and makes Input.mousePosition read center — breaking the mouse
// features and the hover preview whenever the cursor is held still.
[HarmonyPatch(typeof(InputHandler), "SetCursorEnabled", typeof(bool))]
internal static class MapNavigationPatchCursor {
#pragma warning disable HARMONIZE001
    [UsedImplicitly]
    private static void Prefix(ref bool isEnabled) {
#pragma warning restore HARMONIZE001
        if (MapNavigation.MapOpen) isEnabled = true;
    }
}
