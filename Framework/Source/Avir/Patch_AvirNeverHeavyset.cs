using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// AVIR ARE NEVER HEAVYSET, DECIDED WHERE EVERYBODY'S DIFFERENT DECIDES IT.
    ///
    /// WHY THE FRAME LOCK COULD NEVER HAVE FIXED THIS. Read out of EverybodysDifferent.dll:
    /// the mod postfixes PawnGenerator.GetBodyTypeFor, and inside EverybodysDifferentUtil
    /// .DeriveMaleBodyType the branch that returns BodyTypeDefOf.Fat is guarded by
    /// IsHeavyset(pawn) - a judgement about the pawn's WEIGHT AND BUILD, with no reference to the
    /// frame gene at all.
    ///
    /// That is why a pawn carrying "Average physique" was standing there round-bellied while every
    /// gene on it was perfectly acceptable. Swapping EBD_Frame_*Stout genes was correcting a
    /// different thing entirely, and correcting it well.
    ///
    /// AND WHY THE BODY-TYPE INVARIANT WAS NOT ENOUGH EITHER. Setting story.bodyType afterwards is
    /// a repair, and EBD asks GetBodyTypeFor again whenever it wants an answer. Fixing the output
    /// while the source keeps producing the same value is a race this mod does not need to be in.
    ///
    /// So the answer is given at the source: for a Fearborn pawn, IsHeavyset is false. The Fat
    /// branch is never reached, EBD's own logic then falls through to its ordinary build-and-height
    /// derivation, and everything else that mod does to Avir - stature, the physique panel, the
    /// variety Maximus wants - is untouched.
    ///
    /// COMPLETE NO-OP WITHOUT THAT MOD. The type is resolved by name; if it is absent, nothing is
    /// patched and nothing is logged as an error.
    /// </summary>
    public static class Patch_AvirNeverHeavyset
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                Type util = AccessTools.TypeByName("EverybodysDifferentUtil");
                if (util == null)
                {
                    // Everybody's Different is not installed. Entirely normal.
                    return;
                }

                MethodInfo target = AccessTools.Method(util, "IsHeavyset", new[] { typeof(Pawn) });
                if (target == null)
                {
                    Log.Warning("[Avir physique] EverybodysDifferentUtil.IsHeavyset(Pawn) not found. "
                        + "Avir may still be generated with the Fat body type by that mod.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(Patch_AvirNeverHeavyset), nameof(IsHeavyset_Postfix)));

                Log.Message("[Avir physique] bound to EverybodysDifferentUtil.IsHeavyset - Avir are "
                    + "never heavyset.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir physique] failed to apply: " + e);
            }
        }

        private static bool loggedFirst;

        /// <summary>
        /// Parameter named pawn, matching the assembly. Only ever turns a YES into a NO, and only
        /// for an Avir - every other pawn in the game is judged by that mod exactly as before.
        /// </summary>
        public static void IsHeavyset_Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result) return;

            try
            {
                if (!AvirFaction.HasFearborn(pawn)) return;

                __result = false;

                if (!loggedFirst)
                {
                    loggedFirst = true;
                    Log.Message("[Avir physique] " + pawn.LabelShortCap + " was judged heavyset by "
                        + "Everybody's Different; Avir are not. The Fat body type is no longer "
                        + "reachable for them.");
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir physique] postfix failed: " + e, 0x4A5278);
            }
        }
    }
}
