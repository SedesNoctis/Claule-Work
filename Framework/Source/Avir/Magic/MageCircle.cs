using System;
using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Magic
{
    // =====================================================================================
    // THE CIRCLE OF MAGES (the author, Fix268): sanctuaries joined by the silver stream wire are one circle. A great work
    // that only a group can do is given to all of them at once: a mage goes to each seat - the altar and the ritual circles
    // on the same wire - they wait for one another, and channel together, all at the same time. A mage who drops out
    // (hurt, called away, out of Arcana) weakens the circle; the work ends weaker for it, and fails only when no one is
    // left. The same circle is meant for the great rituals and, later, for the mass casting of spells.
    // =====================================================================================

    /// <summary>A great work for a circle: how many it needs, what it takes of each, how long, and what it does.</summary>
    public class CircleWork
    {
        public string label;
        public int need, ticks;
        public float cost, xp;
        public RuneKind[] kinds;
        public Color tint;
        public Func<Pawn, bool> able;
        /// <summary>The end: strength - those standing at the end against those who began (0..1].</summary>
        public Action<Thing, float, List<Pawn>> done;
    }

    public class MageCircleGathering : IExposable
    {
        public Thing head;                   // where it was called from (the weather altar)
        public string work;                  // which great work (its key)
        public List<Pawn> members = new List<Pawn>();
        public List<IntVec3> seats = new List<IntVec3>();
        public bool channelling;
        public int began, progress, startCount;

        public void ExposeData()
        {
            Scribe_References.Look(ref head, "head");
            Scribe_Values.Look(ref work, "work");
            Scribe_Collections.Look(ref members, "members", LookMode.Reference);
            Scribe_Collections.Look(ref seats, "seats", LookMode.Value);
            Scribe_Values.Look(ref channelling, "channelling");
            Scribe_Values.Look(ref began, "began");
            Scribe_Values.Look(ref progress, "progress");
            Scribe_Values.Look(ref startCount, "startCount");
            if (members == null) members = new List<Pawn>();
            if (seats == null) seats = new List<IntVec3>();
        }

        public bool Standing(Pawn p) => p != null && p.Spawned && !p.Dead && !p.Downed && p.CurJob?.def == MageCircle.StandJob && p.CurJob.targetA.Thing == head && members.IndexOf(p) is int i && i >= 0 && i < seats.Count && p.Position == seats[i];
        public int StandingCount => members.Count(Standing);
    }

    public class MapComponent_MageCircle : MapComponent
    {
        public List<MageCircleGathering> all = new List<MageCircleGathering>();
        public MapComponent_MageCircle(Map map) : base(map) { }
        public static MapComponent_MageCircle Of(Map m) => m?.GetComponent<MapComponent_MageCircle>();
        public MageCircleGathering At(Thing head) => all.FirstOrDefault(g => g.head == head);

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref all, "circles", LookMode.Deep);
            if (all == null) all = new List<MageCircleGathering>();
        }

        public override void MapComponentTick()
        {
            if (all.Count == 0) return;
            foreach (MageCircleGathering g in all.ToList()) Step(g);
        }

        private void Step(MageCircleGathering g)
        {
            CircleWork w = MageCircle.Work(g.work);
            if (w == null || g.head == null || !g.head.Spawned) { End(g, null); return; }
            // those who left their place for good leave the circle (a hurt or a called-away mage)
            int now = Find.TickManager.TicksGame;
            if (!g.channelling)
            {
                if (g.members.All(g.Standing))
                {
                    g.channelling = true; g.startCount = g.members.Count; g.progress = 0;
                    Messages.Message("The circle of " + g.members.Count + " stands complete: " + w.label + " begins.", g.head, MessageTypeDefOf.NeutralEvent, false);
                    return;
                }
                // still coming: those whose job ended are gone; two hours at most to gather
                g.members.RemoveAll(p => p == null || p.Dead || p.CurJob?.def != MageCircle.StandJob || p.CurJob.targetA.Thing != g.head);
                if (g.members.Count < w.need || now - g.began > 5000)
                {
                    Messages.Message("The circle did not gather: " + w.label + " needs " + w.need + " standing together.", g.head, MessageTypeDefOf.RejectInput, false);
                    End(g, null);
                }
                return;
            }
            // channelling: the circle weakens as mages drop out; it ends only when no one is left
            foreach (Pawn p in g.members.Where(p => !g.Standing(p)).ToList())
            {
                Hediff_ArcaneCore c = Mages.Core(p);
                if (c != null) c.Add(-w.cost * 0.5f);   // half its share is lost
                g.seats.RemoveAt(g.members.IndexOf(p)); g.members.Remove(p);
                if (p?.Spawned == true) Messages.Message(p.LabelShort + " has fallen out of the circle - it weakens.", p, MessageTypeDefOf.NegativeEvent, false);
            }
            if (g.members.Count == 0) { Messages.Message("The circle has broken: " + w.label + " is lost.", g.head, MessageTypeDefOf.NegativeEvent, false); End(g, null); return; }
            g.progress++;
            if (g.progress < w.ticks) return;
            // the circle weakens as it thins: those standing at the end against those who began
            float strength = Mathf.Clamp((float)g.members.Count / Mathf.Max(1, g.startCount), 0.1f, 1f);
            foreach (Pawn p in g.members) { Hediff_ArcaneCore c = Mages.Core(p); if (c != null) Arcane.Done(c, w.cost, w.xp, w.kinds); }
            w.done?.Invoke(g.head, strength, g.members);
            End(g, g.members);
        }

        public void End(MageCircleGathering g, List<Pawn> keep)
        {
            all.Remove(g);
            foreach (Pawn p in g.members.ToList())
                if (p?.CurJob?.def == MageCircle.StandJob && p.CurJob.targetA.Thing == g.head) p.jobs.EndCurrentJob(JobCondition.Succeeded);
        }

        public override void MapComponentUpdate()
        {
            if (all.Count == 0 || Find.CurrentMap != map) return;
            foreach (MageCircleGathering g in all)
            {
                CircleWork w = MageCircle.Work(g.work);
                if (w == null) continue;
                RitualSchema charm = WorkSpells.Charm("Avir_WeatherAltar", w.kinds);
                float a = g.channelling ? 0.4f + 0.6f * g.progress / Mathf.Max(1f, w.ticks) : 0.25f;
                // each mage's circle under them, and the silver between the seats lit while they channel
                for (int i = 0; i < g.members.Count; i++)
                {
                    Pawn p = g.members[i];
                    if (p?.Spawned == true && g.Standing(p)) SpellSigil.Draw(p.DrawPos, charm, a, 1.4f);
                    if (g.channelling && i + 1 < g.members.Count && g.members[i + 1]?.Spawned == true && p?.Spawned == true)
                        GenDraw.DrawLineBetween(p.DrawPos, g.members[i + 1].DrawPos, SimpleColor.White);
                }
            }
        }
    }

    public static class MageCircle
    {
        public static JobDef StandJob => DefDatabase<JobDef>.GetNamedSilentFail("Avir_CircleStand");

        public static readonly Dictionary<string, CircleWork> Works = new Dictionary<string, CircleWork>();

        public static CircleWork Work(string key) { if (Works.Count == 0) Fill(); return key != null && Works.TryGetValue(key, out CircleWork w) ? w : null; }

        private static void Fill()
        {
            foreach (WeatherCall wc in new[] { WeatherCall.Storm, WeatherCall.StormFront })
            {
                WeatherCall call = wc;
                Works["Weather_" + call] = new CircleWork
                {
                    label = WeatherCalls.Label(call).ToLower(), need = WeatherCalls.Need(call), ticks = WeatherCalls.Channel,
                    cost = WeatherCalls.Cost, xp = WeatherCalls.Xp, kinds = WeatherCalls.Kinds(call), tint = WeatherCalls.Tint(call),
                    able = p => WeatherCalls.Able(p, call, WeatherCalls.Cost) != null,
                    done = (head, strength, members) =>
                    {
                        var comp = MapComponent_WeatherCall.Of(head.Map);
                        if (comp == null) return;
                        int stage = members.Select(Mages.Core).Where(c => c != null).Select(c => Mathf.Min(MageClass.Stage(MageClass.XpIn(c, RuneKind.Water)), MageClass.Stage(MageClass.XpIn(c, RuneKind.Air)))).DefaultIfEmpty(0).Max();
                        IntVec3 lead = (head as Building_WeatherAltar)?.lead ?? IntVec3.Invalid;
                        comp.Set(call, Mathf.RoundToInt(WeatherCalls.Holds * strength), head.Position, (8f + 2f * stage) * strength, lead);
                        Messages.Message("The circle has talked the sky round: " + WeatherCalls.Label(call).ToLower() + (strength < 0.999f ? " - weakened, for less time (" + strength.ToStringPercent() + ")" : ", for about eight hours") + ".", head, MessageTypeDefOf.PositiveEvent, false);
                    }
                };
            }
        }

        /// <summary>The seats of the circle reached from `head` by the silver wire: the head itself and every ritual circle
        /// (sanctuary) on the same net - with the cell a mage stands on at each.</summary>
        public static List<KeyValuePair<Thing, IntVec3>> Seats(Thing head)
        {
            var res = new List<KeyValuePair<Thing, IntVec3>>();
            if (head?.Spawned != true) return res;
            res.Add(new KeyValuePair<Thing, IntVec3>(head, head.def.hasInteractionCell ? head.InteractionCell : head.Position));
            StreamNet net = MapComponent_StreamNetwork.Of(head.Map)?.NetOf(head);
            if (net == null) return res;
            foreach (Thing n in net.nodes.OrderBy(n => n.Position.DistanceTo(head.Position)))
                if (n != head && n.Spawned && n.TryGetComp<CompRitualCircle>() != null && n.Position.Standable(n.Map)) res.Add(new KeyValuePair<Thing, IntVec3>(n, n.Position));
            return res;
        }

        public static IEnumerable<Pawn> Able(Map m, CircleWork w) => m.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed && !p.Drafted && !p.InMentalState && w.able(p) && MapComponent_MageCircle.Of(m)?.all.Any(g => g.members.Contains(p)) != true);

        /// <summary>Why the circle cannot be gathered now (null: it can).</summary>
        public static string Blocked(Thing head, CircleWork w)
        {
            if (w == null) return "Nothing to gather for.";
            if (MapComponent_MageCircle.Of(head.Map)?.At(head) != null) return "A circle is already gathering here.";
            int seats = Seats(head).Count;
            if (seats < w.need) return w.label.CapitalizeFirst() + " needs " + w.need + " seats joined by the silver stream wire: the altar and sanctuaries (ritual circles) on one wire. Joined now: " + seats + ".";
            int mages = Able(head.Map, w).Count();
            if (mages < w.need) return w.label.CapitalizeFirst() + " needs " + w.need + " free casters able to do it (with " + w.cost.ToString("0") + " Arcana each). Here now: " + mages + ".";
            return null;
        }

        /// <summary>Gather the circle: a mage to each seat, nearest first; they leave what they do and go.</summary>
        public static void Gather(Thing head, string key)
        {
            CircleWork w = Work(key);
            if (w == null || Blocked(head, w) != null) return;
            var seats = Seats(head).Take(w.need).ToList();
            var free = Able(head.Map, w).ToList();
            var g = new MageCircleGathering { head = head, work = key, began = Find.TickManager.TicksGame };
            foreach (var s in seats)
            {
                Pawn p = free.OrderBy(x => x.Position.DistanceTo(s.Value)).FirstOrDefault(x => x.CanReach(s.Value, PathEndMode.OnCell, Danger.Some));
                if (p == null) continue;
                free.Remove(p);
                g.members.Add(p); g.seats.Add(s.Value);
            }
            if (g.members.Count < w.need) { Messages.Message("Not enough casters can reach the seats of the circle.", head, MessageTypeDefOf.RejectInput, false); return; }
            MapComponent_MageCircle.Of(head.Map).all.Add(g);
            for (int i = 0; i < g.members.Count; i++)
            {
                Job j = JobMaker.MakeJob(StandJob, head, g.seats[i]);
                g.members[i].jobs.TryTakeOrderedJob(j, JobTag.Misc);
            }
            Messages.Message("The circle is gathering: " + string.Join(", ", g.members.Select(p => p.LabelShort)) + ".", head, MessageTypeDefOf.NeutralEvent, false);
        }
    }

    /// <summary>A mage of the circle: to the seat, and there - waiting for the others, then channelling with them - until
    /// the circle is done or broken.</summary>
    public class JobDriver_CircleStand : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            Toil stand = ToilMaker.MakeToil("CircleStand");
            stand.defaultCompleteMode = ToilCompleteMode.Never;
            stand.tickAction = () =>
            {
                MageCircleGathering g = MapComponent_MageCircle.Of(pawn.Map)?.At(TargetA.Thing);
                if (g == null || !g.members.Contains(pawn)) { EndJobWith(JobCondition.Succeeded); return; }
                pawn.rotationTracker.FaceCell(TargetA.Thing.Position);
            };
            stand.handlingFacing = true;
            yield return stand;
        }

        public override string GetReport()
        {
            MageCircleGathering g = MapComponent_MageCircle.Of(pawn.Map)?.At(TargetA.Thing);
            return g == null ? base.GetReport() : g.channelling ? "channelling with the circle." : "waiting for the circle.";
        }
    }
}
