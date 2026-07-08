using System;
using System.Collections.Generic;
using MapWarp.Source.Toasts;
using GlobalEnums;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceProviders;
using Object = UnityEngine.Object;

namespace MapWarp.Source;

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

    // Safe hazard-respawn the last PlaceHero applied (null if no safe spot was found). For a cross-scene teleport
    // PositionHeroAtSceneEntrance promotes this into pendingReapplyRespawn so it can be re-applied after
    // FinishedEnteringScene re-anchors the respawn to the hero's landing position (see ReapplyHazardRespawnAfterEntry).
    private static (Vector3 pos, bool facingRight)? lastSafeRespawn;
    private static (Vector3 pos, bool facingRight)? pendingReapplyRespawn;

    // The room (loadable scene) currently under the cursor, updated every frame the map is open and drawn next
    // to the cursor by MapNavigation.OnGUI. Null when no map is open / no room is hovered.
    internal static string? PreviewRoom;

    // World-space sprite bounds of the hovered room (valid only while PreviewRoom != null). MapNavigation.OnGUI
    // maps this room's normalized respawn points onto these bounds to draw them on the map.
    internal static Bounds PreviewRoomBounds;

    // The teleport target under the cursor, shown by MapNavigation.OnGUI under the room name. Game-unit world
    // coordinates when the hovered room is the current scene (size known), else the normalized [0,1] position as
    // a percentage — the destination scene's size isn't known until it loads. Null when no room is hovered.
    internal static string? PreviewTarget;

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
        if (!MapWarpPlugin.EnableTeleport.Value) {
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
        var hasRoom = TryGetRoomUnderCursor(mapCam, out var best, out var normalized, out var bestBounds);

        // Update the cursor preview every frame (drawn by MapNavigation.OnGUI).
        PreviewRoom = hasRoom ? best.Name : null;
        if (hasRoom) PreviewRoomBounds = bestBounds;

        // The preview target is the raw clicked position, i.e. where a Shift teleport lands (a default teleport
        // snaps to the nearest safe spot instead). Only show it while Shift is held so it isn't misleading.
        // world = normalized * the room's embedded scene size, matching exactly where the teleport lands.
        var showTarget = hasRoom && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        PreviewTarget = showTarget && SceneSizes.Get(best.Name) is { } size
            ? $"{normalized.x * size.x:0}, {normalized.y * size.y:0}"
            : null;

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

    // Map room whose on-screen sprite bounds contain the cursor; when several overlap, the one whose nearest
    // respawn point is closest to the cursor wins (see SceneCursorScore). Also returns the cursor's normalized
    // [0,1] position within that room.
    private static bool TryGetRoomUnderCursor(Camera mapCam, out GameMapScene best, out Vector2 normalized,
        out Bounds bounds) {
        best = null!;
        normalized = default;
        bounds = default;
        var mouse = (Vector2)Input.mousePosition;
        var bestDist = float.MaxValue;
        Bounds bestBounds = default;

        foreach (var scene in Object.FindObjectsByType<GameMapScene>(FindObjectsInactive.Exclude,
                     FindObjectsSortMode.None)) {
            var sr = scene.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) continue;

            // Letterbox-corrected on-screen pixels (bottom-left, matching Input.mousePosition) — plain
            // WorldToScreenPoint returns render-texture pixels and misaligns under black bars (see MapUtil).
            var smin = MapUtil.WorldToScreen(mapCam, sr.bounds.min);
            var smax = MapUtil.WorldToScreen(mapCam, sr.bounds.max);
            float xMin = Mathf.Min(smin.x, smax.x), xMax = Mathf.Max(smin.x, smax.x);
            float yMin = Mathf.Min(smin.y, smax.y), yMax = Mathf.Max(smin.y, smax.y);

            if (mouse.x < xMin || mouse.x > xMax) continue;
            if (mouse.y < yMin || mouse.y > yMax) continue;

            // Skip pure map segments (e.g. Bonetown_top_right) — visual sub-pieces of a scene that aren't
            // loadable scenes themselves. Only real scenes are teleport targets, and resolving to the base
            // scene's own GameMapScene also makes the normalized position cover the whole room correctly.
            if (!IsLoadableScene(scene.Name)) continue;

            // Among overlapping matches, pick the one whose content is nearest the cursor (SceneCursorScore).
            var dist = SceneCursorScore(mapCam, scene.Name, sr.bounds, mouse);
            if (dist < bestDist) {
                bestDist = dist;
                best = scene;
                bestBounds = sr.bounds;
            }
        }

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (bestDist == float.MaxValue) return false;

        var bsmin = MapUtil.WorldToScreen(mapCam, bestBounds.min);
        var bsmax = MapUtil.WorldToScreen(mapCam, bestBounds.max);
        normalized = new Vector2(
            Mathf.Clamp01((mouse.x - bsmin.x) / (bsmax.x - bsmin.x)),
            Mathf.Clamp01((mouse.y - bsmin.y) / (bsmax.y - bsmin.y)));
        bounds = bestBounds;
        return true;
    }

    // Screen-space (squared) distance from the cursor to the scene's nearest respawn point — concrete in-room
    // locations, so a smaller value means the cursor is over that scene's actual content. This disambiguates
    // overlapping room boxes better than the box center. Falls back to the box-center distance for scenes with
    // no respawn points; both are screen-pixel sqrMagnitudes, so they're comparable across scenes.
    private static float SceneCursorScore(Camera mapCam, string scene, Bounds worldBounds, Vector2 mouse) {
        var points = RespawnPoints.Get(scene);
        if (points == null || points.Count == 0)
            return (MapUtil.WorldToScreen(mapCam, worldBounds.center) - mouse).sqrMagnitude;

        var best = float.MaxValue;
        foreach (var p in points) {
            var world = new Vector3(worldBounds.min.x + p.x * worldBounds.size.x,
                worldBounds.min.y + p.y * worldBounds.size.y, worldBounds.center.z);
            var d = (MapUtil.WorldToScreen(mapCam, world) - mouse).sqrMagnitude;
            if (d < best) best = d;
        }

        return best;
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

        // A cross-scene teleport still runs FinishedEnteringScene after this, which re-anchors the hazard respawn to
        // the hero's (possibly hazardous) landing position because it can't resolve the "dreamGate" entry gate.
        // Queue the safe spot to be re-applied after that (ReapplyHazardRespawnAfterEntry).
        pendingReapplyRespawn = lastSafeRespawn;
    }

    // Re-apply the safe hazard respawn after a cross-scene teleport. FinishedEnteringScene (run after
    // PositionHeroAtSceneEntrance) sets the respawn to the hero's landing position when the entry gate is
    // unresolved ("dreamGate"); if that landing is inside a hazard, the accepted single death would respawn back
    // into it and loop, each respawn forcing a full blocking GC → the game grinds to ~1 fps. Overriding it here
    // (postfix, so after that assignment) points the respawn at a known-safe spot instead.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(HeroController), "FinishedEnteringScene")]
#pragma warning disable HARMONIZE001
    // ReSharper disable once InconsistentNaming
    private static void ReapplyHazardRespawnAfterEntry(HeroController __instance) {
#pragma warning restore HARMONIZE001
        if (pendingReapplyRespawn is not { } respawn) return;
        pendingReapplyRespawn = null;
        __instance.SetHazardRespawn(respawn.pos, respawn.facingRight);
    }

    // Place the hero for a teleport: by default snap to the nearest guaranteed-safe spot (a transition point or
    // hazard-respawn marker) near the target, or ground-snap the target when the scene has none. Exact (Shift)
    // drops the hero precisely at the clicked position instead — no ground snap, so it may land inside terrain
    // or a hazard (the hazard respawn below still anchors a safe recovery spot).
    private static void PlaceHero(Vector2 target, bool exact) {
        var hasSafeSpot = TryFindNearestSafeSpot(target, out var safeSpot);

        // Anchor the hazard-respawn location to a known-safe spot before the hero can touch anything lethal, so a
        // hazard death recovers after one respawn instead of looping (see ReapplyHazardRespawnAfterEntry for why a
        // cross-scene teleport also needs a re-apply after FinishedEnteringScene).
        var hero = HeroController.instance;
        if (hero == null) return;
        if (hasSafeSpot) {
            hero.SetHazardRespawn(safeSpot, hero.cState.facingRight);
            lastSafeRespawn = (safeSpot, hero.cState.facingRight);
        } else {
            lastSafeRespawn = null;
        }

        if (exact)
            hero.transform.position = new Vector3(target.x, target.y, hero.transform.position.z);
        else if (hasSafeSpot)
            PlaceHeroOnGround(safeSpot);
        else
            PlaceHeroOnGround(target);
    }

    // Nearest transition / hazard-respawn marker to `target`, in the currently loaded scene — both are spots
    // the game itself spawns the hero at, so they're always on safe ground.
    private static bool TryFindNearestSafeSpot(Vector2 target, out Vector3 safeSpot) {
        var bestDist = float.MaxValue;
        safeSpot = Vector3.zero;
        var found = false;

        foreach (var m in Object.FindObjectsByType<HazardRespawnMarker>(FindObjectsSortMode.None)) {
            var d = ((Vector2)m.transform.position - target).sqrMagnitude;
            if (d < bestDist) (bestDist, safeSpot, found) = (d, m.transform.position, true);
        }

        foreach (var tp in Object.FindObjectsByType<TransitionPoint>(FindObjectsSortMode.None)) {
            var pos = tp.respawnMarker != null ? tp.respawnMarker.transform.position : tp.transform.position;
            var d = ((Vector2)pos - target).sqrMagnitude;
            if (d < bestDist) (bestDist, safeSpot, found) = (d, pos, true);
        }

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
