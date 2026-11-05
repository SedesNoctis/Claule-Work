using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Restoration by element (Магия-правила.md): each element mends in its own way and leaves a small gift of
    /// its nature. Fire seals the bleeding and kindles the body against sickness; water washes and tends and cools the
    /// pain; earth tends, grows stone skin and makes whole the things in its reach - tools, walls, furniture; air gives
    /// back breath and wakefulness and opens the chest. Blood is water's with Life (not yet).</summary>
    public static class SpellRestore
    {
        public static HediffDef Kindled => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_RestoreKindled");
        public static HediffDef Cooled => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_RestoreCooled");
        public static HediffDef SecondWind => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_RestoreSecondWind");

        /// <summary>The small gift of the element: some hours, longer with power.</summary>
        public static void Gift(Pawn p, HediffDef def, float power)
        {
            if (p == null || p.Dead || def == null) return;
            MagicFx.Give(p, def, Mathf.RoundToInt(GenDate.TicksPerHour * Mathf.Clamp(2f * power, 1f, 8f)));
        }

        /// <summary>Earth makes whole what lies and stands in its reach: a share of each thing's strength comes back,
        /// the most worn first; the stone does not mend the living (that is the tending) nor the wild ground.</summary>
        public static int Repair(Map map, IEnumerable<IntVec3> cells, float power)
        {
            float share = Mathf.Clamp(0.15f + 0.1f * power, 0.1f, 0.6f);
            int n = 0;
            foreach (Thing t in cells.Where(c => c.InBounds(map)).SelectMany(c => c.GetThingList(map)).Distinct().ToList())
            {
                if (t is Pawn || t is Plant || t is Filth || !t.def.useHitPoints || t.HitPoints >= t.MaxHitPoints) continue;
                if (t is Building b && b.Faction == null) continue;
                t.HitPoints = Mathf.Min(t.MaxHitPoints, t.HitPoints + Mathf.Max(1, Mathf.RoundToInt(t.MaxHitPoints * share)));
                if (t.Spawned) { FleckMaker.ThrowDustPuffThick(t.DrawPos, map, 0.8f, MagicFx.EarthDust); }
                n++;
            }
            return n;
        }
    }
}
