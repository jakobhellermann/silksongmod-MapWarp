using System;
using System.Linq;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace MapWarp.Source;

// Make the quick map (single map button) appear instantly instead of playing its ~0.45s open animation.
// The delay lives entirely in the "Quick Map" PlayMaker FSM (_GameCameras/HudCamera/In-game/Quick Map), on
// its open path:
//   * state "Open"    — Wait(time=0.15) before TryOpenQuickMap even runs
//   * state "Has Map" — FadeNestedFadeGroupV3(Game Map Quads, FadeTime=0.3) fades the map content in
// We zero those two action fields on that single FSM instance and restore the authored values when the toggle
// is off. This is the least invasive hook: no global PlayMaker action patch (Wait is shared by every FSM in
// the game) and no per-frame work — PlayMaker never resets action fields at runtime, so setting them sticks.
// Both actions short-circuit cleanly at 0: Wait fires FINISHED immediately, FadeTo snaps to the target alpha.
// Apply() is driven by MapLifecycle (on GameMap init) and by the config's SettingChanged.
internal static class InstantMapOpen {
    private static bool captured;
    private static float origWait;
    private static float origFade;

    internal static void Apply() {
        try {
            var instant = MapWarpPlugin.InstantMapOpen.Value;
            foreach (var fsm in UnityEngine.Object.FindObjectsByType<PlayMakerFSM>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None)) {
                if (fsm.FsmName != "Quick Map") continue;

                var wait = FindAction<Wait>(fsm, "Open")?.time;
                var fade = FindAction<FadeNestedFadeGroupV3>(fsm, "Has Map")?.FadeTime;
                if (wait == null || fade == null) continue;

                // Capture the authored timings once, before we ever overwrite them.
                if (!captured) {
                    origWait = wait.Value;
                    origFade = fade.Value;
                    captured = true;
                }

                wait.Value = instant ? 0f : origWait;
                fade.Value = instant ? 0f : origFade;
            }
        } catch (Exception e) {
            Log.Error(e);
        }
    }

    private static T? FindAction<T>(PlayMakerFSM fsm, string stateName) where T : FsmStateAction {
        var state = fsm.FsmStates.FirstOrDefault(s => s.Name == stateName);
        return state?.Actions.OfType<T>().FirstOrDefault();
    }
}
