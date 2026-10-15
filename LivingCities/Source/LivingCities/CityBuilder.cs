using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace LivingCities
{
    // =====================================================================================
    // THE BUILDER: lays a plan on the map. It clears the footprint (things, rock, natural roof), lays the
    // floors, raises walls and doors, roofs everything but the sky wells, gives each room its zone and
    // furnishes it, stocks the stores and, if asked, settles the residents and the garrison under one
    // DefendBase lord - the same lord a vanilla base gets, so raids, caravans and quests treat it alike.
    // =====================================================================================
    public static class CityBuilder
    {
        public class Result
        {
            public CellRect rect;
            public CityPlan plan;
            public IntVec3 origin;
            public List<Pawn> pawns = new List<Pawn>();
        }

        public static Result Build(Map map, CityDef def, CitySize size, Faction faction, IntVec3 centre, bool pawns, FigureKind? only = null)
        {
            int maxExtent = Mathf.Min(map.Size.x, map.Size.z) - 20;
            var figures = only.HasValue ? new List<FigureKind> { only.Value } : def.figures;
            CityPlan plan = CityFigures.Compose(size, figures, maxExtent, def.enclosureChance, def.gateChance);
            plan.FindRooms();
            AssignZones(plan, def);
            plan.CutDoors(size == CitySize.Outpost ? 2 : size == CitySize.Nest ? 3 : 4);
            plan.Wells(r => r.zone == null || !r.zone.noWell);

            CityStyleDef style = def.styles.RandomElement();
            IntVec3 origin = new IntVec3(centre.x - plan.size / 2, 0, centre.z - plan.size / 2);
            origin.x = Mathf.Clamp(origin.x, 2, map.Size.x - plan.size - 2);
            origin.z = Mathf.Clamp(origin.z, 2, map.Size.z - plan.size - 2);
            var res = new Result { plan = plan, origin = origin, rect = new CellRect(origin.x, origin.z, plan.size, plan.size).ClipInsideMap(map) };

            var ctx = new Ctx(map, plan, origin, style, faction);
            ctx.Clear();
            ctx.Lay();
            ctx.Columns();
            foreach (PlanRoom r in plan.rooms) ctx.Furnish(r);
            ctx.Lights();
            foreach (PlanRoom r in plan.rooms.Where(r => r.place == RoomPlace.Field)) ctx.Sow(r);
            foreach (PlanRoom r in plan.rooms) ctx.Stock(r);
            if (pawns && faction != null) res.pawns = ctx.Settle(def, size);
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            return res;
        }

        // ------------------------------------------------------------------ zones

        private static void AssignZones(CityPlan plan, CityDef def)
        {
            var free = plan.rooms.Where(r => r.place != RoomPlace.Hub).OrderByDescending(r => r.Area).ToList();
            foreach (PlanRoom r in plan.rooms)
                if (r.place == RoomPlace.Hub) r.zone = def.hubZone;
                else if (r.place == RoomPlace.Field) r.zone = def.fieldZone;
                else if (r.place == RoomPlace.Gate) r.zone = def.gateZone;
            free.RemoveAll(r => r.place == RoomPlace.Field || r.place == RoomPlace.Gate);
            var counts = new Dictionary<ZoneDef, int>();
            int Count(ZoneDef z) => counts.TryGetValue(z, out int n) ? n : 0;

            // first what the city must have, each into the room that suits it best
            foreach (CityZoneEntry e in def.zones.Where(e => e.min > 0 && e.zone != null))
                for (int i = 0; i < e.min; i++)
                {
                    PlanRoom best = free.Where(r => e.zone.Fits(r.place, r.Area)).OrderBy(r => Prefer(e.zone, r)).FirstOrDefault();
                    if (best == null) break;
                    best.zone = e.zone;
                    free.Remove(best);
                    counts[e.zone] = Count(e.zone) + 1;
                }
            // then the rest by weight
            foreach (PlanRoom r in free)
            {
                var options = def.zones.Where(e => e.zone != null && Count(e.zone) < e.max && e.zone.Fits(r.place, r.Area)).ToList();
                if (options.Count == 0) continue;
                CityZoneEntry pick = options.RandomElementByWeight(e => e.weight);
                r.zone = pick.zone;
                counts[pick.zone] = Count(pick.zone) + 1;
            }
        }

        /// <summary>Centre zones go to the most central room of their kind, the rest to rooms near their
        /// smallest size (so a big hall is not wasted on a cell block).</summary>
        private static float Prefer(ZoneDef z, PlanRoom r) => r.place == RoomPlace.Center ? -1000 + r.Area * 0.01f : Math.Abs(r.Area - z.minArea * 2f);

        // ------------------------------------------------------------------ laying it down

        private class Ctx
        {
            public readonly Map map;
            public readonly CityPlan plan;
            public readonly IntVec3 origin;
            public readonly CityStyleDef style;
            public readonly Faction faction;
            public readonly ThingDef outerStuff;
            public readonly ThingDef wallDef, wallStuff, heavyDef, heavyStuff, doorDef, doorStuff, furnStuff;
            public readonly TerrainDef floor, fine, ground;
            private readonly HashSet<IntVec3> taken = new HashSet<IntVec3>();

            public Ctx(Map map, CityPlan plan, IntVec3 origin, CityStyleDef style, Faction faction)
            {
                this.map = map; this.plan = plan; this.origin = origin; this.style = style; this.faction = faction;
                wallDef = Named.Thing(style.wall) ?? ThingDefOf.Wall;
                wallStuff = style.Stuff(style.wallStuff) ?? ThingDefOf.BlocksGranite;
                outerStuff = style.Stuff(style.outerWallStuff) ?? wallStuff;
                heavyDef = Named.Thing(style.heavyDoor) ?? ThingDefOf.Door;
                heavyStuff = style.Stuff(style.heavyDoorStuff) ?? ThingDefOf.Steel;
                doorDef = Named.Thing(style.innerDoor) ?? ThingDefOf.Door;
                doorStuff = style.Stuff(style.innerDoorStuff) ?? wallStuff;
                furnStuff = style.Stuff(style.furnitureStuff) ?? ThingDefOf.WoodLog;
                floor = Named.FirstTerrain(style.floor) ?? TerrainDefOf.Concrete;
                fine = style.fineFloors.NullOrEmpty() ? floor : (Named.Terrain(style.fineFloors.Where(n => Named.Terrain(n) != null).RandomElementWithFallback()) ?? floor);
                ground = Named.FirstTerrain(style.ground) ?? TerrainDefOf.Gravel;
            }

            public IntVec3 Cell(int x, int z) => new IntVec3(origin.x + x, 0, origin.z + z);

            private bool InField(int x, int z) { int r = plan.RoomAt(x, z); return r >= 0 && plan.rooms[r].place == RoomPlace.Field; }

            /// <summary>Open sky over this cell: a field, or a wall that touches nothing roofed.</summary>
            private bool Unroofed(int x, int z)
            {
                if (plan.well[plan.Idx(x, z)] || InField(x, z)) return true;
                PlanCell k = plan.At(x, z);
                if (k != PlanCell.Wall && k != PlanCell.Door && k != PlanCell.HeavyDoor) return false;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int r = plan.RoomAt(x + dx, z + dz);
                        if (r >= 0 && plan.rooms[r].place != RoomPlace.Field) return false;
                    }
                return true;
            }

            /// <summary>An outer wall: it faces the outside or the fields.</summary>
            private bool OuterWall(int x, int z)
            {
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 d = GenAdj.CardinalDirections[i];
                    PlanCell k = plan.At(x + d.x, z + d.z);
                    if (CityPlan.Outside(k) || (k == PlanCell.Floor && InField(x + d.x, z + d.z))) return true;
                }
                return false;
            }
            public IntVec3 Cell(IntVec2 c) => Cell(c.x, c.z);

            public void Clear()
            {
                for (int x = 0; x < plan.size; x++)
                    for (int z = 0; z < plan.size; z++)
                    {
                        if (plan.At(x, z) == PlanCell.None) continue;
                        IntVec3 c = Cell(x, z);
                        if (!c.InBounds(map)) continue;
                        foreach (Thing t in c.GetThingList(map).ToList())
                            if (!(t is Pawn) && !t.Destroyed) t.Destroy(DestroyMode.Vanish);
                        map.roofGrid.SetRoof(c, null);
                        TerrainDef cur = c.GetTerrain(map);
                        bool bad = cur.passability == Traversability.Impassable || cur.IsWater || !cur.affordances.Contains(TerrainAffordanceDefOf.Heavy);
                        if (plan.At(x, z) == PlanCell.Ground)
                        {
                            if (bad) map.terrainGrid.SetTerrain(c, ground);
                        }
                        else if (InField(x, z))
                        {
                            // fields keep their soil, or get some
                            if (bad || cur.fertility < 0.7f) map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
                        }
                        else map.terrainGrid.SetTerrain(c, ground);   // under the floor we lay next
                    }
            }

            public void Lay()
            {
                for (int x = 0; x < plan.size; x++)
                    for (int z = 0; z < plan.size; z++)
                    {
                        PlanCell k = plan.At(x, z);
                        if (k == PlanCell.None || k == PlanCell.Ground) continue;
                        IntVec3 c = Cell(x, z);
                        if (!c.InBounds(map)) continue;
                        int rid = plan.RoomAt(x, z);
                        ZoneDef zone = rid >= 0 ? plan.rooms[rid].zone : null;
                        bool field = rid >= 0 && plan.rooms[rid].place == RoomPlace.Field;
                        if (!field)
                        {
                            TerrainDef t = zone == null ? floor : zone.fineFloor ? fine : (Named.FirstTerrain(zone.floor) ?? floor);
                            map.terrainGrid.SetTerrain(c, t);
                        }
                        else if (NearWall(c) || Path(x, z)) map.terrainGrid.SetTerrain(c, ground);   // the walks
                        switch (k)
                        {
                            case PlanCell.Wall: Spawn(wallDef, OuterWall(x, z) ? outerStuff : wallStuff, c, Rot4.North); break;
                            case PlanCell.HeavyDoor: Spawn(heavyDef, heavyStuff, c, Rot4.North); break;
                            case PlanCell.Door: Spawn(doorDef, doorStuff, c, Rot4.North); break;
                        }
                        if (!Unroofed(x, z)) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                    }
            }

            /// <summary>A constructed roof holds only so far from a wall; in the great halls columns carry the rest,
            /// so nothing comes down when a wall falls.</summary>
            public void Columns()
            {
                ThingDef column = Named.Thing("Column");
                int n = plan.size;
                var held = new bool[n * n];
                void Hold(int hx, int hz)
                {
                    for (int dx = -6; dx <= 6; dx++)
                        for (int dz = -6; dz <= 6; dz++)
                            if (dx * dx + dz * dz <= 47 && plan.In(hx + dx, hz + dz)) held[plan.Idx(hx + dx, hz + dz)] = true;
                }
                for (int x = 0; x < n; x++)
                    for (int z = 0; z < n; z++)
                    {
                        PlanCell k = plan.At(x, z);
                        if (k == PlanCell.Wall || k == PlanCell.Door || k == PlanCell.HeavyDoor) Hold(x, z);
                    }
                // rooms from the middle out, so columns line up round the heart of each hall
                foreach (PlanRoom r in plan.rooms)
                {
                    if (r.place == RoomPlace.Field) continue;   // open sky
                    IntVec2 mid = r.Centroid();
                    foreach (IntVec2 v in r.cells.OrderBy(c => CityPlan.Dist2(c, mid)))
                    {
                        int i = plan.Idx(v.x, v.z);
                        if (held[i] || plan.well[i]) continue;
                        IntVec3 c = Cell(v);
                        if (column != null && FreeFloor(c))
                        {
                            Spawn(column, wallStuff, c, Rot4.North);
                            taken.Add(c);
                            Hold(v.x, v.z);
                        }
                        else if (c.InBounds(map)) map.roofGrid.SetRoof(c, null);   // no room for one: open sky there
                    }
                }
            }

            /// <summary>The paths through the fields: from every door straight across the ring.</summary>
            private HashSet<IntVec2> paths;
            private bool Path(int x, int z)
            {
                if (paths == null)
                {
                    paths = new HashSet<IntVec2>();
                    for (int px = 0; px < plan.size; px++)
                        for (int pz = 0; pz < plan.size; pz++)
                        {
                            PlanCell k = plan.At(px, pz);
                            if (k != PlanCell.Door && k != PlanCell.HeavyDoor) continue;
                            // walk into the field from the door in each open direction until the far wall
                            foreach (IntVec3 d in GenAdj.CardinalDirections)
                            {
                                int cx = px + d.x, cz = pz + d.z;
                                if (!InField(cx, cz)) continue;
                                while (plan.At(cx, cz) == PlanCell.Floor) { paths.Add(new IntVec2(cx, cz)); cx += d.x; cz += d.z; }
                            }
                        }
                }
                return paths.Contains(new IntVec2(x, z));
            }

            /// <summary>Sows the ring: plots of one crop each, growing at all stages, the paths left bare.</summary>
            public void Sow(PlanRoom room)
            {
                var crops = room.zone?.crops?.Select(Named.Thing).Where(d => d != null && d.plant != null).ToList();
                if (crops == null || crops.Count == 0) return;
                IntVec2 mid = new IntVec2(plan.size / 2, plan.size / 2);
                foreach (IntVec2 v in room.cells)
                {
                    IntVec3 c = Cell(v);
                    if (!c.InBounds(map) || NearWall(c) || Path(v.x, v.z) || c.GetEdifice(map) != null) continue;
                    // a plot is a slice of the ring: twelve of them round the compass
                    float ang = Mathf.Atan2(v.z - mid.z, v.x - mid.x) + Mathf.PI;
                    int plot = Mathf.Clamp((int)(ang / (Mathf.PI * 2f) * 12f), 0, 11);
                    if (plot % 4 == 3) continue;   // a fallow plot here and there
                    ThingDef crop = crops[plot % crops.Count];
                    if (c.GetPlant(map) != null) c.GetPlant(map).Destroy();
                    var p = (Plant)ThingMaker.MakeThing(crop);
                    p.Growth = Rand.Range(0.1f, 0.95f);
                    p.Age = (int)(p.Growth * crop.plant.LifespanTicks * 0.3f);
                    GenSpawn.Spawn(p, c, map);
                }
            }

            public Thing Spawn(ThingDef def, ThingDef stuff, IntVec3 c, Rot4 rot)
            {
                if (def == null) return null;
                if (def.MadeFromStuff && (stuff == null || !stuff.stuffProps.CanMake(def))) stuff = GenStuff.DefaultStuffFor(def);
                Thing t = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff : null);
                if (faction != null && def.CanHaveFaction) t.SetFaction(faction);
                t = GenSpawn.Spawn(t, c, map, rot, WipeMode.Vanish);
                t.TryGetComp<CompRefuelable>()?.Refuel(t.TryGetComp<CompRefuelable>().Props.fuelCapacity);
                return t;
            }

            private bool FreeFloor(IntVec3 c)
            {
                if (!c.InBounds(map) || taken.Contains(c)) return false;
                int x = c.x - origin.x, z = c.z - origin.z;
                if (plan.At(x, z) != PlanCell.Floor) return false;
                // keep a cell clear in front of every door
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 n = c + GenAdj.CardinalDirections[i];
                    PlanCell k = plan.At(n.x - origin.x, n.z - origin.z);
                    if (k == PlanCell.Door || k == PlanCell.HeavyDoor) return false;
                }
                return c.GetEdifice(map) == null;
            }

            private bool NearWall(IntVec3 c)
            {
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 n = c + GenAdj.CardinalDirections[i];
                    if (plan.At(n.x - origin.x, n.z - origin.z) == PlanCell.Wall) return true;
                }
                return false;
            }

            /// <summary>Puts one thing somewhere in the room: its whole footprint on free floor of this room,
            /// its interaction spot free and inside; against a wall if asked.</summary>
            public Thing Place(ThingDef def, PlanRoom room, bool nearWall)
            {
                if (def == null) return null;
                var cells = room.cells.Select(Cell).Where(FreeFloor).ToList();
                if (nearWall) { var w = cells.Where(NearWall).ToList(); if (w.Count > 0) cells = w; }
                foreach (IntVec3 c in cells.InRandomOrder().Take(60))
                {
                    foreach (Rot4 rot in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West }.InRandomOrder())
                    {
                        if (!def.rotatable && rot != Rot4.North) continue;
                        CellRect occ = GenAdj.OccupiedRect(c, rot, def.size);
                        if (!occ.Cells.All(o => FreeFloor(o) && plan.RoomAt(o.x - origin.x, o.z - origin.z) == room.id)) continue;
                        if (def.hasInteractionCell)
                        {
                            IntVec3 ic = ThingUtility.InteractionCellWhenAt(def, c, rot, map);
                            if (!FreeFloor(ic) || plan.RoomAt(ic.x - origin.x, ic.z - origin.z) != room.id || occ.Contains(ic)) continue;
                            taken.Add(ic);
                        }
                        foreach (IntVec3 o in occ.ExpandedBy(def.passability == Traversability.Impassable ? 1 : 0).Cells)
                            if (def.passability == Traversability.Impassable || occ.Contains(o)) taken.Add(o);
                        foreach (IntVec3 o in occ.Cells) taken.Add(o);
                        return Spawn(def, furnStuff, c, rot);
                    }
                }
                return null;
            }

            public void Furnish(PlanRoom room)
            {
                if (room.zone == null) return;
                // the well stays open: nothing stands under the sky
                foreach (IntVec2 c in room.cells) if (plan.well[plan.Idx(c.x, c.z)]) taken.Add(Cell(c));
                foreach (ZoneThing zt in room.zone.things)
                {
                    ThingDef def = zt.Def();
                    if (def == null) continue;
                    int n = zt.count.RandomInRange + Mathf.FloorToInt(room.Area * zt.perArea);
                    for (int i = 0; i < n; i++)
                    {
                        Thing t = Place(def, room, zt.nearWall);
                        if (t == null) break;
                        if (zt.forPrisoners && t is Building_Bed bed) bed.ForPrisoners = true;
                    }
                }
            }

            public void Lights()
            {
                ThingDef light = Named.FirstThing(style.lights);
                if (light == null) return;
                foreach (PlanRoom r in plan.rooms)
                {
                    if (r.place == RoomPlace.Field) continue;
                    int n = Mathf.Max(r.place == RoomPlace.Hub ? 0 : 1, r.Area / Mathf.Max(10, style.lightSpacing));
                    for (int i = 0; i < n; i++) if (Place(light, r, true) == null) break;
                }
            }

            public void Stock(PlanRoom room)
            {
                if (room.zone == null) return;
                float value = room.zone.lootBase + room.zone.lootPerCell * room.Area;
                if (value <= 1f) return;
                ThingSetMakerDef maker = faction?.def.settlementLootMaker ?? ThingSetMakerDefOf.MapGen_DefaultStockpile;
                var parms = default(ThingSetMakerParams);
                parms.totalMarketValueRange = new FloatRange(value * 0.8f, value * 1.2f);
                parms.makingFaction = faction;
                List<Thing> items;
                try { items = maker.root.Generate(parms); }
                catch (Exception e) { Log.Warning("[Living Cities] loot: " + e.Message); return; }
                var spots = room.cells.Select(Cell).Where(c => c.Standable(map) && !plan.well[plan.Idx(c.x - origin.x, c.z - origin.z)]).ToList();
                // shelves first, then the floor
                var shelves = spots.Where(c => c.GetFirstThing<Building_Storage>(map) != null).ToList();
                foreach (Thing t in items)
                {
                    IntVec3 at = shelves.Count > 0 && Rand.Chance(0.7f) ? shelves.RandomElement() : spots.RandomElementWithFallback(IntVec3.Invalid);
                    if (!at.IsValid) { t.Destroy(); continue; }
                    GenPlace.TryPlaceThing(t, at, map, ThingPlaceMode.Near);
                }
            }

            // ------------------------------------------------------------------ people

            public List<Pawn> Settle(CityDef def, CitySize size)
            {
                var all = new List<Pawn>();
                foreach (PlanRoom r in plan.rooms)
                {
                    if (r.zone == null) continue;
                    foreach (ZoneResident zr in r.zone.residents)
                    {
                        CityRole role = def.Role(zr.role);
                        if (role == null) continue;
                        int n = zr.count.RandomInRange;
                        for (int i = 0; i < n; i++)
                        {
                            PawnKindDef kind = role.RandomKind();
                            if (kind == null) break;
                            Pawn p = Make(kind, role.child);
                            if (p == null) continue;
                            if (SpawnIn(p, r)) all.Add(p);
                        }
                    }
                }

                // the garrison: the faction's own settlement group, in the galleries
                try
                {
                    var parms = new PawnGroupMakerParms
                    {
                        groupKind = PawnGroupKindDefOf.Settlement,
                        tile = map.Tile,
                        faction = faction,
                        points = def.Garrison(size).RandomInRange,
                        inhabitants = true,
                    };
                    if (faction.def.pawnGroupMakers?.Any(m => m.kindDef == PawnGroupKindDefOf.Settlement) != true) parms.groupKind = PawnGroupKindDefOf.Combat;
                    var hubs = plan.rooms.Where(r => r.place == RoomPlace.Hub).ToList();
                    if (hubs.Count == 0) hubs = plan.rooms;
                    foreach (Pawn p in PawnGroupMakerUtility.GeneratePawns(parms, false))
                        if (SpawnIn(p, hubs.RandomElement())) all.Add(p);
                }
                catch (Exception e) { Log.Warning("[Living Cities] garrison: " + e.Message); }

                IntVec3 centre = Cell(plan.size / 2, plan.size / 2);
                LordMaker.MakeNewLord(faction, new LordJob_DefendBase(faction, centre, 25000), map, all);
                return all;
            }

            private Pawn Make(PawnKindDef kind, bool child)
            {
                try
                {
                    return PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer,
                        tile: map.Tile, inhabitant: true, developmentalStages: child ? DevelopmentalStage.Child : DevelopmentalStage.Adult));
                }
                catch (Exception e) { Log.Warning("[Living Cities] could not make " + kind.defName + ": " + e.Message); return null; }
            }

            private bool SpawnIn(Pawn p, PlanRoom room)
            {
                var cells = room.cells.Select(Cell).Where(c => c.InBounds(map) && c.Standable(map)).ToList();
                if (cells.Count == 0) { Find.WorldPawns.PassToWorld(p); return false; }
                GenSpawn.Spawn(p, cells.RandomElement(), map);
                return true;
            }
        }
    }
}
