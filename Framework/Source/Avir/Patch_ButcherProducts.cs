using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// Swaps human leather/meat for feathered fur (Chimera Hide under Medieval Overhaul) and avian
    /// meat when a Feathered-Fur gene pawn is butchered. Applied MANUALLY in a guarded static ctor
    /// (NOT via [HarmonyPatch]/PatchAll) so a bad hook can never abort startup.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_ButcherProducts
    {
        private static bool moChecked, moActive;
        private static bool MOActive
        {
            get { if (!moChecked) { moActive = ModsConfig.IsActive("DankPyon.Medieval.Overhaul"); moChecked = true; } return moActive; }
        }

        static Patch_ButcherProducts()
        {
            try
            {
                var m = AccessTools.Method(typeof(Pawn), "ButcherProducts");
                if (m == null) { Log.Warning("[Avir] Pawn.ButcherProducts not found; leather/meat swap disabled."); return; }
                new Harmony("yourname.avir.butcher").Patch(m, postfix: new HarmonyMethod(typeof(Patch_ButcherProducts), nameof(Postfix)));
            }
            catch (Exception e) { Log.Warning("[Avir] failed to patch ButcherProducts: " + e.Message); }
        }

        public static void Postfix(Pawn __instance, ref IEnumerable<Thing> __result)
        {
            if (__instance?.genes == null || __result == null) return;
            if (FeatheredFurDefOf.Avir_FeatheredFur == null) return;
            if (!__instance.genes.HasActiveGene(FeatheredFurDefOf.Avir_FeatheredFur)) return;

            ThingDef hideOut = (MOActive && FeatheredFurDefOf.Avir_ChimeraHide != null)
                ? FeatheredFurDefOf.Avir_ChimeraHide
                : FeatheredFurDefOf.Leather_AvirFeatheredFur;

            List<Thing> list = __result.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                Thing t = list[i];
                if (t == null) continue;
                ThingDef d = t.def;
                bool isLeather = d == FeatheredFurDefOf.Leather_Human
                    || (FeatheredFurDefOf.DankPyon_Leather_Rawhide != null && d == FeatheredFurDefOf.DankPyon_Leather_Rawhide);

                // Leather only. Meat is left exactly as vanilla produced it.
                if (isLeather && hideOut != null) list[i] = MakeLike(hideOut, t.stackCount);
            }
            __result = list;
        }

        private static Thing MakeLike(ThingDef def, int count)
        {
            Thing t = ThingMaker.MakeThing(def);
            t.stackCount = Mathf.Max(1, count);
            return t;
        }
    }
}
