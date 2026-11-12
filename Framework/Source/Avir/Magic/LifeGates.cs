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
        /// <summary>The colour of the Life that grew it (Fix208): the energy in the ring of roots wears it.</summary>
        public Color tint = LifeMagic.Necrotic;
        private Material energy;
        public const float PassCost = 3f, FriendCost = 6f, FoeCost = 3f, PullRange = 40f;

        /// <summary>The gates standing on each map (Fix251): kept as they are grown and die, not looked for among all things.</summary>
        private static readonly Dictionary<Map, List<Building_LifeGate>> standing = new Dictionary<Map, List<Building_LifeGate>>();
        private static readonly List<Building_LifeGate> none = new List<Building_LifeGate>();
        public static List<Building_LifeGate> On(Map m) => m != null && standing.TryGetValue(m, out var l) ? l : none;
        /// <summary>The druid may open the gates to foes too (the author, Fix251): a foe that goes through feeds the druid's
        /// store and comes out withered.</summary>
        public bool openToFoes;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!standing.TryGetValue(map, out var l)) standing[map] = l = new List<Building_LifeGate>();
            if (!l.Contains(this)) l.Add(this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map m = Map;
            if (m != null && standing.TryGetValue(m, out var l)) { l.Remove(this); if (l.Count == 0) standing.Remove(m); }
            base.DeSpawn(mode);
        }

        /// <summary>A foe goes through an open gate: its strength feeds the druid's store, it comes out withered and spent.</summary>
        public void PassFoe(Pawn walker, Building_LifeGate to)
        {
            if (walker == null || to == null || !to.Spawned) return;
            Feed(); to.Feed();
            Move(walker, to);
            if (!walker.RaceProps.IsFlesh || walker.Dead) return;
            Core?.AddTo(LifeMagic.None, FoeGives);
            if (walker.needs?.rest != null) walker.needs.rest.CurLevel *= 0.6f;
            MagicFx.Give(walker, DefDatabase<HediffDef>.GetNamedSilentFail("Avir_GateDrained"), GenDate.TicksPerHour * 6);
            walker.TakeDamage(new DamageInfo(LifeMagic.WitherDmg, FoeWither, 2f, -1f, druid));
        }
        public const float FoeGives = 6f, FoeWither = 8f;

        public static void Grow(Pawn druid, Map map, IntVec3 at) => Grow(druid, map, at, LifeMagic.Necrotic);

        public static void Grow(Pawn druid, Map map, IntVec3 at, Color tint)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_LifeGate");
            if (def == null || map == null || !at.InBounds(map)) return;
            IntVec3 c = at.Standable(map) && at.GetEdifice(map) == null ? at : CellFinder.StandableCellNear(at, map, 3f, x => x.GetEdifice(map) == null);
            if (!c.IsValid) { Messages.Message("No room for a living gate there.", MessageTypeDefOf.RejectInput, false); return; }
            var g = (Building_LifeGate)ThingMaker.MakeThing(def);
            g.druid = druid;
            g.tint = tint;
            GenSpawn.Spawn(g, c, map);
            if (druid?.Faction != null) g.SetFaction(druid.Faction);
            for (int i = 0; i < 8; i++) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted() + Rand.InsideUnitCircleVec3, map, 1.2f, new Color(0.45f, 0.65f, 0.35f, 0.6f));
            MapComponent_SpellSprites.PutTinted(map, "Life/Mend", tint, c.ToVector3Shifted(), 2.4f, 90);
        }

        /// <summary>A ring of roots; the energy inside it turns slowly, in the colour of the Life that grew it.</summary>
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (energy == null) energy = MaterialPool.MatFrom("Things/Building/Avir/Magic/LifeGate_Energy", ShaderDatabase.Transparent, new Color(tint.r, tint.g, tint.b, 0.95f));
            Vector3 at = drawLoc; at.y -= 0.01f;
            float s = def.graphicData.drawSize.x;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.AngleAxis(-(Find.TickManager.TicksGame % 3600) / 10f, Vector3.up), new Vector3(s, 1f, s)), energy, 0);
            base.DrawAt(drawLoc, flip);
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
            string mine = (openToFoes ? "Open to foes. " : "") + "Life: " + life.ToStringPercent() + (life < 0.35f ? " (drying - let someone go through)" : "") + "\nGrown by " + (druid?.LabelShort ?? "no one") + (Core != null ? " (store of life " + Core.Store(LifeMagic.None).ToString("0") + ")" : "");
            return s.NullOrEmpty() ? mine : s + "\n" + mine;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            if (Faction != Faction.OfPlayer) yield break;
            yield return new Command_Toggle
            {
                defaultLabel = "Open to foes", icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false) ?? BaseContent.BadTex,
                defaultDesc = "Let foes use this gate on their own way. Each one that goes through feeds the druid's store of life (" + FoeGives + ") and comes out withered and spent. A foe only takes a gate whose way out is open to foes too.",
                isActive = () => openToFoes, toggleAction = () => openToFoes = !openToFoes
            };
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
            Scribe_Values.Look(ref tint, "tint", LifeMagic.Necrotic);
            Scribe_Values.Look(ref openToFoes, "openToFoes");
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

    /// <summary>The gates are part of the way (the author, Fix251): no button - whoever sets out on a long walk reckons the
    /// way through the gates too, and takes it when it is much the shorter: to the gate nearest them, out of the gate nearest
    /// where they are going, and on. The druid's own people pass as before (the store pays; empty, the walker pays). Foes
    /// take only gates opened to them - and pay with their strength.</summary>
    public static class GateWays
    {
        public class Way { public Building_LifeGate from, to; public LocalTargetInfo dest; public PathEndMode pe; }
        private static readonly Dictionary<int, Way> ways = new Dictionary<int, Way>();
        public const float MinWalk = 25f, MustSave = 0.6f, GateCost = 6f;
        private static bool inside;

        public static Way Of(Pawn p) => p != null && ways.TryGetValue(p.thingIDNumber, out Way w) ? w : null;
        public static void Forget(Pawn p) { if (p != null) ways.Remove(p.thingIDNumber); }

        private static bool MayUse(Pawn p, Building_LifeGate g, out bool foe)
        {
            foe = false;
            if (g.Faction == null || p.Faction == null) return false;
            if (p.Faction == g.Faction) return p.RaceProps.Humanlike || p.RaceProps.Animal && p.playerSettings?.Master != null;
            if (p.HostFaction == g.Faction && !p.IsPrisoner) return true;   // guests
            foe = p.HostileTo(g.Faction);
            return foe && g.openToFoes && !p.RaceProps.IsMechanoid;
        }

        /// <summary>The way through the gates for this walk, or null when the plain way is as good.</summary>
        public static Way WayFor(Pawn p, LocalTargetInfo dest, PathEndMode pe)
        {
            Map map = p.Map;
            var gates = Building_LifeGate.On(map);
            if (gates.Count < 2 || !dest.IsValid || p.Downed || p.Dead) return null;
            IntVec3 d = dest.Cell, at = p.Position;
            if (!d.InBounds(map)) return null;
            float direct = at.DistanceTo(d);
            if (direct < MinWalk) return null;
            Building_LifeGate a = null, b = null; float da = float.MaxValue, db = float.MaxValue;
            for (int i = 0; i < gates.Count; i++)
            {
                Building_LifeGate g = gates[i];
                if (!MayUse(p, g, out _)) continue;
                float x = g.Position.DistanceTo(at), y = g.Position.DistanceTo(d);
                if (x < da) { da = x; a = g; }
                if (y < db) { db = y; b = g; }
            }
            if (a == null || b == null || a == b) return null;
            if (da + db + GateCost > direct * MustSave) return null;
            if (!p.CanReach(a, PathEndMode.Touch, Danger.Deadly)) return null;
            if (!map.reachability.CanReach(b.Position, dest, pe, TraverseParms.For(p, Danger.Deadly))) return null;
            return new Way { from = a, to = b, dest = dest, pe = pe };
        }

        [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
        public static class Patch_Start
        {
            public static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn, ref LocalTargetInfo dest, ref PathEndMode peMode)
            {
                if (inside || ___pawn == null || !___pawn.Spawned) return true;
                // already on the way to a gate for this very walk (Fix252): a job that asks for its path again every tick
                // because the pather is "not going to its target" (a chase, a follow) must not start the walk over each time
                if (ways.TryGetValue(___pawn.thingIDNumber, out Way cur) && cur.dest == dest && cur.pe == peMode && cur.from != null && cur.from.Spawned
                    && __instance.Moving && __instance.Destination.Thing == cur.from) return false;
                ways.Remove(___pawn.thingIDNumber);
                if (Building_LifeGate.On(___pawn.Map).Count < 2) return true;
                try
                {
                    Way w = WayFor(___pawn, dest, peMode);
                    if (w == null) return true;
                    ways[___pawn.thingIDNumber] = w;
                    dest = w.from; peMode = PathEndMode.Touch;
                }
                catch (System.Exception e) { Log.ErrorOnce("[Avir] gate way: " + e, 0x6A7E); }
                return true;
            }
        }

        [HarmonyPatch(typeof(Pawn_PathFollower), "PatherArrived")]
        public static class Patch_Arrived
        {
            public static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn)
            {
                if (___pawn == null || !ways.TryGetValue(___pawn.thingIDNumber, out Way w)) return true;
                ways.Remove(___pawn.thingIDNumber);
                if (w.from != null && w.from.Spawned && w.to != null && w.to.Spawned && w.from.Map == ___pawn.Map)
                {
                    MayUse(___pawn, w.from, out bool foe);
                    if (foe) w.from.PassFoe(___pawn, w.to); else w.from.Pass(___pawn, w.to);
                }
                if (___pawn.Dead || ___pawn.Downed || !___pawn.Spawned) return true;
                // on, from the far gate, to where they were going all along
                inside = true;
                try { __instance.StartPath(w.dest, w.pe); } finally { inside = false; }
                return false;
            }
        }

        [HarmonyPatch(typeof(Pawn_PathFollower), "PatherFailed")]
        public static class Patch_Failed { public static void Prefix(Pawn ___pawn) => Forget(___pawn); }

        [LudeonTK.DebugAction("Magic", "Life gates: way of the selected pawn to here (log)", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void DevWay()
        {
            Pawn p = Verse.Find.Selector.SingleSelectedThing as Pawn;
            if (p == null) { Messages.Message("Select one pawn first.", MessageTypeDefOf.RejectInput, false); return; }
            IntVec3 c = Verse.UI.MouseCell();
            Way w = WayFor(p, c, PathEndMode.OnCell);
            Log.Message("[Avir] gate way for " + p.LabelShort + " to " + c + ": " + (w == null ? "the plain way (gates: " + Building_LifeGate.On(p.Map).Count + ", walk " + p.Position.DistanceTo(c).ToString("0") + ")" : "in at " + w.from.Position + ", out at " + w.to.Position));
        }

        [LudeonTK.DebugAction("Magic", "Life gates: grow a gate here", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void DevGrow()
        {
            Map m = Verse.Find.CurrentMap; if (m == null) return;
            Building_LifeGate.Grow(m.mapPawns.FreeColonistsSpawned.FirstOrDefault(), m, Verse.UI.MouseCell());
        }
    }
}
