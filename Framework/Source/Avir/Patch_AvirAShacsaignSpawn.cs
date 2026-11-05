using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Rolls the A'Shacsaign subrace onto Fearborn pawns at generation.
    ///
    /// WHY A GENERATION PATCH AND NOT A GENE. The design calls for a 2% chance, and GeneDef has
    /// no chance field - genes arrive whole from a xenotype. Hanging the roll off Fearborn keeps
    /// the Avir genome exactly as it was (taken as a set, never a la carte) while still letting a
    /// rare lineage exist inside it. It fires for every Fearborn pawn the game makes, colonist or
    /// raider or trader, which is the intent: A'Shacsaign are a subrace, not a player feature.
    ///
    /// THE ROLL IS DETERMINISTIC PER PAWN. Seeded from thingIDNumber rather than Rand, so a pawn
    /// is either A'Shacsaign or not from the moment they exist and stays that way across saves,
    /// reloads and world-pawn round trips. A live Rand call would re-roll differently if the
    /// patch ever ran twice on the same pawn.
    ///
    /// ECHOLOCATION IS REPLACED, NOT ADDED TO. Avir_BatlikeHearing gives +25% hearing and
    /// A'Shacsaign gives +80%; together they would reach 205%. Stripping the former lands hearing
    /// on exactly 180%, which is where StatPart_AvirKeenReflexes caps out - the subrace is meant
    /// to sit at the ceiling of the melee-by-ear ladder, not past it. The strip also runs from
    /// the tier comp's periodic sync, so a later re-grant by Gene_GrantHediff cannot resurrect it.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirAShacsaignSpawn
    {
        /// <summary>
        /// Chance for a Fearborn pawn to be A'Shacsaign when nobody's parentage says otherwise.
        /// Raised from 0.02 - at two percent the subrace was a rumour rather than a rarity.
        ///
        /// This is the BACKGROUND rate only. A pawn actually born or hatched to at least one
        /// A'Shacsaign parent never reaches this roll; see Patch_AvirAShacsaignInheritance,
        /// which is authoritative for them in both directions.
        /// </summary>
        public const float Chance = 0.05f;

        private static HediffDef subraceCached;
        private static HediffDef echolocationCached;
        private static bool resolved;
        private static bool loggedFirst;

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            subraceCached = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_AShacsaign");
            echolocationCached = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_BatlikeHearing");
            if (subraceCached == null)
                Log.Warning("[Avir] Avir_AShacsaign not found - the subrace will never spawn.");
        }

        public static HediffDef Subrace { get { Resolve(); return subraceCached; } }
        public static HediffDef Echolocation { get { Resolve(); return echolocationCached; } }

        /// <summary>True when this pawn's fixed identity puts them in the background rate.</summary>
        public static bool RollsAsSubrace(Pawn pawn)
        {
            if (pawn == null) return false;
            Rand.PushState(pawn.thingIDNumber ^ 0x5A17C);
            bool hit = Rand.Value < Chance;
            Rand.PopState();
            return hit;
        }

        public static bool IsSubrace(Pawn pawn)
        {
            if (pawn == null || pawn.health == null) return false;
            HediffDef def = Subrace;
            return def != null && pawn.health.hediffSet.HasHediff(def);
        }

        /// <summary>
        /// Removes the ordinary echolocation hediff. Safe to call repeatedly - it is also invoked
        /// from the periodic tier sync so a re-grant cannot stick.
        /// </summary>
        public static void StripEcholocation(Pawn pawn)
        {
            HediffDef echo = Echolocation;
            if (echo == null || pawn == null || pawn.health == null) return;

            Hediff present = pawn.health.hediffSet.GetFirstHediffOfDef(echo);
            if (present != null) pawn.health.RemoveHediff(present);
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __result)
        {
            try
            {
                Pawn pawn = __result;
                if (pawn == null || pawn.health == null || pawn.Dead) return;

                HediffDef def = Subrace;
                if (def == null) return;
                if (pawn.health.hediffSet.HasHediff(def)) return;
                if (!AvirFaction.HasFearborn(pawn)) return;
                if (!RollsAsSubrace(pawn)) return;

                pawn.health.AddHediff(def);
                StripEcholocation(pawn);

                if (!loggedFirst)
                {
                    loggedFirst = true;
                    Log.Message("[Avir] First A'Shacsaign generated (" + pawn.LabelShortCap + ").");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Avir] A'Shacsaign spawn roll failed: " + ex.Message);
            }
        }
    }
}
