using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Hands every Avir a rhythm marker the moment it becomes visible on a map.
    ///
    /// Spawn is the right hook rather than pawn generation: the marker's only NPC-facing job is
    /// picking the vision stage on KeenSensesStance, and that cannot matter until the pawn is
    /// standing on a tile. Pawns generated and never spawned - caravan stock, world-pawn filler -
    /// cost nothing.
    ///
    /// Everyone is born nocturnal. A'Schialis are the single exception; see
    /// AvirRhythmUtility.IsBornDiurnal.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_AvirRhythmOnSpawn
    {
        public static void Postfix(Pawn __instance)
        {
            try
            {
                if (__instance == null || __instance.Dead || __instance.health == null) return;
                if (!AvirRhythmUtility.IsAvir(__instance)) return;
                if (AvirRhythmUtility.Get(__instance) != null) return;

                bool nocturnal = !AvirRhythmUtility.IsBornDiurnal(__instance)
                                 || AvirRhythmUtility.NightOwlLocked(__instance);

                AvirRhythmUtility.Assign(__instance, nocturnal);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Rhythm assignment on spawn failed: " + e, 0x5B12A9);
            }
        }
    }

    /// <summary>
    /// SUPPRESSES THE VANILLA NIGHT OWL DAYTIME PENALTY ON AVIR - and only that one.
    ///
    /// Vanilla ships TWO Night Owl thoughts, not one:
    ///   ThoughtWorker_IsDayForNightOwl    penalty, hours 11-17, only while awake
    ///   ThoughtWorker_IsNightForNightOwl  bonus,   hours 23-05
    ///
    /// An Avir with the trait is locked nocturnal by GameComponent_AvirRhythm and is already
    /// being judged on its schedule by Avir_RhythmMood. Leaving the vanilla daytime penalty in
    /// place would bill the player twice for the same fact. The NIGHT-TIME BONUS IS LEFT ALONE:
    /// it duplicates nothing of ours and taking it away would be a silent nerf to a vanilla trait.
    ///
    /// Resolved by type name via AccessTools rather than a hard typeof reference, per the
    /// standing pattern - a renamed or removed vanilla worker then costs one log line at startup
    /// instead of stopping the whole assembly from loading. The argument is addressed as __0
    /// because the parameter name on a protected vanilla override is not something worth betting
    /// a build cycle on.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_AvirNightOwlDaytime
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type worker = AccessTools.TypeByName("RimWorld.ThoughtWorker_IsDayForNightOwl");
            if (worker == null)
            {
                Log.Warning("[Avir] ThoughtWorker_IsDayForNightOwl not found - the vanilla Night Owl "
                          + "daytime penalty will stack with the Avir rhythm mood. Harmless, but "
                          + "double-counted.");
                yield break;
            }

            MethodInfo m = AccessTools.Method(worker, "CurrentStateInternal");
            if (m != null) yield return m;
        }

        public static void Postfix(Pawn __0, ref ThoughtState __result)
        {
            try
            {
                if (!__result.Active) return;
                if (!AvirRhythmUtility.IsAvir(__0)) return;

                __result = ThoughtState.Inactive;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Night Owl daytime suppression failed: " + e, 0x5B12AA);
            }
        }
    }
}
