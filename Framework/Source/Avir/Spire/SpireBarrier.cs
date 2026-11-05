using HarmonyLib;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Spire
{
    /// <summary>Under the spire's barrier there is no vacuum: it holds the air as a ship's hull would.</summary>
    [HarmonyPatch(typeof(VacuumUtility), nameof(VacuumUtility.GetVacuum))]
    public static class Patch_SpireBarrierVacuum
    {
        public static void Postfix(IntVec3 cell, Map map, ref float __result)
        {
            if (__result > 0f && BarrierDome.All.Count > 0 && BarrierDome.Seals(map, cell)) __result = 0f;
        }
    }
}
