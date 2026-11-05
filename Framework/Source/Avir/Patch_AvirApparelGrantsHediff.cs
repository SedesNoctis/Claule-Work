using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Grants/removes an apparel-linked hediff the instant it's worn or removed. Same shape as
    /// AvirApparelAbilitySync - see that file (and Comp_GrantsAbilityWhileWorn's docs) for why
    /// this is an instant Harmony hook rather than ThingComp ticking. Picked up automatically by
    /// AvirHarmony's harmony.PatchAll().
    /// </summary>
    public static class AvirApparelHediffSync
    {
        public static void SyncOne(Pawn pawn, Apparel apparel, bool granting)
        {
            if (pawn?.health?.hediffSet == null || apparel?.def == null)
            {
                return;
            }

            HediffDef hediffDef = apparel.def.GetCompProperties<CompProperties_GrantsHediffWhileWorn>()?.hediffDef;
            if (hediffDef == null)
            {
                return;
            }

            if (granting)
            {
                if (!pawn.health.hediffSet.HasHediff(hediffDef))
                {
                    Hediff hediff = HediffMaker.MakeHediff(hediffDef, pawn);
                    pawn.health.AddHediff(hediff);
                }

                return;
            }

            // Only remove if no OTHER currently-worn apparel grants the same hediff.
            if (AnyOtherWornGrants(pawn, apparel, hediffDef))
            {
                return;
            }

            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (existing != null)
            {
                pawn.health.RemoveHediff(existing);
            }
        }

        private static bool AnyOtherWornGrants(Pawn pawn, Apparel excluding, HediffDef hediffDef)
        {
            List<Apparel> worn = pawn.apparel?.WornApparel;
            if (worn == null)
            {
                return false;
            }

            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i] == excluding)
                {
                    continue;
                }

                if (worn[i].def.GetCompProperties<CompProperties_GrantsHediffWhileWorn>()?.hediffDef == hediffDef)
                {
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelAdded))]
    public static class Patch_AvirApparelHediff_Added
    {
        public static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            AvirApparelHediffSync.SyncOne(__instance.pawn, apparel, granting: true);
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelRemoved))]
    public static class Patch_AvirApparelHediff_Removed
    {
        public static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            AvirApparelHediffSync.SyncOne(__instance.pawn, apparel, granting: false);
        }
    }
}
