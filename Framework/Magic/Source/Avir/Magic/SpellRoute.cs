using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Magic
{
    /// <summary>The beam on a route (Fix234, the author). Beam + Anchor + Thread in one circle: a streaming beam whose
    /// point of strike walks along a route the player marks - it sweeps the ground between the marks at its own pace, and
    /// strikes once a second wherever it is. Every Step written in that circle (they do not carry the caster here) turns
    /// it into a chain: the beam reaches a mark and goes on from that mark to the next, not from the caster - up to nine
    /// marks, each reaching 10% less far than the one before (the ninth - a tenth of the spell's reach). Without a Step -
    /// two marks, swept from the caster. The caster stands while it lasts; "Break off the stream" lets it go.</summary>
    public static class SpellRoute
    {
        public const int MaxMarks = 9;
        /// <summary>Done on the next frame (the aiming cannot be begun again from inside its own end).</summary>
        public static System.Action Deferred;

        private static IEnumerable<List<RuneDef>> Circles(RitualSchema s)
        {
            if (s == null) yield break;
            if (s.weave != null) { foreach (SpellCircle c in s.weave.Working) yield return c.OwnRunes.ToList(); yield break; }
            yield return s.rings.SelectMany(g => g.runes).Where(x => x != null).ToList();
        }

        /// <summary>Beam, Anchor, Thread written one after another - and the Thread not turned to mending (no Restoration
        /// right after it: Beam + Anchor + Thread + Restore stays the stream of healing).</summary>
        public static bool IsRoute(List<RuneDef> runes) =>
            // Fix237: not by the order the runes are stored in (a circle of the editor keeps them by sections and places -
            // the strict "one after another" missed the author's spell, and its Steps flew the caster about)
            runes.Any(x => x.kind == RuneKind.Beam) && runes.Any(x => x.kind == RuneKind.Anchor) && runes.Any(x => x.kind == RuneKind.Thread)
            && !runes.Any(x => x.kind == RuneKind.Restore);   // Beam + Anchor + Thread + Restore stays the stream of healing

        /// <summary>Where each caster's route ended (its last mark) - the next circle of the spell strikes there.</summary>
        public static readonly Dictionary<int, IntVec3> EndOf = new Dictionary<int, IntVec3>();

        /// <summary>How many marks the spell asks for (0 - not a route), and whether it chains.</summary>
        public static int Marks(RitualSchema s, out bool chain)
        {
            chain = false;
            foreach (List<RuneDef> rs in Circles(s))
            {
                if (!IsRoute(rs)) continue;
                int steps = rs.Count(x => x.kind == RuneKind.Step);
                chain = steps > 0;
                return chain ? Mathf.Min(MaxMarks, 1 + steps) : 2;
            }
            return 0;
        }

        /// <summary>How far mark i (0 - the first, the spell's target) may be from the one before it.</summary>
        public static float ReachOf(float range, int i, bool chain) => chain ? range * Mathf.Max(0.1f, 1f - 0.1f * i) : range;

        /// <summary>A route part's Steps are its marks, not flights: they are taken out before the circle is read.</summary>
        public static void StripSteps(RitualSchema s)
        {
            if (s?.rings == null || !IsRoute(s.rings.SelectMany(g => g.runes).Where(x => x != null).ToList())) return;
            foreach (RitualRing g in s.rings) g.runes.RemoveAll(x => x != null && x.kind == RuneKind.Step);
        }

        /// <summary>How hard the stream is to hold (Fix235, the author: every element more makes it harder): every element
        /// of the spell past the first shortens each link of the route by 15% (to 40% at most).</summary>
        public static float HoldOf(RitualSchema s)
        {
            int n = s?.weave != null ? Circles(s).Count(rs => rs.Any(x => x.role == RuneRole.Element))
                : s?.rings.SelectMany(g => g.runes).Count(x => x != null && x.role == RuneRole.Element) ?? 1;
            return Mathf.Max(0.4f, 1f - 0.15f * Mathf.Max(0, n - 1));
        }

        /// <summary>Where a mark is now: a mark set on someone goes with them (Fix235); on the ground it stays.</summary>
        public static IntVec3 Pos(LocalTargetInfo t) => t.HasThing && t.Thing.Spawned ? t.Thing.Position : t.Cell;

        // ---- the marks named while aiming, taken when the beam begins
        public class Pending { public List<LocalTargetInfo> marks; public List<float> reach; public bool chain; public float hold = 1f; }
        private static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        public static void Set(Pawn mage, Pending p) { if (mage == null) return; if (p == null || p.marks.Count < 2) pending.Remove(mage.thingIDNumber); else pending[mage.thingIDNumber] = p; }
        public static Pending Take(Pawn mage) { if (mage == null || !pending.TryGetValue(mage.thingIDNumber, out Pending p)) return null; pending.Remove(mage.thingIDNumber); return p; }

        /// <summary>Aiming the marks after the first, one by one: a left click sets the next (on the ground, or on someone -
        /// then the mark goes with them), a right click takes the last back. The whole route is drawn as it is made, and each
        /// link's reach is shortened by how hard the spell is to hold.</summary>
        public static void Aim(Pawn mage, RitualSchema spell, LocalTargetInfo first, System.Action done)
        {
            int need = Marks(spell, out bool chain);
            float range = Spells.Range(spell), hold = HoldOf(spell);
            var p = new Pending { marks = new List<LocalTargetInfo> { first }, reach = new List<float> { range }, chain = chain, hold = hold };
            void Ask()
            {
                if (p.marks.Count >= need) { Set(mage, p); done(); return; }
                int i = p.marks.Count; IntVec3 from = chain ? Pos(p.marks[i - 1]) : mage.Position;
                float reach = ReachOf(range, i, chain) * hold;
                Messages.Message("Route of the beam: mark " + (i + 1) + " of " + need + (chain ? " (it goes on from the last mark)" : "") + ". Right click - take the last mark back.", MessageTypeDefOf.SilentInput, false);
                var parms = new TargetingParameters { canTargetLocations = true, canTargetPawns = true, canTargetSelf = false, canTargetBuildings = false };
                Find.Targeter.BeginTargeting(parms, t =>
                {
                    if (!mage.Spawned) return;
                    if (t.Cell.DistanceTo(from) > reach) { Messages.Message("Too far from the mark before.", MessageTypeDefOf.RejectInput, false); Deferred = Ask; return; }
                    p.marks.Add(t); p.reach.Add(ReachOf(range, i, chain)); Deferred = Ask;
                }, null, t => mage.Spawned && t.IsValid && t.Cell.InBounds(mage.Map) && t.Cell.DistanceTo(from) <= reach, mage,
                () =>
                {
                    // the game ends the aiming before it acts on a click; only a right click is "one back" (Escape - stop)
                    Event e = Event.current;
                    if (e != null && e.type == EventType.MouseDown && e.button == 1 && p.marks.Count > 1)
                    { p.marks.RemoveAt(p.marks.Count - 1); p.reach.RemoveAt(p.reach.Count - 1); Deferred = Ask; }
                },
                null, true, null,
                t => Draw(mage, spell, p.marks.Select(Pos).ToList(), chain, from, reach));
            }
            Ask();
        }

        /// <summary>The route as made so far, and the next leg under the cursor (Fix236: the author saw no beam from a mark):
        /// every leg is drawn as the beam will strike it - its cells edged in the spell's colour, as any beam is shown while
        /// aimed - with a bright line along it; the leg to the cursor yellow while in reach, red past it.</summary>
        public static void Draw(Pawn mage, RitualSchema spell, List<IntVec3> marks, bool chain, IntVec3 from, float reach)
        {
            Map map = mage.Map;
            if (reach < GenRadial.MaxRadialPatternRadius) GenDraw.DrawRadiusRing(from, reach);
            int breadth = Circles(spell).Where(IsRoute).Select(rs => rs.Count(x => x.kind == RuneKind.Breadth)).FirstOrDefault();
            Color col = Spells.ColorOf(spell);
            var cells = new HashSet<IntVec3>();
            void Leg(IntVec3 a, IntVec3 b, Color c)
            {
                cells.UnionWith(FireMagic.Beam(a, b, map, breadth).Where(x => x.InBounds(map)));
                GenDraw.DrawLineBetween(a.ToVector3Shifted(), b.ToVector3Shifted(), SolidColorMaterials.SimpleSolidColorMaterial(new Color(c.r, c.g, c.b, 0.85f)), 0.12f);
            }
            Leg(mage.Position, marks[0], col);
            for (int i = 1; i < marks.Count; i++) Leg(chain ? marks[i - 1] : mage.Position, marks[i], col);
            if (cells.Count > 0) GenDraw.DrawFieldEdges(cells.ToList(), new Color(col.r, col.g, col.b, 1f));
            // where the strike walks between the marks
            for (int i = 1; i < marks.Count; i++) GenDraw.DrawLineBetween(marks[i - 1].ToVector3Shifted(), marks[i].ToVector3Shifted(), SimpleColor.Cyan);
            IntVec3 m = Verse.UI.MouseCell();
            if (m.InBounds(map))
            {
                bool ok = m.DistanceTo(from) <= reach;
                IntVec3 src = chain ? marks[marks.Count - 1] : mage.Position;
                var next = FireMagic.Beam(src, m, map, breadth).Where(x => x.InBounds(map)).ToList();
                if (next.Count > 0) GenDraw.DrawFieldEdges(next, ok ? Color.yellow : Color.red);
                GenDraw.DrawLineBetween(src.ToVector3Shifted(), m.ToVector3Shifted(), SolidColorMaterials.SimpleSolidColorMaterial(ok ? new Color(1f, 0.9f, 0.2f, 0.9f) : new Color(1f, 0.2f, 0.2f, 0.9f)), 0.12f);
                if (!chain) GenDraw.DrawLineBetween(marks[marks.Count - 1].ToVector3Shifted(), m.ToVector3Shifted(), SimpleColor.Cyan);
            }
            foreach (IntVec3 k in marks) GenDraw.DrawTargetHighlight(new LocalTargetInfo(k));
        }

        /// <summary>Length of a route.</summary>
        public static float Length(List<IntVec3> marks) { float l = 0f; for (int i = 1; i < marks.Count; i++) l += marks[i].DistanceTo(marks[i - 1]); return l; }

        /// <summary>Where on the route a beam that has gone `along` cells is: the point, and the mark it last passed.</summary>
        public static Vector3 PointAt(List<IntVec3> marks, float along, out int leg)
        {
            leg = 0;
            for (int i = 1; i < marks.Count; i++)
            {
                float d = marks[i].DistanceTo(marks[i - 1]);
                if (along <= d || i == marks.Count - 1) { leg = i - 1; return Vector3.Lerp(marks[i - 1].ToVector3Shifted(), marks[i].ToVector3Shifted(), d <= 0f ? 1f : Mathf.Clamp01(along / d)); }
                along -= d;
            }
            return marks[marks.Count - 1].ToVector3Shifted();
        }
    }

    /// <summary>The caster of a stream stands (Fix234, the author): an order to go elsewhere is refused while the stream
    /// holds - "Break off the stream" first.</summary>
    [HarmonyLib.HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    public static class Patch_StreamStands
    {
        public static bool Prefix(Pawn_JobTracker __instance, Job job, ref bool __result)
        {
            Pawn p = HarmonyLib.Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (p?.MapHeld == null || job == null || job.def == JobDefOf.Wait_MaintainPosture) return true;
            if (p.MapHeld.GetComponent<MapComponent_Auras>()?.all.Any(a => a.beam && a.caster == p) != true) return true;
            Messages.Message(p.LabelShortCap + " holds a stream and cannot move - break off the stream first.", p, MessageTypeDefOf.RejectInput, false);
            __result = false;
            return false;
        }
    }
}
