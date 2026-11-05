using HarmonyLib;
using Verse;

namespace Avir
{
    /// <summary>
    /// The amulets used to carry no hit points at all (-1), which the game read as a ruined piece and the
    /// Avir as a tattered one of their own. They now have 1000 hit points; an amulet saved before that
    /// still says -1, so it is given its full strength back when the save loads.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.ExposeData))]
    public static class Patch_AvirAmuletHitPoints
    {
        public static void Postfix(Thing __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            ThingDef d = __instance.def;
            if (d == null || !d.useHitPoints || !d.IsApparel || __instance.HitPoints > 0) return;
            if (!d.defName.StartsWith("Avir_") || !d.defName.Contains("Amulet")) return;
            __instance.HitPoints = __instance.MaxHitPoints;
        }
    }
}
