using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// NO PAWN THAT IS NOT AN AVIR IS EVER NAMED LIKE ONE. ABSOLUTELY, AT THE LAST POSSIBLE MOMENT.
    ///
    /// WHY THIS EXISTS ON TOP OF Patch_AvirCulture. That one corrects the culture roll when an ideo
    /// is generated, which is the right place to stop the problem being created - and it is useless
    /// against every ideo that already exists. An ancient danger sealed in a save from before the
    /// fix, a faction whose ideo was made three seasons ago, anything carried over: all of them
    /// still hold Avian, and every pawn they produce is still named from it.
    ///
    /// Maximus asked for the restriction to be ABSOLUTE, and a fix that only applies to things
    /// generated after it was installed is not that.
    ///
    /// SO THIS SITS AT THE NAMING ITSELF. PawnBioAndNameGenerator.GenerateFullPawnName is the one
    /// funnel every humanlike name passes through, and it is handed its naming material as
    /// arguments - the culture, and the rule packs. Whatever decided those, and however long ago,
    /// they arrive here and can be replaced here. Nothing upstream has to be correct.
    ///
    /// THREE SOURCES, ALL OF THEM CHECKED. The culture supplies pawnNameMaker; the pawnkind and the
    /// xenotype can each override it. Blocking only the culture would leave a pawnkind pointing at
    /// an Avir rule pack untouched, and the whole point of this file is that there is no gap.
    ///
    /// AVIR ARE NOT AFFECTED. The test is genes or Avir pawnkind, so an actual Avir names itself
    /// from its own culture exactly as before.
    /// </summary>
    public static class Patch_AvirNameLock
    {
        private static bool loggedFirst;

        /// <summary>
        /// Pawn_StoryTracker.pawn is PRIVATE - confirmed from the field's access flags, after I
        /// read the name out of the metadata and used it directly without checking. A field being
        /// listed is not a field being reachable, and that cost a compile.
        ///
        /// Bound once. If it ever disappears the lock reports it and stands down rather than
        /// throwing on every pawn generated.
        /// </summary>
        private static AccessTools.FieldRef<Pawn_StoryTracker, Pawn> storyPawn;
        private static bool bound;

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(PawnBioAndNameGenerator),
                    "GenerateFullPawnName");

                if (target == null)
                {
                    Log.Warning("[Avir namelock] PawnBioAndNameGenerator.GenerateFullPawnName not "
                        + "found. Non-Avir pawns can still be named from the Avian culture if their "
                        + "ideo already holds it.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(
                    typeof(Patch_AvirNameLock), nameof(FullName_Prefix)));

                storyPawn = AccessTools.FieldRefAccess<Pawn_StoryTracker, Pawn>("pawn");
                bound = storyPawn != null;

                if (!bound)
                {
                    Log.Warning("[Avir namelock] Pawn_StoryTracker.pawn not found; the name lock "
                        + "cannot tell whose name it is looking at and will stand down.");
                    return;
                }

                Log.Message("[Avir namelock] bound to PawnBioAndNameGenerator.GenerateFullPawnName.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir namelock] failed to apply: " + e);
            }
        }

        /// <summary>
        /// Parameter names taken from the assembly's Param table, not typed from habit: story,
        /// xenotype, pawnKindNameMaker, nameGenner, primaryCulture. Harmony binds these by name and
        /// a single wrong letter silently disables the whole patch.
        /// </summary>
        public static void FullName_Prefix(Pawn_StoryTracker story,
                                           XenotypeDef xenotype,
                                           ref RulePackDef pawnKindNameMaker,
                                           ref RulePackDef nameGenner,
                                           ref CultureDef primaryCulture)
        {
            try
            {
                // No pawn to judge. Leave everything alone rather than guess.
                if (!bound || story == null) return;

                Pawn pawn = storyPawn(story);
                if (pawn == null) return;

                // One of ours. Its own names are exactly what it should have.
                if (AvirFaction.ShouldUseAvirContent(pawn)) return;

                bool changed = false;

                if (IsAvirCulture(primaryCulture))
                {
                    primaryCulture = FallbackCulture(primaryCulture);
                    changed = true;
                }

                // A pawnkind or xenotype can carry its own name maker that overrides the culture's,
                // so blocking the culture alone would leave a hole exactly the size of one modded
                // pawnkind.
                if (IsAvirRulePack(pawnKindNameMaker))
                {
                    pawnKindNameMaker = null;
                    changed = true;
                }

                if (IsAvirRulePack(nameGenner))
                {
                    nameGenner = null;
                    changed = true;
                }

                if (changed && !loggedFirst)
                {
                    loggedFirst = true;
                    Log.Message("[Avir namelock] " + (pawn.kindDef?.defName ?? "a pawn")
                        + " was about to be named from Avir material despite not being an Avir; "
                        + "substituted. Avir names are for Avir.");
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir namelock] prefix failed: " + e, 0x4A5276);
            }
        }

        private static bool IsAvirCulture(CultureDef def)
        {
            return def != null && def.defName == "Avian";
        }

        /// <summary>
        /// A rule pack counts as ours if its defName carries the mod's prefix. That covers
        /// Avir_NamerPersonMale, Avir_NamerPersonFemale and anything added later without this file
        /// needing to be revisited - which matters, because a list of names here would rot the
        /// first time a new namer was written.
        /// </summary>
        private static bool IsAvirRulePack(RulePackDef def)
        {
            if (def?.defName == null) return false;
            return def.defName.StartsWith("Avir_") || def.defName.EndsWith("Avian");
        }

        private static CultureDef cachedFallback;

        /// <summary>
        /// Any culture that is not ours. Cached, because this runs during every pawn generation and
        /// the answer never changes within a session.
        ///
        /// If a world somehow has no other culture at all, the original is handed back rather than
        /// null - a strange name is better than a crash in the middle of generating a pawn.
        /// </summary>
        private static CultureDef FallbackCulture(CultureDef exclude)
        {
            if (cachedFallback != null) return cachedFallback;

            List<CultureDef> all = DefDatabase<CultureDef>.AllDefsListForReading;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] == null || IsAvirCulture(all[i])) continue;
                    cachedFallback = all[i];
                    break;
                }
            }

            return cachedFallback ?? exclude;
        }
    }
}
