using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// PawnGroupKindWorker_Trader.CanGenerateFrom requires at least one entry in the
    /// PawnGroupMaker's &lt;carriers&gt; list to be in the destination tile's
    /// BiomeDef.allowedPackAnimals - a flat, hand-authored per-biome whitelist with no
    /// connection to an animal's actual packAnimal flag, temperature tolerance, or wildBiomes
    /// weighting. Vanilla animals are registered into most vanilla biomes; anything added by a
    /// mod (Crow from Odyssey, or any custom carrier added via integration patches, e.g.
    /// ERN_Chocobo/ERN_Amaro/DA_Pilgrim) is registered into ZERO biomes unless that mod's
    /// author specifically remembered to patch it in - so a single missing entry can silently
    /// zero out the Striders' entire Trader groupMaker on any tile whose biome doesn't happen
    /// to carry one of the handful of vanilla animals still left in the list.
    ///
    /// Rather than chase this per-animal, per-biome (an unbounded amount of upkeep every time a
    /// carrier is added by us or by a compatibility patch), the Striders are exempted from this
    /// check entirely for their Trader groupMaker. Thematically this fits: an apex-predator sky
    /// faction isn't limited by which land animals a given biome happens to tolerate. This does
    /// NOT touch any other faction, any other groupKind (raids, settlements, etc.), or the
    /// underlying carrier-validity check Crow originally tripped (a pawn kind whose own
    /// packAnimal flag is false is still rejected during actual pawn generation - this patch
    /// only removes the biome whitelist gate, not the "is this really a pack animal" sanity
    /// check).
    /// </summary>
    [HarmonyPatch(typeof(PawnGroupKindWorker_Trader), nameof(PawnGroupKindWorker_Trader.CanGenerateFrom))]
    public static class Patch_Avir_TraderCaravanBiomeExempt
    {
        public static void Postfix(PawnGroupMakerParms parms, PawnGroupMaker groupMaker, ref bool __result)
        {
            if (__result)
            {
                return; // already usable - nothing to do
            }

            if (groupMaker == null || groupMaker.kindDef != PawnGroupKindDefOf.Trader)
            {
                return;
            }

            if (!AvirFaction.IsAvirFactionDef(parms.faction?.def))
            {
                return;
            }

            // Re-check the one thing that should still legitimately block generation: at
            // least one trader pawn kind must actually be configured. Everything else
            // (points threshold, the biome/carrier whitelist) is intentionally skipped.
            if (groupMaker.traders != null && groupMaker.traders.Any())
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// CanGenerateFrom is only the gate ("can this groupMaker be used at all") - it doesn't
    /// touch the separate carrier-selection step. GenerateCarriers does its OWN biome check
    /// internally when weighting candidates for RandomElementByWeight, completely independent
    /// of the gate above. If every carrier gets biome-zeroed there too, the weighted pick has
    /// nothing to choose from (totalWeight=0) and throws downstream.
    ///
    /// Fixed the same way as the gate, but scoped tighter: a flag is set only for the duration
    /// of GenerateCarriers' own call (via a Finalizer, so it's cleared even if something inside
    /// throws), and BiomeDef.IsPackAnimalAllowed is forced true only while that flag is set -
    /// so this affects nothing outside a single Avir_HighNesting carrier-generation call, and
    /// doesn't touch how any other faction's caravans, or Avir_HighNesting's non-Trader
    /// groupMakers, resolve pack-animal eligibility.
    /// </summary>
    [HarmonyPatch(typeof(PawnGroupKindWorker_Trader), "GenerateCarriers",
        new[] { typeof(PawnGroupMakerParms), typeof(PawnGroupMaker), typeof(Pawn), typeof(List<Thing>), typeof(List<Pawn>) })]
    public static class Patch_Avir_TraderCaravanCarrierBiomeExempt
    {
        [ThreadStatic]
        public static bool Active;

        public static void Prefix(PawnGroupMakerParms parms)
        {
            Active = AvirFaction.IsAvirFactionDef(parms.faction?.def);
        }

        [HarmonyFinalizer]
        public static void Finalizer()
        {
            Active = false;
        }
    }

    [HarmonyPatch(typeof(BiomeDef), nameof(BiomeDef.IsPackAnimalAllowed))]
    public static class Patch_Avir_BiomeIsPackAnimalAllowed
    {
        public static void Postfix(ref bool __result)
        {
            if (!__result && Patch_Avir_TraderCaravanCarrierBiomeExempt.Active)
            {
                __result = true;
            }
        }
    }
}
