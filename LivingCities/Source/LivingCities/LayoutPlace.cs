using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingCities
{
    /// <summary>Stamps a LayoutDef onto a map. Whatever it names that is absent (a mod not loaded) is skipped
    /// and counted; the rest goes down as it was - terrain, paint, walls before furniture, roofs, plants,
    /// items, and for the player's own map the zones and bills too.</summary>
    public static class LayoutPlace
    {
        public class Report
        {
            public int things, items, plants, zones, bills;
            public HashSet<string> missing = new HashSet<string>();
            public override string ToString() =>
                things + " buildings, " + items + " items, " + plants + " plants, " + zones + " zones, " + bills + " bills"
                + (missing.Count > 0 ? "; missing " + missing.Count + ": " + string.Join(", ", missing.Take(12)) + (missing.Count > 12 ? "..." : "") : "");
        }

        public static LayoutDef Load(string file)
        {
            var doc = new XmlDocument();
            doc.Load(file);
            XmlNode node = doc.DocumentElement?.SelectSingleNode("LivingCities.LayoutDef");
            if (node == null) throw new Exception("no LivingCities.LayoutDef in " + file);
            return DirectXmlToObject.ObjectFromXml<LayoutDef>(node, false);
        }

        private static T Def<T>(string name, Report rep) where T : Def
        {
            if (name.NullOrEmpty()) return null;
            T d = DefDatabase<T>.GetNamedSilentFail(name);
            if (d == null) rep.missing.Add(name);
            return d;
        }

        private static float Fl(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
        private static int In(string s) => int.TryParse(s, out int i) ? i : 0;

        public static Report Place(Map map, LayoutDef L, IntVec3 origin, Faction faction, bool playerExtras)
        {
            var rep = new Report();
            var rect = new CellRect(origin.x, origin.z, L.size.x, L.size.z);
            IntVec3 At(int x, int z) => new IntVec3(origin.x + x, 0, origin.z + z);
            bool Ok(IntVec3 c) => c.InBounds(map);

            // clear
            foreach (IntVec3 c in rect)
            {
                if (!Ok(c)) continue;
                foreach (Thing t in c.GetThingList(map).ToList())
                    if (!(t is Pawn) && !t.Destroyed) t.Destroy(DestroyMode.Vanish);
                map.roofGrid.SetRoof(c, null);
            }

            // terrain (north row first), what lies under it, paint
            var keys = L.terrainKeys.Select(k => Def<TerrainDef>(k, rep)).ToList();
            for (int i = 0; i < L.terrain.Count; i++)
            {
                int z = L.size.z - 1 - i;
                string row = L.terrain[i];
                for (int x = 0; x * 2 + 1 < row.Length; x++)
                {
                    IntVec3 c = At(x, z);
                    int k = LayoutDef.Decode(row, x * 2);
                    if (!Ok(c) || k < 0 || k >= keys.Count || keys[k] == null) continue;
                    map.terrainGrid.SetTerrain(c, keys[k]);
                }
            }
            foreach (string s in L.under)
            {
                string[] p = s.Split('|');
                TerrainDef u = p.Length > 2 ? Def<TerrainDef>(p[2], rep) : null;
                IntVec3 c = At(In(p[0]), In(p[1]));
                if (u != null && Ok(c)) map.terrainGrid.SetUnderTerrain(c, u);
            }
            foreach (string s in L.colors)
            {
                string[] p = s.Split('|');
                ColorDef col = p.Length > 2 ? Def<ColorDef>(p[2], rep) : null;
                IntVec3 c = At(In(p[0]), In(p[1]));
                if (col != null && Ok(c)) map.terrainGrid.SetTerrainColor(c, col);
            }

            // buildings: what holds up (walls, doors, columns) first, then the rest
            var parsed = L.things.Select(s => s.Split('|')).Where(p => p.Length >= 5)
                                 .Select(p => (p, def: Def<ThingDef>(p[0], rep))).Where(e => e.def != null)
                                 .OrderByDescending(e => e.def.holdsRoof || e.def.IsDoor).ToList();
            var spawned = new Dictionary<IntVec3, Thing>();
            foreach (var (p, def) in parsed)
            {
                IntVec3 c = At(In(p[2]), In(p[3]));
                if (!Ok(c)) continue;
                ThingDef stuff = p[1].NullOrEmpty() ? null : Def<ThingDef>(p[1], rep);
                if (def.MadeFromStuff && (stuff == null || !stuff.stuffProps.CanMake(def))) stuff = GenStuff.DefaultStuffFor(def);
                Rot4 rot = new Rot4(In(p[4]));
                if (!GenAdj.OccupiedRect(c, rot, def.size).InBounds(map)) continue;
                Thing t;
                try
                {
                    t = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff : null);
                    if (p.Length > 5 && !p[5].NullOrEmpty() && Enum.TryParse(p[5], out QualityCategory q))
                        t.TryGetComp<CompQuality>()?.SetQuality(q, ArtGenerationContext.Outsider);
                    if (p.Length > 6 && !p[6].NullOrEmpty()) { var st = Def<ThingStyleDef>(p[6], rep); if (st != null) t.StyleDef = st; }
                    if (faction != null && def.CanHaveFaction) t.SetFaction(faction);
                    t = GenSpawn.Spawn(t, c, map, rot, WipeMode.Vanish);
                    if (p.Length > 7 && !p[7].NullOrEmpty() && t is Building b) { var pc = Def<ColorDef>(p[7], rep); if (pc != null) b.ChangePaint(pc); }
                    if (p.Length > 8 && !p[8].NullOrEmpty())
                    {
                        string[] rgba = p[8].Split(',');
                        if (rgba.Length == 4) t.SetColor(new Color(Fl(rgba[0]), Fl(rgba[1]), Fl(rgba[2]), Fl(rgba[3])), false);
                    }
                    t.TryGetComp<CompRefuelable>()?.Refuel(t.TryGetComp<CompRefuelable>().Props.fuelCapacity);
                    spawned[c] = t;
                    rep.things++;
                }
                catch (Exception e) { Log.Warning("[Living Cities] could not place " + def.defName + ": " + e.Message); }
            }

            // roof
            var roofKeys = L.roofKeys.Select(k => Def<RoofDef>(k, rep)).ToList();
            for (int i = 0; i < L.roof.Count; i++)
            {
                int z = L.size.z - 1 - i;
                string row = L.roof[i];
                for (int x = 0; x < row.Length; x++)
                {
                    IntVec3 c = At(x, z);
                    if (!Ok(c)) continue;
                    char ch = row[x];
                    RoofDef r = ch == 'c' ? RoofDefOf.RoofConstructed : ch == 't' ? RoofDefOf.RoofRockThin : ch == 'h' ? RoofDefOf.RoofRockThick
                              : ch >= '0' && ch <= '9' && ch - '0' < roofKeys.Count ? roofKeys[ch - '0'] : null;
                    if (r != null) map.roofGrid.SetRoof(c, r);
                }
            }

            // plants and items
            foreach (string s in L.plants)
            {
                string[] p = s.Split('|');
                ThingDef def = p.Length > 3 ? Def<ThingDef>(p[0], rep) : null;
                IntVec3 c = p.Length > 3 ? At(In(p[1]), In(p[2])) : IntVec3.Invalid;
                if (def?.plant == null || !Ok(c) || c.GetPlant(map) != null || c.GetEdifice(map) != null) continue;
                var plant = (Plant)ThingMaker.MakeThing(def);
                plant.Growth = Mathf.Clamp01(Fl(p[3]));
                GenSpawn.Spawn(plant, c, map);
                rep.plants++;
            }
            foreach (string s in L.items)
            {
                string[] p = s.Split('|');
                ThingDef def = p.Length > 4 ? Def<ThingDef>(p[0], rep) : null;
                if (def == null) continue;
                IntVec3 c = At(In(p[2]), In(p[3]));
                if (!Ok(c)) continue;
                ThingDef stuff = p[1].NullOrEmpty() ? null : Def<ThingDef>(p[1], rep);
                if (def.MadeFromStuff && (stuff == null || !stuff.stuffProps.CanMake(def))) stuff = GenStuff.DefaultStuffFor(def);
                try
                {
                    Thing t = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff : null);
                    t.stackCount = Mathf.Clamp(In(p[4]), 1, def.stackLimit);
                    if (p.Length > 5 && !p[5].NullOrEmpty() && Enum.TryParse(p[5], out QualityCategory q))
                        t.TryGetComp<CompQuality>()?.SetQuality(q, ArtGenerationContext.Outsider);
                    GenPlace.TryPlaceThing(t, c, map, ThingPlaceMode.Near);
                    rep.items++;
                }
                catch (Exception e) { Log.Warning("[Living Cities] could not place item " + def.defName + ": " + e.Message); }
            }

            // the player's own: zones and bills
            if (playerExtras)
            {
                foreach (string s in L.zones)
                {
                    string[] p = s.Split('|');
                    if (p.Length < 4) continue;
                    var cells = new List<IntVec3>();
                    foreach (string run in p[3].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] r = run.Split(',');
                        if (r.Length < 3) continue;
                        for (int i = 0; i < In(r[2]); i++) { IntVec3 c = At(In(r[0]) + i, In(r[1])); if (Ok(c) && map.zoneManager.ZoneAt(c) == null) cells.Add(c); }
                    }
                    if (cells.Count == 0) continue;
                    Zone zone;
                    if (p[0] == "growing")
                    {
                        var g = new Zone_Growing(map.zoneManager);
                        ThingDef plant = Def<ThingDef>(p[2], rep);
                        if (plant != null) g.SetPlantDefToGrow(plant);
                        zone = g;
                    }
                    else if (p[0] == "stockpile") zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                    else continue;
                    map.zoneManager.RegisterZone(zone);
                    foreach (IntVec3 c in cells) zone.AddCell(c);
                    if (!p[1].NullOrEmpty()) zone.label = p[1];
                    rep.zones++;
                }
                foreach (string s in L.bills)
                {
                    string[] p = s.Split('|');
                    if (p.Length < 4 || !spawned.TryGetValue(At(In(p[0]), In(p[1])), out Thing t) || !(t is IBillGiver giver)) continue;
                    foreach (string rn in p[3].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        RecipeDef r = Def<RecipeDef>(rn, rep);
                        if (r == null || !r.AvailableOnNow(t)) continue;
                        giver.BillStack.AddBill(r.MakeNewBill());
                        rep.bills++;
                    }
                }
            }

            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            return rep;
        }
    }
}
