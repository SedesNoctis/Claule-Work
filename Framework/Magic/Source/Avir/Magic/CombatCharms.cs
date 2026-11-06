using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>Battle charms the caster puts together by himself (the author, Fix156): not always a blast - one caster
    /// makes himself a burst, another a line, a third a hindering hold or a throw (by the pawn, so each has his own
    /// manner); the higher his stage, the better it is built. Two Barriers in the outer section always (Selective
    /// Protection: his own are spared). Dangerous ones - amplified bursts (explosions), magma - only if the player allows
    /// it. The same maker serves the strangers: a hostile caster puts his charm together and uses it on his foes.</summary>
    public static class CombatCharms
    {
        public const string Mark = "battle-charm";
        public enum Style { Burst, Line, Hinder, Throw }
        public static Style StyleOf(Pawn p) => (Style)((p.thingIDNumber & 0x7fffffff) % 4);
        private static RuneDef R(string n) => DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + n);
        private static RuneDef El(RuneKind k) => DefDatabase<RuneDef>.AllDefsListForReading.FirstOrDefault(d => d.role == RuneRole.Element && d.kind == k);

        /// <summary>The element he fights with: his own, else the one he is best in.</summary>
        public static RuneKind? ElementOf(Hediff_ArcaneCore c)
        {
            RuneDef m = MageClass.Main(c);
            if (m != null && m.kind != RuneKind.Life) return m.kind;
            var known = new[] { RuneKind.Fire, RuneKind.Water, RuneKind.Earth, RuneKind.Air }.Where(k => MagicWork.Knows(c, k)).ToList();
            return known.Count == 0 ? (RuneKind?)null : known.OrderByDescending(k => MageClass.XpIn(c, k)).First();
        }

        public static RitualSchema Make(Hediff_ArcaneCore c, bool dangerous, out string label)
        {
            label = null;
            RuneKind? ek = ElementOf(c);
            if (ek == null) return null;
            RuneKind k = ek.Value;
            int st = Mathf.Clamp(MageClass.Stage(MageClass.XpIn(c, k)), 0, 5);
            Style style = StyleOf(c.pawn);
            var deed = new RitualRing();
            void Add(string n, int times = 1) { for (int i = 0; i < times; i++) { RuneDef r = R(n); if (r != null) deed.runes.Add(r); } }
            switch (style)
            {
                case Style.Burst: Add("Release"); label = "burst"; break;
                case Style.Line: Add("Release"); Add("Beam"); label = "line"; break;
                case Style.Hinder: Add("Hold"); label = "hold"; break;
                default: Add("Lift"); label = "throw"; break;
            }
            if (st >= 1) Add("Reach");                                     // further with the first step
            if (st >= 2) { if (style == Style.Hinder || style == Style.Throw) Add("Breadth"); else if (dangerous) Add("Amplify"); else Add("Hold"); }
            if (st >= 3) { if (dangerous && style != Style.Hinder) Add("Amplify"); else Add("Breadth"); }
            if (st >= 4) { if (style == Style.Burst || style == Style.Line) Add("Lift"); else Add("Release"); }
            if (st >= 5 && dangerous) Add("Amplify");
            deed.slots = deed.runes.Count;
            var ward = new RitualRing { runes = new List<RuneDef> { R("Barrier"), R("Barrier") }.Where(x => x != null).ToList() };
            ward.slots = ward.runes.Count;
            var circle = new SpellCircle { element = El(k) };
            if (circle.element == null) return null;
            circle.sections.Add(deed);
            if (ward.runes.Count == 2) circle.sections.Add(ward);   // the outermost: Selective Protection
            return new RitualSchema { name = c.pawn.LabelShort + "'s battle charm: " + k.ToString().ToLower() + " " + label, size = RitualCircleSize.Small,
                author = Mark + st + (dangerous ? "d" : "") + (int)style, weave = SpellCircle.Bound(circle) };
        }

        public static bool IsCharm(RitualSchema s) => s?.author != null && s.author.StartsWith(Mark);

        /// <summary>Puts his charm together (or betters it as he grows) and keeps it in a free place at hand.</summary>
        public static RitualSchema Ensure(Hediff_ArcaneCore c, bool dangerous)
        {
            RitualSchema made = Make(c, dangerous, out string label);
            if (made == null) return null;
            MageBook b = c.Book;
            RitualSchema old = b.spells.FirstOrDefault(IsCharm);
            if (old == null) old = b.spells.FirstOrDefault(s => s != null && s.name == made.name);
            if (old != null && (old.author == made.author || !IsCharm(old))) { Place(b, old); return old; }   // as good as now, or the player made it his own
            if (old != null) { int i = b.spells.IndexOf(old); b.spells[i] = made; for (int r = 0; r < b.ready.Length; r++) if (b.ready[r] == old) b.ready[r] = made; }
            else b.spells.Add(made);
            Place(b, made);
            if (c.pawn.Faction == Faction.OfPlayer)
                Messages.Message(c.pawn.LabelShortCap + (old == null ? " put a battle charm together: " : " bettered his battle charm: ") + made.name.Substring(made.name.IndexOf(':') + 2) + ".", c.pawn, MessageTypeDefOf.NeutralEvent, false);
            return made;
        }

        private static void Place(MageBook b, RitualSchema s)
        {
            if (b.ready.Contains(s)) return;
            for (int i = 0; i < b.open && i < b.ready.Length; i++) if (b.ready[i] == null) { b.ready[i] = s; return; }
        }

        public static int SlotOf(MageBook b, RitualSchema s) { for (int i = 0; i < b.ready.Length; i++) if (b.ready[i] == s) return i; return -1; }
    }

    /// <summary>Casters who use their battle charms by themselves: hunting, drafted with fire at will, undrafted when a foe
    /// comes near (if allowed) - and hostile casters against their foes.</summary>
    public class MapComponent_AutoCharms : MapComponent
    {
        private readonly Dictionary<int, int> next = new Dictionary<int, int>();
        public MapComponent_AutoCharms(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (!AvirMagic.On || Find.TickManager.TicksGame % 30 != 0) return;
            int now = Find.TickManager.TicksGame;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.Where(x => x.RaceProps.Humanlike && !x.Dead && !x.Downed).ToList())
            {
                if (next.TryGetValue(p.thingIDNumber, out int at) && at > now) continue;
                Hediff_ArcaneCore c = Mages.Core(p);
                if (c == null) continue;
                next[p.thingIDNumber] = now + 60;
                try { Think(p, c, now); } catch (System.Exception e) { Log.ErrorOnce("[Avir] auto charms: " + e, 0x41C4A2); }
            }
        }

        private void Think(Pawn p, Hediff_ArcaneCore c, int now)
        {
            bool player = p.Faction == Faction.OfPlayer;
            if (player && !c.battleCharms) return;
            if (SpellCasting.Busy(p) || p.InMentalState || !p.Awake() || p.InBed()) return;
            RitualSchema s = c.Book.ready.FirstOrDefault(CombatCharms.IsCharm) ?? CombatCharms.Ensure(c, !player || c.dangerousCharms);
            if (s == null) return;
            int slot = CombatCharms.SlotOf(c.Book, s);
            if (slot < 0 || c.Book.cooldown[slot] > now) return;
            if (c.arcana < Spells.CostFor(c, s)) return;
            float range = Mathf.Min(Spells.Range(s), 30f);
            Pawn target = null;
            if (player)
            {
                if ((p.CurJobDef == JobDefOf.Hunt || p.CurJob?.def?.defName == "Avir_ArcaneHunt") && p.CurJob.targetA.Thing is Pawn prey && !prey.Dead) target = prey;                       // hunting
                else if (p.Drafted) { if (p.drafter.FireAtWill && (p.CurJob == null || p.CurJobDef == JobDefOf.Wait_Combat || p.CurJobDef == JobDefOf.Wait)) target = Nearest(p, range); }
                else if (c.fightUndrafted) target = Nearest(p, Mathf.Min(range, 14f));                                                   // a foe near, undrafted
            }
            else if (p.HostileTo(Faction.OfPlayer)) target = Nearest(p, range);
            if (target == null || target.Position.DistanceTo(p.Position) > range || !GenSight.LineOfSight(p.Position, target.Position, map, true)) return;
            SpellCasting.Order(p, s, slot, target, IntVec3.Invalid);
            next[p.thingIDNumber] = now + 120;
        }

        private Pawn Nearest(Pawn p, float range) =>
            map.mapPawns.AllPawnsSpawned.Where(q => q != p && !q.Dead && !q.Downed && q.HostileTo(p) && q.Position.InHorDistOf(p.Position, range) && GenSight.LineOfSight(p.Position, q.Position, map, true))
                .OrderBy(q => q.Position.DistanceToSquared(p.Position)).FirstOrDefault();
    }
}

namespace Avir.Magic
{
    public static class CombatCharmsDev
    {
        [LudeonTK.DebugAction("Avir magic", "Battle charm: put together now (dangerous allowed)", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Make(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c != null) CombatCharms.Ensure(c, true); }

        [LudeonTK.DebugAction("Avir magic", "Battle charm: put together now (tame)", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void MakeTame(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c != null) CombatCharms.Ensure(c, false); }
    }
}
