using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// The Psychic Nature effects that have no vanilla StatDef to hang a StatPart on, so they are
    /// done as Harmony postfixes instead:
    ///   - melee cooldown   (no pawn-level "MeleeCooldownFactor" in vanilla)
    ///   - raid threat points ("Avir hide better")
    ///   - Consciousness    (a PawnCapacityDef, not a stat)
    ///
    /// All are wired up from AvirHarmony via AccessTools string lookups: if an engine method can't
    /// be found the patch is skipped with a warning - it can never abort the static ctor. Every
    /// postfix reads the throttled PsychicNatureCache, so none of them recompute psychic
    /// sensitivity on their own.
    ///
    /// The cooldown/raid postfixes find their Pawn / Map by scanning __args, so they don't care
    /// about parameter order or names.
    /// </summary>
    public static class PsychicNaturePatches
    {
        // ---- melee cooldown --------------------------------------------------------
        // Postfix on VerbProperties.AdjustedCooldownTicks(Tool, Pawn, Thing) -> int ticks.
        // -1.5% per step, floored so cooldown never drops below 20% of normal (max 80% faster).
        public static void CooldownPostfix(VerbProperties __instance, ref int __result, object[] __args)
        {
            if (__instance == null || !__instance.IsMeleeAttack || __result <= 0)
            {
                return;
            }

            float steps = PsychicNatureCache.Steps(FindPawn(__args));
            if (steps <= 0f)
            {
                return;
            }

            float factor = Mathf.Max(0.2f, 1f - (0.015f * steps));
            __result = Mathf.Max(1, Mathf.RoundToInt(__result * factor));
        }

        // ---- raid cost -------------------------------------------------------------
        // Postfix on StorytellerUtility.DefaultThreatPointsNow(IIncidentTarget) -> float.
        // Each carrier on the map reduces threat points by 0.5% per step; summed across carriers,
        // floored so points never fall below 25% of normal.
        public static void RaidPostfix(ref float __result, object[] __args)
        {
            if (__result <= 0f)
            {
                return;
            }

            Map map = FindMap(__args);
            if (map == null || map.mapPawns == null)
            {
                return;
            }

            float totalReduction = 0f;
            var colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                totalReduction += 0.005f * PsychicNatureCache.Steps(colonists[i]);
            }

            if (totalReduction <= 0f)
            {
                return;
            }

            float factor = Mathf.Max(0.25f, 1f - totalReduction);
            __result *= factor;
        }

        // ---- consciousness ---------------------------------------------------------
        // Postfix on PawnCapacitiesHandler.GetLevel(PawnCapacityDef) -> float.
        // +0.25% Consciousness per step. This cascades naturally into every capacity that depends
        // on consciousness (moving, manipulation, etc.).
        public static void ConsciousnessPostfix(PawnCapacityDef capacity, ref float __result, Pawn ___pawn)
        {
            if (capacity != PawnCapacityDefOf.Consciousness || __result <= 0f || ___pawn == null)
            {
                return;
            }

            float steps = PsychicNatureCache.Steps(___pawn);
            if (steps <= 0f)
            {
                return;
            }

            __result *= 1f + (0.0025f * steps);
        }

        // ---- arg scanning ----------------------------------------------------------

        private static Pawn FindPawn(object[] args)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] is Pawn p)
                    {
                        return p;
                    }
                }
            }
            return null;
        }

        private static Map FindMap(object[] args)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] is Map m)
                    {
                        return m;
                    }
                }
            }
            return null;
        }
    }
}
