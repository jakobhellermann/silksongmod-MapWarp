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
    internal static ConfigEntry<bool> InstantMapOpen = null!;
    internal static ConfigEntry<bool> ShowRespawnPoints = null!;
    internal static ConfigEntry<bool> AlwaysCompass = null!;

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
            InstantMapOpen = Config.Bind("Map", "Instant map open", true,
                "Open the quick map instantly instead of waiting for the open animation.");
            InstantMapOpen.SettingChanged += (_, _) => MapWarp.Source.InstantMapOpen.Apply();
            ShowRoomBorders = Config.Bind("Debug", "Show Room Borders", false,
                "Outline each room on the map and label it with its scene name.");
            ShowRespawnPoints = Config.Bind("Teleport", "Show respawn points", true,
                "When hovering a room on the map, mark its safe respawn points (transition / hazard-respawn spots).");
            AlwaysCompass = Config.Bind("Map", "Always show compass", false,
                "Always show your position on the map, as if the Compass tool were equipped.");

            harmony = Harmony.CreateAndPatchAll(GetType().Assembly);
            MapReveal.PatchUnlockGate(harmony);
            ToastManager.Install();

            // Hot reload: the GameMap may already exist when the plugin (re)loads, so MapLifecycle's Start/
            // OnEnable patches won't fire. Dispatch directly (each handler is a no-op when no map is present).
            MapLifecycle.Dispatch();
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
