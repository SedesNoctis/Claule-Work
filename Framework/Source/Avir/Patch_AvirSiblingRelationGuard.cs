using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// A BROKEN FAMILY RELATION MUST NEVER KILL A RAID.
    ///
    /// I have now tried three times to find and fix every shape the corruption takes, and each
    /// time a shape I had not thought of survived and killed the raid anyway. So stop trying to be
    /// clever. The severity is wrong:
    ///
    ///     A sibling relation is COSMETIC. It is a line in the social tab.
    ///     A thrown exception here ABORTS THE ENTIRE PAWN GROUP - the whole raid is cancelled.
    ///
    /// There is no world in which "these two raiders are not listed as siblings" is worth losing
    /// the raid. So: a Harmony FINALIZER on PawnRelationWorker_Sibling.CreateRelation. If it
    /// throws, we swallow it, the sibling link is silently not created, and generation carries on.
    /// The raid arrives.
    ///
    /// WHY IT THROWS, for the record: RimWorld builds a sibling link by reusing or generating the
    /// two pawns' shared PARENTS, and it decides which parent is the "mother" purely by looking at
    /// gender. If a pawn recorded as a mother is male - which older versions of this mod caused, by
    /// rewriting gender after the family tree had already been built - then GetMother() hands back
    /// a null, SetMother() logs
    ///
    ///     "Tried to set <name> with gender Male as <name>'s mother."
    ///
    /// and the null is then dereferenced. GameComponent_AvirRelationRepair cleans this up in the
    /// world, but it can only fix the pawns it can find; this finalizer is the guarantee that even
    /// one it missed cannot cost you a raid.
    ///
    /// THIS IS A SAFETY NET, NOT A FIX. It is deliberately loud the first time it catches
    /// something, so the underlying corruption still gets reported rather than silently papered
    /// over forever.
    /// </summary>
    [HarmonyPatch(typeof(PawnRelationWorker_Sibling), nameof(PawnRelationWorker_Sibling.CreateRelation))]
    public static class Patch_AvirSiblingRelationGuard
    {
        private static int caught;

        public static Exception Finalizer(Exception __exception, Pawn generated, Pawn other)
        {
            if (__exception == null) return null; // no problem - carry on

            caught++;

            if (caught <= 3)
            {
                Log.Warning("[Avir] Swallowed a crash while creating a SIBLING relation"
                            + (generated != null ? " for " + generated.LabelShortCap : "")
                            + (other != null ? " with " + other.LabelShortCap : "")
                            + ". The two will simply not be siblings. THE RAID IS NOT CANCELLED.\n"
                            + "    Cause: one of their parents is recorded with the wrong gender - a male "
                            + "'mother' - so RimWorld dereferenced a null while wiring the family tree. "
                            + "This is old corruption in the save, left by an earlier version of this mod. "
                            + "GameComponent_AvirRelationRepair tries to clean it; this caught one it missed.\n"
                            + "    Original: " + __exception.Message);
            }
            else if (caught == 4)
            {
                Log.Warning("[Avir] (further sibling-relation crashes will be swallowed silently)");
            }

            // Returning null tells Harmony: the exception is handled, do not rethrow.
            return null;
        }
    }
}
