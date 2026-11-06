using System;
using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using LudeonTK;

namespace Avir.Magic
{
    /// <summary>The arcane work of the second-order meetings (Магия-второй-порядок.md). A caster takes it up who knows the
    /// three elements of the meeting; a meeting of a pair with one of its own elements (A-B-A) needs the caster to be at
    /// least an adept in that element. Each work is one entry here: what it needs, costs and teaches, what it is done to,
    /// and what it does. One scanner class and one driver serve them all, by the def's name.</summary>
    public class Work2
    {
        public SecondKind y;
        public float cost = 6f, xp = 12f;
        public int ticks = 240;
        public string desName;                                         // cells marked by an arcane order, or
        public DesignationDef des => desName == null ? null : Works2.D(desName);
        public Func<Pawn, IEnumerable<Thing>> things;                  // things it is done to
        public Func<Pawn, Thing, bool> thingOk;
        public Func<Pawn, IEnumerable<IntVec3>> cells;                 // or cells found by itself
        public Func<Pawn, IntVec3, bool> cellOk;
        public Func<Pawn, LocalTargetInfo, Hediff_ArcaneCore, bool> effect;
    }

    public static class Works2
    {
        public static RuneKind[] Kinds(SecondKind y)
        {
            RuneKind[] pr = Combos.Parts(SecondOrder.PairOf(y));
            RuneKind c = SecondOrder.Third(y);
            return pr.Contains(c) ? pr : pr.Concat(new[] { c }).ToArray();
        }

        /// <summary>Able: knows every element of the meeting; for A-B-A at least an adept in the doubled one; Arcana to pay.</summary>
        public static Hediff_ArcaneCore Able(Pawn p, SecondKind y, float cost)
        {
            // the author (Fix152): the second order from Novice too, like the meetings - at a risk, and from the stage set
            // on the Path page ("Second", lava at least as magma)
            if (!AvirMagic.On || p == null) return null;
            Hediff_ArcaneCore c = Mages.Core(p);
            RuneKind[] ks = Kinds(y);
            return c != null && c.arcana >= cost && ks.All(k => MagicWork.Knows(c, k)) && ArcaneWork.StageOk(c, ks, true) ? c : null;
        }

        public static DesignationDef D(string n) => DefDatabase<DesignationDef>.GetNamedSilentFail(n);
        private static IEnumerable<IntVec3> Marked(Pawn p, string d) => D(d) == null ? Enumerable.Empty<IntVec3>() : p.Map.designationManager.SpawnedDesignationsOfDef(D(d)).Select(x => x.target.Cell).ToList();
        private static bool Home(Pawn p, IntVec3 c) => p.Map.areaManager.Home[c];
        private static void Unmark(Map m, IntVec3 c, string d) { if (D(d) != null) m.designationManager.TryRemoveDesignation(c, D(d)); }
        private static void Spawn(ThingDef def, int count, IntVec3 at, Map map) { if (def == null) return; Thing t = ThingMaker.MakeThing(def); t.stackCount = Mathf.Min(count, def.stackLimit); GenPlace.TryPlaceThing(t, at, map, ThingPlaceMode.Near); }
        private static void Fx(Map m, IntVec3 c, Color col, float s = 1.2f) => FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), m, s, col);

        /// <summary>Clay: Medieval Overhaul's, if it is there; else our own.</summary>
        public static ThingDef Clay => clay ?? (clay = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d => d.category == ThingCategory.Item && d.defName.IndexOf("Clay", StringComparison.OrdinalIgnoreCase) >= 0
                                                    && d.modContentPack?.PackageId?.IndexOf("medieval", StringComparison.OrdinalIgnoreCase) >= 0)
                                                ?? DefDatabase<ThingDef>.GetNamedSilentFail("Avir_Clay"));
        private static ThingDef clay;
        public static ThingDef Def(string n) => DefDatabase<ThingDef>.GetNamedSilentFail(n);

        // a day between two works on the same spot, where it says so
        private static readonly Dictionary<int, int> lastAt = new Dictionary<int, int>();
        private static bool Rested(Map m, IntVec3 c, string k, int ticks) { int key = (k + c + m.uniqueID).GetHashCode(); return !lastAt.TryGetValue(key, out int t) || Find.TickManager.TicksGame - t > ticks; }
        private static void Stamp(Map m, IntVec3 c, string k) => lastAt[(k + c + m.uniqueID).GetHashCode()] = Find.TickManager.TicksGame;

        public static readonly Dictionary<string, Work2> All = new Dictionary<string, Work2>
        {
            // ---- steam
            ["Avir_2Cremate"] = new Work2 { y = SecondKind.Inferno, cost = 4f,
                things = p => p.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).Where(t => Home(p, t.Position) && !t.IsInAnyStorage()),
                thingOk = (p, t) => t is Corpse,
                effect = (p, t, c) => { if (!(t.Thing is Corpse k) || k.Destroyed) return false; IntVec3 at = k.Position; k.Destroy(); FilthMaker.TryMakeFilth(at, p.Map, ThingDefOf.Filth_Ash); FleckMaker.ThrowHeatGlow(at, p.Map, 1.5f); return true; } },
            ["Avir_2Purify"] = new Work2 { y = SecondKind.Boil, cost = 3f, ticks = 180,
                things = p => p.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree),
                thingOk = (p, t) => (t.TryGetComp<CompFoodPoisonable>()?.PoisonPercent ?? 0f) > 0f,
                effect = (p, t, c) =>
                {
                    int n = 0;
                    foreach (Thing x in GenRadial.RadialDistinctThingsAround(t.Cell, p.Map, WorkSpells.Now.R(2.9f), true).ToList())
                    {
                        CompFoodPoisonable fp = x.TryGetComp<CompFoodPoisonable>();
                        if (fp == null || fp.PoisonPercent <= 0f) continue;
                        Traverse.Create(fp).Field("poisonPct").SetValue(0f); n++;
                    }
                    FleckMaker.ThrowSmoke(t.CenterVector3, p.Map, 1f);
                    return n > 0;
                } },
            ["Avir_2RaiseGeyser"] = new Work2 { y = SecondKind.Geyser, cost = 40f, xp = 40f, ticks = 900, desName = "Avir_2RaiseGeyser",
                effect = (p, t, c) =>
                {
                    Map m = p.Map; IntVec3 at = t.Cell;
                    if (!GenAdj.OccupiedRect(at, Rot4.North, ThingDefOf.SteamGeyser.size).All(x => x.InBounds(m) && ArcaneWorks.Clear(m, x) && x.Standable(m))) return false;
                    GenSpawn.Spawn(ThingDefOf.SteamGeyser, at, m); Unmark(m, at, "Avir_2RaiseGeyser"); Gas_ScaldingSteam.Release(at, m, 3); return true;
                } },
            ["Avir_2SealGeyser"] = new Work2 { y = SecondKind.Geyser, cost = 20f, xp = 20f, ticks = 600, desName = "Avir_2SealGeyser",
                effect = (p, t, c) =>
                {
                    Map m = p.Map; Thing g = m.listerThings.ThingsOfDef(ThingDefOf.SteamGeyser).FirstOrDefault(x => x.OccupiedRect().Contains(t.Cell));
                    Unmark(m, t.Cell, "Avir_2SealGeyser");
                    if (g == null || m.thingGrid.ThingsListAt(g.Position).Any(x => x is Building && x != g)) return false;
                    g.Destroy(); Fx(m, t.Cell, MagicFx.EarthDust, 2f); return true;
                } },
            ["Avir_2Rinse"] = new Work2 { y = SecondKind.Typhoon, cost = 6f,
                things = p => p.Map.listerFilthInHomeArea.FilthInHomeArea,
                thingOk = (p, t) => GenRadial.RadialDistinctThingsAround(t.Position, p.Map, 6f, true).Count(x => x is Filth) >= 8,
                effect = (p, t, c) =>
                {
                    int n = 0;
                    foreach (IntVec3 x in GenRadial.RadialCellsAround(t.Cell, WorkSpells.Now.R(7.9f), true).Where(x => x.InBounds(p.Map)))
                    {
                        foreach (Filth f in x.GetThingList(p.Map).OfType<Filth>().ToList()) { f.Destroy(); n++; }
                        Plant pl = x.GetPlant(p.Map); if (pl != null && pl.LifeStage == PlantLifeStage.Growing) pl.Growth = Mathf.Min(1f, pl.Growth + 0.05f);
                    }
                    return n > 0;
                } },
            // ---- magma
            ["Avir_2SmeltItems"] = new Work2 { y = SecondKind.Lava, desName = "Avir_2SmeltItems",
                effect = (p, t, c) =>
                {
                    Map m = p.Map; int n = 0;
                    foreach (Thing x in t.Cell.GetThingList(m).Where(x => x.def.category == ThingCategory.Item && x.def.smeltable).ToList())
                    { foreach (Thing pr in x.SmeltProducts(1f)) GenPlace.TryPlaceThing(pr, t.Cell, m, ThingPlaceMode.Near); x.Destroy(); n++; }
                    if (n == 0) Unmark(m, t.Cell, "Avir_2SmeltItems");
                    FleckMaker.ThrowFireGlow(t.CenterVector3, m, 1.2f);
                    return n > 0;
                } },
            ["Avir_2LavaCore"] = new Work2 { y = SecondKind.Lava, cost = 12f, ticks = 400, desName = "Avir_2LavaCore",
                effect = (p, t, c) => { Spawn(Def("Avir_LavaCore"), 1, t.Cell, p.Map); Unmark(p.Map, t.Cell, "Avir_2LavaCore"); FleckMaker.ThrowFireGlow(t.CenterVector3, p.Map, 1.5f); return true; } },
            ["Avir_2Basalt"] = new Work2 { y = SecondKind.Volcano, cost = 8f, ticks = 300, desName = "Avir_2Basalt",
                effect = (p, t, c) => { Spawn(Def("Avir_ChunkBasalt"), 1, t.Cell, p.Map); Spawn(Def("Avir_ChunkBasalt"), 1, t.Cell, p.Map); Unmark(p.Map, t.Cell, "Avir_2Basalt"); Fx(p.Map, t.Cell, MagicFx.EarthDust, 1.6f); return true; } },
            ["Avir_2Obsidian"] = new Work2 { y = SecondKind.Obsidian, cost = 10f, ticks = 360, desName = "Avir_2Obsidian",
                effect = (p, t, c) => { Spawn(Def("Avir_ObsidianChunk"), 1 + (WorkSpells.Now.amplify > 0 ? 1 : 0), t.Cell, p.Map); Unmark(p.Map, t.Cell, "Avir_2Obsidian"); Gas_ScaldingSteam.Release(t.Cell, p.Map, 1); return true; } },
            ["Avir_2Ash"] = new Work2 { y = SecondKind.Ash, cost = 6f, desName = "Avir_2Ash",
                effect = (p, t, c) =>
                {
                    Map m = p.Map; var temp = MapComponent_TempTerrain.Of(m); int n = 0;
                    foreach (IntVec3 x in GenRadial.RadialCellsAround(t.Cell, WorkSpells.Now.R(2.9f), true).Where(x => x.InBounds(m) && m.designationManager.DesignationAt(x, D("Avir_2Ash")) != null))
                    {
                        Unmark(m, x, "Avir_2Ash");
                        TerrainDef tr = m.terrainGrid.TerrainAt(x);
                        if (tr.fertility <= 0f || tr.IsWater) continue;
                        MapComponent_LifeFertility.Of(m)?.Add(x, SecondOrder.AshBonus, SecondOrder.AshDays); n++;
                        if (Rand.Chance(0.3f)) FleckMaker.ThrowSmoke(x.ToVector3Shifted(), m, 0.8f);
                    }
                    return n > 0;
                } },
            // ---- storm
            ["Avir_2Water"] = new Work2 { y = SecondKind.Deluge, cost = 5f,
                cells = p => p.Map.zoneManager.AllZones.OfType<Zone_Growing>().SelectMany(z => z.Cells).Where(x => (x.GetPlant(p.Map)?.LifeStage ?? PlantLifeStage.Mature) == PlantLifeStage.Growing).ToList(),
                cellOk = (p, x) => Rested(p.Map, x, "water", GenDate.TicksPerDay),
                effect = (p, t, c) =>
                {
                    Map m = p.Map; int n = 0;
                    foreach (IntVec3 x in GenRadial.RadialCellsAround(t.Cell, WorkSpells.Now.R(5.9f), true).Where(x => x.InBounds(m)))
                    {
                        Plant pl = x.GetPlant(m);
                        if (pl == null || pl.LifeStage != PlantLifeStage.Growing || !Rested(m, x, "water", GenDate.TicksPerDay)) continue;
                        pl.Growth = Mathf.Min(1f, pl.Growth + 0.06f * WorkSpells.Now.S); Stamp(m, x, "water"); n++;
                        if (Rand.Chance(0.2f)) FleckMaker.Static(x, m, FleckDefOf.WaterSplash, 0.6f);
                    }
                    return n > 0;
                } },
            ["Avir_2Wind"] = new Work2 { y = SecondKind.Hurricane, cost = 6f,
                things = p => p.Map.listerBuildings.allBuildingsColonist.Where(b => b.TryGetComp<CompPowerPlantWind>() != null),
                thingOk = (p, t) => !WindBoost.On(t),
                effect = (p, t, c) => { WindBoost.Set(t.Thing, Mathf.RoundToInt(GenDate.TicksPerHour * 8f * WorkSpells.Now.S)); Fx(p.Map, t.Cell, MagicFx.AirDust, 2f); return true; } },
            ["Avir_2Charge"] = new Work2 { y = SecondKind.BallLightning, cost = 8f,
                things = p => p.Map.listerBuildings.allBuildingsColonist.Where(b => b.TryGetComp<CompPowerBattery>() != null),
                thingOk = (p, t) => t.TryGetComp<CompPowerBattery>().StoredEnergyPct < 0.7f,
                effect = (p, t, c) => { t.Thing.TryGetComp<CompPowerBattery>().AddEnergy(600f * WorkSpells.Now.S); FleckMaker.ThrowLightningGlow(t.Thing.DrawPos, p.Map, 2f); return true; } },
            ["Avir_2Clear"] = new Work2 { y = SecondKind.Mudflow, desName = "Avir_2Clear",
                effect = (p, t, c) =>
                {
                    Map m = p.Map; int n = 0, wood = 0;
                    foreach (IntVec3 x in GenRadial.RadialCellsAround(t.Cell, WorkSpells.Now.R(3.9f), true).Where(x => x.InBounds(m) && m.designationManager.DesignationAt(x, D("Avir_2Clear")) != null))
                    {
                        Unmark(m, x, "Avir_2Clear");
                        Plant pl = x.GetPlant(m);
                        if (pl == null) continue;
                        if (pl.def.plant.harvestedThingDef == ThingDefOf.WoodLog && pl.CanYieldNow()) wood += pl.YieldNow();
                        pl.Destroy(); n++;
                        if (Rand.Chance(0.4f)) Fx(m, x, new Color(0.45f, 0.35f, 0.25f, 0.6f));
                    }
                    if (wood > 0) Spawn(ThingDefOf.WoodLog, wood, t.Cell, m);
                    return n > 0;
                } },
            // ---- haze
            ["Avir_2HeatRoom"] = new Work2 { y = SecondKind.Swelter, cost = 8f,
                cells = p => p.Map.regionGrid.AllRooms.Where(r => !r.UsesOutdoorTemperature && r.ProperRoom && r.Temperature < 12f && !RoomHeat.Has(r)).Select(r => r.Cells.FirstOrDefault(x => x.Standable(p.Map))).Where(x => x.IsValid).ToList(),
                cellOk = (p, x) => x.GetRoom(p.Map) is Room r && !RoomHeat.Has(r),
                effect = (p, t, c) => { Room r = t.Cell.GetRoom(p.Map); if (r == null) return false; RoomHeat.Add(p.Map, r, Mathf.RoundToInt(GenDate.TicksPerHour * 10f * WorkSpells.Now.S)); FleckMaker.ThrowHeatGlow(t.Cell, p.Map, 2f); return true; } },
            ["Avir_2Vent"] = new Work2 { y = SecondKind.Thermal, cost = 4f, ticks = 180,
                cells = p => p.Map.regionGrid.AllRooms.Where(r => !r.UsesOutdoorTemperature && r.ProperRoom && (r.Temperature > 32f || r.ContainedAndAdjacentThings.Any(x => x is Gas))).Select(r => r.Cells.FirstOrDefault(x => x.Standable(p.Map))).Where(x => x.IsValid).ToList(),
                effect = (p, t, c) =>
                {
                    Room r = t.Cell.GetRoom(p.Map); if (r == null) return false;
                    foreach (Thing g in r.ContainedAndAdjacentThings.Where(x => x is Gas).ToList()) g.Destroy();
                    if (r.Temperature > 32f) r.Temperature = Mathf.Max(21f, r.Temperature - 15f * WorkSpells.Now.S);
                    return true;
                } },
            ["Avir_2Rainbow"] = new Work2 { y = SecondKind.Rainbow, cost = 5f, ticks = 200,
                cells = p => Rested(p.Map, IntVec3.Zero, "rainbow", GenDate.TicksPerDay) && p.Map.mapPawns.FreeColonistsSpawned.Any(q => (q.needs?.joy?.CurLevel ?? 1f) < 0.5f) ? new List<IntVec3> { p.Position } : new List<IntVec3>(),
                effect = (p, t, c) =>
                {
                    Stamp(p.Map, IntVec3.Zero, "rainbow");
                    ThoughtDef seen = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_SawRainbow");
                    foreach (Pawn q in p.Map.mapPawns.FreeColonistsSpawned.Where(q => q.Position.DistanceTo(p.Position) < 40f))
                    { if (seen != null) q.needs?.mood?.thoughts?.memories?.TryGainMemory(seen); if (q.needs?.joy != null) q.needs.joy.CurLevel = Mathf.Min(1f, q.needs.joy.CurLevel + 0.15f); }
                    return true;
                } },
            ["Avir_2Dry"] = new Work2 { y = SecondKind.DryWind, cost = 4f, ticks = 180,
                things = p => p.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Where(t => t.IsInAnyStorage()),
                thingOk = (p, t) => t.TryGetComp<CompRottable>() is CompRottable r && r.RotProgressPct > 0.3f && r.RotProgressPct < 1f,
                effect = (p, t, c) =>
                {
                    int n = 0;
                    foreach (Thing x in GenRadial.RadialDistinctThingsAround(t.Cell, p.Map, WorkSpells.Now.R(3.9f), true).ToList())
                    { CompRottable r = x.TryGetComp<CompRottable>(); if (r == null || r.RotProgress <= 0f) continue; r.RotProgress = r.RotProgress * 0.7f; n++; }
                    return n > 0;
                } },
            // ---- bog
            ["Avir_2Moat"] = new Work2 { y = SecondKind.Quag, cost = 4f, ticks = 200, desName = "Avir_2Moat",
                effect = (p, t, c) => TempTo(p.Map, t.Cell, "Avir_2Moat", TerrainDefOf.WaterShallow, 30) },
            ["Avir_2SiltWater"] = new Work2 { y = SecondKind.Silt, cost = 4f, ticks = 200, desName = "Avir_2SiltWater",
                effect = (p, t, c) => TempTo(p.Map, t.Cell, "Avir_2SiltWater", TerrainDefOf.WaterShallow, 30) },
            ["Avir_2SiltLand"] = new Work2 { y = SecondKind.Silt, cost = 4f, ticks = 200, desName = "Avir_2SiltLand",
                effect = (p, t, c) => TempTo(p.Map, t.Cell, "Avir_2SiltLand", TerrainDefOf.Soil, 30) },
            ["Avir_2SiltDump"] = new Work2 { y = SecondKind.Silt, cost = 3f, ticks = 150, desName = "Avir_2SiltDump",
                cellOk = (p, x) => x.GetThingList(p.Map).Any(t => t.def.category == ThingCategory.Item || t is Corpse),
                effect = (p, t, c) =>
                {
                    int n = 0;
                    foreach (Thing x in t.Cell.GetThingList(p.Map).Where(x => x.def.category == ThingCategory.Item || x is Corpse).ToList()) { x.Destroy(); n++; }
                    Fx(p.Map, t.Cell, new Color(0.45f, 0.4f, 0.3f, 0.6f));
                    return n > 0;
                } },
            ["Avir_2MarshGas"] = new Work2 { y = SecondKind.MarshGas, cost = 6f, ticks = 300, desName = "Avir_2MarshGas",
                cellOk = (p, x) => Rested(p.Map, x, "gas", GenDate.TicksPerDay / 2),
                effect = (p, t, c) => { Stamp(p.Map, t.Cell, "gas"); Spawn(Def("Avir_MarshGas"), 1 + (WorkSpells.Now.amplify > 0 ? 1 : 0), t.Cell, p.Map); return true; } },
            ["Avir_2Preserve"] = new Work2 { y = SecondKind.MarshHaze, cost = 6f,
                things = p => p.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Where(t => t.IsInAnyStorage()).Concat(p.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)),
                thingOk = (p, t) => t.TryGetComp<CompRottable>() is CompRottable r && r.RotProgressPct > 0.5f && r.RotProgressPct < 1f,
                effect = (p, t, c) =>
                {
                    int n = 0;
                    foreach (Thing x in GenRadial.RadialDistinctThingsAround(t.Cell, p.Map, WorkSpells.Now.R(5.9f), true).ToList())
                    { CompRottable r = x is Corpse k ? k.GetComp<CompRottable>() : x.TryGetComp<CompRottable>(); if (r == null || r.RotProgress <= 0f || r.RotProgressPct >= 1f) continue; r.RotProgress = 0f; n++; }
                    for (int i = 0; i < 6; i++) FleckMaker.ThrowSmoke((t.Cell + GenRadial.RadialPattern[Rand.Range(0, 20)]).ToVector3Shifted(), p.Map, 1.2f);
                    return n > 0;
                } },
            // ---- dust
            ["Avir_2Quarry"] = new Work2 { y = SecondKind.Rockfall, cost = 8f, ticks = 300, desName = "Avir_2Quarry",
                effect = (p, t, c) =>
                {
                    Map m = p.Map; int n = 0;
                    foreach (IntVec3 x in GenRadial.RadialCellsAround(t.Cell, WorkSpells.Now.R(1.9f), true).Where(x => x.InBounds(m) && m.designationManager.DesignationAt(x, D("Avir_2Quarry")) != null))
                    {
                        Unmark(m, x, "Avir_2Quarry");
                        Building rock = x.GetEdifice(m);
                        if (rock == null || rock.def.building?.isNaturalRock != true) continue;
                        ThingDef chunk = rock.def.building.mineableThing;
                        rock.Destroy(DestroyMode.KillFinalize);
                        if (chunk != null && x.GetThingList(m).All(q => q.def != chunk)) GenSpawn.Spawn(chunk, x, m);
                        Fx(m, x, MagicFx.EarthDust, 1.4f); n++;
                    }
                    return n > 0;
                } },
            ["Avir_2Hone"] = new Work2 { y = SecondKind.Emery, cost = 3f, ticks = 120,
                things = p => p.Map.mapPawns.FreeColonistsSpawned.Where(q => q.equipment?.Primary?.def.IsMeleeWeapon == true),
                thingOk = (p, t) => t is Pawn q && q != p && !q.health.hediffSet.HasHediff(SecondOrder.H("Avir_Honed")) && q.Position.DistanceTo(p.Position) < 30f,
                effect = (p, t, c) => { if (!(t.Thing is Pawn q)) return false; MagicFx.Give(q, SecondOrder.H("Avir_Honed"), GenDate.TicksPerDay); return true; } },
            ["Avir_2Bricks"] = new Work2 { y = SecondKind.BurningSand, cost = 5f,
                things = p => Clay == null ? Enumerable.Empty<Thing>() : p.Map.listerThings.ThingsOfDef(Clay).Where(t => t.IsInAnyStorage() || Home(p, t.Position)),
                thingOk = (p, t) => t.stackCount >= 10,
                effect = (p, t, c) =>
                {
                    Thing k = t.Thing; if (k == null || k.Destroyed || k.stackCount < 10) return false;
                    int n = Mathf.Min(k.stackCount, 25 + 25 * WorkSpells.Now.breadth); IntVec3 at = k.Position; Map m = k.Map;
                    k.SplitOff(n).Destroy(); Spawn(Def("Avir_Brick"), n, at, m); FleckMaker.ThrowFireGlow(at.ToVector3Shifted(), m, 1.2f); return true;
                } },
            ["Avir_2DigClay"] = new Work2 { y = SecondKind.Clay, cost = 5f, ticks = 300, desName = "Avir_2DigClay",
                cellOk = (p, x) => Rested(p.Map, x, "clay", GenDate.TicksPerDay / 2),
                effect = (p, t, c) => { Stamp(p.Map, t.Cell, "clay"); Spawn(Clay, 10 + 5 * WorkSpells.Now.amplify, t.Cell, p.Map); Fx(p.Map, t.Cell, new Color(0.7f, 0.45f, 0.3f, 0.6f)); return true; } },
        };

        private static bool TempTo(Map m, IntVec3 at, string des, TerrainDef to, int days)
        {
            var temp = MapComponent_TempTerrain.Of(m); int n = 0;
            foreach (IntVec3 x in GenRadial.RadialCellsAround(at, WorkSpells.Now.R(1.9f), true).Where(x => x.InBounds(m) && m.designationManager.DesignationAt(x, D(des)) != null))
            {
                Unmark(m, x, des);
                if (x.GetEdifice(m) != null || x.GetFirstPawn(m) != null) continue;
                if (to.IsWater) { var tr = m.terrainGrid.TerrainAt(x); if (tr.IsWater) continue; }
                else if (!m.terrainGrid.TerrainAt(x).IsWater) continue;
                ForceTemp(temp, m, x, to, GenDate.TicksPerDay * days); n++;
                if (Rand.Chance(0.3f)) FleckMaker.Static(x, m, FleckDefOf.WaterSplash, 0.6f);
            }
            return n > 0;
        }

        /// <summary>Temp terrain that may cross water (the trail of an aura may not; a work of silt may).</summary>
        private static void ForceTemp(MapComponent_TempTerrain temp, Map m, IntVec3 c, TerrainDef def, int ticks)
        {
            if (temp == null) return;
            TerrainDef was = m.terrainGrid.TerrainAt(c);
            if (!temp.from.ContainsKey(c)) temp.from[c] = was;
            temp.to[c] = def;
            temp.until[c] = Find.TickManager.TicksGame + ticks;
            m.terrainGrid.SetTerrain(c, def);
        }
    }

    /// <summary>Hurricane's work: a wind turbine gets a steady strong wind for hours - twice the output.</summary>
    public static class WindBoost
    {
        private static readonly Dictionary<Thing, int> until = new Dictionary<Thing, int>();
        public static bool On(Thing t) => t != null && until.TryGetValue(t, out int u) && u > Find.TickManager.TicksGame;
        public static void Set(Thing t, int ticks) => until[t] = Find.TickManager.TicksGame + ticks;
    }

    [HarmonyPatch(typeof(CompPowerTrader), "get_PowerOutput")]
    public static class Patch_WindBoost
    {
        public static void Postfix(CompPowerTrader __instance, ref float __result)
        {
            if (__result > 0f && __instance is CompPowerPlantWind && WindBoost.On(__instance.parent)) __result *= 2f;
        }
    }

    /// <summary>Swelter's work: a knot of heat that keeps a room warm for hours, with no fuel.</summary>
    public class RoomHeat : MapComponent
    {
        private readonly List<KeyValuePair<Room, int>> knots = new List<KeyValuePair<Room, int>>();
        public RoomHeat(Map map) : base(map) { }
        public static bool Has(Room r) => r?.Map?.GetComponent<RoomHeat>()?.knots.Any(k => k.Key == r && k.Value > Find.TickManager.TicksGame) == true;
        public static void Add(Map m, Room r, int ticks) => m.GetComponent<RoomHeat>()?.knots.Add(new KeyValuePair<Room, int>(r, Find.TickManager.TicksGame + ticks));
        public override void MapComponentTick()
        {
            if (knots.Count == 0 || Find.TickManager.TicksGame % 250 != 0) return;
            knots.RemoveAll(k => k.Value <= Find.TickManager.TicksGame || k.Key == null || k.Key.Dereferenced);
            foreach (var k in knots) if (k.Key.Temperature < 21f) k.Key.Temperature = Mathf.Min(21f, k.Key.Temperature + 3f);
        }
    }

    /// <summary>Marsh gas is a fuel too, and a stronger one: wherever chemfuel feeds a thing, a canister of it gives two.</summary>
    [StaticConstructorOnStartup]
    public static class Works2Fuel
    {
        static Works2Fuel()
        {
            ThingDef gas = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_MarshGas"), core = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_LavaCore");
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                CompProperties_Refuelable r = d.GetCompProperties<CompProperties_Refuelable>();
                if (r?.fuelFilter == null) continue;
                if (gas != null && r.fuelFilter.Allows(ThingDefOf.Chemfuel)) r.fuelFilter.SetAllow(gas, true);
                if (core != null && r.fuelFilter.Allows(ThingDefOf.WoodLog)) r.fuelFilter.SetAllow(core, true);
            }
        }
    }

    [HarmonyPatch(typeof(CompRefuelable), nameof(CompRefuelable.Refuel), new[] { typeof(List<Thing>) })]
    public static class Patch_StrongFuel
    {
        public static void Prefix(List<Thing> fuelThings, out int __state)
        {
            __state = fuelThings?.Where(t => t.def.defName == "Avir_MarshGas" || t.def.defName == "Avir_LavaCore").Sum(t => t.Destroyed ? 0 : t.stackCount * (t.def.defName == "Avir_LavaCore" ? 3 : 1)) ?? 0;
        }
        public static void Postfix(CompRefuelable __instance, List<Thing> fuelThings, int __state)
        {
            if (__state <= 0) return;
            int left = fuelThings.Where(t => t.def.defName == "Avir_MarshGas" || t.def.defName == "Avir_LavaCore").Sum(t => t.Destroyed ? 0 : t.stackCount * (t.def.defName == "Avir_LavaCore" ? 3 : 1));
            int used = __state - left;
            if (used > 0) __instance.Refuel((float)used);   // one more unit for each canister (a lava core: three more)
        }
    }

    // ================================================================== scanner and driver for all of them

    public class WorkGiver_Arcane2 : WorkGiver_Scanner
    {
        private Work2 W => Works2.All.TryGetValue(def.defName, out Work2 w) ? w : null;
        public override PathEndMode PathEndMode => PathEndMode.Touch;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Undefined);
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => W == null || Works2.Able(pawn, W.y, W.cost) == null;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => W?.things?.Invoke(pawn) ?? Enumerable.Empty<Thing>();
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn)
        {
            Work2 w = W;
            if (w == null) return Enumerable.Empty<IntVec3>();
            if (w.des != null) return pawn.Map.designationManager.SpawnedDesignationsOfDef(w.des).Select(x => x.target.Cell).ToList();
            return w.cells?.Invoke(pawn) ?? Enumerable.Empty<IntVec3>();
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Work2 w = W;
            return w?.things != null && !t.IsForbidden(pawn) && (w.thingOk == null || w.thingOk(pawn, t)) && pawn.CanReserve(t, 1, -1, null, forced) && Works2.Able(pawn, w.y, w.cost) != null;
        }

        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false)
        {
            Work2 w = W;
            if (w == null || (w.des == null && w.cells == null) || c.IsForbidden(pawn) || !pawn.CanReserve(c, 1, -1, null, forced) || Works2.Able(pawn, w.y, w.cost) == null) return false;
            if (w.des != null && pawn.Map.designationManager.DesignationAt(c, w.des) == null) return false;
            return w.cellOk == null || w.cellOk(pawn, c);
        }

        private Job Make(LocalTargetInfo t) => JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(def.defName), t);
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => Make(t);
        public override Job JobOnCell(Pawn pawn, IntVec3 cell, bool forced = false) => Make(cell);
    }

    public class JobDriver_Arcane2 : JobDriver
    {
        private Work2 W => Works2.All.TryGetValue(job.def.defName, out Work2 w) ? w : null;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Work2 w = W;
            if (w == null) yield break;
            RuneKind[] kinds = Works2.Kinds(w.y);
            // a household spell of the three elements works it better, as for any work (WorkSpells)
            WorkCast wc = WorkSpells.Make(Mages.Core(pawn), job.def.defName, kinds, w.cost);
            bool far = kinds.Contains(RuneKind.Earth);
            if (job.targetA.HasThing) this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return WorkSpells.GoTo(this, far, wc.Range);
            Color tint = SecondOrder.ColorOf(w.y, SecondOrder.PairOf(w.y), SecondOrder.Third(w.y)); tint.a = 0.55f;
            yield return ArcaneChannel.Channel(this, wc.FromBook ? w.ticks * 3 / 4 : w.ticks, wc.spell ?? WorkSpells.Charm(job.def.defName, kinds), far, tint);
            Toil done = ToilMaker.MakeToil("Arcane2");
            done.initAction = () =>
            {
                Hediff_ArcaneCore c = Works2.Able(pawn, w.y, wc.cost);
                if (c == null) return;
                WorkSpells.Now = wc;
                bool ok;
                try { ok = w.effect(pawn, job.targetA, c); }
                finally { WorkSpells.Now = new WorkCast(); }
                if (ok) Arcane.Done(c, wc.cost, w.xp * (wc.FromBook ? 1.5f : 1f), kinds);
            };
            done.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return done;
        }
    }

    // ================================================================== the arcane orders of the second order

    public abstract class Designator_Arcane2 : Designator_Arcane
    {
        protected abstract string Key { get; }
        protected abstract string Label2 { get; }
        protected abstract string Desc2 { get; }
        protected virtual string Icon2 => "UI/Designators/SmoothSurface";
        protected Designator_Arcane2() { icon = ArcaneIcons.For(Key, Icon2); }
        public override string Label => Label2;
        public override string Desc => Desc2;
        protected override DesignationDef Designation => Works2.D(Key);
        public override AcceptanceReport CanDesignateCell(IntVec3 c) => c.InBounds(Map) && !c.Fogged(Map) && Designation != null && Map.designationManager.DesignationAt(c, Designation) == null && Ok(c);
        protected virtual AcceptanceReport Ok(IntVec3 c) => true;
    }

    public class Designator_2RaiseGeyser : Designator_Arcane2 { protected override string Key => "Avir_2RaiseGeyser"; protected override string Label2 => "Raise a geyser (geyser)"; protected override string Desc2 => "A caster of fire, water and earth calls a steam geyser up here - for a geothermal generator. Costly, slow."; protected override AcceptanceReport Ok(IntVec3 c) => c.Standable(Map) && c.GetEdifice(Map) == null; }
    public class Designator_2SealGeyser : Designator_Arcane2 { protected override string Key => "Avir_2SealGeyser"; protected override string Label2 => "Seal a geyser (geyser)"; protected override string Desc2 => "A caster of fire, water and earth seals the steam geyser here for good."; protected override AcceptanceReport Ok(IntVec3 c) => Map.listerThings.ThingsOfDef(ThingDefOf.SteamGeyser).Any(g => g.OccupiedRect().Contains(c)); }
    public class Designator_2SmeltItems : Designator_Arcane2 { protected override string Key => "Avir_2SmeltItems"; protected override string Label2 => "Smelt here (lava)"; protected override string Desc2 => "Weapons and apparel left on these cells are smelted down to their metal by a caster of lava (fire, earth; an adept of fire). The mark stays until the cell is empty."; }
    public class Designator_2LavaCore : Designator_Arcane2 { protected override string Key => "Avir_2LavaCore"; protected override string Label2 => "Seal a lava core (lava)"; protected override string Desc2 => "A caster of lava seals a core of lava in basalt here: a long fuel for any wood-fed forge, smelter or stove (three units of wood each)."; }
    public class Designator_2Basalt : Designator_Arcane2 { protected override string Key => "Avir_2Basalt"; protected override string Label2 => "Draw basalt (volcano)"; protected override string Desc2 => "A caster of the volcano (fire, earth; an adept of earth) draws two chunks of basalt up here."; }
    public class Designator_2Obsidian : Designator_Arcane2 { protected override string Key => "Avir_2Obsidian"; protected override string Label2 => "Make obsidian (obsidian)"; protected override string Desc2 => "A caster of fire, earth and water quenches magma here into obsidian - sharp black glass for blades."; }
    public class Designator_2Ash : Designator_Arcane2 { protected override string Key => "Avir_2Ash"; protected override string Label2 => "Ash the ground (ash)"; protected override string Desc2 => "A caster of fire, earth and air lets ash fall here: a plain fertiliser, +20% fertility for ten days."; protected override AcceptanceReport Ok(IntVec3 c) => Map.terrainGrid.TerrainAt(c).fertility > 0f; }
    public class Designator_2Clear : Designator_Arcane2 { protected override string Key => "Avir_2Clear"; protected override string Label2 => "Clear land (mudflow)"; protected override string Desc2 => "A caster of water, air and earth tears up the trees and bushes here; the wood is piled up."; protected override AcceptanceReport Ok(IntVec3 c) => c.GetPlant(Map) != null; }
    public class Designator_2Moat : Designator_Arcane2 { protected override string Key => "Avir_2Moat"; protected override string Label2 => "Moat (quagmire)"; protected override string Desc2 => "A caster of the quagmire (water, earth; an adept of water) floods these cells into shallow water for 30 days - a moat."; }
    public class Designator_2SiltWater : Designator_Arcane2 { protected override string Key => "Avir_2SiltWater"; protected override string Label2 => "Ground to water (silt)"; protected override string Desc2 => "A caster of silt (water, earth; an adept of earth) turns this ground into shallow water for 30 days."; }
    public class Designator_2SiltLand : Designator_Arcane2 { protected override string Key => "Avir_2SiltLand"; protected override string Label2 => "Water to ground (silt)"; protected override string Desc2 => "A caster of silt turns this shallow water into ground for 30 days."; protected override AcceptanceReport Ok(IntVec3 c) => Map.terrainGrid.TerrainAt(c).IsWater && Map.terrainGrid.TerrainAt(c).passability != Traversability.Impassable; }
    public class Designator_2SiltDump : Designator_Arcane2 { protected override string Key => "Avir_2SiltDump"; protected override string Label2 => "Silt pit (silt)"; protected override string Desc2 => "What is left on these cells - rubble, slag, rags, corpses - the silt swallows. The mark stays: a pit for refuse."; }
    public class Designator_2MarshGas : Designator_Arcane2 { protected override string Key => "Avir_2MarshGas"; protected override string Label2 => "Draw marsh gas (marsh gas)"; protected override string Desc2 => "A caster of water, earth and fire draws marsh gas into a canister here - twice a day at most. A fuel stronger than chemfuel."; }
    public class Designator_2Quarry : Designator_Arcane2 { protected override string Key => "Avir_2Quarry"; protected override string Label2 => "Quarry (rockfall)"; protected override string Desc2 => "A caster of the rockfall (earth, air; an adept of earth) brings these rock walls down into chunks, several at once."; protected override AcceptanceReport Ok(IntVec3 c) => c.GetEdifice(Map)?.def.building?.isNaturalRock == true; }
    public class Designator_2DigClay : Designator_Arcane2 { protected override string Key => "Avir_2DigClay"; protected override string Label2 => "Draw clay (clay)"; protected override string Desc2 => "A caster of earth, air and water draws clay out of the ground here - twice a day at most. Medieval Overhaul's clay if it is there."; }

    // ================================================================== developer mode

    public static class Works2Dev
    {
        private const string Cat = "Avir magic";

        [DebugAction(Cat, "Second-order works: what can this caster do", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Report(Pawn p)
        {
            var lines = Works2.All.Select(kv => kv.Key.Substring(6) + " (" + SecondOrder.Label(kv.Value.y) + "): " + (Works2.Able(p, kv.Value.y, kv.Value.cost) != null ? "yes" : "no")
                + (kv.Value.des != null ? ", marked " + p.Map.designationManager.SpawnedDesignationsOfDef(kv.Value.des).Count() : ""));
            Find.WindowStack.Add(new Dialog_MessageBox(p.LabelShort + " - second-order works\n\n" + string.Join("\n", lines)));
        }

        [DebugAction(Cat, "Second-order works: teach all elements (adept)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Teach(Pawn p)
        {
            Hediff_ArcaneCore c = Mages.Core(p); if (c == null) return;
            foreach (RuneKind k in new[] { RuneKind.Fire, RuneKind.Water, RuneKind.Earth, RuneKind.Air })
                if (MageClass.XpIn(c, k) < MageClass.SorcererStart) c.xp[k] = MageClass.SorcererStart;
            c.arcana = c.Capacity;
        }

        [DebugAction(Cat, "Second-order works: do every one here now", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DoAll(Pawn p)
        {
            Hediff_ArcaneCore c = Mages.Core(p); var done = new List<string>();
            foreach (var kv in Works2.All)
            {
                Work2 w = kv.Value; LocalTargetInfo t = LocalTargetInfo.Invalid;
                if (w.things != null) { Thing th = w.things(p).Where(x => w.thingOk == null || w.thingOk(p, x)).OrderBy(x => x.Position.DistanceToSquared(p.Position)).FirstOrDefault(); if (th != null) t = th; }
                else if (w.des != null) { var d = p.Map.designationManager.SpawnedDesignationsOfDef(w.des).FirstOrDefault(); if (d != null) t = d.target.Cell; }
                else if (w.cells != null) { IntVec3 x = w.cells(p).FirstOrDefault(); if (x.IsValid) t = x; }
                if (!t.IsValid) continue;
                WorkSpells.Now = new WorkCast();
                try { if (w.effect(p, t, c)) done.Add(kv.Key.Substring(6)); }
                catch (Exception e) { Log.Warning("[Avir] " + kv.Key + ": " + e.Message); }
                finally { WorkSpells.Now = new WorkCast(); }
            }
            Messages.Message("Done: " + (done.Count == 0 ? "nothing (nothing marked or found)" : string.Join(", ", done)), MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction(Cat, "Second-order works: spawn lava core, marsh gas, obsidian, clay, brick", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Items()
        {
            IntVec3 at = Verse.UI.MouseCell(); Map m = Find.CurrentMap;
            foreach (string n in new[] { "Avir_LavaCore", "Avir_MarshGas", "Avir_ObsidianChunk", "Avir_Brick" })
            { ThingDef d = Works2.Def(n); if (d == null) continue; Thing t = ThingMaker.MakeThing(d, d.MadeFromStuff ? GenStuff.DefaultStuffFor(d) : null); t.stackCount = Mathf.Min(10, d.stackLimit); GenPlace.TryPlaceThing(t, at, m, ThingPlaceMode.Near); }
            if (Works2.Clay != null) { Thing t = ThingMaker.MakeThing(Works2.Clay); t.stackCount = Mathf.Min(30, Works2.Clay.stackLimit); GenPlace.TryPlaceThing(t, at, m, ThingPlaceMode.Near); }
        }
    }
}
