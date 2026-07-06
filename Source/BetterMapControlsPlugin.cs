using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BetterMapControls.Source;

[BepInAutoPlugin("io.github.jakobhellermann.bettermapcontrols")]
[BepInDependency("io.github.jakobhellermann.devutils")]
public partial class BetterMapControlsPlugin : BaseUnityPlugin {
    internal static ConfigEntry<bool> ShowRoomBorders = null!;
    internal static ConfigEntry<bool> ShowFullMapInQuickmap = null!;
    internal static ConfigEntry<bool> UnlockEntireMap = null!;

    private Harmony harmony = null!;

    private void Awake() {
        Log.Init(Logger);
        Log.Info($"Plugin {Name} ({Id}) has loaded!");

        try {
            ShowRoomBorders = Config.Bind("Map", "Show Room Borders", true,
                "Outline each room on the map and label it with its scene name.");
            ShowFullMapInQuickmap = Config.Bind("Map", "Show full map in quickmap", false,
                "Display every room fully mapped, including ones you haven't explored yet.");
            UnlockEntireMap = Config.Bind("Map", "Unlock entire map", false,
                "Act as if you've acquired every zone's map, so you can open and pan the whole map even where "
                + "you haven't been — but only rooms you've actually explored are filled in (combine with "
                + "\"Show full map in quickmap\" to fill in the rest).");

            harmony = Harmony.CreateAndPatchAll(GetType().Assembly);
            MapReveal.PatchUnlockGate(harmony);

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
        } catch (Exception e) {
            Log.Info($"Plugin {Name} ({Id}) failed to clean up: {e}");
        }

        Log.Info($"Plugin {Name} ({Id}) has been unloaded!");
    }
}
