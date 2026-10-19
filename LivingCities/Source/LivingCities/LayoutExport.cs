using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingCities
{
    /// <summary>Writes a part of the current map as a LayoutDef file - see LayoutDef for the format.</summary>
    public static class LayoutExport
    {
        public static string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "LivingCities", "Exports");

        private static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

        public static string Export(Map map, CellRect rect, string name)
        {
            rect = rect.ClipInsideMap(map);
            var used = new HashSet<string>();
            void Use(Def d) { string id = d?.modContentPack?.PackageId; if (!id.NullOrEmpty()) used.Add(id.ToLowerInvariant()); }
            string N(Def d) { if (d == null) return ""; Use(d); return d.defName; }

            var sb = new StringBuilder();
            string defName = "LC_Layout_" + new string(name.Where(char.IsLetterOrDigit).ToArray()) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm");
            var L = new LayoutDef { defName = defName };
            L.label = name;
            L.exported = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            L.gameVersion = VersionControl.CurrentVersionStringWithRev;
            L.colony = map.Parent?.LabelCap ?? "";
            L.biome = map.Biome?.defName ?? "";
            L.daysPlayed = GenDate.DaysPassed;

            // ---------------- the colony's ideoligions and the styles they build in
            if (ModsConfig.IdeologyActive && Faction.OfPlayer?.ideos != null)
            {
                Ideo primary = Faction.OfPlayer.ideos.PrimaryIdeo;
                foreach (Ideo ideo in Faction.OfPlayer.ideos.AllIdeos)
                {
                    string memes = string.Join(";", ideo.memes.Select(m => N(m)));
                    string styles = ideo.thingStyleCategories == null ? "" : string.Join(";", ideo.thingStyleCategories
                        .Where(s => s.category != null).Select(s => N(s.category) + ":" + F(s.priority)));
                    L.ideos.Add((ideo == primary ? "primary" : "") + "|" + ideo.name + "|" + N(ideo.culture) + "|" + memes + "|" + styles);
                }
            }
            L.size = new IntVec2(rect.Width, rect.Height);
            Use(map.Biome);

            // ---------------- terrain, under-terrain, paint
            var tKeys = new Dictionary<TerrainDef, int>();
            for (int z = rect.maxZ; z >= rect.minZ; z--)
            {
                var row = new StringBuilder(rect.Width * 2);
                for (int x = rect.minX; x <= rect.maxX; x++)
                {
                    IntVec3 c = new IntVec3(x, 0, z);
                    TerrainDef t = map.terrainGrid.TerrainAt(c);
                    if (!tKeys.TryGetValue(t, out int k)) { k = tKeys.Count; tKeys[t] = k; L.terrainKeys.Add(N(t)); }
                    row.Append(LayoutDef.Code(k));
                    TerrainDef u = map.terrainGrid.UnderTerrainAt(c);
                    if (u != null) L.under.Add((x - rect.minX) + "|" + (z - rect.minZ) + "|" + N(u));
                    ColorDef col = map.terrainGrid.ColorAt(c);
                    if (col != null) L.colors.Add((x - rect.minX) + "|" + (z - rect.minZ) + "|" + N(col));
                }
                L.terrain.Add(row.ToString());
            }

            // ---------------- roof
            var rKeys = new List<RoofDef>();
            for (int z = rect.maxZ; z >= rect.minZ; z--)
            {
                var row = new StringBuilder(rect.Width);
                for (int x = rect.minX; x <= rect.maxX; x++)
                {
                    RoofDef r = map.roofGrid.RoofAt(new IntVec3(x, 0, z));
                    if (r == null) row.Append('.');
                    else if (r == RoofDefOf.RoofConstructed) row.Append('c');
                    else if (r == RoofDefOf.RoofRockThin) row.Append('t');
                    else if (r == RoofDefOf.RoofRockThick) row.Append('h');
                    else
                    {
                        int i = rKeys.IndexOf(r);
                        if (i < 0) { i = rKeys.Count; rKeys.Add(r); L.roofKeys.Add(N(r)); }
                        row.Append((char)('0' + Math.Min(9, i)));
                    }
                }
                L.roof.Add(row.ToString());
            }

            // ---------------- things
            var seen = new HashSet<Thing>();
            foreach (IntVec3 c in rect)
                foreach (Thing t in c.GetThingList(map))
                {
                    if (t.Position != c || !seen.Add(t)) continue;   // multi-cell things once, at their own position (checked first: a bench met first on a side cell must still be taken at its centre)
                    if (t is Pawn || t is Blueprint || t is Frame || t is Mote || t is Filth || t.def.category == ThingCategory.Ethereal) continue;
                    int x = t.Position.x - rect.minX, z = t.Position.z - rect.minZ;
                    t.TryGetQuality(out QualityCategory q);
                    string quality = t.TryGetComp<CompQuality>() != null ? q.ToString() : "";
                    if (t is Plant p)
                    {
                        L.plants.Add(N(t.def) + "|" + x + "|" + z + "|" + F(p.Growth));
                        continue;
                    }
                    if (t.def.category == ThingCategory.Item)
                    {
                        L.items.Add(N(t.def) + "|" + N(t.Stuff) + "|" + x + "|" + z + "|" + t.stackCount + "|" + quality);
                        continue;
                    }
                    if (t.def.category != ThingCategory.Building) continue;
                    string paint = t is Building b && b.PaintColorDef != null ? N(b.PaintColorDef) : "";
                    var colorable = t.TryGetComp<CompColorable>();
                    string color = colorable != null && colorable.Active
                        ? F(colorable.Color.r) + "," + F(colorable.Color.g) + "," + F(colorable.Color.b) + "," + F(colorable.Color.a) : "";
                    string style = "";
                    try { style = N(t.StyleDef); } catch { }
                    string faction = t.Faction == null ? "" : t.Faction.IsPlayer ? "player" : "other";
                    L.things.Add(N(t.def) + "|" + N(t.Stuff) + "|" + x + "|" + z + "|" + t.Rotation.AsInt + "|" + quality + "|" + style + "|" + paint + "|" + color + "|" + faction);

                    if (t is IBillGiver giver && giver.BillStack != null && giver.BillStack.Count > 0 && !(t is Pawn))
                        L.bills.Add(x + "|" + z + "|" + t.def.defName + "|" + string.Join(";", giver.BillStack.Bills.Select(bl => N(bl.recipe))));
                }

            // ---------------- zones
            foreach (Zone zone in map.zoneManager.AllZones)
            {
                var cells = zone.Cells.Where(rect.Contains).OrderBy(c => c.z).ThenBy(c => c.x).ToList();
                if (cells.Count == 0) continue;
                string kind = zone is Zone_Growing ? "growing" : zone is Zone_Stockpile ? "stockpile" : zone.GetType().Name;
                string plant = zone is Zone_Growing g ? N(g.GetPlantDefToGrow()) : "";
                var runs = new StringBuilder();
                for (int i = 0; i < cells.Count;)
                {
                    int j = i;
                    while (j + 1 < cells.Count && cells[j + 1].z == cells[i].z && cells[j + 1].x == cells[j].x + 1) j++;
                    runs.Append(cells[i].x - rect.minX).Append(',').Append(cells[i].z - rect.minZ).Append(',').Append(j - i + 1).Append(';');
                    i = j + 1;
                }
                L.zones.Add(kind + "|" + zone.label + "|" + plant + "|" + runs);
            }

            // ---------------- rooms
            var doneRooms = new HashSet<Room>();
            foreach (IntVec3 c in rect)
            {
                Room room = c.GetRoom(map);
                if (room == null || room.PsychologicallyOutdoors || !doneRooms.Add(room)) continue;
                IntVec3 any = room.Cells.FirstOrDefault(rect.Contains);
                var owners = room.ContainedBeds.SelectMany(b => b.OwnersForReading).Select(o => o.LabelShort).Distinct();
                L.rooms.Add(N(room.Role) + "|" + room.CellCount + "|" + (any.x - rect.minX) + "," + (any.z - rect.minZ) + "|" + string.Join(",", owners));
            }

            // ---------------- the people who lived there, and the animals
            foreach (Pawn p in map.mapPawns.FreeColonists)
            {
                string xeno = p.genes == null ? "" : p.genes.CustomXenotype != null ? p.genes.CustomXenotype.name : N(p.genes.Xenotype);
                Building_Bed bed = p.ownership?.OwnedBed;
                string bedAt = bed != null && rect.Contains(bed.Position) ? (bed.Position.x - rect.minX) + "," + (bed.Position.z - rect.minZ) : "";
                string skills = p.skills == null ? "" : string.Join(",", p.skills.skills.OrderByDescending(s => s.Level).Take(3).Select(s => s.def.defName + ":" + s.Level));
                string title = p.royalty?.MostSeniorTitle?.def?.defName ?? "";
                L.residents.Add(p.Name?.ToStringFull + "|" + N(p.kindDef) + "|" + xeno + "|" + p.gender + "|" + p.ageTracker.AgeBiologicalYears + "|" + bedAt + "|" + skills + "|" + title);
            }
            foreach (var g in map.mapPawns.PawnsInFaction(Faction.OfPlayer).Where(p => p.RaceProps.Animal).GroupBy(p => p.kindDef))
                L.animals.Add(N(g.Key) + "|" + g.Count());

            // ---------------- the mods
            foreach (ModMetaData m in ModsConfig.ActiveModsInLoadOrder)
                L.mods.Add(new LayoutMod { packageId = m.PackageIdPlayerFacing, name = m.Name, used = used.Contains(m.PackageId.ToLowerInvariant()) });

            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, defName + ".xml");
            File.WriteAllText(file, Write(L), new UTF8Encoding(false));
            return file;
        }

        private static string E(string s) => SecurityElement.Escape(s ?? "");

        private static string Write(LayoutDef L)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<Defs>");
            sb.AppendLine("  <!-- Exported by Living Cities. Drop this file into a mod's Defs to make it a layout. -->");
            sb.AppendLine("  <LivingCities.LayoutDef>");
            void V(string tag, string v) => sb.AppendLine("    <" + tag + ">" + E(v) + "</" + tag + ">");
            void List(string tag, List<string> l)
            {
                if (l.Count == 0) return;
                sb.AppendLine("    <" + tag + ">");
                foreach (string s in l) sb.AppendLine("      <li>" + E(s) + "</li>");
                sb.AppendLine("    </" + tag + ">");
            }
            V("defName", L.defName);
            V("label", L.label);
            V("exported", L.exported);
            V("gameVersion", L.gameVersion);
            V("colony", L.colony);
            V("biome", L.biome);
            V("daysPlayed", L.daysPlayed.ToString());
            V("size", "(" + L.size.x + "," + L.size.z + ")");
            sb.AppendLine("    <mods>");
            foreach (LayoutMod m in L.mods)
                sb.AppendLine("      <li><packageId>" + E(m.packageId) + "</packageId><name>" + E(m.name) + "</name><used>" + (m.used ? "true" : "false") + "</used></li>");
            sb.AppendLine("    </mods>");
            List("terrainKeys", L.terrainKeys);
            List("terrain", L.terrain);
            List("roofKeys", L.roofKeys);
            List("roof", L.roof);
            List("under", L.under);
            List("colors", L.colors);
            List("things", L.things);
            List("items", L.items);
            List("plants", L.plants);
            List("zones", L.zones);
            List("rooms", L.rooms);
            List("bills", L.bills);
            List("residents", L.residents);
            List("animals", L.animals);
            List("ideos", L.ideos);
            sb.AppendLine("  </LivingCities.LayoutDef>");
            sb.AppendLine("</Defs>");
            return sb.ToString();
        }
    }
}
