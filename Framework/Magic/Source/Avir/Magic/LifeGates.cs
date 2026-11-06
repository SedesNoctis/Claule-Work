using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The living gates (the author's plan, «Живые врата», Fix195): Step + Anchor in a circle of Life grows a gate
    /// of roots where the spell lands - as many as one likes. A gate lives on those who go through it: every passage feeds
    /// it; unused, it dries and dies.
    ///   Passage, the druid's store has it: the store pays, no harm done.
    ///   Passage, the store is empty: the walker pays - weakened for a while, sleepy; the druid's store takes some, the
    ///     walker mends a little quicker.
    ///   Drawing a friend through: the store pays; the drawn one comes out mending and light.
    ///   Drawing a foe through: cheaper; most of its strength is taken to cover it - it comes out dragging, spent.</summary>
    public class Building_LifeGate : Building
    {
        public Pawn druid;
        public float life = 1f;
        public const float PassCost = 3f, FriendCost = 6f, FoeCost = 3f, PullRange = 40f;

        public static IEnumerable<Building_LifeGate> On(Map m) => m == null ? Enumerable.Empty<Building_LifeGate>() : m.listerThings.AllThings.OfType<Building_LifeGate>().Where(g => g.Spawned);

        public static void Grow(Pawn druid, Map map, IntVec3 at)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_LifeGate");
            if (def == null || map == null || !at.InBounds(map)) return;
            IntVec3 c = at.Standable(map) && at.GetEdifice(map) == null ? at : CellFinder.StandableCellNear(at, map, 3f, x => x.GetEdifice(map) == null);
            if (!c.IsValid) { Messages.Message("No room for a living gate there.", MessageTypeDefOf.RejectInput, false); return; }
            var g = (Building_LifeGate)ThingMaker.MakeThing(def);
            g.druid = druid;
            GenSpawn.Spawn(g, c, map);
            if (druid?.Faction != null) g.SetFaction(druid.Faction);
            for (int i = 0; i < 8; i++) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted() + Rand.InsideUnitCircleVec3, map, 1.2f, new Color(0.45f, 0.65f, 0.35f, 0.6f));
            MapComponent_SpellSprites.PutTinted(map, "Life/Mend", new Color(0.55f, 0.85f, 0.45f), c.ToVector3Shifted(), 2.4f, 90);
        }

        private Hediff_ArcaneCore Core => druid != null && !druid.Dead ? Mages.Core(druid) : null;

        /// <summary>Takes `need` of plain Life from the druid; how much it got (0..1).</summary>
        private float Pay(float need)
        {
            Hediff_ArcaneCore c = Core;
            if (c == null) return 0f;
            float have = c.Store(LifeMagic.None), got = Mathf.Min(have, need);
            c.SetStore(LifeMagic.None, have - got);
            return got / need;
        }

        private void Feed() { life = Mathf.Min(1f, life + 0.08f); }

        /// <summary>A walker comes out of `to`.</summary>
        public void Pass(Pawn walker, Building_LifeGate to)
        {
            if (walker == null || to == null || !to.Spawned) return;
            float paid = Pay(PassCost);
            if (paid < 0.99f && walker.RaceProps.IsFlesh)
            {
                // the store is empty: the walker pays - weakened, sleepy; the druid gains, the walker mends a little
                MagicFx.Give(walker, DefDatabase<HediffDef>.GetNamedSilentFail("Avir_GateDrained"), GenDate.TicksPerHour * 4);
                if (walker.needs?.rest != null) walker.needs.rest.CurLevel = Mathf.Max(0f, walker.needs.rest.CurLevel - 0.2f * (1f - paid));
                Core?.AddTo(LifeMagic.None, 1.5f);
                LifeMagic.AddRegen(walker, 0.1f);
            }
            Feed(); to.Feed();
            Move(walker, to);
        }

        public static void Move(Pawn p, Building_LifeGate to)
        {
            Map map = to.Map;
            IntVec3 cell = to.Position.Standable(map) && to.Position.GetFirstPawn(map) == null ? to.Position : CellFinder.StandableCellNear(to.Position, map, 2f);
            if (!cell.IsValid) cell = to.Position;
            Wisps(p.Position, p.Map);
            p.pather?.StopDead();
            if (p.Map != map) return;   // a gate does not cross maps
            p.Position = cell;
            p.Notify_Teleported(true, true);
            Wisps(cell, map);
        }

        private static void Wisps(IntVec3 c, Map m)
        {
            if (m == null) return;
            for (int i = 0; i < 5; i++) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted() + Rand.InsideUnitCircleVec3 * 0.6f, m, 1f, new Color(0.55f, 0.85f, 0.45f, 0.55f));
            MapComponent_SpellSprites.PutTinted(m, "Life/Petals", new Color(0.6f, 0.9f, 0.5f), c.ToVector3Shifted(), 1.4f, 50);
        }

        /// <summary>Draw someone through to this gate: a friend for the store, a foe cheaper - and spent.</summary>
        public void Draw(Pawn p)
        {
            if (p == null || p.Dead || !p.Spawned || p.Map != Map) return;
            if (p.Position.DistanceTo(Position) > PullRange) { Messages.Message("Too far from the gate.", MessageTypeDefOf.RejectInput, false); return; }
            bool foe = druid != null && p.HostileTo(druid);
            float paid = Pay(foe ? FoeCost : FriendCost);
            if (paid < 0.99f) { Messages.Message("The druid's store of life is too low to draw " + p.LabelShort + " through.", MessageTypeDefOf.RejectInput, false); return; }
            Feed();
            Move(p, this);
            if (foe)
            {
                // most of its strength taken to cover the drawing: it comes out dragging, spent
                if (p.needs?.rest != null) p.needs.rest.CurLevel *= 0.3f;
                MagicFx.Give(p, DefDatabase<HediffDef>.GetNamedSilentFail("Avir_GateDrained"), GenDate.TicksPerHour * 6);
            }
            else
            {
                LifeMagic.AddRegen(p, 0.2f);
                MagicFx.Give(p, DefDatabase<HediffDef>.GetNamedSilentFail("Avir_GateLight"), GenDate.TicksPerHour * 4);
            }
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!this.IsHashIntervalTick(2500, delta)) return;
            // unused, it dries: about a week without a passage and it dies
            life -= 0.15f / 24f;
            if (life <= 0f)
            {
                Messages.Message("A living gate has dried up and died: no one went through it.", new TargetInfo(Position, Map), MessageTypeDefOf.NeutralEvent, false);
                for (int i = 0; i < 6; i++) FleckMaker.ThrowDustPuffThick(DrawPos + Rand.InsideUnitCircleVec3, Map, 1f, new Color(0.45f, 0.38f, 0.28f, 0.6f));
                Destroy(DestroyMode.KillFinalize);
            }
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string mine = "Life: " + life.ToStringPercent() + (life < 0.35f ? " (drying - let someone go through)" : "") + "\nGrown by " + (druid?.LabelShort ?? "no one") + (Core != null ? " (store of life " + Core.Store(LifeMagic.None).ToString("0") + ")" : "");
            return s.NullOrEmpty() ? mine : s + "\n" + mine;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            if (Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Draw a friend through", icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchShip", false) ?? BaseContent.BadTex,
                defaultDesc = "Choose one of yours within " + PullRange + " cells: they come out of this gate mending and light. The druid's store pays (" + FriendCost + ").",
                action = () => Find.Targeter.BeginTargeting(new TargetingParameters { canTargetPawns = true, canTargetLocations = false, validator = t => t.Thing is Pawn p && !p.HostileTo(Faction.OfPlayer) }, t => Draw(t.Thing as Pawn))
            };
            yield return new Command_Action
            {
                defaultLabel = "Draw a foe through", icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false) ?? BaseContent.BadTex,
                defaultDesc = "Choose a foe within " + PullRange + " cells: it is dragged out of this gate spent, most of its strength taken. Cheaper (" + FoeCost + ").",
                action = () => Find.Targeter.BeginTargeting(new TargetingParameters { canTargetPawns = true, canTargetLocations = false, validator = t => t.Thing is Pawn p && p.HostileTo(Faction.OfPlayer) }, t => Draw(t.Thing as Pawn))
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref druid, "druid");
            Scribe_Values.Look(ref life, "life", 1f);
        }
    }

    /// <summary>A colonist goes to the nearest gate and comes out of the one chosen.</summary>
    public class JobDriver_LifeGateWalk : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !(TargetA.Thing is Building_LifeGate a) || !a.Spawned || !(TargetB.Thing is Building_LifeGate b) || !b.Spawned);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil step = ToilMaker.MakeToil("LifeGateStep");
            step.initAction = () => { if (TargetA.Thing is Building_LifeGate a && TargetB.Thing is Building_LifeGate b) a.Pass(pawn, b); };
            step.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return step;
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class Patch_LifeGateGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            Pawn p = __instance;
            if (p?.Map == null || p.Faction != Faction.OfPlayer || !p.IsColonistPlayerControlled) yield break;
            var gates = Building_LifeGate.On(p.Map).ToList();
            if (gates.Count < 2) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Through the living gates",
                defaultDesc = "Choose the gate to come out of: the colonist goes to the nearest other gate and steps through. The druid's store pays; with it empty, the walker pays - weakened and sleepy for a while.",
                icon = ContentFinder<Texture2D>.Get("Things/Mote/Avir/Magic/Life_BranchDome", false) ?? BaseContent.BadTex,
                action = () => Find.Targeter.BeginTargeting(new TargetingParameters { canTargetBuildings = true, canTargetPawns = false, canTargetLocations = false, validator = t => t.Thing is Building_LifeGate },
                    t =>
                    {
                        if (!(t.Thing is Building_LifeGate to)) return;
                        Building_LifeGate from = Building_LifeGate.On(p.Map).Where(g => g != to && p.CanReach(g, PathEndMode.Touch, Danger.Deadly)).OrderBy(g => g.Position.DistanceToSquared(p.Position)).FirstOrDefault();
                        if (from == null) { Messages.Message("No other gate within reach.", MessageTypeDefOf.RejectInput, false); return; }
                        p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_LifeGateWalk"), from, to), JobTag.Misc);
                    })
            };
        }
    }
}
