using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Avir
{
    /// <summary>
    /// NO ONE SELLS AN AVIR. An Avir taken alive would rather die attacking its masters than live
    /// in chains, so no slave trader on the planet - of any faction - keeps one in stock.
    ///
    /// "An Avir" is AvirFaction.ShouldUseAvirContent: the Fearborn gene or an Avir_ pawn kind,
    /// both of which travel with the pawn.
    ///
    /// Two layers, both on vanilla's StockGenerator_Slaves, which every slave trader uses:
    ///   1. Avir factions are taken out of the pool the trader draws slaves from. That picker is a
    ///      compiler-made lambda, so it is found by name; if an update renames it, a warning is
    ///      logged and layer 2 still holds.
    ///   2. Anything that still comes out of the generator as an Avir (an Avir-gened pawn in some
    ///      other faction) is thrown away before it reaches the trader - one slave fewer in stock.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_AvirNeverSoldAsSlaves
    {
        static Patch_AvirNeverSoldAsSlaves()
        {
            Harmony harmony = new Harmony("Tenebris.Avir.NeverSoldAsSlaves");

            try
            {
                Type closure = AccessTools.Inner(typeof(StockGenerator_Slaves), "<>c");
                MethodInfo picker = closure == null ? null
                    : AccessTools.Method(closure, "<GenerateThings>b__2_0", new[] { typeof(Faction) });
                if (picker != null)
                {
                    harmony.Patch(picker, postfix: new HarmonyMethod(typeof(Patch_AvirNeverSoldAsSlaves), nameof(PickerPostfix)));
                }
                else
                {
                    Log.Warning("[Avir] Slave faction picker not found; Avir are still filtered out of slave stock afterwards.");
                }
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Failed to patch the slave faction picker: " + e);
            }

            try
            {
                harmony.Patch(AccessTools.Method(typeof(StockGenerator_Slaves), nameof(StockGenerator_Slaves.GenerateThings)),
                    postfix: new HarmonyMethod(typeof(Patch_AvirNeverSoldAsSlaves), nameof(GenerateThingsPostfix)));
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Failed to patch slave stock generation: " + e);
            }
        }

        public static void PickerPostfix(Faction fac, ref bool __result)
        {
            if (__result && fac != null && AvirFaction.IsAvirFactionDef(fac.def))
            {
                __result = false;
            }
        }

        public static void GenerateThingsPostfix(ref IEnumerable<Thing> __result)
        {
            __result = WithoutAvir(__result);
        }

        private static IEnumerable<Thing> WithoutAvir(IEnumerable<Thing> source)
        {
            if (source == null)
            {
                yield break;
            }
            foreach (Thing thing in source)
            {
                if (thing is Pawn pawn && AvirFaction.ShouldUseAvirContent(pawn))
                {
                    if (!pawn.Destroyed && Find.WorldPawns != null && !Find.WorldPawns.Contains(pawn))
                    {
                        Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
                    }
                    continue;
                }
                yield return thing;
            }
        }
    }
}
