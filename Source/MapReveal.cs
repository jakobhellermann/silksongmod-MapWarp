using System;
using HarmonyLib;

namespace BetterMapControls.Source;

// Two independent map cheats, all gates bypassed (config-gated) here:
//   UnlockEntireMap ("act as if the map is unlocked, even where you never went"):
//     * PlayerData.HasAnyMap   — gates the map pane being available in the inventory at all.
//     * ParentInfo.IsUnlocked  — gates opening the quick map and which zones' areas get enabled.
//     * GameMap.IsLostInAbyss* — restrict the map to only the Abyss zone in the Abyss "lost" states.
//   ShowFullMapInQuickmap ("actually show every room, including unexplored ones"):
//     * GameMap.EnableUnlockedAreas postfix — activate + force-map every room (the reliable per-open event;
//       SetupMap does NOT run on every open).
[HarmonyPatch]
internal static class MapReveal {
    private static bool Enabled => BetterMapControlsPlugin.UnlockEntireMap.Value;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameMap), "EnableUnlockedAreas")]
#pragma warning disable HARMONIZE001
    // ReSharper disable once InconsistentNaming
    private static void EnableUnlockedAreas(GameMap __instance) {
#pragma warning restore HARMONIZE001
        // Showing all rooms (vs. UnlockEntireMap, which only unlocks access to the real map).
        if (!BetterMapControlsPlugin.ShowFullMapInQuickmap.Value) return;
        try {
            var scenes = __instance.GetComponentsInChildren<GameMapScene>(true);
            MapUtil.ActivateAllRooms(scenes, __instance.transform);
            foreach (var scene in scenes) {
                scene.SetVisited();
                scene.SetMapped();
            }
        } catch (Exception e) {
            Log.Error(e);
        }
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
