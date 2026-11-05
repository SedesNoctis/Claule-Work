using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// One (severity, weight) entry in a spawn-roll table. Bigger severity should get a smaller
    /// weight if you want "born already adapted" to be rare - the class doesn't enforce that,
    /// it just rolls whatever table you give it.
    /// </summary>
    public class AdaptationSeverityOption
    {
        public float severity = 0.1f;
        public float weight = 1f;
    }

    /// <summary>
    /// Shared weighted-random roll for "how adapted is this newly-generated pawn already".
    /// Used by both HediffComp_BeakConditioning and HediffComp_ClawConditioning so the two
    /// genes can share one table format and one dice-roll implementation.
    /// </summary>
    public static class AdaptationRandomizer
    {
        /// <summary>
        /// NOBODY IS BORN MASTERED.
        ///
        /// The spawn roll exists so that an adult Avir wandering in from the world has a plausible
        /// history behind its claws - some arrive barely conditioned, some arrive formidable. A
        /// chick has no history. It hatched an hour ago.
        ///
        /// The roll never asked how old the pawn was, so a newborn could and did draw the top of
        /// the table: "Essentium claws (mastered)" on a pawn aged zero days, with the beak beside
        /// it reading "unhoned" purely because that die landed differently.
        ///
        /// Juveniles start at the floor and climb the ladder the way the system was designed to be
        /// climbed - by working. That is the whole point of a conditioning mechanic, and handing a
        /// hatchling the end state gives away the only thing it had to earn.
        /// </summary>
        public static bool BornUnconditioned(Pawn pawn)
        {
            return pawn != null && pawn.DevelopmentalStage.Juvenile();
        }

        public static float RollSeverity(List<AdaptationSeverityOption> options, float fallback)
        {
            if (options == null || options.Count == 0)
            {
                return fallback;
            }

            float totalWeight = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                totalWeight += Mathf.Max(0f, options[i].weight);
            }

            if (totalWeight <= 0f)
            {
                return fallback;
            }

            float roll = Rand.Range(0f, totalWeight);
            float cumulative = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                cumulative += Mathf.Max(0f, options[i].weight);
                if (roll <= cumulative)
                {
                    return options[i].severity;
                }
            }

            // Floating-point edge case: fall through to the last entry.
            return options[options.Count - 1].severity;
        }
    }
}
