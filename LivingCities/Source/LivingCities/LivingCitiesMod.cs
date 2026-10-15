using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace LivingCities
{
    public class LivingCitiesMod : Mod
    {
        public const string Stamp = "LC-2026-10-15-A";

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
