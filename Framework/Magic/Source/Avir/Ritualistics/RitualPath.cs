using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>
    /// The mage's path: infusion beside an element (the preset leaves the element's place empty for the player to write).
    /// It pours into the mage chosen until the path is theirs - the element - and of its spells as many as the ritual
    /// earned: all of them at its best. Small and great elements on a medium circle; darkness - the distortion of
    /// paths - only on a large one.
    /// </summary>
    public partial class CompRitualCircle
    {
        public static bool IsPath(RitualReading r) => r.Has(RuneKind.Infuse) && r.elements.Count > 0;

        public static string PathCircle(RitualReading r, RitualCircleSize size)
        {
            if (r.elements.Distinct().Count() > 1) return "one element only - a path is one";
            if (r.elements[0].kind == RuneKind.Darkness && size != RitualCircleSize.Large) return "the path of darkness - distortion - needs a large circle";
            if (size == RitualCircleSize.Small) return "a path needs a medium circle";
            return null;
        }

        private void PathMenu(Pawn p, IntVec3 at)
        {
            RuneDef el = Reading.elements[0];
            var opts = new List<FloatMenuOption>();
            var who = new List<Pawn> { p };
            who.AddRange(parent.Map.mapPawns.FreeColonistsSpawned.Where(q => q != p && Inside(q.Position)));
            foreach (Pawn q in who)
            {
                Pawn qq = q;
                string why = Avir.Magic.MagePath.CannotTake(q);
                string label = "Give " + q.LabelShort + " the path of " + el.label + (q == p ? " (the performer)" : "");
                opts.Add(new FloatMenuOption(why == null ? label : label + " - " + why, why != null ? null : (System.Action)(() => Order(p, at, null, qq, 0))));
            }
            opts.Add(new FloatMenuOption("(The mage, if not the performer, must stand in the circle)", null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private bool DoPath(RitualReading r, int delta)
        {
            Pawn who = AwakenTarget;
            RuneDef el = r.elements[0];
            if (who == null || who.Dead || Avir.Magic.MagePath.CannotTake(who) != null) { End("the mage is gone, or already has a path."); return true; }
            if (!Pours(who, delta, Avir.Magic.MagePath.Need, el.color)) return false;
            float share = Mathf.Clamp01(drawQuality * r.Efficiency * (1f + 0.3f * r.amplify) * (Size == RitualCircleSize.Large ? 1.25f : 1f));
            int n = Avir.Magic.MagePath.Grant(who, el, share);
            ResetAwakening();
            FleckMaker.ThrowLightningGlow(who.DrawPos, parent.Map, 2f);
            Messages.Message(who.LabelShortCap + " walks the path of " + el.label + " now. "
                             + (n > 0 ? n + " of its spells are written into " + who.LabelShort + "'s book." : "Its spells are not made yet."), who, MessageTypeDefOf.PositiveEvent, false);
            return true;
        }
    }
}
