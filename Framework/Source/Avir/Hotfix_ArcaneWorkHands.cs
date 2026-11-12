using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Magic
{
    /// <summary>
    /// HOTFIX - arcane works vs. hands. REMOVE = delete this one file and rebuild Avir.dll. Nothing else references it.
    ///
    /// Bug: a caster with poor hands (skill under ArcaneWork.HandSkill) is meant to do a work by magic only ("M"), or not
    /// at all. But the original ArcaneWork.Tick only turns "M" on while MagicNow(group) is true, i.e. while the Arcana is
    /// ABOVE the band's lower mark. A caster whose core is empty (Arcana 0) never gets "M" - so the ordinary work stays on
    /// with its skill-0 hands: a harpy of Earth with 0 Arcana mines the wall by claw instead of idling/meditating.
    /// Same hole after greatness lifts (the held work comes back as plain hand work, never "M").
    ///
    /// Second hole: the work-giver list is cached, and the cache is only rebuilt when the h/m state of an "M" work flips.
    /// For poor hands that state never flips, so when the Arcana comes back above the mark the magic givers stay out of
    /// the cached list (they were skipped while it was low) and the caster never returns to magic.
    ///
    /// Fix (a postfix on ArcaneWork.Tick, the original is untouched):
    ///  1. "M" is taken up by poor hands whenever the group is OPEN to the caster (he knows its elements, stage allows it,
    ///     group priority above 0) - whatever the Arcana is right now. With no Arcana he simply does not do the work.
    ///     Obeys the same switches as before: the "take up M by themselves" setting, a declined M, a group set to 0.
    ///  2. The work lists are made again whenever the magic state of an "M" group flips (Arcana crossing its lower mark).
    /// </summary>
    [HarmonyPatch(typeof(ArcaneWork), nameof(ArcaneWork.Tick))]
    public static class Hotfix_ArcaneWorkHands
    {
        private static readonly Dictionary<int, string> seen = new Dictionary<int, string>();
        private static bool warned;

        public static void Postfix(Hediff_ArcaneCore c)
        {
            try
            {
                Pawn p = c?.pawn;
                if (p?.workSettings == null || !p.workSettings.EverWork || !AvirMagic.On) return;

                // 1. poor hands take up "M" by themselves where the work is open to them - Arcana or not
                if (AvirMod.Settings == null || AvirMod.Settings.magicAutoM)
                {
                    foreach (ArcaneWork.Group g in ArcaneWork.Groups)
                    {
                        if (g.vanilla == null || g.vanilla.Length == 0 || ArcaneWork.Prio(c, g) <= 0) continue;
                        if (!g.givers.Any(n => ArcaneWork.CanDo(c, n))) continue;
                        foreach (string wn in g.vanilla)
                        {
                            WorkTypeDef w = DefDatabase<WorkTypeDef>.GetNamedSilentFail(wn);
                            if (w == null || c.magicOnly.Contains(wn) || c.magicDeclined.Contains(wn)) continue;
                            if (p.WorkTypeIsDisabled(w) || p.workSettings.GetPriority(w) == 0) continue;
                            if (ArcaneWork.Hands(p, w) < ArcaneWork.HandSkill) ArcaneWork.SetMagicOnly(c, w, true);
                        }
                    }
                }

                // 2. the cached work lists follow the magic state of the "M" groups (Arcana above / below its lower mark)
                string sig = string.Join(",", ArcaneWork.Groups
                    .Where(g => g.vanilla != null && g.vanilla.Any(n => c.magicOnly.Contains(n)))
                    .Select(g => g.key + (ArcaneWork.MagicNow(c, g) ? "1" : "0")));
                if (!seen.TryGetValue(p.thingIDNumber, out string old) || old != sig)
                {
                    seen[p.thingIDNumber] = sig;
                    ArcaneWork.Dirty(p);
                }
            }
            catch (Exception e)
            {
                if (!warned) { warned = true; Log.Warning("[Avir hotfix] arcane works / hands: " + e.Message); }
            }
        }
    }
}
