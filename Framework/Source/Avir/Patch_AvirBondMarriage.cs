using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// AN AVIR HAS NO WEDDING. THE FIRST NIGHT IS THE MARRIAGE.
    ///
    /// There is no ceremony in this species and nothing to wait for: the choice was the whole of it,
    /// and it was made long before anybody spoke. So the moment a courtship succeeds the pair are
    /// married outright rather than becoming lovers who might one day get round to it.
    ///
    /// MECHANICALLY THIS IS NOT COSMETIC. Lover and Spouse are different relations to the game:
    /// spouses share a bed by default, carry the marriage mood, take the widow thoughts on death,
    /// and are handled differently by half the social code. Leaving Avir as permanent lovers would
    /// have meant a species that pairs for life and is treated by every system as dating.
    ///
    /// NO CEREMONY EVENT IS FIRED. Vanilla's marriage ceremony is a gathering with a party, a
    /// spot and guests - precisely the thing these two would not hold. The relation changes and the
    /// colony is told in one line.
    /// </summary>
    public static class Patch_AvirBondMarriage
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                Type worker = AccessTools.TypeByName("RimWorld.InteractionWorker_RomanceAttempt");
                MethodInfo interacted = worker == null
                    ? null
                    : AccessTools.Method(worker, "Interacted");

                if (interacted == null)
                {
                    Log.Warning("[Avir bond] romance Interacted not found; Avir will remain lovers "
                        + "rather than marrying on the first night.");
                    return;
                }

                harmony.Patch(interacted, postfix: new HarmonyMethod(
                    typeof(Patch_AvirBondMarriage), nameof(Interacted_Postfix)));

                Log.Message("[Avir bond] marriage-on-courtship bound.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir bond] could not bind marriage: " + e);
            }
        }

        /// <summary>
        /// Runs after the romance resolves. Success is detected by the Lover relation now existing
        /// between the two - there is no return value to read, and inspecting the relation is the
        /// only honest way to tell a successful courtship from a refused one.
        /// </summary>
        public static void Interacted_Postfix(Pawn initiator, Pawn recipient)
        {
            try
            {
                if (initiator?.relations == null || recipient?.relations == null) return;

                // At least one of them must be an Avir for this rule to apply at all. A human
                // couple in an Avir colony marries whenever humans marry.
                if (!AvirPairBond.HasBondGene(initiator) && !AvirPairBond.HasBondGene(recipient))
                {
                    return;
                }

                if (!initiator.relations.DirectRelationExists(PawnRelationDefOf.Lover, recipient))
                {
                    return;
                }

                initiator.relations.RemoveDirectRelation(PawnRelationDefOf.Lover, recipient);
                initiator.relations.AddDirectRelation(PawnRelationDefOf.Spouse, recipient);

                // The bond now exists as a thing on both of them, not only as a rule about them.
                AvirBondHediff.Grant(initiator, recipient);

                Messages.Message(
                    initiator.LabelShortCap + " and " + recipient.LabelShortCap
                    + " are bonded. Avir hold no ceremony - the choice was the whole of it.",
                    new LookTargets(new List<Pawn> { initiator, recipient }),
                    MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] marriage postfix failed: " + e, 0x4A5287);
            }
        }
    }

    /// <summary>
    /// THE ONE WAY BACK.
    ///
    /// Maximus asked whether a resurrected partner should mean anything, and it has to. A grief
    /// that is permanent BY DESIGN becomes a grief that is permanent BY OVERSIGHT the moment the
    /// game offers a resurrector serum and the mod ignores it. The bond was severed by a death; if
    /// the death is undone, so is the severing.
    ///
    /// WHAT HAPPENS ON RESURRECTION:
    ///   - the grief hediff is removed from the mourner, which also lifts the permanent mood and
    ///     restores their ability to be a spouse rather than a widow;
    ///   - the marriage is restored, because vanilla rewrote it into an ex-spouse relation when the
    ///     partner died;
    ///   - both of them get a memory of it, and it is a large one.
    ///
    /// IT DOES NOT MATTER HOW LONG IT HAS BEEN. A month or a year - the settled stage of the grief
    /// is not healing, it is holding, and what it is holding is the absence of one specific person.
    /// Bring that person back and there is nothing left for it to hold.
    /// </summary>
    public static class Patch_AvirBondResurrection
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(ResurrectionUtility), "TryResurrect");
                if (target == null)
                {
                    Log.Warning("[Avir bond] ResurrectionUtility.TryResurrect not found; a "
                        + "resurrected partner will not lift the grief.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(Patch_AvirBondResurrection), nameof(Resurrect_Postfix)));

                Log.Message("[Avir bond] resurrection reunion bound to "
                    + "ResurrectionUtility.TryResurrect.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir bond] could not bind resurrection: " + e);
            }
        }

        /// <summary>
        /// Parameter named pawn, matching the assembly. __result is checked because a failed
        /// resurrection must not reunite anybody.
        /// </summary>
        public static void Resurrect_Postfix(Pawn pawn, bool __result)
        {
            if (!__result) return;

            try
            {
                if (pawn?.relations == null || pawn.Dead) return;

                List<Pawn> mourners = FindMournersFor(pawn);
                if (mourners == null) return;

                for (int i = 0; i < mourners.Count; i++)
                {
                    Reunite(mourners[i], pawn);
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] resurrection postfix failed: " + e, 0x4A5288);
            }
        }

        /// <summary>
        /// Anyone carrying severed-bond grief who was married to this pawn.
        ///
        /// SEARCHED THROUGH EX-SPOUSE, not spouse: vanilla rewrites the marriage into an ex-marriage
        /// when one of them dies, so by the time anybody is resurrected the living partner is on
        /// record as a widow rather than a wife.
        /// </summary>
        private static List<Pawn> FindMournersFor(Pawn revived)
        {
            List<DirectPawnRelation> relations = revived.relations.DirectRelations;
            if (relations == null) return null;

            List<Pawn> found = null;

            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation rel = relations[i];
                if (rel?.def == null || rel.otherPawn == null) continue;

                bool wasMarried = rel.def == PawnRelationDefOf.ExSpouse
                                  || rel.def == PawnRelationDefOf.Spouse
                                  || rel.def == PawnRelationDefOf.ExLover
                                  || rel.def == PawnRelationDefOf.Lover;
                if (!wasMarried) continue;

                Pawn other = rel.otherPawn;
                if (other.Dead) continue;
                if (!AvirPairBond.HasLostAMate(other)) continue;

                if (found == null) found = new List<Pawn>();
                found.Add(other);
            }

            return found;
        }

        private static void Reunite(Pawn mourner, Pawn revived)
        {
            // The grief goes. Nothing about it was healing - it was holding the absence of one
            // specific person, and that person is standing there.
            Hediff grief = mourner.health?.hediffSet?.GetFirstHediffOfDef(
                AvirPairBondDefOf.Avir_BondSevered);

            if (grief != null) mourner.health.RemoveHediff(grief);

            RestoreMarriage(mourner, revived);
            AvirBondHediff.Grant(mourner, revived);

            GiveReunionMemory(mourner, revived);
            GiveReunionMemory(revived, mourner);

            // WORDED SO IT CANNOT BE MISREAD AS "THE PENALTY REMAINS".
            //
            // The first version said the grief "did not heal - it simply has its other half back",
            // which was trying to say that time had done nothing and the return had done
            // everything. In a message feed it reads as "still there", and that flatly contradicts
            // what the four lines above this one just did: the hediff is gone, the -12 with it, and
            // the marriage is restored.
            //
            // A message that makes a player doubt what the code did is worse than no message.
            Messages.Message(
                mourner.LabelShortCap + " and " + revived.LabelShortCap
                + " are bonded again. The grief is over - it was never time that would have ended "
                + "it, only this.",
                new LookTargets(new List<Pawn> { mourner, revived }),
                MessageTypeDefOf.PositiveEvent, false);
        }

        private static void RestoreMarriage(Pawn a, Pawn b)
        {
            if (a.relations.DirectRelationExists(PawnRelationDefOf.Spouse, b)) return;

            if (a.relations.DirectRelationExists(PawnRelationDefOf.ExSpouse, b))
            {
                a.relations.RemoveDirectRelation(PawnRelationDefOf.ExSpouse, b);
            }

            if (a.relations.DirectRelationExists(PawnRelationDefOf.ExLover, b))
            {
                a.relations.RemoveDirectRelation(PawnRelationDefOf.ExLover, b);
            }

            if (a.relations.DirectRelationExists(PawnRelationDefOf.Lover, b))
            {
                a.relations.RemoveDirectRelation(PawnRelationDefOf.Lover, b);
            }

            a.relations.AddDirectRelation(PawnRelationDefOf.Spouse, b);
        }

        private static void GiveReunionMemory(Pawn p, Pawn other)
        {
            if (p?.needs?.mood?.thoughts?.memories == null) return;

            ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_BondRestored");
            if (def == null) return;

            // No otherPawn argument: this is a plain Thought_Memory now, not a social one. See the
            // note on the def - the +30 opinion it used to carry was doubling the offset the Spouse
            // relation already applies permanently, and drove no behaviour these two can have.
            p.needs.mood.thoughts.memories.TryGainMemory(def);
        }
    }
}
