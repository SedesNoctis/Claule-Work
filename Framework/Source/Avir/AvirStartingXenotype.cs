using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Starting colonists of an Avir player faction come out as Avir, not baseliners.
    ///
    /// The vanilla scenarios (Crashlanded and the rest) list their starting pawns by xenotype:
    /// "3 x Baseliner". ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes writes that xenotype
    /// into every starting request as ForcedXenotype, and a forced xenotype beats the faction's
    /// own xenotypeSet - so a Free Nesting colony started on Crashlanded got three humans.
    ///
    /// When the player faction's xenotype set holds Avir and not Baseliner, the scenario's
    /// Baseliner slots are read as Avir slots: the requests made while the scenario generates
    /// its pawns are switched, and so is the "must have N baseliners" start requirement.
    /// A xenotype the player picks by hand on the pawn page afterwards is left alone, and so
    /// is any scenario slot that names a non-baseline xenotype.
    /// </summary>
    public static class AvirStartingXenotype
    {
        internal static bool generating;

        /// <summary>The xenotype a Baseliner slot becomes for the current player faction, or null.</summary>
        public static XenotypeDef Replacement()
        {
            FactionDef f = Find.GameInitData?.playerFaction?.def ?? Faction.OfPlayerSilentFail?.def;
            XenotypeSet set = f?.xenotypeSet;
            if (set == null || set.Count == 0) return null;
            XenotypeDef avir = DefDatabase<XenotypeDef>.GetNamedSilentFail("Avir");
            if (avir == null || set.BaselinerChance > 0f) return null;
            for (int i = 0; i < set.Count; i++)
            {
                XenotypeChance c = set[i];
                if (c?.xenotype == avir && c.chance > 0f) return avir;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes), "GenerateStartingPawns")]
    public static class Patch_AvirStartingXenotype_Generate
    {
        public static void Prefix() { AvirStartingXenotype.generating = true; }

        public static void Finalizer() { AvirStartingXenotype.generating = false; }
    }

    [HarmonyPatch(typeof(StartingPawnUtility), nameof(StartingPawnUtility.SetGenerationRequest))]
    public static class Patch_AvirStartingXenotype_Request
    {
        public static void Prefix(ref PawnGenerationRequest request)
        {
            if (!AvirStartingXenotype.generating || request.ForcedXenotype != XenotypeDefOf.Baseliner) return;
            XenotypeDef x = AvirStartingXenotype.Replacement();
            if (x != null) request.ForcedXenotype = x;
        }
    }

    /// <summary>The scenario's "required at start" Baseliner counts become Avir counts as well.</summary>
    [HarmonyPatch(typeof(ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes), nameof(ScenPart_ConfigPage_ConfigureStartingPawns_Xenotypes.PostIdeoChosen))]
    public static class Patch_AvirStartingXenotype_Required
    {
        // Prefix: the override fills startingXenotypesRequired and then calls the base, which
        // generates the pawns - the flag has to be up for that whole call.
        public static void Prefix() { AvirStartingXenotype.generating = true; }

        public static void Postfix()
        {
            List<XenotypeCount> req = Find.GameInitData?.startingXenotypesRequired;
            if (req == null) return;
            XenotypeDef x = AvirStartingXenotype.Replacement();
            if (x == null) return;
            for (int i = 0; i < req.Count; i++)
            {
                XenotypeCount c = req[i];
                if (c?.xenotype != XenotypeDefOf.Baseliner) continue;
                // a copy: the original belongs to the scenario and must stay as written
                req[i] = new XenotypeCount
                {
                    xenotype = x,
                    count = c.count,
                    requiredAtStart = c.requiredAtStart,
                    allowedDevelopmentalStages = c.allowedDevelopmentalStages,
                    description = c.description
                };
            }
        }

        public static void Finalizer() { AvirStartingXenotype.generating = false; }
    }
}
