using System;
using HarmonyLib;
using JetBrains.Annotations;

namespace MapWarp.Source;

// Central "the GameMap (re)initialised" hook. Patched once on GameMap.Start and GameMap.OnEnable, then
// dispatched to every feature that needs to (re)install itself when a map appears — on scene entry, a fresh
// game load, or a hot reload. Features add their init call to Dispatch() here instead of each declaring its
// own GameMap lifecycle patch. Each call is isolated so one failing feature doesn't skip the rest.
[HarmonyPatch]
internal static class MapLifecycle {
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameMap), "Start")]
#pragma warning disable HARMONIZE001
    [UsedImplicitly]
    private static void Start() => Dispatch();
#pragma warning restore HARMONIZE001

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameMap), "OnEnable")]
#pragma warning disable HARMONIZE001
    [UsedImplicitly]
    private static void OnEnable() => Dispatch();
#pragma warning restore HARMONIZE001

    // Also called directly from the plugin's Awake so a hot reload (GameMap already present, so the patches
    // above won't fire) still initialises. Every handler is a no-op when no map is present.
    internal static void Dispatch() {
        Run(MapRoomBorders.Install);
        Run(InstantMapOpen.Apply);
    }

    private static void Run(Action handler) {
        try {
            handler();
        } catch (Exception e) {
            Log.Error(e);
        }
    }
}
