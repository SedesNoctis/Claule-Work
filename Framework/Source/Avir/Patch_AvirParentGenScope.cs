using HarmonyLib;
using Verse;

namespace Avir
{
    /// <summary>
    /// "ARE WE BUILDING SOMEBODY'S PARENT RIGHT NOW?"
    ///
    /// This exists to tell two IDENTICAL-LOOKING requests apart. Both reach PawnGenerator with
    /// FixedGender already set, and the request itself cannot tell you who set it:
    ///
    ///   (A) The relation worker asks for a MALE Avir_Huntress, because it is building that
    ///       huntress's FATHER. This MUST be obeyed. Refuse it and you put a female in the father
    ///       slot, dereference a null, and cancel the raid - the bug that ate five rounds.
    ///
    ///   (B) Sex Matters Redux (Evyatar108.SexMattersRedux) transpiles
    ///       PawnGroupKindWorker_Normal.GeneratePawns and fills in the Nullable&lt;Gender&gt; local
    ///       that vanilla leaves NULL, using a coin flip weighted by its maleProbability setting.
    ///       Every raider is handed a gender before PawnGenerator ever sees them. This MUST NOT be
    ///       obeyed for a caste that has a gender, or huntresses and priestesses arrive male.
    ///
    /// So we ask WHERE we are rather than WHAT we were handed. While relation generation is on the
    /// stack, a fixed gender is load-bearing and Patch_AvirGenderRequest defers to it. Everywhere
    /// else - raids included - the caste rule wins.
    ///
    /// WHY THIS METHOD AND NOT PawnRelationWorkerUtility.GenerateParent:
    ///
    ///   Because that type does not exist under that name -
    ///   "CS0246: The type or namespace name 'PawnRelationWorkerUtility' could not be found".
    ///   Rather than go hunting for whatever it is called this version, anchor to a method we KNOW
    ///   is there, because Patch_AvirDisableRelationGeneration already patches it successfully:
    ///   PawnGenerator.GeneratePawnRelations. Every parent RimWorld invents is generated somewhere
    ///   underneath it, so wrapping it catches all of them and depends on nothing unverified.
    ///
    ///   It is PRIVATE, so it is named with a STRING. Harmony finds it by reflection; nameof()
    ///   cannot see it. (Same reason as Patch_AvirDisableRelationGeneration - do not "fix" either
    ///   of these into nameof().)
    ///
    /// Depth is a COUNTER, not a bool, because relation generation nests: making a parent can make
    /// that parent's relations, and so on.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawnRelations")]
    public static class Patch_AvirParentGenScope
    {
        private static int depth;

        /// <summary>True while RimWorld is inventing family for somebody.</summary>
        public static bool GeneratingParent
        {
            get { return depth > 0; }
        }

        // First, so the flag is up before Patch_AvirDisableRelationGeneration's prefix can return
        // false and skip the remaining prefixes. (If it does return false, no relations are
        // generated at all and the flag simply goes back down - harmless.)
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            depth++;
        }

        // A FINALIZER, not a postfix: it runs even when the original throws, AND even when another
        // prefix returned false. A postfix would not.
        //
        // This matters more than it looks. If an exception ever left depth stuck above zero, every
        // Avir generated afterwards would look like a parent request, Patch_AvirGenderRequest would
        // stand aside forever, and the caste rule would silently stop working for the rest of the
        // session. A permanent, invisible failure is worth guarding against.
        public static void Finalizer()
        {
            depth--;
            if (depth < 0) depth = 0;
        }
    }
}
