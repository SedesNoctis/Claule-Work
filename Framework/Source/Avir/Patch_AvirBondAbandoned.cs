using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// SHE CANNOT LEAVE. HE CAN. THIS IS WHAT HAPPENS AFTER HE DOES.
    ///
    /// A breakup initiated by a non-Avir partner is the one way a bond ends without a death, and it
    /// does not end the bond - it only ends the relationship. She is still bonded, with every
    /// consequence that carries: she will never court anyone again, never accept anyone else, and
    /// carries the loss for the rest of her life.
    ///
    /// WHY THE SAME HEDIFF AS A DEATH. Avir_BondSevered is not "my mate died", it is "the one I
    /// chose is not here". Abandonment satisfies that exactly, and reusing it means she
    /// automatically inherits the whole apparatus already built around it - the permanent mood, the
    /// refusal to bond again, and the reunion path if he ever comes back. Writing a second, parallel
    /// grief would have meant maintaining two of everything and having them disagree within a month.
    ///
    /// AND THEN THERE IS THE RIVAL. If he takes someone else while she is on the same map, she goes
    /// after that person with whatever she is holding. Not a social fight - a hunt. See
    /// Avir_RivalHunt.
    /// </summary>
    public static class Patch_AvirBondAbandoned
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                Type breakup = AccessTools.TypeByName("RimWorld.InteractionWorker_Breakup");
                MethodInfo interacted = breakup == null
                    ? null
                    : AccessTools.Method(breakup, "Interacted");

                if (interacted != null)
                {
                    harmony.Patch(interacted, postfix: new HarmonyMethod(
                        typeof(Patch_AvirBondAbandoned), nameof(Breakup_Postfix)));
                }

                Type romance = AccessTools.TypeByName("RimWorld.InteractionWorker_RomanceAttempt");
                MethodInfo romanced = romance == null
                    ? null
                    : AccessTools.Method(romance, "Interacted");

                if (romanced != null)
                {
                    harmony.Patch(romanced, postfix: new HarmonyMethod(
                        typeof(Patch_AvirBondAbandoned), nameof(NewRival_Postfix)));
                }

                Log.Message("[Avir bond] abandonment bound: breakup=" + (interacted != null)
                    + " rival=" + (romanced != null) + ".");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir bond] could not bind abandonment: " + e);
            }
        }

        /// <summary>
        /// He left. The relationship is over; the bond is not.
        /// </summary>
        public static void Breakup_Postfix(Pawn initiator, Pawn recipient)
        {
            try
            {
                // Only the one who was LEFT, and only if she is the one the bond binds. An Avir
                // never initiates a breakup - Patch_AvirPairBondRomance sets that weight to zero -
                // so reaching here as the initiator would mean something else caused it, and the
                // rule is about being abandoned rather than about parting.
                if (!AvirPairBond.HasBondGene(recipient)) return;
                if (recipient.Dead) return;

                AvirBondGrief.Sever(recipient, initiator);

                Messages.Message(
                    initiator.LabelShortCap + " has left " + recipient.LabelShortCap
                    + ". The bond does not end with the relationship - she chose once, and that is "
                    + "still true.",
                    new LookTargets(new List<Pawn> { recipient, initiator }),
                    MessageTypeDefOf.NegativeEvent, false);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] breakup postfix failed: " + e, 0x4A5292);
            }
        }

        /// <summary>
        /// Somebody new has taken the place she still considers occupied.
        ///
        /// Fires on any successful courtship, then asks whether either of the two new lovers is a
        /// person an abandoned Avir was bonded to. There may be more than one such Avir; each of
        /// them answers for herself.
        /// </summary>
        public static void NewRival_Postfix(Pawn initiator, Pawn recipient)
        {
            try
            {
                if (initiator == null || recipient == null) return;
                if (!initiator.relations.DirectRelationExists(PawnRelationDefOf.Lover, recipient)
                    && !initiator.relations.DirectRelationExists(PawnRelationDefOf.Spouse, recipient))
                {
                    return;
                }

                // Both directions: either of the new couple could be the one who walked out.
                CheckAbandonedOf(initiator, recipient);
                CheckAbandonedOf(recipient, initiator);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] rival postfix failed: " + e, 0x4A5293);
            }
        }

        /// <summary>
        /// Every abandoned Avir still bonded to <paramref name="strayed"/> goes after
        /// <paramref name="rival"/>.
        /// </summary>
        private static void CheckAbandonedOf(Pawn strayed, Pawn rival)
        {
            List<DirectPawnRelation> relations = strayed.relations?.DirectRelations;
            if (relations == null) return;

            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation rel = relations[i];
                if (rel?.def == null || rel.otherPawn == null) continue;

                // Ex-lover and ex-spouse: the relationship he ended. A current partner is not
                // somebody he abandoned.
                if (rel.def != PawnRelationDefOf.ExLover && rel.def != PawnRelationDefOf.ExSpouse)
                {
                    continue;
                }

                Pawn abandoned = rel.otherPawn;
                if (abandoned == rival || abandoned.Dead) continue;
                if (!AvirPairBond.HasBondGene(abandoned)) continue;

                // She has to still be carrying it. An Avir whose grief was lifted - by his
                // resurrection, or by dev tools - has no claim to press.
                if (!AvirPairBond.HasLostAMate(abandoned)) continue;

                StartHunt(abandoned, rival);
            }
        }

        /// <summary>
        /// SAME MAP ONLY, which is Maximus's condition and also the only version that works.
        ///
        /// The hunt is a mental state driving a pawn toward a target; a target three tiles away on
        /// the world map is not something the state can pursue, and a caravan Avir entering an
        /// unreachable rage would simply stand there flagged as hostile forever.
        /// </summary>
        private static void StartHunt(Pawn hunter, Pawn rival)
        {
            if (hunter.mindState?.mentalStateHandler == null) return;
            if (!hunter.Spawned || !rival.Spawned || hunter.Map != rival.Map) return;
            if (hunter.Downed || hunter.InMentalState) return;

            MentalStateDef def = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_RivalHunt");
            if (def == null) return;

            bool started = hunter.mindState.mentalStateHandler.TryStartMentalState(
                def,
                reason: null,
                forced: true,
                forceWake: true,
                causedByMood: false,
                otherPawn: rival);

            if (!started) return;

            // The state class stores its quarry in `target`, and TryStartMentalState's otherPawn
            // does not always reach it depending on the state. Set directly so the hunt cannot
            // begin without knowing who it is for.
            MentalState_MurderousRage rage =
                hunter.MentalState as MentalState_MurderousRage;

            if (rage != null) rage.target = rival;
        }
    }
}
