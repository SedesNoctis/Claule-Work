using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Avir traits are commonality 0, so the vanilla roller never hands them out. This postfix is
    /// the only thing that grants them, and it only ever fires on a pawn carrying Fearborn.
    ///
    /// Runs after PawnGenerator.GenerateTraits, so the pawn already has its normal traits; we add
    /// 1-2 Avir ones on top, skipping anything that conflicts with what's already there.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GenerateTraits")]
    public static class Patch_AvirTraits
    {
        private const int MinTraits = 1;
        private const int MaxTraits = 2;

        // Capricious is deliberately rarer than the rest of the pool, so it's excluded from the
        // uniform draw entirely (same as Avir_PridefulTrait) and given its own independent,
        // low-probability roll instead - see the RollForCapricious call in GrantAvirTraits.
        private const float CapriciousChance = 0.05f;

        /// <summary>
        /// KIN-DRAWN AND OUTKIND GET THEIR OWN ROLLS, and both are excluded from the uniform pool.
        ///
        /// They were already in that pool - EnsureCached takes everything prefixed "Avir_" - which
        /// meant each had roughly a one-in-thirty chance, the same as any other trait. That is
        /// flatly wrong for what they describe. Kin-drawn is the species norm turned up a little
        /// too far, not a curiosity; outkind is one Avir in a great many born without the instinct
        /// at all. A uniform draw cannot say either of those things.
        ///
        /// Rolled separately so they also do not eat one of the pawn's 1-2 ordinary trait slots -
        /// an Avir should be able to be kin-drawn AND stormwise.
        ///
        /// OUTKIND IS ROLLED FIRST. The two conflict, and CanGain honours that, so whichever is
        /// tried first wins. Trying the rare one first is what keeps it possible at all; the other
        /// order would let a one-in-four roll swallow a one-in-thirty one nearly every time.
        /// </summary>
        private const float OutkindChance = 0.03f;
        private const float KindrawnChance = 0.25f;

        private static List<TraitDef> avirTraits;

        public static void Postfix(Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.story == null || pawn.story.traits == null) return;
                if (!AvirFaction.ShouldUseAvirContentAtGeneration(pawn)) return;

                GrantAvirTraits(pawn);
                RollForCapricious(pawn);

                // Rare first - see the note on OutkindChance.
                if (!RollFor(pawn, "Avir_Outkind", OutkindChance))
                {
                    RollFor(pawn, "Avir_Kindrawn", KindrawnChance);
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Avir trait assignment failed: " + e, 0x4A51B4);
            }
        }

        /// <summary>Gives the pawn 1-2 Avir traits. Safe to call twice - it won't duplicate.</summary>
        public static void GrantAvirTraits(Pawn pawn)
        {
            EnsureCached();
            if (avirTraits.Count == 0) return;

            int wanted = Rand.RangeInclusive(MinTraits, MaxTraits);

            // Pre-filter by category eligibility BEFORE rolling, not after. Nothing here
            // changes what a pawn CAN end up with - Patch_Avir_TraitCategoryRestriction still
            // vetoes GainTrait regardless - this purely stops an ordinary Predator or Warrior
            // from burning one of its 1-2 roll slots on a High-Artificer-only trait it could
            // never actually receive. See Avir_TraitCategoryRestriction.md.
            List<TraitDef> pool = new List<TraitDef>();
            foreach (TraitDef def in avirTraits)
            {
                if (IsCategoryEligible(pawn, def)) pool.Add(def);
            }

            int given = 0;
            while (given < wanted && pool.Count > 0)
            {
                TraitDef def = pool[Rand.Range(0, pool.Count)];
                pool.Remove(def);

                if (CanGain(pawn, def))
                {
                    pawn.story.traits.GainTrait(new Trait(def, 0, true));
                    given++;
                }
            }

            pawn.Notify_DisabledWorkTypesChanged();
        }

        /// <summary>Independent low-probability roll for Capricious, separate from the normal
        /// 1-2 uniform draw above - this is what keeps it genuinely rarer than the rest of the
        /// pool instead of just another equally-weighted option.</summary>
        private static void RollForCapricious(Pawn pawn)
        {
            RollFor(pawn, "Avir_Capricious", CapriciousChance);
        }

        /// <summary>
        /// One independent roll for a trait that is deliberately not part of the uniform draw.
        /// Returns whether the trait was actually granted, so callers can chain rolls that exclude
        /// one another.
        /// </summary>
        private static bool RollFor(Pawn pawn, string defName, float chance)
        {
            TraitDef def = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
            if (def == null) return false;
            if (Rand.Value >= chance) return false;
            if (!CanGain(pawn, def)) return false;

            pawn.story.traits.GainTrait(new Trait(def, 0, true));
            return true;
        }

        /// <summary>
        /// True for an untagged trait (always eligible - the vast majority of the pool), or a
        /// tagged one whose category appears in this pawn's PawnKindTraitCategoryExtension.
        /// Mirrors Patch_Avir_TraitCategoryRestriction's own check exactly, just run earlier.
        /// </summary>
        private static bool IsCategoryEligible(Pawn pawn, TraitDef def)
        {
            TraitCategoryExtension traitExt = def.GetModExtension<TraitCategoryExtension>();
            if (traitExt == null) return true;

            PawnKindTraitCategoryExtension kindExt =
                pawn?.kindDef?.GetModExtension<PawnKindTraitCategoryExtension>();

            return kindExt != null && kindExt.allowedCategories != null
                && kindExt.allowedCategories.Contains(traitExt.category);
        }

        public static bool IsAvirTrait(TraitDef def)
        {
            EnsureCached();
            return def != null && avirTraits.Contains(def);
        }

        private static bool CanGain(Pawn pawn, TraitDef def)
        {
            if (pawn.story.traits.HasTrait(def)) return false;

            // Respect conflictingTraits in both directions, and vanilla's own exclusion rules.
            List<Trait> held = pawn.story.traits.allTraits;
            for (int i = 0; i < held.Count; i++)
            {
                TraitDef other = held[i].def;

                if (def.conflictingTraits != null && def.conflictingTraits.Contains(other)) return false;
                if (other.conflictingTraits != null && other.conflictingTraits.Contains(def)) return false;
            }

            return true;
        }

        private static void EnsureCached()
        {
            if (avirTraits != null) return;

            avirTraits = new List<TraitDef>();
            foreach (TraitDef t in DefDatabase<TraitDef>.AllDefsListForReading)
            {
                // Everything in Traits_Avir_Fearborn.xml, but never the Prideful trait - that one
                // is granted by its own gene and must not be handed out at random - and never
                // Capricious, which gets its own separate rare roll (RollForCapricious) instead
                // of being just another equally-weighted option in this uniform pool.
                // Kindrawn and Outkind are excluded here for the same reason as Capricious: each
                // has its own roll above, at a rate that means something. Leaving them in would
                // both flatten those rates back to one-in-thirty and let them steal an ordinary
                // trait slot.
                if (t.defName.StartsWith("Avir_") && t != AvirDefOf.Avir_PridefulTrait
                    && t.defName != "Avir_Capricious"
                    && t.defName != "Avir_Kindrawn"
                    && t.defName != "Avir_Outkind")
                {
                    avirTraits.Add(t);
                }
            }
        }
    }
}
