using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// Feignallite works FASTER than whatever it stood in for. The Singing Metal is already
    /// half-shaped; a crafter is finishing a form rather than forcing one.
    ///
    /// WHY THIS IS NOT XML. A recipe carries a single workAmount and has no idea which of its
    /// allowed ingredients a pawn will actually bring - the choice happens per job, not per def.
    /// workSpeedStat is no help either: that is a stat of the PAWN, and it never sees the
    /// material. So the multiplier has to be applied at the moment a specific job's work total
    /// is established, which is what this patch does.
    ///
    /// WHERE IT HOOKS, AND WHY HERE. JobDriver_DoBill.workLeft is a plain public field, set
    /// inside the initAction of the recipe toil. That initAction is an anonymous delegate inside
    /// Toils_Recipe.DoRecipeWork - patching it directly means targeting a compiler-generated
    /// closure, which breaks on any RimWorld update that reshuffles that method. Instead this
    /// postfixes MakeNewToils (a real, named, virtual method) and CHAINS onto each returned
    /// toil's initAction. After vanilla's own initAction has run, we look at workLeft; the first
    /// toil that leaves it above zero is by definition the one that just set it, so that is where
    /// the division lands. No closure targeting, no reliance on toil ordering.
    ///
    /// RATE. A four-step ladder on the feignallite SHARE of the placed material, scaled to the
    /// ceiling set by the "Feignallite work speed" slider (default 5.0x):
    ///
    ///     share  factor            at ceiling 5.0
    ///     100%   ceiling                   5.00x
    ///     >=75%  1 + (C-1) * 0.75          4.00x
    ///     >=50%  1 + (C-1) * 0.50          3.00x
    ///     >0%    1 + (C-1) * 0.25          2.00x
    ///     0      1.0                       untouched
    ///
    /// Expressing the rungs as fractions of the ceiling rather than as four hardcoded numbers
    /// means the slider stays a single honest control: move it and the whole ladder moves with
    /// it, keeping 5/4/3/2 proportions at any setting.
    ///
    /// Share is by UNITS, not by ingredient lines. A recipe wanting 20 steel that a pawn fills
    /// with 19 steel + 1 feignallite is 5% - the bottom rung, 2x. Counting lines instead would
    /// have made that same job 5x, and would have degenerated on the single-line recipes that
    /// make up most of what the substitution patch touches.
    ///
    /// READING THE COUNTS. ThingCountClass's count member is the one piece of API here I will
    /// not name from memory, so it is resolved once at startup against a list of candidates and
    /// the result is logged. If none bind, the patch degrades to a kind-only rule (all
    /// feignallite -> ceiling, mixed -> bottom rung) rather than throwing or going inert.
    ///
    /// COST. Runs once per crafting job start, never per tick. Nothing here touches TickRare or
    /// anything hotter.
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_DoBill), "MakeNewToils")]
    public static class Patch_AvirFeignalliteWorkSpeed
    {
        private static ThingDef feignalliteCached;
        private static bool feignalliteResolved;
        private static bool loggedFirstUse;
        private static bool loggedFailure;

        // ThingCountClass's count member, resolved by name at startup. Field first, then
        // property - both shapes exist in the wild across RimWorld versions and mod forks.
        private static System.Reflection.FieldInfo countField;
        private static System.Reflection.PropertyInfo countProperty;
        private static bool countResolved;

        private static void ResolveCountMember()
        {
            if (countResolved) return;
            countResolved = true;

            string[] candidates = { "Count", "count", "stackCount" };
            for (int i = 0; i < candidates.Length; i++)
            {
                countField = AccessTools.Field(typeof(ThingCountClass), candidates[i]);
                if (countField != null && countField.FieldType == typeof(int))
                {
                    Log.Message("[Avir] Feignallite work speedup bound ingredient counts to field ThingCountClass." + candidates[i] + ".");
                    return;
                }
                countField = null;

                countProperty = AccessTools.Property(typeof(ThingCountClass), candidates[i]);
                if (countProperty != null && countProperty.PropertyType == typeof(int) && countProperty.CanRead)
                {
                    Log.Message("[Avir] Feignallite work speedup bound ingredient counts to property ThingCountClass." + candidates[i] + ".");
                    return;
                }
                countProperty = null;
            }

            Log.Warning("[Avir] Feignallite work speedup could not find a count member on "
                        + "ThingCountClass - falling back to a kind-only rule (all feignallite = "
                        + "full ceiling, mixed = bottom rung).");
        }

        private static int ReadCount(ThingCountClass entry)
        {
            if (countField != null) return (int)countField.GetValue(entry);
            if (countProperty != null) return (int)countProperty.GetValue(entry, null);
            return -1;   // unresolved - caller switches to the kind-only rule
        }

        /// <summary>
        /// Maps a feignallite share onto the ladder, expressed as a fraction of the ceiling's
        /// bonus so the four rungs keep their 5/4/3/2 shape whatever the slider is set to.
        /// </summary>
        private static float LadderFactor(float share, float ceiling)
        {
            if (share <= 0f) return 1f;
            float bonusShare;
            if (share >= 0.9999f) bonusShare = 1.00f;
            else if (share >= 0.75f) bonusShare = 0.75f;
            else if (share >= 0.50f) bonusShare = 0.50f;
            else bonusShare = 0.25f;
            return 1f + (ceiling - 1f) * bonusShare;
        }

        private static ThingDef Feignallite
        {
            get
            {
                if (!feignalliteResolved)
                {
                    feignalliteResolved = true;
                    feignalliteCached = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_Feignallite");
                    if (feignalliteCached == null)
                        Log.Warning("[Avir] Avir_Feignallite not found - feignallite work speedup is inert.");
                }
                return feignalliteCached;
            }
        }

        [HarmonyPostfix]
        public static IEnumerable<Toil> Postfix(IEnumerable<Toil> __result, JobDriver_DoBill __instance)
        {
            // Settings may legitimately be null very early in load; treat that as "off".
            float ceiling = AvirMod.Settings != null ? AvirMod.Settings.feignalliteWorkMultiplier : 1f;
            if (ceiling <= 1.0001f || Feignallite == null)
            {
                foreach (Toil untouched in __result) yield return untouched;
                yield break;
            }

            bool applied = false;

            foreach (Toil toil in __result)
            {
                Toil captured = toil;
                Action original = captured.initAction;

                captured.initAction = delegate
                {
                    // Vanilla behaviour always runs first and unmodified.
                    if (original != null) original();

                    if (applied) return;

                    // workLeft is still zero on every toil before the recipe toil, so this is
                    // what identifies the right one without caring about their order.
                    if (__instance.workLeft <= 0f) return;

                    float factor = SpeedFactor(__instance, ceiling);
                    if (factor <= 1.0001f)
                    {
                        // Marked as handled anyway: the ingredients for THIS job are already
                        // known and will not turn into feignallite in a later toil.
                        applied = true;
                        return;
                    }

                    __instance.workLeft /= factor;
                    applied = true;

                    if (!loggedFirstUse)
                    {
                        loggedFirstUse = true;
                        Log.Message("[Avir] Feignallite work speedup active (first application at "
                                    + factor.ToString("0.00") + "x).");
                    }
                };

                yield return captured;
            }
        }

        /// <summary>
        /// 1.0 when no feignallite is involved, otherwise the ladder rung matching its share of
        /// the placed material.
        /// </summary>
        private static float SpeedFactor(JobDriver_DoBill driver, float ceiling)
        {
            try
            {
                Job job = driver.job;
                if (job == null) return 1f;

                ResolveCountMember();

                ThingDef feignallite = Feignallite;
                int feignalliteUnits = 0;
                int totalUnits = 0;
                bool hasFeignallite = false;
                bool hasOther = false;
                bool countsUsable = true;

                List<ThingCountClass> placed = job.placedThings;
                if (placed != null && placed.Count > 0)
                {
                    for (int i = 0; i < placed.Count; i++)
                    {
                        ThingCountClass entry = placed[i];
                        if (entry == null || entry.thing == null) continue;

                        bool isFeignallite = entry.thing.def == feignallite;
                        if (isFeignallite) hasFeignallite = true; else hasOther = true;

                        int units = ReadCount(entry);
                        if (units < 0)
                        {
                            // Count member never bound. Fall back to the thing's own stack, and
                            // if that is somehow unusable, drop to the kind-only rule below.
                            units = entry.thing.stackCount;
                            if (units <= 0) countsUsable = false;
                        }

                        totalUnits += units;
                        if (isFeignallite) feignalliteUnits += units;
                    }
                }
                else if (job.targetQueueB != null && job.targetQueueB.Count > 0)
                {
                    // Window before ingredients are placed. Thing.stackCount is a plain public
                    // field, so this path needs no reflection at all.
                    for (int i = 0; i < job.targetQueueB.Count; i++)
                    {
                        Thing thing = job.targetQueueB[i].Thing;
                        if (thing == null) continue;

                        bool isFeignallite = thing.def == feignallite;
                        if (isFeignallite) hasFeignallite = true; else hasOther = true;

                        int units = thing.stackCount > 0 ? thing.stackCount : 1;
                        totalUnits += units;
                        if (isFeignallite) feignalliteUnits += units;
                    }
                }

                if (!hasFeignallite) return 1f;

                // Kind-only fallback: no usable unit counts anywhere.
                if (!countsUsable || totalUnits <= 0)
                    return hasOther ? LadderFactor(0.25f, ceiling) : ceiling;

                return LadderFactor((float)feignalliteUnits / totalUnits, ceiling);
            }
            catch (Exception ex)
            {
                if (!loggedFailure)
                {
                    loggedFailure = true;
                    Log.Warning("[Avir] Feignallite work speedup could not read job ingredients, "
                                + "falling back to normal speed. " + ex.Message);
                }
                return 1f;
            }
        }
    }
}
