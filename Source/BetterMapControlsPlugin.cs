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
    internal static ConfigEntry<bool> ShowAllRoomsInAreaMap = null!;
    internal static ConfigEntry<bool> RevealEntireMap = null!;

    private Harmony harmony = null!;

    private void Awake() {
        Log.Init(Logger);
        Log.Info($"Plugin {Name} ({Id}) has loaded!");

        try {
            ShowRoomBorders = Config.Bind("Map", "Show Room Borders", true,
                "Outline each room on the map and label it with its scene name.");
            ShowAllRoomsInAreaMap = Config.Bind("Map", "Show entire map in quickmap", false,
                "Keep every room in the current area visible, including ones you haven't explored yet.");
            RevealEntireMap = Config.Bind("Map", "Reveal entire map", false,
                "Show the whole map as fully explored — every room in every zone, even zones whose map you "
                + "haven't acquired and rooms you haven't visited.");

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
