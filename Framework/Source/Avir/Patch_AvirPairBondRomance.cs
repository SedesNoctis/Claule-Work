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
    /// THE THREE HOOKS THAT MAKE THE PAIR BOND BEHAVE, ALL ON THE ROMANCE WORKER.
    ///
    ///   RandomSelectionWeight - whether a courtship is even considered. An Avir never courts a
    ///                           stranger and a bonded one never courts at all, so its own attempts
    ///                           simply do not arise.
    ///   SuccessChance         - once an Avir does court someone it has known long enough, it is
    ///                           not refused. Certainty, not a high roll.
    ///   Interacted            - where an outsider's attempt lands. If it should not have happened,
    ///                           the Avir and its mate come for whoever tried.
    ///
    /// WHY THE WEIGHT HOOK CANNOT DO ALL OF IT. Setting the weight to zero stops OUR pawns from
    /// starting something, and does nothing about a human deciding to court an Avir - that
    /// interaction is chosen by the initiator's worker with the initiator's rules. The attempt has
    /// to be allowed to happen so that it can be answered.
    /// </summary>
    public static class Patch_AvirPairBondRomance
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                Type worker = AccessTools.TypeByName("RimWorld.InteractionWorker_RomanceAttempt");
                if (worker == null)
                {
                    Log.Warning("[Avir bond] InteractionWorker_RomanceAttempt not found; pair "
                        + "bonding rules are inactive.");
                    return;
                }

                MethodInfo weight = AccessTools.Method(worker, "RandomSelectionWeight",
                    new[] { typeof(Pawn), typeof(Pawn) });
                MethodInfo success = AccessTools.Method(worker, "SuccessChance",
                    new[] { typeof(Pawn), typeof(Pawn), typeof(float) });
                MethodInfo interacted = AccessTools.Method(worker, "Interacted");

                if (weight != null)
                {
                    harmony.Patch(weight, postfix: new HarmonyMethod(
                        typeof(Patch_AvirPairBondRomance), nameof(Weight_Postfix)));
                }

                if (success != null)
                {
                    harmony.Patch(success, postfix: new HarmonyMethod(
                        typeof(Patch_AvirPairBondRomance), nameof(Success_Postfix)));
                }

                if (interacted != null)
                {
                    harmony.Patch(interacted, postfix: new HarmonyMethod(
                        typeof(Patch_AvirPairBondRomance), nameof(Interacted_Postfix)));
                }

                // BREAKUP. An Avir does not leave. Vanilla's breakup interaction is chosen by
                // its own weight, so a bonded Avir would otherwise walk out of a bond the rest of
                // this system swears is for life.
                //
                // Only the INITIATOR side is silenced. Maximus's own point: a non-Avir partner is
                // not held by any of this and may still end it, which is a considerably sadder
                // outcome than the mechanic preventing it - she cannot leave and he can.
                Type breakup = AccessTools.TypeByName("RimWorld.InteractionWorker_Breakup");
                MethodInfo breakupWeight = breakup == null
                    ? null
                    : AccessTools.Method(breakup, "RandomSelectionWeight",
                        new[] { typeof(Pawn), typeof(Pawn) });

                if (breakupWeight != null)
                {
                    harmony.Patch(breakupWeight, postfix: new HarmonyMethod(
                        typeof(Patch_AvirPairBondRomance), nameof(Breakup_Postfix)));
                }

                Log.Message("[Avir bond] romance hooks bound: weight=" + (weight != null)
                    + " success=" + (success != null) + " interacted=" + (interacted != null)
                    + " breakup=" + (breakupWeight != null) + ".");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir bond] failed to apply: " + e);
            }
        }

        /// <summary>
        /// An Avir does not court strangers, and a bonded Avir does not court anyone.
        /// </summary>
        public static void Weight_Postfix(Pawn initiator, Pawn recipient, ref float __result)
        {
            if (__result <= 0f) return;

            try
            {
                // A REBUFFED SUITOR STOPS, WHATEVER HE IS - and this is the hole that made the
                // marker useless. The check below returns early unless the INITIATOR carries the
                // gene, so a human who had already been driven off kept proposing forever: the
                // hediff was applied and then never consulted, because a human initiator never
                // reached the line that reads it.
                if (AvirPairBond.WasRebuffed(initiator) && AvirPairBond.HasBondGene(recipient))
                {
                    __result = 0f;
                    return;
                }

                if (!AvirPairBond.HasBondGene(initiator)) return;

                if (!AvirPairBond.CourtshipAllowed(initiator, recipient))
                {
                    __result = 0f;
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] weight postfix failed: " + e, 0x4A5282);
            }
        }

        /// <summary>
        /// A bonded Avir never initiates a breakup. Ever, for any reason.
        /// </summary>
        public static void Breakup_Postfix(Pawn initiator, ref float __result)
        {
            if (__result <= 0f) return;

            try
            {
                if (AvirPairBond.HasBondGene(initiator)) __result = 0f;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] breakup postfix failed: " + e, 0x4A5291);
            }
        }

        /// <summary>
        /// CERTAINTY, NOT A GOOD CHANCE.
        ///
        /// Applied when the pairing is one the rules permit - which for an Avir initiator means it
        /// has already decided, and for an Avir recipient means the suitor has been around long
        /// enough to have been considered. Either way the answer is yes.
        ///
        /// Deliberately not applied to a pairing the rules forbid: those go to Interacted and are
        /// answered with violence instead.
        /// </summary>
        public static void Success_Postfix(Pawn initiator, Pawn recipient, ref float __result)
        {
            try
            {
                if (!AvirPairBond.HasBondGene(initiator) && !AvirPairBond.HasBondGene(recipient))
                {
                    return;
                }

                // ZERO WHEN IT IS NOT ALLOWED, AND THIS IS THE HOLE MAXIMUS FOUND.
                //
                // The weight hook only silences an AVIR initiator. When a stranger courts an Avir
                // the weight is untouched - deliberately, because the attempt has to happen for the
                // refusal and the fight to fire - and success was left at vanilla odds. So an
                // outsider could walk in and be accepted on the first day, and the acquaintance
                // threshold restrained nobody but the Avir themselves.
                //
                // The attempt still happens. It simply cannot succeed. That keeps the punishment
                // and makes the threshold absolute in both directions.
                if (!AvirPairBond.CourtshipAllowed(initiator, recipient))
                {
                    __result = 0f;
                    return;
                }

                __result = 1f;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] success postfix failed: " + e, 0x4A5283);
            }
        }

        /// <summary>
        /// Somebody courted an Avir who should not have been courted. This is where they find out.
        /// </summary>
        public static void Interacted_Postfix(Pawn initiator, Pawn recipient)
        {
            try
            {
                if (initiator == null || recipient == null) return;

                // Only the Avir side answers. An Avir initiator with a permitted target has
                // already succeeded and has nothing to be angry about; one with a forbidden target
                // never got a weight above zero and never spoke.
                if (!AvirPairBond.HasBondGene(recipient)) return;

                // DID IT ACTUALLY SUCCEED? ASK THAT FIRST, BEFORE ANYTHING ELSE.
                //
                // Interacted runs AFTER the relation has been created. So on a successful
                // courtship, CourtshipAllowed is re-evaluated against a pawn who is now bonded -
                // to the very suitor being judged - sees IsBonded, and reports a violation. The
                // couple got married and beaten up over the same event.
                //
                // Worse, DefendersOf then includes the new mate, who IS the suitor: Kimmy was sent
                // to attack Kimmy and stood there hitting herself while Slate wandered off.
                //
                // A courtship that produced a relationship is not a courtship that was refused.
                if (Succeeded(initiator, recipient)) return;

                if (AvirPairBond.CourtshipAllowed(initiator, recipient)) return;

                Rebuff(recipient, initiator);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] interacted postfix failed: " + e, 0x4A5284);
            }
        }

        /// <summary>
        /// Whether the two are now a couple - which is how a successful courtship is detected,
        /// Interacted having no return value to read.
        /// </summary>
        private static bool Succeeded(Pawn a, Pawn b)
        {
            if (a?.relations == null) return false;

            return a.relations.DirectRelationExists(PawnRelationDefOf.Lover, b)
                   || a.relations.DirectRelationExists(PawnRelationDefOf.Spouse, b)
                   || a.relations.DirectRelationExists(PawnRelationDefOf.Fiance, b);
        }

        /// <summary>
        /// The Avir, and its mate if it has one, come for the pawn who tried.
        ///
        /// WHY THE FIGHT DID NOT START, AND WHAT FIXED IT.
        ///
        /// TryStartMentalState's otherPawn argument is stored on the BASE MentalState class, in a
        /// field called causedByPawn. MentalState_SocialFighting keeps its opponent somewhere else
        /// entirely - its own `otherPawn` field - and its ShouldStop property reads that one first.
        ///
        /// So the state started correctly, found no opponent, and ended itself on the next tick.
        /// The marker landed, nothing else did, and from outside it looked as though the call had
        /// been ignored.
        ///
        /// The opponent is now written into the state's own field after it starts. This is exactly
        /// the mistake I did not make in the rival hunt, where MentalState_MurderousRage.target is
        /// assigned by hand - I did it there by accident and not here.
        /// </summary>
        /// <summary>Also used by AvirMateFlirting for an outsider who flirts with a bonded Avir.</summary>
        internal static void Rebuff(Pawn avir, Pawn suitor, string how = "courted")
        {
            MarkRebuffed(suitor);

            List<Pawn> defenders = AvirPairBond.DefendersOf(avir);
            for (int i = 0; i < defenders.Count; i++)
            {
                TryFight(defenders[i], suitor);
            }

            Messages.Message(
                avir.LabelShortCap + " was " + how + " by " + suitor.LabelShortCap
                + " and will not have it. " + suitor.LabelShortCap + " will not try again.",
                new LookTargets(new List<Pawn> { avir, suitor }),
                MessageTypeDefOf.NegativeEvent, false);
        }


        /// <summary>
        /// Nobody who has been driven off ever tries again - with this Avir or with any other.
        ///
        /// A hediff rather than a list in a component, because it saves itself, travels with the
        /// pawn, and can be seen on the health tab: a player wondering why a suitor stopped trying
        /// gets an answer instead of a mystery.
        /// </summary>
        /// <summary>
        /// A SOCIAL FIGHT IS MUTUAL, AND THAT IS WHY THE FIRST TWO ATTEMPTS DIED.
        ///
        /// MentalState_SocialFighting exposes IsOtherPawnSocialFightingWithMe, and its ShouldStop
        /// reads it: the state only survives while the OTHER pawn is also social-fighting, with me
        /// specifically. Starting it on one pawn produces a state that ends itself on the next
        /// tick, which is precisely what Maximus kept seeing - the message fired, nothing else did.
        ///
        /// So both sides are put into it and cross-linked. Vanilla does the same thing through
        /// SocialFightController; there is no one-sided social fight in this game.
        ///
        /// The suitor is started FIRST. If he cannot be - asleep, downed, already in some other
        /// state - the Avir is not put into a fight that has nobody in it, and the refusal stands
        /// on the marker alone.
        /// </summary>
        private static void TryFight(Pawn avir, Pawn suitor)
        {
            // Cheap and absolute. Even with the success check above, a defender list that ever
            // contains the suitor again for any reason must not produce a pawn punching herself.
            if (avir == suitor) return;

            if (!StartMutualFight(avir, suitor))
            {
                // A SOCIAL FIGHT HOLDS EXACTLY TWO, so the second defender is always refused while
                // the first is still swinging - which is why Maximus watched a husband stroll past
                // while his wife dealt with the man who propositioned her.
                //
                // He is not written out of it; he is queued and takes his turn once the suitor is
                // free. See GameComponent_AvirBondRetaliation.
                GameComponent_AvirBondRetaliation.Queue(avir, suitor);
            }
        }

        /// <summary>
        /// Puts both pawns into a social fight with one another, or reports that it could not be
        /// done. Public so the retaliation queue can use the same path rather than a second copy
        /// of it that would drift.
        /// </summary>
        public static bool StartMutualFight(Pawn avir, Pawn suitor)
        {
            if (avir == null || suitor == null || avir == suitor) return false;
            if (!CanFight(avir) || !CanFight(suitor)) return false;
            if (avir.Map != suitor.Map) return false;

            if (!StartFight(suitor, avir)) return false;

            if (!StartFight(avir, suitor))
            {
                // One-sided is worse than none: it would leave the suitor swinging at somebody who
                // is not fighting back and cannot be pulled out of it.
                suitor.MentalState?.RecoverFromState();
                return false;
            }

            return true;
        }

        private static bool CanFight(Pawn p)
        {
            if (p?.mindState?.mentalStateHandler == null) return false;
            if (p.Dead || p.Downed || !p.Spawned) return false;
            return !p.InMentalState;
        }

        private static bool StartFight(Pawn p, Pawn opponent)
        {
            bool started = p.mindState.mentalStateHandler.TryStartMentalState(
                MentalStateDefOf.SocialFighting,
                reason: null,
                forced: true,
                forceWake: true,
                causedByMood: false,
                otherPawn: opponent);

            if (!started) return false;

            // TryStartMentalState's otherPawn goes to the base class's causedByPawn. The social
            // fight keeps its opponent in its OWN otherPawn field, and reads that one.
            MentalState_SocialFighting fight = p.MentalState as MentalState_SocialFighting;
            if (fight == null) return false;

            fight.otherPawn = opponent;
            return true;
        }

        private static void MarkRebuffed(Pawn suitor)
        {
            if (suitor?.health == null || AvirPairBondDefOf.Avir_RomanceRebuffed == null) return;
            if (AvirPairBond.WasRebuffed(suitor)) return;

            // THE SPLINTER-HEARTED NEVER LEARNS, AND THAT IS THE POINT OF HER.
            //
            // Everyone else takes one refusal and is finished with the species. Marking her the
            // same way would end her promiscuity after a single mistimed approach and quietly
            // delete the trait a few days into any game it appeared in.
            //
            // Her description says she "has never managed to want otherwise no matter how the
            // Circle looks at her for it". A pawn who stops after being hit once is not that pawn.
            // She gets driven off, takes the beating, and tries again next week.
            if (AvirPairBond.IsSplinterHearted(suitor)) return;

            Log.Message("[Avir bond] " + suitor.LabelShortCap + " marked as rebuffed; they will not "
                + "court an Avir again. The hediff is hidden - look with dev mode.");

            suitor.health.AddHediff(AvirPairBondDefOf.Avir_RomanceRebuffed);
        }
    }
}
