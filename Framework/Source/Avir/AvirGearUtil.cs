using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>Shared gear helpers, so the Elves and Barbarians patches agree with each other.</summary>
    public static class AvirGearUtil
    {
        /// <summary>
        /// Nothing comes off a raider brand new: 65%-100% hit points, skewed AWAY from pristine.
        /// Only about one piece in seven is near-perfect.
        /// </summary>
        public static void Weather(Thing t)
        {
            if (t == null || t.def == null || !t.def.useHitPoints) return;
            if (t.MaxHitPoints <= 1) return;

            float frac;
            float r = Rand.Value;

            if (r < 0.15f)      frac = Rand.Range(0.95f, 1.00f);
            else if (r < 0.55f) frac = Rand.Range(0.80f, 0.95f);
            else                frac = Rand.Range(0.65f, 0.80f);

            t.HitPoints = Mathf.Clamp(Mathf.RoundToInt(t.MaxHitPoints * frac), 1, t.MaxHitPoints);
        }

        /// <summary>
        /// BOOTS AND GLOVES, ALWAYS. Every Avir, every caste, every body type - Hulk included,
        /// Barbarians plate included, hatchlings included. There is no pawn in this faction who
        /// goes barefoot. Feathered fur, because both pieces are Leathery.
        /// </summary>
        public static void WearBootsAndGloves(Pawn pawn, bool masterwork)
        {
            if (pawn == null || pawn.apparel == null) return;

            ThingDef fur = DefDatabase<ThingDef>.GetNamedSilentFail("Leather_AvirFeatheredFur");

            // Our own defs as of the extremity pass. Previously these were the Elves (Continued)
            // boots and gloves, which meant that without that mod installed GetNamedSilentFail
            // returned null and every Avir spawned barefoot and bare-handed, silently.
            WearExtremity(pawn, "Avir_ToeGuards", fur, masterwork);
            WearExtremity(pawn, "Avir_ClawWraps", fur, masterwork);
        }

        private static void WearExtremity(Pawn pawn, string defName, ThingDef fur, bool masterwork)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null) return;

            foreach (Apparel w in pawn.apparel.WornApparel)
            {
                if (!ApparelUtility.CanWearTogether(def, w.def, pawn.RaceProps.body)) return;
            }

            if (def.MadeFromStuff && fur == null) return;

            Apparel a = (Apparel)ThingMaker.MakeThing(def, def.MadeFromStuff ? fur : null);

            CompQuality q = a.TryGetComp<CompQuality>();
            if (q != null)
            {
                QualityCategory cat;
                if (masterwork) cat = QualityCategory.Masterwork;
                else
                {
                    float r = Rand.Value;
                    cat = r < 0.45f ? QualityCategory.Good
                        : r < 0.85f ? QualityCategory.Excellent
                        : QualityCategory.Masterwork;
                }
                q.SetQuality(cat, ArtGenerationContext.Outsider);
            }

            // Feathered fur is slate grey - 92,95,105 - and always reads correctly.
            CompColorable col = a.TryGetComp<CompColorable>();
            if (col != null) col.SetColor(new Color(92f/255f, 95f/255f, 105f/255f));

            Weather(a);
            pawn.apparel.Wear(a, false);
        }
    }
}
