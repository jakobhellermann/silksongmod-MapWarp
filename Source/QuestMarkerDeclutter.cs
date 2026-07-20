using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace MapWarp.Source;

// The game clamps off-screen quest targets to the viewport edge with a direction arrow. That's useful at the
// normal map zoom, but once you zoom in past it (which this mod makes easy) it just fills the edges with arrows.
// Beyond the game's default zoom, keep quest markers at their real map position instead, so off-screen ones simply
// aren't drawn. Only QuestMapMarker is affected; the shade marker keeps its arrow.
[HarmonyPatch(typeof(MapMarkerArrow), "OnGameMapViewPosUpdated")]
internal static class QuestMarkerDeclutter {
    private static readonly AccessTools.FieldRef<MapMarkerArrow, Vector2> initialPos =
        AccessTools.FieldRefAccess<MapMarkerArrow, Vector2>("initialPos");
    private static readonly AccessTools.FieldRef<MapMarkerArrow, GameObject> arrow =
        AccessTools.FieldRefAccess<MapMarkerArrow, GameObject>("arrow");
    private static readonly AccessTools.FieldRef<MapMarkerArrow, bool> wasOutsideView =
        AccessTools.FieldRefAccess<MapMarkerArrow, bool>("wasOutsideView");

    [HarmonyPrefix]
#pragma warning disable HARMONIZE001
    [UsedImplicitly]
    private static bool Prefix(MapMarkerArrow __instance) {
#pragma warning restore HARMONIZE001
        if (__instance is not QuestMapMarker) return true;
        // Only past the game's default zoom scale; at or below it, keep the normal edge arrow.
        var map = MapTeleport.Current;
        if (map == null || map.transform.localScale.x <= InventoryMapManager.SceneMapEndScale.x) return true;
        if (!__instance.gameObject.activeSelf) return false;

        var t = __instance.transform;
        var pos = initialPos(__instance);
        t.localPosition = new Vector3(pos.x, pos.y, t.localPosition.z);
        var a = arrow(__instance);
        if (a != null) a.SetActive(false);
        wasOutsideView(__instance) = false;
        return false;
    }
}
