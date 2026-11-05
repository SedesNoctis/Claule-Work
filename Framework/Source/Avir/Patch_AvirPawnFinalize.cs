using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE important patch.
    ///
    /// PawnGenerator assigns genes AFTER it assigns the name, the backstories and the traits. That
    /// means every HasActiveGene(Fearborn) check at those earlier hook points sees a pawn with no
    /// genes yet and quietly does nothing - which is why Avir were being generated with human
    /// names, faction backstories and no Avir traits.
    ///
    /// This runs at the very end of GeneratePawn, when the genes are finally there, and fixes all
    /// three. The earlier patches are left in place: when they do fire (a pawn whose genes were
    /// already set), this one sees Avir content and skips.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirPawnFinalize
    {
        public static void Postfix(Pawn __result)
        {
            try
            {
                Pawn pawn = __result;
                if (pawn == null) return;

                // Only pay for this when someone is actually watching. This postfix runs for EVERY
                // pawn every mod in the pack generates, so anything unconditional here is a tax on
                // the whole game, not just on Avir.
                if (AvirDiag.Verbose)
                {
                    bool avirish = AvirFaction.IsAvirPawnKind(pawn)
                                   || AvirFaction.IsAvirFaction(pawn)
                                   || AvirFaction.HasFearborn(pawn)
                                   || (pawn.kindDef != null && pawn.kindDef.defName.IndexOf("Avir", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (avirish) AvirDiag.TracePawn(pawn, "GENERATE (pre-gate)");
                }

                if (!AvirFaction.ShouldUseAvirContent(pawn))
                {
                    // Not one of ours - but check nobody has filed them as one of ours.
                    EvictImpostor(pawn);
                    return;
                }

                FixBackstories(pawn);
                FixTraits(pawn);
                FixName(pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Pawn finalize failed: " + e, 0x4A51B9);
            }
        }

        // ---------- MEMBERSHIP ----------

        private static bool loggedEviction;

        /// <summary>
        /// A BASELINER HUMAN IS NOT A MEMBER OF AN AVIR ORDER, WHATEVER GENERATED HIM.
        ///
        /// THE CASE. A transport pod crashed with "Viktor Einstrom, refugee of A'Shaign" - a
        /// nineteen-year-old baseliner with an organ-farm childhood. Avir do not use pods, and the
        /// A'Shaign Order does not have human refugees. The quest picked a faction the way every
        /// such quest does - any non-hostile humanlike faction on the world - and the Avir orders
        /// are ordinary Outlander-tagged factions as far as that choice is concerned.
        ///
        /// WHY THE FIX IS HERE AND NOT IN THE QUEST. RefugeePodCrash resolves through
        /// QuestNode_Root_RefugeePodCrash, a compiled class; and it is one of MANY. Downed
        /// refugees, wanderers, rescue quests, prisoner-rescue sites and refugee-chased all pick a
        /// faction the same way. Patching the pod crash would fix the pod crash and leave the rest
        /// of them to be discovered one screenshot at a time.
        ///
        /// So the rule is stated once, about membership rather than about events: no pawn is
        /// generated as a member of an Avir faction unless the pawn is an Avir. Every quest that
        /// picks an Avir order as somebody's origin is corrected by this, including the ones
        /// neither of us has thought of.
        ///
        /// WHAT IT DOES NOT DO. It does not cancel the event. The refugee still crashes, still
        /// needs help, still joins if you take him in - he is simply from somewhere else. Blocking
        /// the quest outright would quietly delete content Maximus never asked to lose.
        ///
        /// The faction swap happens INSIDE PawnGenerator.GeneratePawn, before the quest has a
        /// finished pawn to read, which is why the letter and the pawn agree afterwards.
        /// </summary>
        private static void EvictImpostor(Pawn pawn)
        {
            if (!AvirFaction.IsAvirFaction(pawn)) return;

            // Only humanlikes are ever handed out as somebody's refugee, and restricting to them
            // keeps this away from animals an Avir settlement might legitimately own.
            if (pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return;

            Faction wrong = pawn.Faction;
            Faction replacement = FindReplacementFaction(wrong);

            // NO SWAP RATHER THAN A BAD SWAP. On a world with no other suitable faction, leaving
            // the pawn where he is costs one odd label; moving him to a hostile faction or to
            // nothing at all could break the quest that is mid-flight around him.
            if (replacement == null)
            {
                if (!loggedEviction)
                {
                    loggedEviction = true;
                    Log.Warning("[Avir] " + pawn.LabelShortCap + " was generated as a member of "
                        + wrong.Name + " but is not an Avir, and no other suitable faction exists "
                        + "on this world to reassign him to. Left as generated.");
                }

                return;
            }

            pawn.SetFaction(replacement);

            if (!loggedEviction)
            {
                loggedEviction = true;
                Log.Message("[Avir] " + pawn.LabelShortCap + " was generated as a member of "
                    + wrong.Name + " without being an Avir; reassigned to " + replacement.Name
                    + ". Avir orders do not have non-Avir members.");
            }
        }

        private static Faction FindReplacementFaction(Faction wrong)
        {
            List<Faction> all = Find.FactionManager?.AllFactionsListForReading;
            if (all == null) return null;

            Faction player = Faction.OfPlayer;
            Faction best = null;
            int seen = 0;

            for (int i = 0; i < all.Count; i++)
            {
                Faction f = all[i];
                if (f == null || f.def == null) continue;
                if (f == wrong || f.IsPlayer || f.def.hidden) continue;
                if (AvirFaction.IsAvirFactionDef(f.def)) continue;

                // Somebody's refugee comes from somewhere that is not currently trying to kill us.
                if (player != null && f.HostileTo(player)) continue;

                // Reservoir sample, so the replacement is not always whichever faction the world
                // happens to list first.
                seen++;
                if (Rand.Range(0, seen) == 0) best = f;
            }

            return best;
        }

        // ---------- NAME ----------

        private static void FixName(Pawn pawn)
        {
            if (pawn.Name == null) return;

            NameTriple current = pawn.Name as NameTriple;

            // Keep the surname ONLY if it's already an Avir one - that means it was inherited from
            // an Avir parent and is a real family line. A human surname left over from the faction
            // namer gets replaced, which is why Avir were ending up as "Meliirie ... Marie".
            string keepLast = null;
            if (current != null && Patch_AvirNames.IsAvirSurname(current.Last))
            {
                keepLast = current.Last;
            }
            // A FAMILY'S OWN NAME IS KEPT TOO. A hatched chick carries its mother's surname (or its father's,
            // Patch_AvirHarpyBirth.InheritSurname) - and a family named by the player (Starsong, Wildsong...) is in
            // no word list, so the check above replaced it with a random one. A surname shared with a parent is a
            // family line by definition.
            else if (current != null && !current.Last.NullOrEmpty()
                     && ((pawn.GetMother()?.Name as NameTriple)?.Last == current.Last || (pawn.GetFather()?.Name as NameTriple)?.Last == current.Last))
            {
                keepLast = current.Last;
            }

            Name replacement = Patch_AvirNames.GenerateAvirName(pawn, keepLast);
            if (replacement != null) pawn.Name = replacement;
        }

        // ---------- BACKSTORIES ----------

        private static void FixBackstories(Pawn pawn)
        {
            if (pawn.story == null) return;

            BackstoryDef oldChild = pawn.story.Childhood;
            BackstoryDef oldAdult = pawn.story.Adulthood;

            BackstoryDef newChild = null;
            BackstoryDef newAdult = null;

            if (oldChild != null && !Patch_AvirBackstories.BelongsToRoleOf(pawn, oldChild))
            {
                newChild = Patch_AvirBackstories.PickAvirBackstory(
                    BackstorySlot.Childhood, pawn.gender, null,
                    pawn.kindDef != null ? pawn.kindDef.defName : null);
            }
            // A CHILD HAS NO ADULTHOOD. Hatchlings are children (see Patch_AvirBodyAndAge), and
            // handing them an adulthood backstory is what gave that six-year-old a grown man's
            // history and a grown man's skills.
            bool isChild = pawn.DevelopmentalStage.Juvenile();

            if (!isChild && oldAdult != null && !Patch_AvirBackstories.BelongsToRoleOf(pawn, oldAdult))
            {
                // Pass the childhood (the new one if we just replaced it) so a linked line is
                // followed: the silent hatchling grows into the one nobody hears coming.
                BackstoryDef basis = newChild ?? oldChild;
                string kindName = pawn.kindDef != null ? pawn.kindDef.defName : null;
                newAdult = Patch_AvirBackstories.PickAvirBackstory(
                    BackstorySlot.Adulthood, pawn.gender, basis, kindName);
            }

            if (newChild == null && newAdult == null) return;

            // Skills were already rolled from the old backstories, so undo their gains and apply
            // the new ones - otherwise the pawn keeps a farmer's Plants score forever. Both swaps
            // go through Patch_AvirBackstories now, which is the only place that knows how to move
            // a title and its numbers together.
            if (newChild != null)
            {
                AvirDiag.TraceSwap(pawn, "Childhood", oldChild, newChild, "finalize");
                Patch_AvirBackstories.ReplaceChildhood(pawn, newChild);
            }

            if (newAdult != null)
            {
                AvirDiag.TraceSwap(pawn, "Adulthood", oldAdult, newAdult, "finalize");
                Patch_AvirBackstories.ReplaceAdulthood(pawn, newAdult);
            }
        }

        // ---------- TRAITS ----------

        private static void FixTraits(Pawn pawn)
        {
            if (pawn.story == null || pawn.story.traits == null) return;

            // If an earlier patch already gave them Avir traits, leave well alone.
            List<Trait> held = pawn.story.traits.allTraits;
            for (int i = 0; i < held.Count; i++)
            {
                if (Patch_AvirTraits.IsAvirTrait(held[i].def)) return;
            }

            Patch_AvirTraits.GrantAvirTraits(pawn);
        }
    }
}
