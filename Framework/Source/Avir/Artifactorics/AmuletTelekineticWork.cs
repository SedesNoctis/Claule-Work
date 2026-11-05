using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // Telekinetic helping hands (A'Shaign amulets and the junior castes' - see AmuletCasteDef
    // .telekineticHelpers). While the wearer cleans, tends plants or mines, the amulet's telekinesis
    // works more targets of the same kind nearby, at its own speed (AmuletSettingsDef.tkHelper*,
    // by the amulet's telekinesis level 1-5).
    // Telekinesis never reserves what it works on, so the hand-off is soft: the pawn (or anyone)
    // may take a target over. Cleaning and mining keep their progress in the thing itself (filth
    // thickness, rock hit points); a plant's progress is handed to the pawn's own plant work.
    // =====================================================================================
    public enum TkHelperKind { Clean, Plants, Mine }

    public class MapComponent_AmuletTelekineticWork : MapComponent
    {
        private const int Interval = 30;

        /// <summary>Progress telekinesis has put into plants (harvest/cut work) and sowing cells.</summary>
        private static readonly Dictionary<Plant, float> plantWork = new Dictionary<Plant, float>();
        private readonly Dictionary<Filth, float> filthWork = new Dictionary<Filth, float>();
        private readonly Dictionary<IntVec3, float> sowWork = new Dictionary<IntVec3, float>();

        private static AmuletSettingsDef S => AmuletSettingsDef.Main;

        public MapComponent_AmuletTelekineticWork(Map map) : base(map) { }

        /// <summary>A pawn starting its own work on a plant takes the telekinetic progress over.</summary>
        public static float TakePlantProgress(Plant p)
        {
            if (p == null || !plantWork.TryGetValue(p, out float w)) return 0f;
            plantWork.Remove(p);
            return w;
        }

        public static bool Helps(CompAvirAmulet a) =>
            a != null && a.HasCore && a.tkLevel > 0 && (a.IsMulticlass || (a.caste != null && a.caste.telekineticHelpers));

        public static int Count(TkHelperKind k, int level) =>
            Mathf.RoundToInt(At(k == TkHelperKind.Clean ? S?.tkHelperCleanCount : k == TkHelperKind.Plants ? S?.tkHelperPlantCount : S?.tkHelperMineCount, level, 0f));

        public static float Speed(TkHelperKind k, int level) =>
            At(k == TkHelperKind.Clean ? S?.tkHelperCleanSpeed : k == TkHelperKind.Plants ? S?.tkHelperPlantSpeed : S?.tkHelperMineSpeed, level, 0f);

        private static float At(List<float> list, int level, float fallback) =>
            list != null && level >= 1 && level <= list.Count ? list[level - 1] : fallback;

        public override void MapComponentTick()
        {
            if (S == null || Find.TickManager.TicksGame % Interval != 0) return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Downed || p.Drafted || p.CurJobDef == null) continue;
                TkHelperKind kind;
                JobDef j = p.CurJobDef;
                if (j == JobDefOf.Clean) kind = TkHelperKind.Clean;
                else if (j == JobDefOf.Mine) kind = TkHelperKind.Mine;
                else if (j == JobDefOf.Harvest || j == JobDefOf.HarvestDesignated || j == JobDefOf.CutPlant || j == JobDefOf.CutPlantDesignated || j == JobDefOf.Sow)
                    kind = TkHelperKind.Plants;
                else continue;
                CompAvirAmulet a = AmuletDevelopment.WornBy(p);
                if (!Helps(a) || !AmuletDevelopment.WorksFor(a, p)) continue;
                int n = Count(kind, a.tkLevel);
                float speed = Speed(kind, a.tkLevel);
                if (n <= 0 || speed <= 0f) continue;
                float reach = Mathf.Min(S.tkHelperMaxReach, a.parent.TryGetComp<Comp_Telekinesis>()?.Radius ?? S.tkHelperMaxReach);
                switch (kind)
                {
                    case TkHelperKind.Clean: Clean(p, n, speed, reach); break;
                    case TkHelperKind.Mine: Mine(p, n, speed, reach); break;
                    default:
                        if (j == JobDefOf.Sow) Sow(p, n, speed, reach);
                        else PlantWork(p, j, n, speed, reach);
                        break;
                }
            }
            if (Find.TickManager.TicksGame % 2500 == 0) Prune();
        }

        // The pawn's own job reserves a whole queue of targets (cleaning up to 15 filth, harvesting dozens of plants):
        // those are exactly what telekinesis should help with - only the one in its hands and other pawns' are left alone.
        private bool Free(Thing t, Pawn p) =>
            t != null && t.Spawned && t != p.CurJob?.targetA.Thing && !t.IsForbidden(p)
            && (!map.reservationManager.IsReservedByAnyoneOf(t, p.Faction) || map.reservationManager.ReservedBy(t, p));

        private static bool Near(Pawn p, IntVec3 c, float reach) => (c - p.Position).LengthHorizontalSquared <= reach * reach;

        // ---------------------------------------------------------------- cleaning

        private void Clean(Pawn p, int n, float speed, float reach)
        {
            float work = p.GetStatValue(StatDefOf.CleaningSpeed) * Interval * speed;
            int done = 0;
            foreach (Filth f in map.listerFilthInHomeArea.FilthInHomeArea.OfType<Filth>()
                         .Where(f => Near(p, f.Position, reach) && Free(f, p))
                         .OrderBy(f => (f.Position - p.Position).LengthHorizontalSquared).Take(n).ToList())
            {
                float need = Mathf.Max(1f, f.def.filth.cleaningWorkToReduceThickness);
                float have = (filthWork.TryGetValue(f, out float w) ? w : 0f) + work;
                while (have >= need && f.Spawned) { have -= need; f.ThinFilth(); }
                if (f.Spawned) filthWork[f] = have; else filthWork.Remove(f);
                if (Rand.Chance(0.3f)) FleckMaker.ThrowDustPuff(f.DrawPos, map, 0.6f);
                done++;
            }
        }

        // ---------------------------------------------------------------- mining

        private void Mine(Pawn p, int n, float speed, float reach)
        {
            float perTick = p.GetStatValue(StatDefOf.MiningSpeed) * speed;
            int done = 0;
            foreach (Designation d in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine).ToList())
            {
                if (done >= n) break;
                IntVec3 c = d.target.Cell;
                if (!Near(p, c, reach)) continue;
                Mineable m = c.GetFirstMineable(map);
                if (m == null || !Free(m, p) || m.Position == p.CurJob?.targetA.Cell) continue;
                done++;
                // vanilla: 80 damage (40 for built stone) a pick hit, a hit every 100 ticks / mining speed
                float dmg = (m.def.building?.isNaturalRock == true ? 0.8f : 0.4f) * perTick * Interval;
                if (m.HitPoints <= dmg)
                {
                    m.DestroyMined(p);
                    map.designationManager.TryRemoveDesignation(c, DesignationDefOf.Mine);
                    p.records?.Increment(RecordDefOf.CellsMined);
                }
                else m.TakeDamage(new DamageInfo(DamageDefOf.Mining, dmg, 0f, -1f, p));
                p.skills?.Learn(SkillDefOf.Mining, 0.07f * Interval * speed);
                if (Rand.Chance(0.4f)) FleckMaker.ThrowDustPuff(c.ToVector3Shifted(), map, 0.8f);
            }
        }

        // ---------------------------------------------------------------- plants

        private void PlantWork(Pawn p, JobDef j, int n, float speed, float reach)
        {
            bool cut = j == JobDefOf.CutPlant || j == JobDefOf.CutPlantDesignated;
            int done = 0;
            foreach (Plant pl in PlantTargets(p, cut, reach))
            {
                if (done >= n) break;
                done++;
                // (WorkDonePerTick also hands over stored progress - see the patch below - so the
                //  stored value is read after it)
                float add = JobDriver_PlantWork.WorkDonePerTick(p, pl) * Interval * speed;
                float have = (plantWork.TryGetValue(pl, out float w) ? w : 0f) + add;
                p.skills?.Learn(SkillDefOf.Plants, 0.085f * Interval * speed);
                if (have < pl.def.plant.harvestWork) { plantWork[pl] = have; continue; }
                plantWork.Remove(pl);
                Finish(p, pl);
            }
        }

        private IEnumerable<Plant> PlantTargets(Pawn p, bool cut, float reach)
        {
            DesignationDef want = cut ? DesignationDefOf.CutPlant : DesignationDefOf.HarvestPlant;
            foreach (Designation d in map.designationManager.SpawnedDesignationsOfDef(want).ToList())
                if (d.target.Thing is Plant pl && Near(p, pl.Position, reach) && Free(pl, p) && (cut || pl.HarvestableNow))
                    yield return pl;
            if (cut) yield break;
            // growing zones: ripe crops of the zone's own plant
            foreach (Zone z in map.zoneManager.AllZones)
            {
                if (!(z is Zone_Growing g)) continue;
                ThingDef crop = g.GetPlantDefToGrow();
                foreach (IntVec3 c in g.Cells)
                {
                    if (!Near(p, c, reach)) continue;
                    Plant pl = c.GetPlant(map);
                    if (pl != null && pl.def == crop && pl.HarvestableNow && pl.LifeStage == PlantLifeStage.Mature && Free(pl, p))
                        yield return pl;
                }
            }
        }

        private void Finish(Pawn p, Plant pl)
        {
            IntVec3 at = pl.Position;
            if (pl.def.plant.harvestedThingDef != null && pl.HarvestableNow)
            {
                int yield = GenMath.RoundRandom(pl.YieldNow() * p.GetStatValue(StatDefOf.PlantHarvestYield));
                if (yield > 0)
                {
                    Thing t = ThingMaker.MakeThing(pl.def.plant.harvestedThingDef);
                    t.stackCount = yield;
                    if (p.Faction != Faction.OfPlayer) t.SetForbidden(true);
                    GenPlace.TryPlaceThing(t, at, map, ThingPlaceMode.Near);
                    p.records?.Increment(RecordDefOf.PlantsHarvested);
                }
            }
            map.designationManager.RemoveAllDesignationsOn(pl);
            pl.PlantCollected(p, PlantDestructionMode.Cut);
            FleckMaker.ThrowDustPuff(at.ToVector3Shifted(), map, 0.6f);
        }

        private void Sow(Pawn p, int n, float speed, float reach)
        {
            int done = 0;
            float add = p.GetStatValue(StatDefOf.PlantWorkSpeed) * Interval * speed;
            foreach (Zone z in map.zoneManager.AllZones)
            {
                if (done >= n) break;
                if (!(z is Zone_Growing g) || !g.allowSow) continue;
                ThingDef crop = g.GetPlantDefToGrow();
                if (crop?.plant == null || (p.skills != null && p.skills.GetSkill(SkillDefOf.Plants).Level < crop.plant.sowMinSkill)) continue;
                if (!PlantUtility.GrowthSeasonNow(map, crop)) continue;
                foreach (IntVec3 c in g.Cells)
                {
                    if (done >= n) break;
                    if (!Near(p, c, reach) || c == p.CurJob?.targetA.Cell) continue;
                    if (c.GetPlant(map) != null || !PlantUtility.CanNowPlantAt(crop, c, map, false)) continue;
                    if (map.reservationManager.IsReservedByAnyoneOf(c, p.Faction)) continue;
                    done++;
                    float have = (sowWork.TryGetValue(c, out float w) ? w : 0f) + add;
                    p.skills?.Learn(SkillDefOf.Plants, 0.085f * Interval * speed);
                    if (have < crop.plant.sowWork) { sowWork[c] = have; continue; }
                    sowWork.Remove(c);
                    Plant pl = (Plant)GenSpawn.Spawn(crop, c, map);
                    pl.Growth = 0.0001f;
                    pl.sown = true;
                    FleckMaker.ThrowDustPuff(c.ToVector3Shifted(), map, 0.5f);
                }
            }
        }

        private void Prune()
        {
            foreach (Plant k in plantWork.Keys.Where(k => k == null || !k.Spawned).ToList()) plantWork.Remove(k);
            foreach (Filth k in filthWork.Keys.Where(k => k == null || !k.Spawned).ToList()) filthWork.Remove(k);
            if (sowWork.Count > 200) sowWork.Clear();
        }
    }

    /// <summary>Soft hand-off: the first tick of a pawn's own work on a plant brings the work the
    /// telekinesis already put into it.</summary>
    [HarmonyPatch(typeof(JobDriver_PlantWork), nameof(JobDriver_PlantWork.WorkDonePerTick))]
    public static class Patch_TkPlantHandOff
    {
        public static void Postfix(Plant plant, ref float __result)
        {
            float w = MapComponent_AmuletTelekineticWork.TakePlantProgress(plant);
            if (w > 0f) __result += w;
        }
    }
}
