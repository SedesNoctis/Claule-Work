using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// INTEGRATION: Medieval Overhaul - Barbarians (pphhyy.BarbariansMOSubmod).
    ///
    /// Barbarians ships 61 Hulk-body textures, which Elves (Continued) does not. It is used
    /// for two things:
    ///   1. ANY Avir on a Hulk body - the Elves gear would render them naked.
    ///   2. Male Predators, always, whatever body they have. Warlord plate is what they wear.
    ///
    /// NEVER RECOLOURED. Barbarians gear ships already coloured and it looks right as it is.
    /// The Avir palette is applied to elven gear only.
    ///
    /// The kit is exact, not rolled:
    ///   Male Predator      Warlord Armor  + Warlord Helm
    ///   Any other fighter  Chosen Mail    + Chosen Antler Helm
    ///   Artificer/Guild    Barb Tribal    + Head Band          (and NEVER a weapon)
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirBarbarianGear
    {
        public static readonly bool Active;

        private static ThingDef WarlordArmor, WarlordHelm;
        private static ThingDef ChosenMail, ChosenAntler;
        private static ThingDef BarbTribal, HeadBand;

        private static ThingDef Essentium, Tisserium;

        static AvirBarbarianGear()
        {
            WarlordArmor = Find("pphhyy_Barbarian_WarlordArmor");
            WarlordHelm  = Find("pphhyy_Barbarians_WarlordHelm");

            ChosenMail   = Find("pphhyy_Barbarian_ChosenMail");
            ChosenAntler = Find("pphhyy_Barbarians_ChosenAntler");

            BarbTribal   = Find("pphhyy_Barbarian_BarbTribal");
            HeadBand     = Find("pphhyy_Barbarian_HeadBand");

            Essentium = Find("Essentium");
            Tisserium = Find("BloodstoneTisserium");

            Active = ChosenMail != null || WarlordArmor != null || BarbTribal != null;

            if (Active) Log.Message("[Avir] Medieval Overhaul: Barbarians detected - Avir plate integration active.");
        }

        private static ThingDef Find(string n) { return DefDatabase<ThingDef>.GetNamedSilentFail(n); }

        /// <summary>
        /// Artificers and Guild Masters are NEVER given tisserium - that is the war-metal, and
        /// they are not warriors. Essentium only.
        /// </summary>
        private static ThingDef StuffFor(ThingDef apparel, bool warTier)
        {
            if (apparel == null || apparel.stuffCategories == null) return null;

            foreach (StuffCategoryDef cat in apparel.stuffCategories)
            {
                if (cat == StuffCategoryDefOf.Metallic)
                    return (warTier && Tisserium != null) ? Tisserium : Essentium;

                if (cat == StuffCategoryDefOf.Leathery)
                    return DefDatabase<ThingDef>.GetNamedSilentFail("Leather_AvirFeatheredFur");

                if (cat == StuffCategoryDefOf.Fabric)
                    return DefDatabase<ThingDef>.GetNamedSilentFail("Cloudcloth");
            }
            return null;
        }

        private static void WearOne(Pawn pawn, ThingDef def, bool masterwork, bool warTier)
        {
            if (def == null || pawn.apparel == null) return;

            foreach (Apparel w in pawn.apparel.WornApparel)
            {
                if (!ApparelUtility.CanWearTogether(def, w.def, pawn.RaceProps.body)) return;
            }

            ThingDef stuff = StuffFor(def, warTier);
            if (def.MadeFromStuff && stuff == null) return;

            Apparel a = (Apparel)ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff : null);

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

            // NO RECOLOUR. Barbarians gear is already coloured.

            AvirGearUtil.Weather(a);
            pawn.apparel.Wear(a, false);
        }

        /// <summary>Male Predator: Warlord Armor + Warlord Helm. Nothing else, ever.</summary>
        public static bool DressPredator(Pawn pawn)
        {
            if (!Active || pawn == null || pawn.apparel == null) return false;

            pawn.apparel.DestroyAll();
            WearOne(pawn, WarlordArmor, true, true);
            WearOne(pawn, WarlordHelm, true, true);
            return true;
        }

        /// <summary>
        /// Any other Avir stuck on a body the Elves mod cannot render.
        ///   fighters       -> Chosen Mail + Chosen Antler Helm
        ///   makers/traders -> Barb Tribal + Head Band, essentium only
        /// </summary>
        public static bool DressHulk(Pawn pawn)
        {
            if (!Active || pawn == null || pawn.apparel == null) return false;

            string kind = pawn.kindDef != null ? pawn.kindDef.defName : "";

            pawn.apparel.DestroyAll();

            if (kind == "Avir_Artificer" || kind == "Avir_GuildMaster")
            {
                // Makers and traders. Tribal cloth and a headband - no plate, no war-metal.
                WearOne(pawn, BarbTribal, false, false);
                WearOne(pawn, HeadBand, false, false);
                return true;
            }

            if (kind == "Avir_Predator")
            {
                WearOne(pawn, WarlordArmor, true, true);
                WearOne(pawn, WarlordHelm, true, true);
                return true;
            }

            // Everyone else who fights.
            WearOne(pawn, ChosenMail, false, true);
            WearOne(pawn, ChosenAntler, false, true);
            return true;
        }
    }
}
