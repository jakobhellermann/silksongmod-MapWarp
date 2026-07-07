using System;
using GlobalEnums;
using HarmonyLib;

namespace MapWarp.Source;

// Two independent, config-gated map cheats
//   UnlockEntireMap ("act as if the map is unlocked, even where you never went"):
//     * PlayerData.HasAnyMap   — gates the map pane being available in the inventory at all.
//     * ParentInfo.IsUnlocked  — gates whether a zone counts as unlocked (open/pan without owning its map).
//     * GameMap.IsLostInAbyss  — restrict the map to only the Abyss zone in the Abyss "lost" states.
//     * GameMap.EnableUnlockedAreas postfix — SetMapped the shown zone's rooms so they render fully mapped
//       instead of the rough "visited" sketch, even without the zone's map item or quill (see below).
//   ShowFullMapInQuickmap ("in the quick map show the whole map, not just the current area"):
//     * GameMap.EnableUnlockedAreas prefix  — widen the quick map's scope to every zone (see below).
//     * GameMap.EnableUnlockedAreas postfix — additionally reveal + map every zone's rooms (see below).
[HarmonyPatch]
internal static class MapReveal {
    private static bool Enabled => MapWarpPlugin.UnlockEntireMap.Value;

    // EnableUnlockedAreas activates a zone iff (zone.IsUnlocked && zone == setCurrent-or-all): the quick map
    // (single Tab) passes the current zone as setCurrent, the world map (double Tab) passes null for "all".
    // Nulling setCurrent makes the quick map show every unlocked zone, like the world map. Scale/position are
    // set in TryOpenQuickMap, not here, so they stay centred on the current zone.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameMap), "EnableUnlockedAreas")]
#pragma warning disable HARMONIZE001
    // ReSharper disable once InconsistentNaming
    private static void EnableUnlockedAreasPrefix(ref MapZone? setCurrent) {
#pragma warning restore HARMONIZE001
        if (MapWarpPlugin.ShowFullMapInQuickmap.Value) setCurrent = null;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameMap), "EnableUnlockedAreas")]
#pragma warning disable HARMONIZE001
    // ReSharper disable once InconsistentNaming
    private static void EnableUnlockedAreas(GameMap __instance) {
#pragma warning restore HARMONIZE001
        // Force rooms to their full mapped sprite instead of the rough sketch. Without the zone's map item the
        // game only ever renders visited rooms as Rough (SetupMap gates SetMapped on hasQuill/scenesMapped), so
        // "act as if the map is unlocked" needs us to SetMapped here on every open.
        //   * UnlockEntireMap: map the zones that EnableUnlockedAreas already made visible (the current zone in
        //     the quick map, every unlocked zone on the world map) — keep the game's scope.
        //   * ShowFullMapInQuickmap: additionally reveal every zone's rooms (widen the quick map's scope).
        var showFull = MapWarpPlugin.ShowFullMapInQuickmap.Value;
        if (!showFull && !Enabled) return;
        try {
            var scenes = __instance.GetComponentsInChildren<GameMapScene>(true);
            if (showFull) MapUtil.ActivateAllRooms(scenes, __instance.transform);
            foreach (var scene in scenes) {
                // Only map rooms whose zone is currently shown; skip zones EnableUnlockedAreas left hidden so
                // UnlockEntireMap alone doesn't leak other zones into the quick map.
                var parent = scene.transform.parent;
                if (parent == null || !parent.gameObject.activeInHierarchy) continue;
                scene.SetVisited();
                scene.SetMapped();
            }
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    // The "→ <Area>" next-area pointers hint at adjacent zones you haven't mapped yet. With the full map
    // shown in the quick map, the target zone is already drawn behind them, so they're redundant clutter.
    // They only ever appear in the quick map (display == isQuickMap), and SetDisplayNextArea fires this
    // Refresh *after* our EnableUnlockedAreas postfix has forced everything mapped — so deactivating them
    // in that postfix wouldn't stick. Force display off here instead.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(MapNextAreaDisplay), "Refresh")]
#pragma warning disable HARMONIZE001
    // ReSharper disable once InconsistentNaming
    private static void MapNextAreaDisplayRefresh(ref bool display) {
#pragma warning restore HARMONIZE001
        if (MapWarpPlugin.ShowFullMapInQuickmap.Value) display = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.HasAnyMap), MethodType.Getter)]
    // ReSharper disable once InconsistentNaming
    private static void HasAnyMap(ref bool __result) {
        if (Enabled) __result = true;
    }

    // In the Abyss "lost" states the game restricts the map to only the Abyss zone (EnableUnlockedAreas
    // hides every other zone) and hides the inventory map content entirely (HasNoMap uses these). Clearing
    // them lets the whole map show as normal.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameMap), nameof(GameMap.IsLostInAbyssPreMap))]
    // ReSharper disable once InconsistentNaming
    private static void IsLostInAbyssPreMap(ref bool __result) {
        if (Enabled) __result = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameMap), nameof(GameMap.IsLostInAbyssPostMap))]
    // ReSharper disable once InconsistentNaming
    private static void IsLostInAbyssPostMap(ref bool __result) {
        if (Enabled) __result = false;
    }

    // GameMap.ParentInfo is a private nested type, so patch its IsUnlocked getter manually rather than via
    // attributes. Called from the plugin's Awake after CreateAndPatchAll; Harmony.UnpatchSelf cleans it up.
#pragma warning disable HARMONIZE004 // manual runtime patch, not an attribute target — analyzer can't infer kind
    internal static void PatchUnlockGate(Harmony harmony) {
        var parentInfo = AccessTools.Inner(typeof(GameMap), "ParentInfo");
        var getter = AccessTools.PropertyGetter(parentInfo, "IsUnlocked");
        harmony.Patch(getter,
            postfix: new HarmonyMethod(AccessTools.Method(typeof(MapReveal), nameof(IsUnlockedPostfix))));
    }
#pragma warning restore HARMONIZE004

    // Applied manually as a postfix in PatchUnlockGate (ParentInfo is a private nested type), so it has no
    // [HarmonyPostfix] attribute for the analyzer to key off — hence the HARMONIZE004 suppression.
#pragma warning disable HARMONIZE004
    // ReSharper disable once InconsistentNaming
    private static void IsUnlockedPostfix(ref bool __result) {
#pragma warning restore HARMONIZE004
        if (Enabled) __result = true;
    }
}
