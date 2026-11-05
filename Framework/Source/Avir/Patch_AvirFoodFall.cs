using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// HOW FAST AN AVIR GETS HUNGRY - two corrections to vanilla's food fall, in one place.
    ///
    /// 1. BIRD MILK. While the milk flows she carries vanilla's Lactating (so she can nurse a chick), and
    ///    Lactating costs food twice: it adds its nutrition-per-day to the fall rate all the time, and it
    ///    pulls food out of her to fill its own reserve. The bird milk is the milk; the reserve is kept
    ///    full for free (HediffComp_BirdMilk) and the constant extra is taken back out here. A mother no
    ///    longer lives at the food shelf.
    ///
    /// 2. ARCANE HUNGER. Below the safe line (60%) the body burns ordinary food to make up for the missing
    ///    essence: on an empty reserve the Avir eats as if its metabolism were 2 lower, scaling in
    ///    linearly from 60% down. (The gene itself carries +2 metabolism to pay for it: an Avir with a
    ///    full reserve eats less than before, one with an empty reserve more.)
    /// </summary>
    [HarmonyPatch(typeof(Need_Food), nameof(Need_Food.FoodFallPerTickAssumingCategory))]
    public static class Patch_AvirFoodFall
    {
        private static readonly AccessTools.FieldRef<Need, Pawn> PawnRef = AccessTools.FieldRefAccess<Need, Pawn>("pawn");

        public static void Postfix(Need_Food __instance, ref float __result)
        {
            Pawn pawn = PawnRef(__instance);
            if (pawn?.genes == null || !ModsConfig.BiotechActive || __result <= 0f) return;
            Gene_ArcaneHunger hunger = Gene_ArcaneHunger.Of(pawn);
            HediffDef flow = AvirBirdMilk.FlowDef;
            bool milk = flow != null && pawn.health.hediffSet.HasHediff(flow);
            if ((hunger == null || !hunger.Active) && !milk) return;

            int met = 0;
            foreach (Gene g in pawn.genes.GenesListForReading)
                if (!g.Overridden) met += g.def.biostatMet;
            float metFactor = GeneTuning.MetabolismToFoodConsumptionFactorCurve.Evaluate(met);

            if (milk)
            {
                Hediff l = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Lactating);
                HediffComp_Lactating comp = l?.TryGetComp<HediffComp_Lactating>();
                if (comp != null) __result = Mathf.Max(0f, __result - comp.AddedNutritionPerDay() / 60000f * metFactor);
            }

            if (hunger != null && hunger.Active && metFactor > 0f)
            {
                float deficit = Mathf.Clamp01(1f - hunger.Value / Gene_ArcaneHunger.SafeThreshold);
                if (deficit > 0f)
                    __result *= GeneTuning.MetabolismToFoodConsumptionFactorCurve.Evaluate(met - 2f * deficit) / metFactor;
            }
        }
    }
}
