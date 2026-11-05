using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FlightGene;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Avir
{
    /// <summary>
    /// AN AVIR IN SOMEONE ELSE'S HANDS, and the essence options in a colony with no Avir.
    ///
    ///   1. THE ESSENCE OPTIONS ONLY WHERE AN AVIR CAN USE THEM. Essence Feeding and Essence
    ///      Extraction are hidden from the prisoner tab unless the colony has at least one Avir of
    ///      its own - free colonists on any map or in any player caravan. The same thing Biotech
    ///      does for Bloodfeed with hideIfNoBloodfeeders.
    ///
    ///   2. A PRISON WITHOUT A ROOF IS NOT A PRISON. A winged Avir prisoner who could take off
    ///      right now, in a cell with any open sky, breaks out within a couple of hours (MTB
    ///      RooflessEscapeMtbDays) instead of on the usual schedule - and out of a roofless cell an
    ///      escape is a flight.
    ///
    ///   3. AN AVIR SLAVE DOES NOT STAY. Rebellion (and escape) MTB for an Avir slave is a quarter
    ///      of vanilla's. With or without the Kharassians; the Kharassian slavery system adds its
    ///      own rules on top (KharassianSlaveHooks, via the Avir compat folder). Only an Instrument
    ///      - the Avir_Instrumented hediff, defined in Compat/Kharassian - holds one, and when it
    ///      is on, this leaves the slave alone.
    ///
    /// "An Avir" is AvirFaction.ShouldUseAvirContent: the Fearborn gene or an Avir_ pawn kind.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_AvirCaptivity
    {
        /// <summary>Mean days to a break-out for a winged Avir under open sky. 0.1 day = 2.4 hours.</summary>
        public const float RooflessEscapeMtbDays = 0.1f;

        /// <summary>Multiplier on an Avir slave's rebellion MTB. Smaller means more often.</summary>
        public const float SlaveRebellionMtbFactor = 0.25f;

        private static HediffDef instrumented;
        private static bool instrumentedResolved;

        static Patch_AvirCaptivity()
        {
            Harmony harmony = new Harmony("Tenebris.Avir.Captivity");

            // 1. The prisoner tab's visibility test is a local function inside DoPrisonerTab.
            try
            {
                Type closure = AccessTools.Inner(typeof(ITab_Pawn_Visitor), "<>c__DisplayClass8_0");
                MethodInfo canUse = null;
                if (closure != null)
                {
                    foreach (MethodInfo m in AccessTools.GetDeclaredMethods(closure))
                    {
                        if (m.Name.Contains("CanUsePrisonerInteractionMode") && m.ReturnType == typeof(bool))
                        {
                            canUse = m;
                            break;
                        }
                    }
                }
                if (canUse != null)
                {
                    harmony.Patch(canUse, postfix: new HarmonyMethod(typeof(Patch_AvirCaptivity), nameof(CanUseModePostfix)));
                }
                else
                {
                    Log.Warning("[Avir] Prisoner tab visibility test not found; the essence options stay visible in colonies without Avir.");
                }
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Failed to patch the prisoner tab: " + e);
            }

            // 2.
            try
            {
                harmony.Patch(AccessTools.Method(typeof(PrisonBreakUtility), nameof(PrisonBreakUtility.InitiatePrisonBreakMtbDays)),
                    postfix: new HarmonyMethod(typeof(Patch_AvirCaptivity), nameof(PrisonBreakPostfix)));
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Failed to patch prison breaks: " + e);
            }

            // 3. The helper, not the cached outer method: the answer is a fact about the pawn, and
            // vanilla's cache keeps it for us.
            try
            {
                harmony.Patch(AccessTools.Method(typeof(SlaveRebellionUtility), "InitiateSlaveRebellionMtbDaysHelper"),
                    postfix: new HarmonyMethod(typeof(Patch_AvirCaptivity), nameof(SlaveMtbPostfix)));
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Failed to patch slave rebellions: " + e);
            }
        }

        // ------------------------------------------------------------------ 1

        private static PrisonerInteractionModeDef feedMode;
        private static PrisonerInteractionModeDef extractMode;
        private static bool modesResolved;

        private static int colonyCheckedTick = -99999;
        private static bool colonyHasAvir;
        private const int ColonyRecheckTicks = 250;

        public static void CanUseModePostfix(PrisonerInteractionModeDef mode, ref bool __result)
        {
            if (!__result || mode == null)
            {
                return;
            }
            if (!modesResolved)
            {
                modesResolved = true;
                feedMode = DefDatabase<PrisonerInteractionModeDef>.GetNamedSilentFail("Avir_EssenceFarm");
                extractMode = DefDatabase<PrisonerInteractionModeDef>.GetNamedSilentFail("Avir_ExtractEssenceFarm");
            }
            if (mode != feedMode && mode != extractMode)
            {
                return;
            }
            if (!ColonyHasAvir())
            {
                __result = false;
            }
        }

        /// <summary>Any Avir among the player's own free colonists, on any map or in any caravan.</summary>
        public static bool ColonyHasAvir()
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (now - colonyCheckedTick < ColonyRecheckTicks && now >= colonyCheckedTick)
            {
                return colonyHasAvir;
            }
            colonyCheckedTick = now;
            colonyHasAvir = ScanForAvir();
            return colonyHasAvir;
        }

        private static bool ScanForAvir()
        {
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                List<Pawn> colonists = maps[i].mapPawns.FreeColonists;
                for (int j = 0; j < colonists.Count; j++)
                {
                    if (AvirFaction.ShouldUseAvirContent(colonists[j]))
                    {
                        return true;
                    }
                }
            }

            List<Caravan> caravans = Find.WorldObjects?.Caravans;
            if (caravans != null)
            {
                for (int i = 0; i < caravans.Count; i++)
                {
                    if (!caravans[i].IsPlayerControlled)
                    {
                        continue;
                    }
                    List<Pawn> pawns = caravans[i].PawnsListForReading;
                    for (int j = 0; j < pawns.Count; j++)
                    {
                        Pawn p = pawns[j];
                        if (p.IsFreeColonist && AvirFaction.ShouldUseAvirContent(p))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ 2

        public static void PrisonBreakPostfix(Pawn pawn, StringBuilder sb, ref float __result)
        {
            // Vanilla has already said yes in principle (awake, able, in a prison cell); only the
            // timing changes.
            if (__result <= 0f || pawn == null || !pawn.Spawned)
            {
                return;
            }
            if (!AvirFlightUtility.CarriesFlightGene(pawn) || !AvirFlightUtility.CanTakeOff(pawn))
            {
                return;
            }
            Room room = pawn.GetRoom();
            if (room == null || room.OpenRoofCount <= 0)
            {
                return;
            }
            if (__result > RooflessEscapeMtbDays)
            {
                __result = RooflessEscapeMtbDays;
                sb?.AppendLine().Append("Open sky over the cell: an Avir will simply fly out.");
            }
        }

        // ------------------------------------------------------------------ 3

        public static void SlaveMtbPostfix(Pawn pawn, ref float __result)
        {
            if (__result <= 0f || pawn == null || !AvirFaction.ShouldUseAvirContent(pawn))
            {
                return;
            }
            if (!instrumentedResolved)
            {
                instrumentedResolved = true;
                instrumented = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Instrumented");
            }
            if (instrumented != null && pawn.health?.hediffSet != null && pawn.health.hediffSet.HasHediff(instrumented))
            {
                return;
            }
            __result *= SlaveRebellionMtbFactor;
        }
    }
}
