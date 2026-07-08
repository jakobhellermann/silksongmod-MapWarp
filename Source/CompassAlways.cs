using GlobalSettings;
using HarmonyLib;

namespace MapWarp.Source;

// Act as if the Compass tool were equipped — force its IsEquipped getter, which is all that gates the
// player position marker on the map (GameMap.PositionCompassAndCorpse).
[HarmonyPatch]
internal static class CompassAlways {
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ToolItem), nameof(ToolItem.IsEquipped), MethodType.Getter)]
    // ReSharper disable once InconsistentNaming
    private static void IsEquipped(ToolItem __instance, ref bool __result) {
        if (MapWarpPlugin.AlwaysCompass.Value && __instance == Gameplay.CompassTool) __result = true;
    }
}
