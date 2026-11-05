using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.North
{
    // =====================================================================================
    // FIREPROOFING OIL - the sap of the burning forest's plants, which do not burn. Spread on a building that can burn,
    // it does not catch fire for fifteen days. Quick, unskilled work (basic). The order stays on the building, so it is
    // renewed when it wears off, while there is oil.
    // =====================================================================================

    [DefOf]
    public static class FireproofDefOf
    {
        public static ThingDef Avir_FireproofOil;
        public static DesignationDef Avir_Fireproof;
        public static JobDef Avir_ApplyFireproofing;
        static FireproofDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(FireproofDefOf)); }
    }

    public class MapComponent_Fireproofing : MapComponent
    {
        public const int Duration = 15 * 60000;
        private Dictionary<Thing, int> until = new Dictionary<Thing, int>();
        private List<Thing> tmpKeys; private List<int> tmpVals;

        public MapComponent_Fireproofing(Map map) : base(map) { }
        public static MapComponent_Fireproofing Of(Map m) => m?.GetComponent<MapComponent_Fireproofing>();

        public bool Protected(Thing t) => until.TryGetValue(t, out int u) && Find.TickManager.TicksGame < u;
        public int TicksLeft(Thing t) => until.TryGetValue(t, out int u) ? Mathf.Max(0, u - Find.TickManager.TicksGame) : 0;
        public void Apply(Thing t) => until[t] = Find.TickManager.TicksGame + Duration;

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 2500 != 1201 || until.Count == 0) return;
            var gone = new List<Thing>();
            foreach (var kv in until) if (kv.Key.Destroyed || Find.TickManager.TicksGame >= kv.Value) gone.Add(kv.Key);
            foreach (Thing t in gone) until.Remove(t);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving) until.RemoveAll(kv => kv.Key == null || kv.Key.Destroyed);
            Scribe_Collections.Look(ref until, "avirFireproof", LookMode.Reference, LookMode.Value, ref tmpKeys, ref tmpVals);
            if (until == null) until = new Dictionary<Thing, int>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) until.RemoveAll(kv => kv.Key == null);
        }
    }

    public static class Fireproofing
    {
        public static bool IsProtected(Thing t) => t?.MapHeld != null && t.def.category == ThingCategory.Building
            && MapComponent_Fireproofing.Of(t.MapHeld)?.Protected(t) == true;

        /// <summary>A building that can burn at all (before the oil).</summary>
        public static bool CanTake(Thing t) => t is Building && !(t is Plant) && t.def.useHitPoints && !t.def.building.isNaturalRock
            && t.def.GetStatValueAbstract(StatDefOf.Flammability, t.Stuff) > 0.01f;
    }

    /// <summary>Flammability 0 while the oil holds.</summary>
    public class StatPart_Fireproofed : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (req.HasThing && req.Thing.def.category == ThingCategory.Building && Fireproofing.IsProtected(req.Thing)) val = 0f;
        }

        public override string ExplanationPart(StatRequest req) =>
            req.HasThing && Fireproofing.IsProtected(req.Thing) ? "Fireproofing oil: x0 (" + MapComponent_Fireproofing.Of(req.Thing.MapHeld).TicksLeft(req.Thing).ToStringTicksToPeriod() + " left)" : null;
    }

    public class Designator_Fireproof : Designator
    {
        public Designator_Fireproof()
        {
            defaultLabel = "Fireproof";
            defaultDesc = "Mark buildings that can burn to be coated with fireproofing oil (from the plants of the burning forest). Coated, a building does not catch fire for 15 days; the mark stays, so colonists renew the coat while there is oil. Basic work, no skill needed.";
            icon = ContentFinder<Texture2D>.Get("UI/Designators/Avir_Fireproof", false) ?? BaseContent.BadTex;
            useMouseIcon = true;
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_Haul;
        }

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;
        protected override DesignationDef Designation => FireproofDefOf.Avir_Fireproof;

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map)) return false;
            foreach (Thing t in c.GetThingList(Map)) if (CanDesignateThing(t).Accepted) return true;
            return "Must designate buildings that can burn.";
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            foreach (Thing t in c.GetThingList(Map).ToArray()) if (CanDesignateThing(t).Accepted) DesignateThing(t);
        }

        public override AcceptanceReport CanDesignateThing(Thing t) =>
            Fireproofing.CanTake(t) && Map.designationManager.DesignationOn(t, Designation) == null;

        public override void DesignateThing(Thing t) => Map.designationManager.AddDesignation(new Designation(t, Designation));
    }

    public class WorkGiver_ApplyFireproofing : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            foreach (Designation d in pawn.Map.designationManager.SpawnedDesignationsOfDef(FireproofDefOf.Avir_Fireproof))
                if (d.target.HasThing) yield return d.target.Thing;
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            !pawn.Map.designationManager.AnySpawnedDesignationOfDef(FireproofDefOf.Avir_Fireproof)
            || pawn.Map.listerThings.ThingsOfDef(FireproofDefOf.Avir_FireproofOil).Count == 0;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn.Map.designationManager.DesignationOn(t, FireproofDefOf.Avir_Fireproof) == null) return false;
            MapComponent_Fireproofing fp = MapComponent_Fireproofing.Of(pawn.Map);
            if (fp != null && fp.TicksLeft(t) > 60000) return false;                       // renewed within its last day
            if (t.IsBurning() || !pawn.CanReserve(t, 1, -1, null, forced)) return false;
            return FindOil(pawn) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Thing oil = FindOil(pawn);
            if (oil == null) return null;
            Job job = JobMaker.MakeJob(FireproofDefOf.Avir_ApplyFireproofing, t, oil);
            job.count = 1;
            return job;
        }

        private static Thing FindOil(Pawn pawn) =>
            GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(FireproofDefOf.Avir_FireproofOil),
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, x => !x.IsForbidden(pawn) && pawn.CanReserve(x));
    }

    public class JobDriver_ApplyFireproofing : JobDriver
    {
        private Thing Building => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed)
            && pawn.Reserve(job.GetTarget(TargetIndex.B), job, 1, 1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Map.designationManager.DesignationOn(Building, FireproofDefOf.Avir_Fireproof) == null);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(TargetIndex.B);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, true);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil coat = Toils_General.Wait(120, TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            yield return coat;
            yield return new Toil
            {
                initAction = () =>
                {
                    MapComponent_Fireproofing.Of(Map)?.Apply(Building);
                    Thing carried = pawn.carryTracker.CarriedThing;
                    if (carried != null) { if (carried.stackCount > 1) carried.SplitOff(1).Destroy(); else carried.Destroy(); }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    /// <summary>The plants of the burning forest give fireproofing oil when they are harvested or cut.</summary>
    [HarmonyPatch(typeof(Plant), nameof(Plant.PlantCollected))]
    public static class Patch_BurningPlantsGiveOil
    {
        public static void Prefix(Plant __instance, Pawn by)
        {
            if (!__instance.Spawned || FireproofDefOf.Avir_FireproofOil == null) return;
            if (MapComponent_BurningForest.Of(__instance.Map)?.Ext == null) return;
            bool tree = __instance.def.plant.IsTree;
            if (!Rand.Chance(tree ? 0.6f : 0.15f)) return;
            int n = tree ? Mathf.Max(1, Mathf.RoundToInt(Rand.Range(1, 4) * __instance.Growth)) : 1;
            Thing oil = ThingMaker.MakeThing(FireproofDefOf.Avir_FireproofOil);
            oil.stackCount = n;
            GenPlace.TryPlaceThing(oil, __instance.Position, __instance.Map, ThingPlaceMode.Near);
        }
    }
}
