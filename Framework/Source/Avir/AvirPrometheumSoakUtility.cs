using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// Mirrors CompAbilityEffect_Combustion's own PrometheumSoaked handling exactly (see
    /// Source/AvirHautsCompat/CompAbilityEffect_Combustion.cs) - a plain
    /// DefDatabase&lt;HediffDef&gt;.GetNamedSilentFail("PrometheumSoaked") lookup, not reflection
    /// into Combat Extended's own types. Whatever mod (CE or otherwise) defines that HediffDef,
    /// if any is loaded, this picks it up the same way Combustion does; if none is found, every
    /// call here is a silent no-op, same as Combustion's own fallback.
    /// </summary>
    public static class AvirPrometheumSoakUtility
    {
        private const float SoakRadius = 4.5f; // "9x9", per fire tile

        private static bool resolved;
        private static HediffDef prometheumSoakedDef;

        private static void EnsureResolved()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            prometheumSoakedDef = DefDatabase<HediffDef>.GetNamedSilentFail("PrometheumSoaked");
        }

        /// <summary>Soaks every pawn within 3x3 of "fireCell" - called once per fire tile Purify
        /// actually creates/refreshes, not once per whole explosion, so a big blast with many
        /// fire tiles ends up with broad overlapping soak coverage around its edges too.</summary>
        public static void SoakAroundFireTile(Map map, IntVec3 fireCell, float severity)
        {
            EnsureResolved();
            if (prometheumSoakedDef == null || map == null)
            {
                return;
            }

            HashSet<Pawn> nearby = new HashSet<Pawn>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(fireCell, SoakRadius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn p)
                    {
                        nearby.Add(p);
                    }
                }
            }

            foreach (Pawn pawn in nearby)
            {
                if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null)
                {
                    continue;
                }

                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(prometheumSoakedDef);
                if (existing != null)
                {
                    existing.Severity = Mathf.Max(existing.Severity, severity);
                }
                else
                {
                    Hediff hediff = HediffMaker.MakeHediff(prometheumSoakedDef, pawn);
                    hediff.Severity = severity;
                    pawn.health.AddHediff(hediff);
                }
            }
        }
    }
}
