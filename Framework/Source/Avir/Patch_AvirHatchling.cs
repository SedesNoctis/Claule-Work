using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    [DefOf]
    public static class AvirHatchlingDefOf
    {
        public static BackstoryDef Avir_Hatchling;   // replaces vanilla "Baby"
        public static BackstoryDef Avir_Nestling;    // replaces vanilla "Child"

        static AvirHatchlingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirHatchlingDefOf));
        }
    }

    /// <summary>
    /// Colony-born Avir are hatchlings, not human babies.
    ///
    /// Biotech hands a newborn the vanilla "Baby" backstory, swaps it for "Child" at age 3, and
    /// then rolls a real childhood from the player's faction culture at the age-13 growth moment
    /// (and an adulthood at 20). This component sweeps once an in-game hour and corrects all of
    /// that on any pawn carrying Fearborn:
    ///
    ///     Baby                   -> hatchling
    ///     Child                  -> nestling
    ///     any non-Avir backstory -> a proper Avir one, gender-matched
    ///
    /// A sweep rather than a Harmony patch on the generator or the age tracker: those methods get
    /// renamed between versions - SetBackstoryInSlot no longer exists in 1.6 - and a patch against
    /// a renamed method is a hard error. A swap that lands up to an hour late is invisible.
    /// </summary>
    public class GameComponent_AvirHatchling : GameComponent
    {
        private const int CheckInterval = 2500; // one in-game hour

        // Looked up by name rather than through BackstoryDefOf, which doesn't exist in 1.6.
        private static BackstoryDef babyDef;
        private static BackstoryDef childDef;

        private static BackstoryDef BabyDef
        {
            get
            {
                if (babyDef == null) babyDef = DefDatabase<BackstoryDef>.GetNamedSilentFail("Baby");
                return babyDef;
            }
        }

        private static BackstoryDef ChildDef
        {
            get
            {
                if (childDef == null) childDef = DefDatabase<BackstoryDef>.GetNamedSilentFail("Child");
                return childDef;
            }
        }

        public GameComponent_AvirHatchling(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % CheckInterval != 0) return;

            List<Pawn> pawns = AvirPawnScan.AllLivePawns();
            for (int i = 0; i < pawns.Count; i++)
            {
                TryHatchlingify(pawns[i]);
            }

            AuditOnce(pawns);
        }

        private static bool audited;

        /// <summary>
        /// NAMES THE DAMAGE THIS SWEEP DID BEFORE THE GATE WAS FIXED, ONCE PER SESSION.
        ///
        /// The gate used to treat anyone in an Avir-prefixed faction as an Avir, and the player's
        /// own colony is Avir_FreeNesting - so every prisoner and every recruited outsider had
        /// their childhood, adulthood and name rewritten by this sweep. Fixing the gate stops it
        /// happening again; it cannot give anyone their history back, because the original was
        /// overwritten in the save and is simply gone.
        ///
        /// So the least this can do is say WHO. A pawn with no Fearborn and no Avir pawnkind who is
        /// nevertheless carrying an Avir backstory was caught by the old rule, and the log names
        /// them so the damage can be repaired by hand rather than discovered one pawn at a time,
        /// months later, on a character sheet.
        ///
        /// Reporting rather than repairing on purpose. Rolling a replacement would mean inventing a
        /// history for a pawn whose real one is unrecoverable, and doing it silently to pawns
        /// across a live save.
        /// </summary>
        private static void AuditOnce(List<Pawn> pawns)
        {
            if (audited) return;
            audited = true;

            try
            {
                int found = 0;

                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn pawn = pawns[i];
                    if (pawn?.story == null) continue;
                    if (AvirFaction.ShouldUseAvirContent(pawn)) continue;

                    bool child = Patch_AvirBackstories.IsAvirBackstory(pawn.story.Childhood);
                    bool adult = Patch_AvirBackstories.IsAvirBackstory(pawn.story.Adulthood);
                    if (!child && !adult) continue;

                    found++;
                    Log.Warning("[Avir] " + pawn.LabelShortCap + " is not an Avir but carries "
                        + (child && adult ? "an Avir childhood and adulthood"
                            : child ? "an Avir childhood" : "an Avir adulthood")
                        + " - written by the old faction-based gate. Their original history is not "
                        + "recoverable from the save; edit by hand if it matters.");
                }

                if (found > 0)
                {
                    Log.Warning("[Avir] " + found + " non-Avir pawn(s) affected by the old gate. "
                        + "No further pawns will be converted.");
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] backstory audit failed: " + e, 0x4A5267);
            }
        }

        public static void TryHatchlingify(Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.story == null) return;
                if (!AvirFaction.ShouldUseAvirContent(pawn)) return;

                FixChildhood(pawn);
                FixAdulthood(pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Hatchling backstory swap failed: " + e, 0x4A51B5);
            }
        }

        private static void FixChildhood(Pawn pawn)
        {
            BackstoryDef current = pawn.story.Childhood;
            if (current == null) return;

            // AGE FIRST, POOL SECOND - AND THE ORDER IS THE WHOLE FIX.
            //
            // A chick hatched an hour ago was arriving with a full caste childhood: Maximus's came
            // out of a two-egg clutch carrying "Most clutches are two eggs, and so a MIDDLE
            // hatchling is a rare thing", a story that contradicts the clutch it was born from and
            // describes a life the pawn has not had a single day of.
            //
            // It survived because the pool check below runs first. That story genuinely belongs to
            // the chick's role pool, so BelongsToRoleOf accepted it and the replacement never ran.
            // The check is correct - it is what stops adult Avir being re-rolled hourly - it simply
            // has no business being consulted for someone with no past to describe.
            //
            // So developmental stage is asked before pool membership. A newborn is a hatchling and
            // a child is a nestling, whatever the generator handed them; the real caste childhood
            // arrives later, at the growth moment, through the path further down.
            if (pawn.DevelopmentalStage.Baby() || pawn.DevelopmentalStage.Newborn())
            {
                if (AvirHatchlingDefOf.Avir_Hatchling != null && current != AvirHatchlingDefOf.Avir_Hatchling)
                {
                    Patch_AvirBackstories.ReplaceChildhood(pawn, AvirHatchlingDefOf.Avir_Hatchling);
                }
                return;
            }

            if (pawn.DevelopmentalStage.Child())
            {
                if (AvirHatchlingDefOf.Avir_Nestling != null && current != AvirHatchlingDefOf.Avir_Nestling)
                {
                    Patch_AvirBackstories.ReplaceChildhood(pawn, AvirHatchlingDefOf.Avir_Nestling);
                }
                return;
            }

            // ONCE IT IS IN HIS OWN POOL, IT IS SETTLED. Two things had to be true here and
            // neither was:
            //   - the guard must RECOGNISE caste backstories (it didn't - so they were re-rolled
            //     once an in-game hour, forever, for every talon and artificer in the colony)
            //   - the guard must not accept a story from the WRONG pool (it did - a general-pool
            //     story is still an Avir story, so the warrior with the stone-setter's childhood
            //     passed the check and kept it for good)
            // BelongsToRoleOf answers both. Pool membership is fixed, so this settles and stays settled.
            if (Patch_AvirBackstories.BelongsToRoleOf(pawn, current)) return;

            if (BabyDef != null && current == BabyDef)
            {
                Patch_AvirBackstories.ReplaceChildhood(pawn, AvirHatchlingDefOf.Avir_Hatchling);
                return;
            }

            if (ChildDef != null && current == ChildDef)
            {
                Patch_AvirBackstories.ReplaceChildhood(pawn, AvirHatchlingDefOf.Avir_Nestling);
                return;
            }

            // Anything else came from the faction culture at a growth moment. Replace it - and
            // unwind its skill gains as we go, which the old SetChildhood path never did. That
            // omission is why the numbers on the sheet stopped matching the titles above them.
            string kindName = pawn.kindDef != null ? pawn.kindDef.defName : null;
            BackstoryDef replacement =
                Patch_AvirBackstories.PickAvirBackstory(BackstorySlot.Childhood, pawn.gender, null, kindName);

            if (replacement != null)
            {
                AvirDiag.TraceSwap(pawn, "Childhood", current, replacement, "hourly sweep");
                Patch_AvirBackstories.ReplaceChildhood(pawn, replacement);
            }
        }

        private static void FixAdulthood(Pawn pawn)
        {
            BackstoryDef current = pawn.story.Adulthood;
            if (current == null) return; // still a child - nothing to fix yet

            if (Patch_AvirBackstories.BelongsToRoleOf(pawn, current)) return;

            string kindName = pawn.kindDef != null ? pawn.kindDef.defName : null;
            BackstoryDef replacement = Patch_AvirBackstories.PickAvirBackstory(
                BackstorySlot.Adulthood, pawn.gender, pawn.story.Childhood, kindName);

            if (replacement != null)
            {
                AvirDiag.TraceSwap(pawn, "Adulthood", current, replacement, "hourly sweep");
                Patch_AvirBackstories.ReplaceAdulthood(pawn, replacement);
            }
        }
    }
}
