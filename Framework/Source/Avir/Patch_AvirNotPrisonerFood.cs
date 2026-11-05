using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// AVIR PRODUCTION IS NOT PRISONER RATIONS, WHEREVER IT HAPPENS TO BE LYING.
    ///
    /// THE TRAP, AND IT IS A GOOD ONE. Essence extract can only be obtained FROM a prisoner, so the
    /// vial is created inside a prison cell every single time. Vanilla treats anything ingestible
    /// lying in a prison cell as reserved for the prisoners in it - which is exactly right for a
    /// bowl of gruel and exactly wrong for the thing the prisoner was just harvested to produce.
    ///
    /// The result was an item that could only be made in the one place from which it could never be
    /// removed: "Drink essence extract: Reserved for prisoners: Unsuitable food", and no hauling
    /// either.
    ///
    /// ONE PREDICATE, BOTH SYMPTOMS. SocialProperness.IsSociallyProper is what the ingest menu and
    /// the hauling scan both consult - found by tracing the "ReservedForPrisoners" string back
    /// through FloatMenuOptionProvider_Ingest and HaulAIUtility. Correcting it there fixes taking,
    /// drinking and hauling at once, rather than three patches chasing the same cause.
    ///
    /// APPLIED TO ALL AVIR PRODUCTION, not just the extract, because Maximus asked for the check to
    /// be swept across everything the species makes and the trap is not special to one item. Any
    /// Avir good that ends up on a prison floor - a vial dropped during a fight, wine set down by a
    /// warden, feathers from a captive Avir - would have been caught by the same rule.
    ///
    /// WHAT IS DELIBERATELY LEFT ALONE: blueberries and cookies. Those ARE food, and food in a
    /// prison cell being reserved for the prisoner is the rule working correctly.
    /// </summary>
    public static class Patch_AvirNotPrisonerFood
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                int bound = 0;

                MethodInfo[] all = typeof(SocialProperness).GetMethods(
                    BindingFlags.Public | BindingFlags.Static);

                for (int i = 0; i < all.Length; i++)
                {
                    MethodInfo m = all[i];
                    if (m.Name != "IsSociallyProper") continue;
                    if (m.ReturnType != typeof(bool)) continue;

                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 0 || ps[0].ParameterType != typeof(Thing)) continue;

                    // BOTH OVERLOADS. The two-argument form and the four-argument one are separate
                    // entry points and different callers reach different ones; patching only the
                    // short one would fix the float menu and leave hauling exactly as it was.
                    harmony.Patch(m, postfix: new HarmonyMethod(
                        typeof(Patch_AvirNotPrisonerFood), nameof(Proper_Postfix)));
                    bound++;
                }

                if (bound == 0)
                {
                    Log.Warning("[Avir prisonfood] SocialProperness.IsSociallyProper not found. "
                        + "Essence extract will stay stuck in the cell it was made in.");
                    return;
                }

                Log.Message("[Avir prisonfood] bound to " + bound
                    + " IsSociallyProper overload(s) - Avir production is not prisoner rations.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir prisonfood] failed to apply: " + e);
            }
        }

        private static bool loggedFirst;

        /// <summary>
        /// Parameter named t, matching the assembly. Only ever turns a NO into a YES, and only for
        /// Avir production - every other thing in the game is judged exactly as before.
        /// </summary>
        public static void Proper_Postfix(Thing t, ref bool __result)
        {
            if (__result) return;

            try
            {
                if (!IsAvirProduction(t?.def)) return;

                __result = true;

                if (!loggedFirst)
                {
                    loggedFirst = true;
                    Log.Message("[Avir prisonfood] " + t.def.defName + " was reserved for prisoners "
                        + "because it was lying in a cell. It is Avir production, not rations - "
                        + "released.");
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir prisonfood] postfix failed: " + e, 0x4A5279);
            }
        }

        private static readonly HashSet<string> ActualFood = new HashSet<string>
        {
            // These are meals. A meal in a cell belonging to the prisoner is the rule working.
            "Avir_Blueberries",
            "Avir_BlueberryCookies",
        };

        /// <summary>
        /// Mod items whose defNames carry NO prefix, listed because the prefix rule cannot see them.
        ///
        /// Every ingestible the mod defines does start with "Avir_" - checked - so the prefix alone
        /// would have covered the essence extract that prompted this. These are materials, and a
        /// stack of essentium dropped on a cell floor is stuck by the same rule; a fix that only
        /// released the drinkables would have left that to be rediscovered later.
        /// </summary>
        private static readonly HashSet<string> UnprefixedProduction = new HashSet<string>
        {
            "Essentium",
            "Cloudcloth",
            "BloodstoneTisserium",
            "Leather_AvirFeatheredFur",
        };

        private static readonly Dictionary<ThingDef, bool> cache = new Dictionary<ThingDef, bool>();

        /// <summary>
        /// Anything the species makes, by defName prefix, minus the things that really are food.
        ///
        /// A prefix rather than a list, so an item added next month is covered without this file
        /// being revisited. Cached per def because this runs inside a predicate the game asks
        /// constantly.
        /// </summary>
        private static bool IsAvirProduction(ThingDef def)
        {
            if (def?.defName == null) return false;

            bool known;
            if (cache.TryGetValue(def, out known)) return known;

            bool result = (def.defName.StartsWith("Avir_")
                           || UnprefixedProduction.Contains(def.defName))
                          && !ActualFood.Contains(def.defName);

            cache[def] = result;
            return result;
        }
    }
}
