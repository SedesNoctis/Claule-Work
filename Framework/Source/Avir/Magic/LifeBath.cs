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
    // ================================================================== the bath (Fever - the author, Fix198)

    /// <summary>A light steam of Fever (Life with fire and water): a small magical source that warms a close room and fills
    /// it with steam. With seats in such a room (about one source to ten cells) the colonists come to bathe: they undress,
    /// sit and steam - recreation, and after it the body bears both heat and cold better for a while and learns its
    /// sicknesses quicker. A caster of Fever keeps the source going (renews it when it runs low).</summary>
    public class Building_BathSteam : Building
    {
        public int left = GenDate.TicksPerDay * 5;
        public const int Life = GenDate.TicksPerDay * 5;
        public bool Low => left < GenDate.TicksPerDay;

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            left -= delta;
            if (left <= 0) { Messages.Message("A bath steam has gone cold.", new TargetInfo(Position, Map), MessageTypeDefOf.NeutralEvent, false); Destroy(); return; }
            if (!this.IsHashIntervalTick(90, delta) || Map == null) return;
            FleckMaker.ThrowDustPuffThick(DrawPos + Rand.InsideUnitCircleVec3 * 0.3f, Map, Rand.Range(0.8f, 1.3f), new Color(1f, 1f, 1f, 0.35f));
            // warm, never past a hot bath
            Room room = Position.GetRoom(Map);
            if (room != null && !room.PsychologicallyOutdoors && room.Temperature < 42f) GenTemperature.PushHeat(Position, Map, 20f);
            AtmosphereBridge.Moisture(Map, Position, 0.4f);
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string mine = "Steam for " + left.ToStringTicksToPeriod() + (Low ? " (a caster of Fever will renew it)" : "");
            Room r = Position.GetRoom(Map);
            if (r != null && !r.PsychologicallyOutdoors) mine += "\n" + (LifeBath.Fits(r) ? "A bath: the colonists will come to steam here." : "The room is too large for its steam (about ten cells to a source).");
            return s.NullOrEmpty() ? mine : s + "\n" + mine;
        }

        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref left, "left", Life); }
    }

    public static class LifeBath
    {
        public static bool Fever(Hediff_ArcaneCore c) => c != null && MagicWork.Knows(c, RuneKind.Fire) && MagicWork.Knows(c, RuneKind.Water) && MagicWork.Knows(c, RuneKind.Life);

        public static IEnumerable<Building_BathSteam> Sources(Room r) => r == null ? Enumerable.Empty<Building_BathSteam>() : r.ContainedAndAdjacentThings.OfType<Building_BathSteam>().Where(s => s.Spawned && s.GetRoom() == r);

        /// <summary>A bath: a closed room, about ten cells to each source of steam.</summary>
        public static bool Fits(Room r) => r != null && !r.PsychologicallyOutdoors && !r.TouchesMapEdge && Sources(r).Any() && r.CellCount <= 11 * Sources(r).Count();

        public static bool Bathing(Pawn p) => p?.CurJobDef != null && p.CurJobDef.defName == "Avir_Bathe" && p.pather != null && !p.pather.Moving;
    }

    /// <summary>Where a caster of Fever is to raise a light steam for a bath.</summary>
    public class Designator_LifeBathSteam : Designator_Arcane
    {
        public Designator_LifeBathSteam()
        {
            defaultLabel = "Bath steam (Fever)";
            defaultDesc = "Mark a cell in a small closed room for a caster of Fever (Life with fire and water) to raise a light magical steam there - a bath. About one source to ten cells; put seats in the room. The caster renews the steam when it runs low.";
            icon = ArcaneIcons.For("Avir_LifeBathSteam", "UI/Designators/SmoothSurface");
        }
        protected override DesignationDef Designation => DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_LifeBathSteam");
        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map) || Designation == null || Map.designationManager.DesignationAt(c, Designation) != null) return false;
            if (!c.Standable(Map) || c.GetEdifice(Map) != null) return false;
            Room r = c.GetRoom(Map);
            if (r == null || r.PsychologicallyOutdoors) return "A bath needs a closed room.";
            return true;
        }
    }

    /// <summary>Raising the steam where marked, and renewing the sources that run low.</summary>
    public class WorkGiver_LifeBathSteam : WorkGiver_Scanner
    {
        public const float Store = 4f;
        private static DesignationDef D => DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_LifeBathSteam");
        private static bool Able(Pawn p) { Hediff_ArcaneCore c = LifeWorks.Able(p, Store); return c != null && LifeBath.Fever(c); }
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Undefined);
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !Able(pawn);
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn)
        {
            var cells = D != null ? pawn.Map.designationManager.SpawnedDesignationsOfDef(D).Select(x => x.target.Cell).ToList() : new List<IntVec3>();
            cells.AddRange(pawn.Map.listerThings.AllThings.OfType<Building_BathSteam>().Where(s => s.Low).Select(s => s.Position));
            return cells;
        }
        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            bool marked = D != null && pawn.Map.designationManager.DesignationAt(c, D) != null;
            bool low = c.GetFirstThing<Building_BathSteam>(pawn.Map) is Building_BathSteam s && s.Low;
            return (marked || low) && !c.IsForbidden(pawn) && pawn.CanReserve(c, 1, -1, null, forced) && Able(pawn);
        }
        public override Job JobOnCell(Pawn pawn, IntVec3 c, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_LifeBathSteam"), c);
    }

    public class JobDriver_LifeBathSteam : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return WorkSpells.GoTo(this, false, 0f);
            yield return ArcaneChannel.Channel(this, 300, WorkSpells.Charm("Avir_LifeBathSteam", new[] { RuneKind.Life, RuneKind.Fire, RuneKind.Water }), false, new Color(1f, 0.6f, 0.5f, 0.5f));
            Toil done = ToilMaker.MakeToil("LifeBathSteam");
            done.initAction = () =>
            {
                Map map = pawn.Map; IntVec3 c = TargetA.Cell;
                Hediff_ArcaneCore core = MagicWork.Able(pawn, RuneKind.Life, LifeWorks.Arcana);
                if (core == null || LifeWorks.Spend(core, WorkGiver_LifeBathSteam.Store) <= 0f) return;
                DesignationDef d = DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_LifeBathSteam");
                if (d != null) map.designationManager.TryRemoveDesignation(c, d);
                if (c.GetFirstThing<Building_BathSteam>(map) is Building_BathSteam s) s.left = Building_BathSteam.Life;   // renewed
                else
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_BathSteam");
                    if (def == null || c.GetEdifice(map) != null) return;
                    Thing t = GenSpawn.Spawn(def, c, map);
                    t.SetFaction(pawn.Faction);
                }
                for (int i = 0; i < 5; i++) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted() + Rand.InsideUnitCircleVec3 * 0.6f, map, 1.3f, new Color(1f, 1f, 1f, 0.4f));
                MagicWork.Done(core, RuneKind.Life, LifeWorks.Arcana, LifeWorks.Xp);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    /// <summary>The bath as recreation: a seat in a bath room.</summary>
    public class JoyGiver_Bathe : JoyGiver
    {
        public override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.Map == null || !pawn.RaceProps.Humanlike) return null;
            foreach (Room r in pawn.Map.listerThings.AllThings.OfType<Building_BathSteam>().Select(s => s.GetRoom()).Where(LifeBath.Fits).Distinct().InRandomOrder())
            {
                Building seat = r.ContainedAndAdjacentThings.OfType<Building>().Where(b => b.def.building != null && b.def.building.isSittable && b.GetRoom() == r && !b.IsForbidden(pawn)
                    && pawn.CanReserveAndReach(b, PathEndMode.OnCell, Danger.Some)).RandomElementWithFallback();
                if (seat != null) return JobMaker.MakeJob(def.jobDef, seat);
            }
            return null;
        }
    }

    public class JobDriver_Bathe : JobDriver
    {
        private int sat;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !LifeBath.Fits(TargetA.Thing?.GetRoom()));
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil steam = ToilMaker.MakeToil("Bathe");
            steam.defaultCompleteMode = ToilCompleteMode.Delay;
            steam.defaultDuration = job.def.joyDuration;
            steam.socialMode = RandomSocialMode.SuperActive;
            steam.handlingFacing = true;
            steam.tickIntervalAction = delta =>
            {
                sat += delta;
                JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.EndJob, 1f, TargetA.Thing as Building);
                if (pawn.IsHashIntervalTick(200, delta)) FleckMaker.ThrowDustPuffThick(pawn.DrawPos + new Vector3(0f, 0f, 0.3f), pawn.Map, 0.6f, new Color(1f, 1f, 1f, 0.3f));
            };
            steam.AddFinishAction(() =>
            {
                if (sat < 600) return;   // a dip, not a bath
                HediffDef d = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Bathed");
                if (d != null) MagicFx.Give(pawn, d, GenDate.TicksPerDay);
            });
            yield return steam;
        }
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref sat, "sat"); }
    }

    /// <summary>In the bath they sit undressed (the clothes are only not drawn - nothing is taken off or dropped).</summary>
    [HarmonyPatch(typeof(PawnRenderer), "get_DefaultRenderFlagsNow")]
    public static class Patch_BathUndressed
    {
        public static void Postfix(Pawn ___pawn, ref PawnRenderFlags __result)
        {
            if (LifeBath.Bathing(___pawn)) __result &= ~(PawnRenderFlags.Clothes | PawnRenderFlags.Headgear);
        }
    }

    // ================================================================== Pestilence sees the sick (Fix199)

    /// <summary>A caster of Pestilence, selected, sees the sick round them in a sickly yellow - the unseen too (useful when
    /// they leave a gas that spreads sickness). Not themselves, and not other casters of Pestilence.</summary>
    public class MapComponent_PestSight : MapComponent
    {
        private static Material mat;
        private static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        public MapComponent_PestSight(Map map) : base(map) { }
        private static bool PestMage(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); return c != null && c.pestMage; }
        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map || !(Find.Selector.SingleSelectedThing is Pawn mage) || mage.Map != map || !PestMage(mage)) return;
            if (mat == null) mat = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Life/PoisonFog", ShaderDatabase.MoteGlow, Color.white);
            float t = Time.realtimeSinceStartup;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == mage || p.Dead || PestMage(p) || p.health?.hediffSet == null || !p.health.hediffSet.hediffs.Any(Pestilence.Sickness)) continue;
                Vector3 at = p.DrawPos; at.y = AltitudeLayer.MetaOverlays.AltitudeFor();
                block.SetColor(ShaderPropertyIDs.Color, new Color(0.85f, 0.85f, 0.25f, 0.45f + 0.15f * Mathf.Sin(t * 2.5f + p.thingIDNumber)));
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.AngleAxis((t * 20f + p.thingIDNumber * 31f) % 360f, Vector3.up), new Vector3(1.6f, 1f, 1.6f)), mat, 0, null, 0, block);
            }
        }
    }

    // ================================================================== the couch: a caster works over one who lies

    /// <summary>A couch of Life care (the author, Fix198): a colonist lies down on it for recreation and waits; a caster of
    /// Life comes and works over them - kneading, warmth, the slow hands. Stronger than a passing soothing.</summary>
    public class Building_LifeCouch : Building
    {
        public Pawn lying;
        public bool cared;
        public bool Waiting => lying != null && lying.Spawned && lying.CurJobDef?.defName == "Avir_LieForCare" && !cared;
        public IntVec3 LieCell => Position;
        public override void ExposeData() { base.ExposeData(); Scribe_References.Look(ref lying, "lying"); Scribe_Values.Look(ref cared, "cared"); }
        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string mine = Waiting ? lying.LabelShort + " lies waiting for a caster of Life." : "Free.";
            return s.NullOrEmpty() ? mine : s + "\n" + mine;
        }
    }

    public class JoyGiver_LieForCare : JoyGiver
    {
        public override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.Map == null || !pawn.RaceProps.Humanlike) return null;
            // only where a caster of Life is about to come
            if (!pawn.Map.mapPawns.FreeColonistsSpawned.Any(m => m != pawn && WorkGiver_LifeCare.Able(m) && !m.Downed && m.workSettings != null && m.workSettings.WorkIsActive(DefDatabase<WorkTypeDef>.GetNamedSilentFail("Avir_MagicWork")))) return null;
            Building_LifeCouch couch = pawn.Map.listerThings.AllThings.OfType<Building_LifeCouch>().Where(c => c.lying == null && !c.IsForbidden(pawn) && pawn.CanReserveAndReach(c, PathEndMode.OnCell, Danger.Some)).OrderBy(c => c.Position.DistanceToSquared(pawn.Position)).FirstOrDefault();
            return couch != null ? JobMaker.MakeJob(def.jobDef, couch) : null;
        }
    }

    public class JobDriver_LieForCare : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil lie = ToilMaker.MakeToil("LieForCare");
            lie.initAction = () => { if (TargetA.Thing is Building_LifeCouch c) { c.lying = pawn; c.cared = false; } pawn.jobs.posture = PawnPosture.LayingOnGroundFaceUp; };
            lie.defaultCompleteMode = ToilCompleteMode.Delay;
            lie.defaultDuration = job.def.joyDuration;
            lie.tickIntervalAction = delta =>
            {
                pawn.jobs.posture = PawnPosture.LayingOnGroundFaceUp;
                JoyUtility.JoyTickCheckEnd(pawn, delta, JoyTickFullJoyAction.None, 0.4f, TargetA.Thing as Building);
                if (TargetA.Thing is Building_LifeCouch c && c.cared) ReadyForNextToil();
            };
            lie.AddFinishAction(() => { if (TargetA.Thing is Building_LifeCouch c && c.lying == pawn) { c.lying = null; c.cared = false; } pawn.jobs.posture = PawnPosture.Standing; });
            yield return lie;
        }
    }

    public class WorkGiver_LifeCare : WorkGiver_Scanner
    {
        public const float Store = 2f, StoneArcana = 3f;
        /// <summary>Who can work over the one on the couch (the author, Fix199: not Life's alone): a caster of Life, out of
        /// its store; a caster of earth with warm stones, for Arcana.</summary>
        public static bool Able(Pawn p) => LifeWorks.Able(p, Store) != null || MagicWork.Able(p, RuneKind.Earth, StoneArcana) != null;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !Able(pawn);
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            t is Building_LifeCouch c && c.Waiting && c.lying != pawn && !c.IsForbidden(pawn) && pawn.CanReach(c, PathEndMode.Touch, Danger.Some) && Able(pawn);
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_LifeCare"), t);
    }

    public class JobDriver_LifeCare : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !(TargetA.Thing is Building_LifeCouch c) || !c.Waiting);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return ArcaneChannel.Channel(this, 900, WorkSpells.Charm("Avir_LifeCare", new[] { RuneKind.Life }), false, new Color(0.7f, 0.95f, 0.6f, 0.45f));
            Toil done = ToilMaker.MakeToil("LifeCare");
            done.initAction = () =>
            {
                if (!(TargetA.Thing is Building_LifeCouch c) || c.lying == null) return;
                Pawn p = c.lying;
                Hediff_ArcaneCore core = MagicWork.Able(pawn, RuneKind.Life, LifeWorks.Arcana);
                float f = core != null && LifeWorks.Has(core, WorkGiver_LifeCare.Store) ? LifeWorks.Spend(core, WorkGiver_LifeCare.Store) : 0f;
                if (f <= 0f)
                {
                    // warm stones: a caster of earth (Fix199) - laid along the back, rolled, pressed
                    Hediff_ArcaneCore ec = MagicWork.Able(pawn, RuneKind.Earth, WorkGiver_LifeCare.StoneArcana);
                    if (ec == null) return;
                    float sq = Mathf.Clamp(0.5f + 0.12f * MageClass.Stage(MageClass.XpIn(ec, RuneKind.Earth)), 0.4f, 1.1f);
                    p.needs?.joy?.GainJoy(0.4f * sq, DefDatabase<JoyKindDef>.GetNamedSilentFail("Avir_Care") ?? JoyKindDefOf.Meditative);
                    if (p.needs?.rest != null) p.needs.rest.CurLevel = Mathf.Min(1f, p.needs.rest.CurLevel + 0.1f * sq);
                    MagicFx.Give(p, DefDatabase<HediffDef>.GetNamedSilentFail("Avir_StoneMassaged"), Mathf.RoundToInt(GenDate.TicksPerDay * 0.75f));
                    ThoughtDef th0 = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_LifeCaredFor");
                    if (th0 != null) p.needs?.mood?.thoughts?.memories?.TryGainMemory(th0, pawn);
                    FleckMaker.ThrowDustPuffThick(p.DrawPos, p.Map, 1f, MagicFx.EarthDust);
                    c.cared = true;
                    MagicWork.Done(ec, RuneKind.Earth, WorkGiver_LifeCare.StoneArcana, LifeWorks.Xp);
                    return;
                }
                float q = LifeWorks.Quality(core) * f;
                p.needs?.joy?.GainJoy(0.45f * q, DefDatabase<JoyKindDef>.GetNamedSilentFail("Avir_Care") ?? JoyKindDefOf.Meditative);
                if (p.needs?.rest != null) p.needs.rest.CurLevel = Mathf.Min(1f, p.needs.rest.CurLevel + 0.15f * q);
                MagicFx.Give(p, DefDatabase<HediffDef>.GetNamedSilentFail("Avir_LifeCared"), Mathf.RoundToInt(GenDate.TicksPerDay * 0.75f));
                ThoughtDef th = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_LifeCaredFor");
                if (th != null) p.needs?.mood?.thoughts?.memories?.TryGainMemory(th, pawn);
                LifeWorks.AfterGift(core, p, q * 0.5f);   // the caster's own kind shows in the hands too
                MapComponent_SpellSprites.PutTinted(p.Map, "Life/Petals", new Color(0.75f, 0.95f, 0.65f), p.DrawPos, 1.4f, 80);
                c.cared = true;
                MagicWork.Done(core, RuneKind.Life, LifeWorks.Arcana, LifeWorks.Xp);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }
}
