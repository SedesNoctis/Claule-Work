using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Ritualistics
{
    /// <summary>Ritualism work: draw the chosen ritual into its circle. Intellectual and Artistic set
    /// both the speed and the quality.</summary>
    public class WorkGiver_DrawRitual : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.OnCell;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            pawn.Map.listerBuildings.allBuildingsColonist.Where(b => b.TryGetComp<CompRitualCircle>()?.NeedsDrawing == true);

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !PotentialWorkThingsGlobal(pawn).Any();

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompRitualCircle c = t.TryGetComp<CompRitualCircle>();
            return c != null && c.NeedsDrawing && !c.NeedsChisel && !c.Running && !t.IsForbidden(pawn) && pawn.CanReserveAndReach(t, PathEndMode.OnCell, Danger.Some, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(RitualDefOf.Avir_DrawRitual, t);
    }

    public class JobDriver_DrawRitual : JobDriver
    {
        private CompRitualCircle Circle => job.targetA.Thing?.TryGetComp<CompRitualCircle>();

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => Circle == null || !Circle.NeedsDrawing || Circle.NeedsChisel);
            yield return Toils_Goto.GotoCell(job.targetA.Thing.Position, PathEndMode.OnCell);
            Toil draw = ToilMaker.MakeToil("DrawRitual");
            int intel = pawn.skills?.GetSkill(SkillDefOf.Intellectual)?.Level ?? 0;
            int art = pawn.skills?.GetSkill(SkillDefOf.Artistic)?.Level ?? 0;
            float speed = 0.4f + 0.03f * (intel + art);   // both the intellect and the hand set the pace (and the quality)
            // an Avir cuts an engraving with its own essentium claws - far faster than any chisel
            bool claws = pawn.genes?.HasActiveGene(DefDatabase<GeneDef>.GetNamedSilentFail("Avir_EssentiumClaws")) == true;
            draw.defaultCompleteMode = ToilCompleteMode.Never;
            draw.WithProgressBar(TargetIndex.A, () => Circle == null ? 0f : Circle.drawWork / Circle.DrawWorkNeeded);
            draw.tickIntervalAction = delta =>
            {
                CompRitualCircle c = Circle;
                if (c == null) { EndJobWith(JobCondition.Incompletable); return; }
                pawn.skills?.Learn(SkillDefOf.Intellectual, 0.05f * delta);
                pawn.skills?.Learn(SkillDefOf.Artistic, 0.05f * delta);
                c.drawWork += speed * delta * (c.engraveNext && claws ? CompRitualCircle.ClawEngraving : 1f);
                if (c.drawWork >= c.DrawWorkNeeded) ReadyForNextToil();
            };
            yield return draw;
            yield return Toils_General.Do(() => Circle?.FinishDrawing(pawn));
        }
    }

    /// <summary>The performer stands in the centre and holds the flow until the ritual is done.</summary>
    public class JobDriver_PerformRitual : JobDriver
    {
        private CompRitualCircle Circle => job.targetA.Thing?.TryGetComp<CompRitualCircle>();

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => Circle == null || Circle.performer != pawn);
            yield return Toils_Goto.GotoCell(job.targetA.Thing.Position, PathEndMode.OnCell);
            Toil hold = ToilMaker.MakeToil("PerformRitual");
            hold.defaultCompleteMode = ToilCompleteMode.Never;
            hold.tickIntervalAction = delta =>
            {
                if (!Circle.RitualTick(pawn, delta)) ReadyForNextToil();
            };
            hold.AddFinishAction(() => { if (Circle?.performer == pawn) Circle.End(null); });
            yield return hold;
        }
    }
}

namespace Avir.Ritualistics
{
    /// <summary>Ritual circles may not lie on one another (nor on one being built).</summary>
    public class PlaceWorker_RitualCircle : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            foreach (IntVec3 c in GenAdj.OccupiedRect(loc, rot, checkingDef.Size))
            {
                if (!c.InBounds(map)) return "The circle must lie whole on the map.";
                // the circle's whole area must be open, dry floor: no water, no walls or anything solid across it
                TerrainDef terr = c.GetTerrain(map);
                if (terr != null && (terr.IsWater || terr.passability == Traversability.Impassable)) return "A ritual circle cannot lie on water.";
                if (c.Impassable(map)) return "A ritual circle cannot be drawn through walls or anything solid.";
                foreach (Thing t in c.GetThingList(map))
                {
                    if (t == thingToIgnore || t == thing) continue;
                    BuildableDef d = t.def.entityDefToBuild ?? t.def;
                    if (d is ThingDef td && td.HasComp(typeof(CompRitualCircle))) return "Another ritual circle lies here.";
                }
            }
            return true;
        }

        /// <summary>The circle is drawn by code (its own texture is blank): show its rings while placing or moving it.</summary>
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            CompProperties_RitualCircle props = def.GetCompProperties<CompProperties_RitualCircle>();
            if (props == null) return;
            Vector3 c = center.ToVector3Shifted();
            c.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            float R = RitualCircles.Cells(props.size) / 2f * 0.96f;
            RitualSchema s = (thing as ThingWithComps)?.GetComp<CompRitualCircle>()?.schema;
            int n = s != null && s.rings.Count > 0 ? s.rings.Count : RitualCircles.Rings(props.size);
            // the ground it takes: the circle's cells, outlined
            float rad = RitualCircles.Cells(props.size) / 2f;
            Map map = Find.CurrentMap;
            var cells = GenRadial.RadialCellsAround(center, rad, true).Where(x => map == null || x.InBounds(map)).ToList();
            GenDraw.DrawFieldEdges(cells, ghostCol);
            // a faint disc under the rings, so the circle reads on any ground
            Color fill = ghostCol; fill.a = 0.18f;
            Ring(c - new Vector3(0f, 0.01f, 0f), R * 2f, MaterialPool.MatFrom(RitualTex.Disc, ShaderDatabase.Transparent, fill));
            Color col = ghostCol; col.a = 1f;
            Material m = MaterialPool.MatFrom(RitualTex.Ring, ShaderDatabase.Transparent, col);
            float core = R * 0.28f;
            Ring(c, R * 2f, m);
            for (int i = 0; i <= n - 1; i++) Ring(c, (core + (R - core) * i / n) * 2f, m);
        }

        private static void Ring(Vector3 c, float d, Material m) =>
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(c, Quaternion.identity, new Vector3(d, 1f, d)), m, 0);
    }
}

namespace Avir.Ritualistics
{
    /// <summary>An engraved circle is not deconstructed in a moment: chisel the engraving off first.</summary>
    [HarmonyLib.HarmonyPatch(typeof(Designator_Deconstruct), nameof(Designator_Deconstruct.CanDesignateThing))]
    public static class Patch_EngravingNotDeconstructed
    {
        public static void Postfix(Thing t, ref AcceptanceReport __result)
        {
            if (!__result.Accepted) return;
            CompRitualCircle c = t?.TryGetComp<CompRitualCircle>();
            if (c != null && c.engraved && c.Drawn) __result = "Level the engraving away first (a constructor's work).";
        }
    }
}

namespace Avir.Ritualistics
{
    /// <summary>Chalk is wiped away like any dirt: anyone who cleans does it.</summary>
    public class WorkGiver_EraseRitualChalk : WorkGiver_Scanner
    {
        protected virtual bool Engraved => false;
        public override PathEndMode PathEndMode => PathEndMode.OnCell;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            pawn.Map.listerBuildings.allBuildingsColonist.Where(b => Wants(b.TryGetComp<CompRitualCircle>()));

        private bool Wants(CompRitualCircle c) => c != null && c.NeedsChisel && c.engraved == Engraved;

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !PotentialWorkThingsGlobal(pawn).Any();

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompRitualCircle c = t.TryGetComp<CompRitualCircle>();
            return Wants(c) && !c.Running && !t.IsForbidden(pawn) && pawn.CanReserveAndReach(t, PathEndMode.OnCell, Danger.Some, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => JobMaker.MakeJob(RitualDefOf.Avir_ClearRitualCircle, t);
    }

    /// <summary>An engraving is levelled away like rough ground: a constructor's work.</summary>
    public class WorkGiver_LevelEngraving : WorkGiver_EraseRitualChalk
    {
        protected override bool Engraved => true;
    }

    /// <summary>Wiping the chalk off, or levelling the floor where an engraving was cut.</summary>
    public class JobDriver_ClearRitualCircle : JobDriver
    {
        private CompRitualCircle Circle => job.targetA.Thing?.TryGetComp<CompRitualCircle>();

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => Circle == null || !Circle.NeedsChisel);
            yield return Toils_Goto.GotoCell(job.targetA.Thing.Position, PathEndMode.OnCell);
            Toil work = ToilMaker.MakeToil("ClearRitualCircle");
            work.defaultCompleteMode = ToilCompleteMode.Never;
            work.WithProgressBar(TargetIndex.A, () => Circle == null ? 0f : Circle.chiselWork / Circle.ChiselWorkNeeded);
            work.WithEffect(() => Circle?.engraved == true ? EffecterDefOf.ConstructDirt : EffecterDefOf.Clean, TargetIndex.A);
            work.tickIntervalAction = delta =>
            {
                CompRitualCircle c = Circle;
                if (c == null) { EndJobWith(JobCondition.Incompletable); return; }
                bool levelling = c.engraved;
                float speed = pawn.GetStatValue(levelling ? StatDefOf.ConstructionSpeed : StatDefOf.CleaningSpeed);
                if (levelling) pawn.skills?.Learn(SkillDefOf.Construction, 0.05f * delta);
                c.chiselWork += speed * delta;
                if (c.chiselWork >= c.ChiselWorkNeeded) ReadyForNextToil();
            };
            yield return work;
            yield return Toils_General.Do(() => Circle?.FinishChisel(pawn));
        }
    }
}
