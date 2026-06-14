using System;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace BetterMapControls.Source;

[RequireComponent(typeof(Camera))]
public class MapNavigation : MonoBehaviour {
    private const float ZoomSpeed = 0.15f;
    private const float MinSize = 1f;
    private const float MaxSize = 30f;

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
    private GUIStyle? previewStyle;
    private float prevSize = -1f;

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
            if (!cam || !cam.enabled || !cam.gameObject.activeInHierarchy) {
                prevSize = -1f;
                return;
            }

            HandleDrag();
            HandleZoom();
            AnchorZoomToCursor();
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    // The map is mouse-driven, but InputHandler.Update hides the OS cursor every frame unless the game is
    // paused. Force it visible from LateUpdate (runs after all Update()s, so we win); on close this stops and
    // InputHandler hides it again on its own.
    private void LateUpdate() {
        try {
            if (!cam || !cam.enabled || !cam.gameObject.activeInHierarchy) return;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    // Draw the teleport target (the room under the cursor, computed by MapTeleport) next to the cursor.
    private void OnGUI() {
        try {
            if (!cam || !cam.enabled || !cam.gameObject.activeInHierarchy) return;
            var room = MapTeleport.PreviewRoom;
            if (string.IsNullOrEmpty(room)) return;

            previewStyle ??= new GUIStyle(GUI.skin.label) {
                fontSize = 13, fontStyle = FontStyle.Bold, padding = new RectOffset(5, 5, 3, 3),
                normal = { textColor = new Color(0.6f, 1f, 0.6f) }
            };

            var content = new GUIContent(room);
            var size = previewStyle.CalcSize(content);
            var mp = Input.mousePosition;
            var rect = new Rect(mp.x + 16f, Screen.height - mp.y + 8f, size.x, size.y);

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(rect, content, previewStyle);
        } catch (Exception e) {
            Log.Error(e);
        }
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

    private void HandleDrag() {
        if (Input.GetMouseButtonDown(0)) {
            dragOrigin = MouseWorldPoint();
            dragging = true;
        }

        if (!Input.GetMouseButton(0)) {
            dragging = false;
            return;
        }

        if (!dragging) return;
        var current = MouseWorldPoint();
        var delta = dragOrigin - current;
        cam.transform.position += delta;
        if (decoratorCam) decoratorCam!.transform.position += delta;
    }

    private void HandleZoom() {
        var scroll = Input.mouseScrollDelta.y;
        if (scroll == 0) return;
        cam.orthographicSize = Mathf.Clamp(cam.orthographicSize * (1f - scroll * ZoomSpeed), MinSize, MaxSize);
    }

    // Keep the world point under the cursor fixed across ANY orthographicSize change this frame — whoever
    // caused it (our scroll wheel, or the game's own zoom smoothing that keeps driving the size between frames).
    // Working off the per-frame size delta instead of our own scroll value makes this immune to that external
    // driver. Skipped while dragging, which moves the camera deliberately and doesn't touch the size.
    private void AnchorZoomToCursor() {
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
