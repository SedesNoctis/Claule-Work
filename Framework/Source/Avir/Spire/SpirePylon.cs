using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Spire
{
    /// <summary>
    /// SPIRE PYLON. Joined to the cradle by the stream network, it carries the spire further: its own dome of the
    /// barrier (one barrier with the cradle's - a shot inside any of them is inside all) and its own piece of the
    /// domain, which rises with the spire. Every pylon makes the barrier cost more to hold.
    /// </summary>
    public class CompSpirePylon : ThingComp
    {
        public BarrierDome dome;
        private CompSpireCradle cradle;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            MapComponent_StreamNetwork.Of(parent.Map)?.SetDirty();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            if (dome != null) BarrierDome.All.Remove(dome);
            dome = null;
            MapComponent_StreamNetwork.Of(map)?.SetDirty();
        }

        /// <summary>The cradle's barrier stands: hold this pylon's part of it.</summary>
        public void Hold(CompSpireCradle c)
        {
            cradle = c;
            if (dome == null || !BarrierDome.All.Contains(dome))
            {
                dome = new BarrierDome { map = parent.Map, center = parent.Position, sealsVacuum = true, group = c, hit = c.Hit };
                BarrierDome.All.Add(dome);
            }
            dome.radius = c.PylonBarrierRadius;
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (dome != null && cradle != null && BarrierDome.All.Contains(dome)) cradle.DrawDome(dome);
        }

        public override string CompInspectStringExtra()
        {
            CompSpireCradle c = parent.Spawned ? parent.Map.listerBuildings.allBuildingsColonist.Select(b => b.TryGetComp<CompSpireCradle>()).FirstOrDefault(x => x != null) : null;
            if (c == null) return "No cradle of the spire on this map.";
            bool joined = c.Pylons.Contains(this);
            return joined
                ? "Joined to the spire: barrier radius " + c.PylonBarrierRadius.ToString("0") + ", domain radius " + c.PylonDomainRadius.ToString("0") + (dome != null ? " - the barrier stands." : "")
                : "Not joined: link it to the cradle with stream wire.";
        }
    }
}

namespace Avir.Spire
{
    /// <summary>Whoever stands in the spire pylon's working seat faces its instruments (north).</summary>
    [HarmonyLib.HarmonyPatch(typeof(Verse.Pawn_RotationTracker), nameof(Verse.Pawn_RotationTracker.UpdateRotation))]
    public static class Patch_PylonSeatFacing
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<Verse.Pawn_RotationTracker, Verse.Pawn> PawnRef =
            HarmonyLib.AccessTools.FieldRefAccess<Verse.Pawn_RotationTracker, Verse.Pawn>("pawn");

        public static void Postfix(Verse.Pawn_RotationTracker __instance)
        {
            Verse.Pawn p = PawnRef(__instance);
            if (p == null || !p.Spawned || p.pather.Moving || p.Downed) return;
            var list = p.Map.thingGrid.ThingsListAtFast(p.Position);
            for (int i = 0; i < list.Count; i++)
                if (list[i].def.defName == "Avir_SpirePylon" && list[i].InteractionCell == p.Position)
                {
                    p.Rotation = Verse.Rot4.North;
                    return;
                }
        }
    }
}

namespace Avir.Spire
{
    using Verse;
    using Verse.AI;
    using RimWorld;

    /// <summary>Attuning: while the spire's barrier is raised and hurt, an attuner sits at a spire pylon's console
    /// and mends it - faster than it mends itself, at the same cost in the cores' energy.</summary>
    public class WorkGiver_MendSpireBarrier : RimWorld.WorkGiver_Scanner
    {
        public override Verse.ThingRequest PotentialWorkThingRequest => Verse.ThingRequest.ForDef(Verse.DefDatabase<Verse.ThingDef>.GetNamed("Avir_SpirePylon"));
        public override Verse.AI.PathEndMode PathEndMode => Verse.AI.PathEndMode.InteractionCell;

        public static CompSpireCradle CradleOn(Verse.Map map) => MapComponent_SpireLift.Of(map)?.Cradle;

        public static bool Hurt(CompSpireCradle c) => c != null && c.barrierOn && c.Barrier > 0f && c.barrierHp < c.Barrier * 0.98f && c.Stored > 1f;

        public override bool ShouldSkip(Verse.Pawn pawn, bool forced = false) => !Hurt(CradleOn(pawn.Map));

        public override bool HasJobOnThing(Verse.Pawn pawn, Verse.Thing t, bool forced = false)
        {
            if (t.Faction != RimWorld.Faction.OfPlayer || t.IsForbidden(pawn) || !Hurt(CradleOn(t.Map))) return false;
            return pawn.CanReserveAndReach(t, Verse.AI.PathEndMode.InteractionCell, Verse.Danger.Some, 1, -1, null, forced);
        }

        public override Verse.AI.Job JobOnThing(Verse.Pawn pawn, Verse.Thing t, bool forced = false) =>
            JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_MendSpireBarrier"), t);
    }

    public class JobDriver_MendSpireBarrier : Verse.AI.JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override System.Collections.Generic.IEnumerable<Verse.AI.Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(Verse.AI.TargetIndex.A);
            yield return Verse.AI.Toils_Goto.GotoThing(Verse.AI.TargetIndex.A, Verse.AI.PathEndMode.InteractionCell);
            var mend = ToilMaker.MakeToil("MendSpireBarrier");
            mend.defaultCompleteMode = Verse.AI.ToilCompleteMode.Never;
            mend.handlingFacing = true;
            mend.tickAction = () =>
            {
                pawn.rotationTracker.FaceCell(pawn.Position + Verse.IntVec3.North);
                if (!pawn.IsHashIntervalTick(60)) return;
                CompSpireCradle c = WorkGiver_MendSpireBarrier.CradleOn(pawn.Map);
                if (!WorkGiver_MendSpireBarrier.Hurt(c)) { EndJobWith(Verse.AI.JobCondition.Succeeded); return; }
                // a second of mending: 1.5% of the barrier, more for a sensitive mind; the cores pay half of it
                float sens = UnityEngine.Mathf.Clamp(pawn.GetStatValue(RimWorld.StatDefOf.PsychicSensitivity), 0.2f, 3f);
                float want = UnityEngine.Mathf.Min(c.Barrier - c.barrierHp, c.Barrier * 0.015f * sens);
                c.barrierHp += c.Take(want * 0.5f) * 2f;
                FleckMaker.ThrowLightningGlow(job.targetA.Thing.DrawPos, pawn.Map, 0.6f);
            };
            mend.WithEffect(RimWorld.EffecterDefOf.Research, Verse.AI.TargetIndex.A);
            yield return mend;
        }
    }
}
