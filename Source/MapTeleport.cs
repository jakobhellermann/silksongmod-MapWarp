using System;
using System.Collections.Generic;
using DevUtils.Toasts;
using GlobalEnums;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceProviders;
using Object = UnityEngine.Object;

namespace BetterMapControls.Source;

[HarmonyPatch]
internal static class MapTeleport {
    private static readonly AccessTools.FieldRef<GameMap, Vector2> currentSceneSize =
        AccessTools.FieldRefAccess<GameMap, Vector2>("currentSceneSize");

    // Cross-scene teleports go through a "dreamGate" transition. The destination position isn't known until the new
    // scene's tilemap is loaded, so we stash the click's normalized room position here and resolve it to world
    // coordinates in the PositionHeroAtSceneEntrance postfix once GetSceneWidth/Height are valid for the destination.
    private static bool pendingDreamGate;
    private static Vector2 pendingNormalized;
    private static bool pendingExact;

    // The room (loadable scene) currently under the cursor, updated every frame the map is open and drawn next
    // to the cursor by MapNavigation.OnGUI. Null when no map is open / no room is hovered.
    internal static string? PreviewRoom;

    private static readonly Dictionary<string, bool> loadableCache = new();

    internal static bool IsLoadableScene(string sceneName) {
        if (loadableCache.TryGetValue(sceneName, out var cached)) return cached;

        var key = "Scenes/" + sceneName;
        var loadable = false;
        foreach (var locator in Addressables.ResourceLocators)
            if (locator.Locate(key, typeof(SceneInstance), out _)) {
                loadable = true;
                break;
            }

        loadableCache[sceneName] = loadable;
        return loadable;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameMap), "Update")]
#pragma warning disable HARMONIZE001
    // ReSharper disable once InconsistentNaming
    private static void GameMapUpdate(GameMap __instance) {
#pragma warning restore HARMONIZE001
        // Runs every frame on the game's GameMap.Update — guard so an exception can't break it.
        try {
            HandleMap(__instance);
        } catch (Exception e) {
            PreviewRoom = null;
            Log.Error(e);
        }
    }

    private static void HandleMap(GameMap gameMap) {
        if (!BetterMapControlsPlugin.EnableTeleport.Value) {
            PreviewRoom = null;
            return;
        }

        // The Map Camera renders only while a map (world or quick) is actually open. gameObject.activeInHierarchy on
        // GameMap stays true even when closed, so this is what gates the feature to "a map is open" — and unlike
        // canPan it's also true in the quick map, which is too small to pan.
        var mapCamGo = gameMap.gameObject.activeInHierarchy ? GameObject.Find("Map Camera") : null;
        var mapCam = mapCamGo != null ? mapCamGo.GetComponent<Camera>() : null;
        if (mapCam == null || !mapCam.isActiveAndEnabled) {
            PreviewRoom = null;
            return;
        }

        var gm = GameManager.instance;
        var hasRoom = TryGetRoomUnderCursor(mapCam, out var best, out var normalized);

        // Update the cursor preview every frame (drawn by MapNavigation.OnGUI).
        PreviewRoom = hasRoom ? best.Name : null;

        // Left mouse is used for drag-panning (MapNavigation), so teleport is bound to a discrete right-click.
        if (!Input.GetMouseButtonDown(1)) return;

        if (!hasRoom) {
            ToastManager.Toast("No room under cursor");
            return;
        }

        if (gm == null) return;

        // Default lands at the nearest safe spot (transition / hazard-respawn marker) to the click; holding
        // Shift teleports to the exact clicked position instead (which may be inside terrain or hazards).
        var exact = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        var targetScene = best.Name;
        if (targetScene == gm.sceneName) {
            var sceneSize = currentSceneSize(gameMap);
            PlaceHero(new Vector2(normalized.x * sceneSize.x, normalized.y * sceneSize.y), exact);
            ToastManager.Toast($"Teleported within {targetScene}");
            return;
        }

        if (!IsLoadableScene(targetScene)) {
            ToastManager.Toast($"Can't teleport: scene '{targetScene}' is not loaded");
            return;
        }

        pendingDreamGate = true;
        pendingNormalized = normalized;
        pendingExact = exact;
        // PreventCameraFadeOut: the dreamGate entry path never sends "SCENE FADE IN", so allowing the fade-out would
        // leave the screen black. Suppressing it (a hard cut) matches what DebugMod/PreciseSavestates do for dreamGate.
        gm.BeginSceneTransition(new GameManager.SceneLoadInfo {
            SceneName = targetScene,
            HeroLeaveDirection = GatePosition.unknown,
            EntryGateName = "dreamGate",
            EntryDelay = 0f,
            PreventCameraFadeOut = true,
            WaitForSceneTransitionCameraFade = false
        });
        ToastManager.Toast($"Teleporting to {targetScene}");
    }

    // Map room whose on-screen sprite bounds contain the cursor; when several overlap, the one whose center
    // is closest to the cursor wins. Also returns the cursor's normalized [0,1] position within that room.
    private static bool TryGetRoomUnderCursor(Camera mapCam, out GameMapScene best, out Vector2 normalized) {
        best = null!;
        normalized = default;
        var mouse = (Vector2)Input.mousePosition;
        var bestDist = float.MaxValue;
        Bounds bestBounds = default;

        foreach (var scene in Object.FindObjectsByType<GameMapScene>(FindObjectsInactive.Exclude,
                     FindObjectsSortMode.None)) {
            var sr = scene.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) continue;

            var smin = mapCam.WorldToScreenPoint(sr.bounds.min);
            var smax = mapCam.WorldToScreenPoint(sr.bounds.max);
            float xMin = Mathf.Min(smin.x, smax.x), xMax = Mathf.Max(smin.x, smax.x);
            float yMin = Mathf.Min(smin.y, smax.y), yMax = Mathf.Max(smin.y, smax.y);

            if (mouse.x < xMin || mouse.x > xMax) continue;
            if (mouse.y < yMin || mouse.y > yMax) continue;

            // Skip pure map segments (e.g. Bonetown_top_right) — visual sub-pieces of a scene that aren't
            // loadable scenes themselves. Only real scenes are teleport targets, and resolving to the base
            // scene's own GameMapScene also makes the normalized position cover the whole room correctly.
            if (!IsLoadableScene(scene.Name)) continue;

            // Among overlapping matches, pick the one whose center is nearest the cursor.
            var center = (Vector2)mapCam.WorldToScreenPoint(sr.bounds.center);
            var dist = (center - mouse).sqrMagnitude;
            if (dist < bestDist) {
                bestDist = dist;
                best = scene;
                bestBounds = sr.bounds;
            }
        }

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (bestDist == float.MaxValue) return false;

        var bsmin = mapCam.WorldToScreenPoint(bestBounds.min);
        var bsmax = mapCam.WorldToScreenPoint(bestBounds.max);
        normalized = new Vector2(
            Mathf.Clamp01((mouse.x - bsmin.x) / (bsmax.x - bsmin.x)),
            Mathf.Clamp01((mouse.y - bsmin.y) / (bsmax.y - bsmin.y)));
        return true;
    }

    // dreamGate enters the scene at a raw position resolved by PositionHeroAtSceneEntrance (vanilla can't resolve the
    // "dreamGate" entry point, so it lands at a fallback). We override the final position here, only for teleports we
    // initiated. Patching the caller rather than FindEntryPoint avoids sharing a patch slot with Silksong.DebugMod's
    // greedy dreamGate prefix — this postfix runs after the position is applied, so it wins deterministically.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameManager), "PositionHeroAtSceneEntrance")]
#pragma warning disable HARMONIZE001
    // ReSharper disable once InconsistentNaming
    private static void PositionHeroAtSceneEntrance(GameManager __instance) {
#pragma warning restore HARMONIZE001
        if (!pendingDreamGate) return;
        pendingDreamGate = false;
        PlaceHero(new Vector2(pendingNormalized.x * __instance.GetSceneWidth(),
            pendingNormalized.y * __instance.GetSceneHeight()), pendingExact);
    }

    // Place the hero for a teleport: by default snap to the nearest guaranteed-safe spot (a transition point
    // or hazard-respawn marker) near the target; if exact (Shift) or none is found, drop onto the ground at
    // the target itself.
    private static void PlaceHero(Vector2 target, bool exact) {
        if (exact || !PlaceAtNearestSafeSpot(target))
            PlaceHeroOnGround(target);
    }

    // Nearest transition / hazard-respawn marker to `target`, in the currently loaded scene — both are spots
    // the game itself spawns the hero at, so they're always on safe ground.
    private static bool PlaceAtNearestSafeSpot(Vector2 target) {
        var bestDist = float.MaxValue;
        var best = Vector3.zero;
        var found = false;

        foreach (var m in Object.FindObjectsByType<HazardRespawnMarker>(FindObjectsSortMode.None)) {
            var d = ((Vector2)m.transform.position - target).sqrMagnitude;
            if (d < bestDist) (bestDist, best, found) = (d, m.transform.position, true);
        }

        foreach (var tp in Object.FindObjectsByType<TransitionPoint>(FindObjectsSortMode.None)) {
            var pos = tp.respawnMarker != null ? tp.respawnMarker.transform.position : tp.transform.position;
            var d = ((Vector2)pos - target).sqrMagnitude;
            if (d < bestDist) (bestDist, best, found) = (d, pos, true);
        }

        if (found) PlaceHeroOnGround(best);
        return found;
    }

    // A raw normalized position almost always lands inside terrain. FindGroundPoint is the game's own ground-snap
    // (used on scene entry / respawn): it raycasts down from the target onto the terrain and accounts for the hero's
    // collider height. useExtended searches the full scene height, so the click drops onto the floor beneath it.
    private static void PlaceHeroOnGround(Vector2 target) {
        var hero = HeroController.instance;
        if (hero == null) return;
        var ground = hero.FindGroundPoint(target, true);
        hero.transform.position = new Vector3(ground.x, ground.y, hero.transform.position.z);
    }
}
