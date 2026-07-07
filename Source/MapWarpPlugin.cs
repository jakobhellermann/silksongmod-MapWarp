using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MapWarp.Source.Toasts;
using UnityEngine;

namespace MapWarp.Source;

[BepInAutoPlugin("io.github.jakobhellermann.mapwarp")]
public partial class MapWarpPlugin : BaseUnityPlugin {
    internal static ConfigEntry<bool> EnableTeleport = null!;
    internal static ConfigEntry<bool> ShowRoomBorders = null!;
    internal static ConfigEntry<bool> ShowFullMapInQuickmap = null!;
    internal static ConfigEntry<bool> UnlockEntireMap = null!;

    private Harmony harmony = null!;

    private void Awake() {
        Log.Init(Logger);
        Log.Info($"Plugin {Name} ({Id}) has loaded!");

        try {
            EnableTeleport = Config.Bind("Teleport", "Enable teleport", true,
                "Right-click a room on the map to warp to the the nearest safe spot (hold Shift exact spot.)");
            UnlockEntireMap = Config.Bind("Map", "Unlock entire map", true,
                "Open and pan the whole map even in zones you haven't acquired it for.");
            ShowFullMapInQuickmap = Config.Bind("Map", "Show full map in quickmap", false,
                "Show the entire map instead of the current area in quickmap");
            ShowRoomBorders = Config.Bind("Debug", "Show Room Borders", false,
                "Outline each room on the map and label it with its scene name.");

            harmony = Harmony.CreateAndPatchAll(GetType().Assembly);
            MapReveal.PatchUnlockGate(harmony);
            ToastManager.Install();

            // Hot reload: the GameMap may already exist when the plugin (re)loads, so the Start/OnEnable patches
            // won't fire. Install directly in that case.
            if (FindFirstObjectByType<GameMap>() != null)
                MapRoomBorders.Install();
        } catch (Exception e) {
            Log.Info($"Plugin {Name} ({Id}) failed to initialize: {e}");
        }
    }

    private void OnDestroy() {
        // Clean up everything, in order to support hot reloading

        try {
            harmony.UnpatchSelf();

            foreach (var c in FindObjectsByType<MapRoomBorders>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Destroy(c);
            foreach (var c in FindObjectsByType<MapNavigation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Destroy(c);
            foreach (var c in FindObjectsByType<ToastManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Destroy(c.gameObject);
        } catch (Exception e) {
            Log.Info($"Plugin {Name} ({Id}) failed to clean up: {e}");
        }

        Log.Info($"Plugin {Name} ({Id}) has been unloaded!");
    }
}
