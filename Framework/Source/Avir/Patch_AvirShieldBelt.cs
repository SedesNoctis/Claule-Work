using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// EVERY Avir OF THE A'SHAIGN wears a shield belt. No exceptions, no rolls. In the
    /// A'Schialis Legion it is restricted to the three command ranks - see AllowedBelt().
    ///
    /// It is the one piece of kit that is not an affectation. The Avir fight in melee - beak,
    /// claws, an essentium blade - and close the distance from the air. A belt that eats
    /// incoming fire on the way in is not a luxury for them, it is the entire tactic. It is
    /// also the clearest statement of what they actually are: a species that folded psychic
    /// light into flesh and then went and built a field generator to wear on its hip.
    ///
    /// PREFERENCE ORDER
    ///   1. CeleTech Arsenal's Type-ZG shield belt MKI  (CMC_ZGRangedShieldBelt_MKI)
    ///      - the MKI ONLY. The MKII exists and is roughly three times stronger; the Avir do
    ///        not get it. Good quality, fixed - not Excellent, not Masterwork. This is the one
    ///        place where Avir gear is deliberately held back.
    ///   2. Vanilla ShieldBelt, if CeleTech is not installed.
    ///
    /// Self-disabling in the usual way: both defs are found by name at startup. If neither
    /// exists, nothing is worn and nothing errors.
    ///
    /// Note the synergy: a vanilla shield belt blocks ranged fire but cannot be worn with a
    /// ranged weapon. The Avir are barred from ranged weapons anyway - so the drawback that
    /// makes shield belts a hard choice for everyone else costs them precisely nothing.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirShieldBelt
    {
        public static readonly bool Active;

        private static readonly ThingDef Belt;
        private static readonly bool IsCeleTech;

        static AvirShieldBelt()
        {
            // CeleTech Arsenal (TOT.CeleTech.MKIII). MKI only.
            ThingDef mk1 = DefDatabase<ThingDef>.GetNamedSilentFail("CMC_ZGRangedShieldBelt_MKI");

            if (mk1 != null)
            {
                Belt = mk1;
                IsCeleTech = true;
            }
            else
            {
                Belt = DefDatabase<ThingDef>.GetNamedSilentFail("ShieldBelt");
                IsCeleTech = false;
            }

            // DISABLED 15 October 2026: the amulets carry shields of their own now, so the borrowed
            // shield belt is no longer handed out. The code stays for reference.
            Active = false;

            if (Active)
            {
                Log.Message("[Avir] Shield belts: using " + Belt.defName
                    + (IsCeleTech ? " (CeleTech Arsenal MKI)" : " (vanilla fallback)"));
            }
        }

        /// <summary>
        /// THE A'SCHIALIS EXCEPTION. Field generators are A'Shaign work. The Legion is the
        /// poorer, cruder order - it burns things and closes on foot - and it does not have the
        /// artificers to belt an entire line unit. Only the three ranks that command wear one:
        /// Legionnaire, Inquisitor, Highlord.
        ///
        /// Everyone else in the Legion goes without: centurions, and the shared castes when they
        /// spawn under the A'Schialis banner (Huntress, Artificer, Guild Master, Architect,
        /// Artist, Hatchling). Note that this is a FACTION rule, not a caste rule - an A'Shaign
        /// Huntress still gets her belt, because IsAShialisPawn is false for her. The A'Shaign
        /// side of the mod is completely untouched by this.
        /// </summary>
        private static readonly HashSet<string> LegionBeltRanks = new HashSet<string>
        {
            "Avir_Legionnaire",
            "Avir_Inquisitor",
            "Avir_Highlord",
        };

        public static bool AllowedBelt(Pawn pawn)
        {
            if (pawn == null) return false;

            // A'Shaign and anything that is not Legion: unchanged, everyone gets a belt.
            if (!AvirElvesGear.IsAShialisPawn(pawn)) return true;

            string kind = pawn.kindDef != null ? pawn.kindDef.defName : "";
            return LegionBeltRanks.Contains(kind);
        }

        public static void GiveBelt(Pawn pawn)
        {
            // Predators and Striders get an Excellent belt. Everyone else gets a standard Good one.
            bool elite = pawn != null && pawn.kindDef != null
                      && (pawn.kindDef.defName == "Avir_Predator" || pawn.kindDef.defName == "Avir_Strider");

            GiveBelt(pawn, elite ? QualityCategory.Excellent : QualityCategory.Good);
        }

        public static void GiveBelt(Pawn pawn, QualityCategory quality)
        {
            if (!Active || pawn == null || pawn.apparel == null) return;

            // Faction gate. Sits in the two-arg overload deliberately, so it is authoritative
            // no matter which entry point a caller uses.
            if (!AllowedBelt(pawn)) return;

            // Already has one? Leave it.
            foreach (Apparel worn in pawn.apparel.WornApparel)
            {
                if (worn.def == Belt) return;
            }

            // A belt is utility-layer, so it should not fight anything already on. Check anyway.
            foreach (Apparel worn in pawn.apparel.WornApparel)
            {
                if (!ApparelUtility.CanWearTogether(Belt, worn.def, pawn.RaceProps.body)) return;
            }

            Apparel belt = (Apparel)ThingMaker.MakeThing(
                Belt, Belt.MadeFromStuff ? GenStuff.DefaultStuffFor(Belt) : null);

            CompQuality q = belt.TryGetComp<CompQuality>();
            if (q != null) q.SetQuality(quality, ArtGenerationContext.Outsider);

            // Worn, like everything else they carry. A belt that has never taken a hit is a
            // belt that has never been used.
            if (belt.MaxHitPoints > 1)
            {
                float frac;
                float r = Rand.Value;
                if (r < 0.15f)      frac = Rand.Range(0.95f, 1.00f);
                else if (r < 0.55f) frac = Rand.Range(0.80f, 0.95f);
                else                frac = Rand.Range(0.65f, 0.80f);

                belt.HitPoints = Mathf.Clamp(Mathf.RoundToInt(belt.MaxHitPoints * frac), 1, belt.MaxHitPoints);
            }

            pawn.apparel.Wear(belt, false);
        }
    }

    /// <summary>
    /// Runs after the gear patches, so the belt goes on over whatever they ended up wearing -
    /// elven armour, Barbarians plate on a Hulk body, or nothing at all.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirShieldBelt
    {
        public static void Postfix(Pawn __result)
        {
            try
            {
                if (!AvirShieldBelt.Active) return;

                Pawn pawn = __result;
                if (pawn == null || !AvirFaction.IsAvirPawnKind(pawn)) return;

                // The player's own Avir are never dressed by us. They start naked and
                // empty-handed on purpose.
                if (AvirFaction.IsPlayerAvir(pawn)) return;

                AvirShieldBelt.GiveBelt(pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Shield belt assignment failed: " + e, 0x4A51C4);
            }
        }
    }
}
