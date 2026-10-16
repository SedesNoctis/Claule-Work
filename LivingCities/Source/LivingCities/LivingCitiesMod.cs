using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace LivingCities
{
    public class LivingCitiesMod : Mod
    {
        public const string Stamp = "LC-2026-10-16-D";

        public LivingCitiesMod(ModContentPack content) : base(content)
        {
            new Harmony("SedesNoctis.LivingCities").PatchAll();
            Log.Message("[Living Cities] " + Stamp);
        }
    }

    /// <summary>A faction with a CityDef has its bases built by us; everyone else keeps vanilla's. The
    /// city stands in the middle of the map, whatever spot the scatterer picked for a small vanilla base.</summary>
    [HarmonyPatch(typeof(GenStep_Settlement), "ScatterAt")]
    public static class Patch_SettlementScatter
    {
        public static bool Prefix(GenStep_Settlement __instance, Map map)
        {
            Faction f = __instance.overrideFaction ?? map.ParentFaction;
            if (f == null || f.IsPlayer) return true;
            CityDef def = CityDef.For(f.def);
            if (def == null) return true;
            try
            {
                var res = CityBuilder.Build(map, def, def.RandomSize(), f, map.Center, __instance.generatePawns);
                MapGenerator.SetVar("SettlementRect", res.rect);
                return false;
            }
            catch (Exception e)
            {
                Log.Error("[Living Cities] city generation failed, falling back to a vanilla base: " + e);
                return true;
            }
        }
    }

    public static class LivingCitiesDebug
    {
        // ------------------------------------------------------------------ layouts: export and place

        private static IntVec3 firstCorner = IntVec3.Invalid;

        private static void Exported(string file)
        {
            Messages.Message("Living Cities: layout saved to " + file, MessageTypeDefOf.PositiveEvent, false);
            Log.Message("[Living Cities] layout saved: " + file);
            try { Application.OpenURL("file:///" + System.IO.Path.GetDirectoryName(file).Replace('\\', '/')); } catch { }
        }

        private static string Name(Map map) => map.Parent?.Label ?? "Base";

        [DebugAction("Living Cities", "Export layout: whole map", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ExportMap()
        {
            Map map = Find.CurrentMap;
            Exported(LayoutExport.Export(map, new CellRect(0, 0, map.Size.x, map.Size.z), Name(map)));
        }

        [DebugAction("Living Cities", "Export layout: home area", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ExportHome()
        {
            Map map = Find.CurrentMap;
            var cells = map.areaManager.Home.ActiveCells.ToList();
            if (cells.Count == 0) { Messages.Message("No home area.", MessageTypeDefOf.RejectInput, false); return; }
            var rect = CellRect.FromLimits(cells.Min(c => c.x), cells.Min(c => c.z), cells.Max(c => c.x), cells.Max(c => c.z)).ExpandedBy(3);
            Exported(LayoutExport.Export(map, rect, Name(map)));
        }

        [DebugAction("Living Cities", "Export layout: rectangle (click 2 corners)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ExportRect()
        {
            IntVec3 c = UI.MouseCell();
            Map map = Find.CurrentMap;
            if (!firstCorner.IsValid) { firstCorner = c; Messages.Message("First corner set - click the opposite one.", MessageTypeDefOf.NeutralEvent, false); return; }
            var rect = CellRect.FromLimits(firstCorner, c);
            firstCorner = IntVec3.Invalid;
            Exported(LayoutExport.Export(map, rect, Name(map)));
        }

        [DebugAction("Living Cities", "Place layout...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PlaceLayout()
        {
            var opts = new List<DebugMenuOption>();
            foreach (LayoutDef d in DefDatabase<LayoutDef>.AllDefsListForReading)
            {
                LayoutDef def = d;
                opts.Add(new DebugMenuOption("[def] " + def.defName, DebugMenuOptionMode.Action, () => PickMode(() => def)));
            }
            if (System.IO.Directory.Exists(LayoutExport.Folder))
                foreach (string f in System.IO.Directory.GetFiles(LayoutExport.Folder, "*.xml").OrderByDescending(System.IO.File.GetLastWriteTime))
                {
                    string file = f;
                    opts.Add(new DebugMenuOption("[file] " + System.IO.Path.GetFileNameWithoutExtension(file), DebugMenuOptionMode.Action, () => PickMode(() => LayoutPlace.Load(file))));
                }
            if (opts.Count == 0) { Messages.Message("No layouts yet: export one first.", MessageTypeDefOf.RejectInput, false); return; }
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        private static void PickMode(Func<LayoutDef> get)
        {
            LayoutDef def;
            try { def = get(); }
            catch (Exception e) { Messages.Message("Could not read the layout: " + e.Message, MessageTypeDefOf.RejectInput, false); return; }
            var opts = new List<DebugMenuOption>
            {
                new DebugMenuOption("as mine (with zones and bills)", DebugMenuOptionMode.Tool, () => Place(def, Faction.OfPlayer, true)),
                new DebugMenuOption("unowned (no zones)", DebugMenuOptionMode.Tool, () => Place(def, null, false)),
            };
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        private static void Place(LayoutDef def, Faction f, bool extras)
        {
            Map map = Find.CurrentMap;
            IntVec3 c = UI.MouseCell();
            var origin = new IntVec3(c.x - def.size.x / 2, 0, c.z - def.size.z / 2);
            var rep = LayoutPlace.Place(map, def, origin, f, extras);
            Messages.Message("Living Cities: placed " + def.label + " - " + rep, MessageTypeDefOf.NeutralEvent, false);
            if (rep.missing.Count > 0) Log.Warning("[Living Cities] layout " + def.defName + " - missing defs: " + string.Join(", ", rep.missing));
        }

        // ------------------------------------------------------------------ generated cities

        [DebugAction("Living Cities", "Build city here...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void BuildHere()
        {
            var opts = new List<DebugMenuOption>();
            foreach (CityDef def in DefDatabase<CityDef>.AllDefsListForReading)
                opts.Add(new DebugMenuOption(def.defName, DebugMenuOptionMode.Action, () => PickSize(def)));
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        private static void PickSize(CityDef def)
        {
            var opts = new List<DebugMenuOption>();
            foreach (CitySize s in Enum.GetValues(typeof(CitySize)))
            {
                CitySize size = s;
                if (size == CitySize.Nest)
                    foreach (FigureKind k in def.figures.Where(k => k != FigureKind.SmallRing))
                    {
                        FigureKind kind = k;
                        opts.Add(new DebugMenuOption("Nest: " + kind, DebugMenuOptionMode.Tool, () => Build(def, size, kind, false)));
                    }
                else opts.Add(new DebugMenuOption(size.ToString(), DebugMenuOptionMode.Tool, () => Build(def, size, null, false)));
            }
            opts.Add(new DebugMenuOption("Nest (random) with residents", DebugMenuOptionMode.Tool, () => Build(def, CitySize.Nest, null, true)));
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        private static void Build(CityDef def, CitySize size, FigureKind? kind, bool pawns)
        {
            Map map = Find.CurrentMap;
            Faction f = def.factions.Select(n => DefDatabase<FactionDef>.GetNamedSilentFail(n)).Where(d => d != null)
                           .Select(d => Find.FactionManager.FirstFactionOfDef(d)).FirstOrDefault(x => x != null);
            var res = CityBuilder.Build(map, def, size, f, UI.MouseCell(), pawns && f != null, kind);
            foreach (IntVec3 c in res.rect) if (c.InBounds(map)) map.fogGrid.Unfog(c);
            Messages.Message("Living Cities: " + def.defName + " " + size + " (" + res.plan.rooms.Count + " rooms, " + res.pawns.Count + " pawns)", MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
