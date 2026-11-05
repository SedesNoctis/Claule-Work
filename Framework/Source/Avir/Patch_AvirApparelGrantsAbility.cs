using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Grants/revokes an apparel-linked ability the instant it's worn or removed. Mirrors
    /// Patch_PridefulApparelMood's Notify_ApparelAdded/Notify_ApparelRemoved hooks exactly -
    /// that pattern is already proven to work in this mod; ThingComp.CompTick on worn apparel is
    /// not (see Comp_GrantsAbilityWhileWorn's docs). Picked up automatically by AvirHarmony's
    /// harmony.PatchAll() - no other wiring needed.
    /// </summary>
    public static class AvirApparelAbilitySync
    {
        public static void SyncOne(Pawn pawn, Apparel apparel, bool granting)
        {
            if (pawn?.abilities == null || apparel?.def == null)
            {
                return;
            }

            AbilityDef abilityDef = apparel.def.GetCompProperties<CompProperties_GrantsAbilityWhileWorn>()?.abilityDef;
            if (abilityDef == null)
            {
                return;
            }

            if (granting)
            {
                pawn.abilities.GainAbility(abilityDef);
                return;
            }

            // Only revoke if no OTHER currently-worn apparel grants the same ability - defensive,
            // in case a future item shares an AbilityDef with this one.
            if (!AnyOtherWornGrants(pawn, apparel, abilityDef))
            {
                pawn.abilities.RemoveAbility(abilityDef);
            }
        }

        private static bool AnyOtherWornGrants(Pawn pawn, Apparel excluding, AbilityDef abilityDef)
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

                if (worn[i].def.GetCompProperties<CompProperties_GrantsAbilityWhileWorn>()?.abilityDef == abilityDef)
                {
                    return true;
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelAdded))]
    public static class Patch_AvirApparelAbility_Added
    {
        public static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            AvirApparelAbilitySync.SyncOne(__instance.pawn, apparel, granting: true);
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelRemoved))]
    public static class Patch_AvirApparelAbility_Removed
    {
        public static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            AvirApparelAbilitySync.SyncOne(__instance.pawn, apparel, granting: false);
        }
    }
}
