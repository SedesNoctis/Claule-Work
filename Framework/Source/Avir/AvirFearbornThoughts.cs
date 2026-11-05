using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// FEARBORN'S MOOD NULLIFIERS, IN CODE.
    ///
    /// These used to be nullifyingGenes entries added by two XML patches
    /// (Patches/Avir_ThoughtPatches.xml and Patches/Avir_PredatorDeathThoughts.xml). That works,
    /// but the game prints every such thought as a "Removes: ..." line under "Mood:" in the gene
    /// tooltip - fourteen lines for Fearborn, which ran the tooltip off the screen. Fearborn's
    /// customEffectDescriptions now say the same thing in two lines, and the nullification itself
    /// happens here, through the one method the game asks: ThoughtUtility.NullifyingGene. Every
    /// path that decides whether a thought applies (ThoughtNullified, CanGetThought, the mood
    /// readout's "nullified by" message) goes through it, so the behaviour is unchanged.
    ///
    /// THE SPLIT, kept from the old patch: Bloodlust does not care who died; the Avir do. Every
    /// thought below is one an Avir feels nothing about - an outsider's death, or a corpse as a
    /// corpse. Their own dead are deliberately absent: WitnessedDeathAlly, WitnessedDeathFamily,
    /// KnowColonistDied, KnowColonistOrganHarvested. An Avir who watches a stranger die is
    /// unmoved; one who watches one of theirs die grieves like anyone else.
    ///
    /// ButcheredHumanlikeCorpseOpinion is an opinion thought - how OTHERS think of the butcher.
    /// It is checked on the pawn doing the judging, so Avir do not hold it against one another and
    /// a human colonist still will.
    ///
    /// To move a thought back across the line, delete its name below; to pull one over, add it.
    /// A name that does not exist in the current game is simply never matched.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirFearbornThoughts
    {
        private static readonly HashSet<string> Names = new HashSet<string>
        {
            // Rough living.
            "SleptOnGround",
            "SleptOutside",
            "AteWithoutTable",

            // A stranger's death.
            "WitnessedDeathNonAlly",
            "KnowGuestExecuted",
            "KnowPrisonerDiedInnocent",
            "KnowGuestOrganHarvested",

            // A corpse is a corpse.
            "ObservedLayingCorpse",
            "ObservedLayingRottingCorpse",
            "DeadMansApparel",
            "HumanLeatherApparelSad",

            // Butchery.
            "ButcheredHumanlikeCorpse",
            "KnowButcheredHumanlikeCorpse",
            "ButcheredHumanlikeCorpseOpinion",
        };

        private static HashSet<ThoughtDef> defs;

        static AvirFearbornThoughts()
        {
            try
            {
                new Harmony("Tenebris.Avir.FearbornThoughts").Patch(
                    AccessTools.Method(typeof(ThoughtUtility), nameof(ThoughtUtility.NullifyingGene)),
                    postfix: new HarmonyMethod(typeof(AvirFearbornThoughts), nameof(Postfix)));
            }
            catch (Exception e)
            {
                Log.Error("[Avir] Fearborn mood nullifiers could not be applied: " + e);
            }
        }

        private static HashSet<ThoughtDef> Defs
        {
            get
            {
                if (defs == null)
                {
                    defs = new HashSet<ThoughtDef>();
                    foreach (string name in Names)
                    {
                        ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail(name);
                        if (def != null)
                        {
                            defs.Add(def);
                        }
                    }
                }
                return defs;
            }
        }

        public static void Postfix(ThoughtDef def, Pawn pawn, ref Gene __result)
        {
            if (__result != null || def == null || pawn?.genes == null || AvirDefOf.Fearborn == null)
            {
                return;
            }
            if (!Defs.Contains(def))
            {
                return;
            }
            // Same lookup vanilla does for a listed gene: the pawn carries it.
            Gene fearborn = pawn.genes.GetGene(AvirDefOf.Fearborn);
            if (fearborn != null && fearborn.Active)
            {
                __result = fearborn;
            }
        }
    }
}
