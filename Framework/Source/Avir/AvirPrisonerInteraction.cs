using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// "IS THIS PRISONER SET TO X", ASKED CORRECTLY FOR BOTH KINDS OF OPTION.
    ///
    /// THE BREAKAGE THIS PREVENTS. Both essence options have just been marked
    /// isNonExclusiveInteraction so they sit in the checkbox panel beside hemogen farm rather than
    /// in the radio list. That is a UI change with a code consequence: a non-exclusive option is
    /// NOT stored in Pawn_GuestTracker.ExclusiveInteractionMode, so every existing
    /// `guest.ExclusiveInteractionMode == someDef` test silently becomes permanently false. The
    /// options would appear in the right place and do nothing at all - the worst kind of failure,
    /// because it looks like success.
    ///
    /// AND IT WAS ALREADY HAPPENING. AvirDrainEssence.IsFarmed carried a fallback so that
    /// prisoners set to the VANILLA hemogen farm would also feed Avir. Hemogen farm has been
    /// non-exclusive since Biotech shipped, so that comparison has never once returned true. The
    /// fallback has been dead since it was written; nobody noticed, because a feature that quietly
    /// does nothing produces no log line.
    ///
    /// WHY REFLECTION RATHER THAN THE OBVIOUS CALL. The method that answers this on
    /// Pawn_GuestTracker is exactly the sort of name this mod has repeatedly got wrong from memory
    /// - each miss costing a compile round trip. So it is resolved once, by shape, and what it
    /// bound to is printed. If nothing matches, the exclusive comparison still runs as a fallback
    /// and the log says plainly that the non-exclusive half is unavailable, rather than leaving a
    /// dead feature to be rediscovered in a month.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirPrisonerInteraction
    {
        private static MethodInfo isEnabled;
        private static bool resolved;

        static AvirPrisonerInteraction()
        {
            Resolve();
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;

            try
            {
                // The name in this version first.
                isEnabled = AccessTools.Method(typeof(Pawn_GuestTracker), "IsInteractionEnabled",
                    new[] { typeof(PrisonerInteractionModeDef) });

                if (isEnabled == null)
                {
                    // By shape: any instance method taking one PrisonerInteractionModeDef and
                    // returning bool. There is only one thing that can reasonably be.
                    MethodInfo[] all = typeof(Pawn_GuestTracker).GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    for (int i = 0; i < all.Length; i++)
                    {
                        MethodInfo m = all[i];
                        if (m.ReturnType != typeof(bool)) continue;

                        ParameterInfo[] ps = m.GetParameters();
                        if (ps.Length != 1) continue;
                        if (ps[0].ParameterType != typeof(PrisonerInteractionModeDef)) continue;

                        isEnabled = m;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] resolving the prisoner interaction test threw: " + e);
                isEnabled = null;
            }

            if (isEnabled != null)
            {
                Log.Message("[Avir] prisoner interaction test bound to Pawn_GuestTracker."
                    + isEnabled.Name + ".");
            }
            else
            {
                Log.Warning("[Avir] no non-exclusive prisoner interaction test found on "
                    + "Pawn_GuestTracker. Essence feeding and essence extraction will only work "
                    + "while they are the prisoner's exclusive mode - which, now that they are "
                    + "checkboxes, they never are. The method wanted takes one "
                    + "PrisonerInteractionModeDef and returns bool.");
            }
        }

        /// <summary>
        /// True when the prisoner has this option turned on, whether it is a radio choice or a
        /// checkbox. Both are asked, so this keeps answering correctly for any option that changes
        /// category later, and for saves made before this change.
        /// </summary>
        public static bool IsSetTo(Pawn prisoner, PrisonerInteractionModeDef def)
        {
            if (def == null || prisoner?.guest == null) return false;

            // The exclusive slot, which still covers options that are genuinely exclusive and any
            // save written before the change.
            if (prisoner.guest.ExclusiveInteractionMode == def) return true;

            if (isEnabled == null) return false;

            try
            {
                return (bool)isEnabled.Invoke(prisoner.guest, new object[] { def });
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] prisoner interaction test threw: " + e, 0x4A5266);
                return false;
            }
        }
    }
}
