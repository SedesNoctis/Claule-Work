using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    // Integration with [XND] Nocturnal Animals (Mlie.XNDNocturnalAnimals).
    //
    // XND stores an animal's body clock per-SPECIES (a DefModExtension on the
    // ThingDef, plus its settings). There is no per-animal field, so the
    // "induce nocturnal cycle" operation cannot work through XML alone.
    //
    // XND funnels every rest decision through one method that DOES receive the
    // individual pawn:
    //
    //     static bool NocturnalAnimals.JobGiver_GetRest_GetPriority.SleepHourFor(int hour, Pawn pawn)
    //
    // ...which returns true when the pawn should be ASLEEP at that hour. We add a
    // prefix: if the pawn carries the Avir_Nocturnal hediff, we answer as if the
    // pawn were Nocturnal and skip XND's own (species-based) answer. Everything
    // else - the info card, settings, other animals - is untouched.
    //
    // This is a SOFT dependency: if XND is not loaded the target type is absent,
    // the patch is simply never applied, and nothing breaks. It is wired in from
    // AvirHarmony's static constructor, next to the other soft patches.
    public static class NocturnalSurgeryPatch
    {
        // Asleep during the day, awake at night. This is the exact mirror of XND's
        // own Diurnal window (asleep when hour < 7 or hour > 21), so a surgically
        // nocturnal animal lines up with a natively nocturnal one. Tune here if you
        // want a different active window.
        private const int DaySleepStartHour = 7;   // inclusive
        private const int DaySleepEndHour = 21;     // inclusive

        private static HediffDef nocturnalHediffCached;
        private static bool nocturnalHediffResolved;

        private static HediffDef NocturnalHediff
        {
            get
            {
                if (!nocturnalHediffResolved)
                {
                    nocturnalHediffCached = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Nocturnal");
                    nocturnalHediffResolved = true;
                }
                return nocturnalHediffCached;
            }
        }

        // Called from AvirHarmony after PatchAll(). Does nothing if XND is absent.
        public static void Apply(Harmony harmony)
        {
            try
            {
                Type xndType = AccessTools.TypeByName("NocturnalAnimals.JobGiver_GetRest_GetPriority");
                if (xndType == null)
                {
                    // XND not installed - the operation's defs aren't loaded either, so this is normal.
                    return;
                }

                MethodInfo target = AccessTools.Method(xndType, "SleepHourFor", new[] { typeof(int), typeof(Pawn) });
                if (target == null)
                {
                    Log.Warning("[Avir] Found [XND] Nocturnal Animals but not SleepHourFor(int, Pawn); "
                                + "the induce-nocturnal-cycle operation will have no effect. "
                                + "XND may have changed its internals.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(typeof(NocturnalSurgeryPatch), nameof(SleepHourForPrefix)));
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Failed to wire the nocturnal-cycle operation into [XND] Nocturnal Animals: " + e);
            }
        }

        // Prefix on XND's SleepHourFor(int hour, Pawn pawn).
        // Returning false skips the original and uses our __result.
        public static bool SleepHourForPrefix(int hour, Pawn pawn, ref bool __result)
        {
            try
            {
                HediffDef def = NocturnalHediff;
                if (def != null
                    && pawn != null
                    && pawn.health != null
                    && pawn.health.hediffSet != null
                    && pawn.health.hediffSet.HasHediff(def))
                {
                    __result = hour >= DaySleepStartHour && hour <= DaySleepEndHour;
                    return false; // handled; don't run XND's species-based logic
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Nocturnal prefix error: " + e, 0x4E6F6374);
            }

            return true; // not our animal - let XND decide normally
        }
    }
}
