using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE AVIAN CULTURE BELONGS TO THE AVIR ORDERS AND NOBODY ELSE.
    ///
    /// THE BUG. Ancient danger soldiers - human, vanilla, nothing to do with this mod - were
    /// turning up with names like Kae'Zorraal and Nac'Nythrassa. No Harmony patch of ours was
    /// involved and the name gate was working correctly; the culture was.
    ///
    /// A FactionDef that declares no allowedCultures draws from EVERY CultureDef in the game, and
    /// Core's Ancients and AncientsHostile declare none - checked in their own defs, along with the
    /// handful of vanilla factions that do restrict themselves. Avian is a CultureDef like any
    /// other, so the ancient danger could roll it, and every pawn generated for that faction then
    /// took its names from Avir_NamerPersonMale / Female. Human soldiers, Avir names, exactly as
    /// the game was told.
    ///
    /// WHY THIS IS A PATCH AND NOT A DEF EDIT. The lever vanilla provides is faction-side, and
    /// there is no "this culture is not for general use" field to set on our end. Adding
    /// allowedCultures to Ancients by PatchOperation would work and would also silently freeze
    /// their culture pool against every other mod that adds one. The rule belongs to Avian, so it
    /// lives here.
    ///
    /// WHAT IT DOES NOT DO. It never touches an ideo that legitimately chose Avian - an Avir order
    /// keeps it - and it does not stop a player picking Avian by hand for their own colony. Only
    /// the random roll for a faction that is not ours is corrected.
    /// </summary>
    public static class Patch_AvirCulture
    {
        private static bool loggedOnce;

        public static void Apply(Harmony harmony)
        {
            try
            {
                System.Reflection.MethodInfo target = AccessTools.Method(typeof(IdeoFoundation), "RandomizeCulture");
                if (target == null)
                {
                    Log.Warning("[Avir culture] IdeoFoundation.RandomizeCulture not found; the "
                        + "Avian culture can still be rolled by unrelated factions, which shows up "
                        + "as human pawns with Avir names.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(Patch_AvirCulture), nameof(RandomizeCulture_Postfix)));

                Log.Message("[Avir culture] bound to IdeoFoundation.RandomizeCulture.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir culture] failed to apply: " + e);
            }
        }

        /// <summary>
        /// Parameter named parms, matching the assembly - taken from the Param table rather than
        /// typed from habit, after a single misspelled parameter name silently disabled the
        /// map-edge patch for a whole session. Harmony binds these by name.
        /// </summary>
        public static void RandomizeCulture_Postfix(IdeoFoundation __instance, IdeoGenerationParms parms)
        {
            try
            {
                Ideo ideo = __instance?.ideo;
                if (ideo?.culture == null) return;

                if (!IsAvianCulture(ideo.culture)) return;

                // An Avir order picking the Avir culture is the entire point of it existing.
                if (parms.forFaction != null && AvirFaction.IsAvirFactionDef(parms.forFaction)) return;

                CultureDef replacement = PickOther(parms.forFaction, ideo.culture);
                if (replacement == null) return;

                CultureDef was = ideo.culture;
                ideo.culture = replacement;

                if (!loggedOnce)
                {
                    loggedOnce = true;
                    Log.Message("[Avir culture] " + (parms.forFaction?.defName ?? "a factionless ideo")
                        + " rolled " + was.defName + ", which is not theirs to have; replaced with "
                        + replacement.defName + ". Without this its pawns would carry Avir names.");
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir culture] postfix failed: " + e, 0x4A5273);
            }
        }

        private static bool IsAvianCulture(CultureDef def)
        {
            return def != null && def.defName == "Avian";
        }

        /// <summary>
        /// Any culture this faction could legitimately have had instead.
        ///
        /// Honours allowedCultures when the faction declares one, so a restricted faction that
        /// somehow reached here is put back inside its own list rather than handed something
        /// arbitrary. When it declares none - the Ancients case - anything but Avian will do.
        /// </summary>
        private static CultureDef PickOther(FactionDef forFaction, CultureDef exclude)
        {
            List<CultureDef> all = DefDatabase<CultureDef>.AllDefsListForReading;
            if (all == null) return null;

            List<CultureDef> allowed = forFaction?.allowedCultures;
            CultureDef best = null;
            int seen = 0;

            for (int i = 0; i < all.Count; i++)
            {
                CultureDef c = all[i];
                if (c == null || c == exclude || IsAvianCulture(c)) continue;
                if (allowed != null && allowed.Count > 0 && !allowed.Contains(c)) continue;

                // Reservoir sample, so it is not always whichever culture the database lists first.
                seen++;
                if (Rand.Range(0, seen) == 0) best = c;
            }

            return best;
        }
    }
}
