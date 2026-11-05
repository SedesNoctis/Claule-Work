using System;
using System.Reflection;
using System.Linq;
using System.Text;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Avir
{
    /// <summary>
    /// Dev menu: Avir -> "Why no raids or traders?". Asks the game itself, in the same order the storyteller does,
    /// why a raid or a trade caravan cannot come to this map, and writes every answer to the log.
    /// </summary>
    public static class AvirThreatDiagnostics
    {
        [DebugAction("Avir", "Why no raids or traders?", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Diagnose()
        {
            Map map = Find.CurrentMap;
            var sb = new StringBuilder("[Avir threats] ==== diagnosis ====\n");
            try
            {
                Storyteller st = Find.Storyteller;
                sb.AppendLine("storyteller: " + st?.def?.defName + ", difficulty: " + st?.difficultyDef?.defName
                    + ", threatScale " + st?.difficulty?.threatScale + ", introThreats " + st?.difficulty?.allowIntroThreats
                    + ", bigThreats " + st?.difficulty?.allowBigThreats);
                if (st?.def?.comps != null)
                    foreach (var c in st.def.comps) sb.AppendLine("  comp: " + c.GetType().Name);
                sb.AppendLine("map: " + map + ", IsPlayerHome " + map.IsPlayerHome + ", parent " + map.Parent?.def?.defName
                    + " of " + map.Parent?.Faction + ", layer " + map.Tile.LayerDef?.defName);
                sb.AppendLine("days since settle " + GenDate.DaysPassedSinceSettle + ", ticks " + Find.TickManager.TicksGame
                    + ", queued incidents " + (st?.incidentQueue?.Count ?? -1));
                sb.AppendLine("player faction: " + Faction.OfPlayer?.def?.defName + " (" + Faction.OfPlayer?.Name + ")");

                sb.AppendLine("-- factions --");
                foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
                {
                    if (f.IsPlayer) continue;
                    bool hostile = f.HostileTo(Faction.OfPlayer);
                    bool settlement = map.Tile.Valid && Find.WorldObjects.AnyFactionSettlementOnLayer(f, map.Tile.Layer);
                    bool temp = f.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.OutdoorTemp)
                                && f.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.SeasonalTemp);
                    sb.AppendLine("  " + f.Name + " [" + f.def.defName + "] goodwill " + f.PlayerGoodwill
                        + (hostile ? " HOSTILE" : " friendly/neutral")
                        + (f.Hidden ? " hidden" : "") + (f.defeated ? " DEFEATED" : "") + (f.deactivated ? " DEACTIVATED" : "")
                        + (f.temporary ? " temporary" : "")
                        + ", settlement on layer: " + settlement
                        + ", arrival temp ok: " + temp + " (" + f.def.allowedArrivalTemperatureRange + ")"
                        + ", earliestRaidDays " + f.def.earliestRaidDays
                        + ", traders " + (f.def.caravanTraderKinds?.Count ?? 0));
                }

                sb.AppendLine("-- arrival --");
                sb.AppendLine("  outdoor temp " + map.mapTemperature.OutdoorTemp.ToString("F1") + ", seasonal " + map.mapTemperature.SeasonalTemp.ToString("F1")
                    + ", conditions: " + string.Join(", ", map.gameConditionManager.ActiveConditions.ConvertAll(c => c.def.defName)));
                int edge = 0, standable = 0, reach = 0;
                foreach (IntVec3 c in CellRect.WholeMap(map).EdgeCells)
                {
                    edge++;
                    if (!c.Standable(map)) continue;
                    standable++;
                    if (map.reachability.CanReachColony(c)) reach++;
                }
                sb.AppendLine("  map edge cells " + edge + ", standable " + standable + ", from which a walker reaches the colony " + reach
                    + (reach == 0 ? "  <-- NOBODY CAN WALK IN: raids, traders, visitors and travellers all need this" : ""));
                bool entry = RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 ec, map, CellFinder.EdgeRoadChance_Hostile);
                sb.AppendLine("  pawn entry cell found: " + entry + (entry ? " " + ec : ""));

                sb.AppendLine("-- incidents --");
                Probe(sb, IncidentDefOf.RaidEnemy, map, IncidentCategoryDefOf.ThreatBig);
                RaidReasons(sb, map);
                Probe(sb, IncidentDefOf.TraderCaravanArrival, map, IncidentCategoryDefOf.Misc);
                Probe(sb, IncidentDefOf.VisitorGroup, map, IncidentCategoryDefOf.Misc);
            }
            catch (Exception e) { sb.AppendLine("diagnosis failed: " + e); }
            Log.Message(sb.ToString());
            Messages.Message("Avir: threat diagnosis written to the log.", MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>For each visible hostile faction: the first raid check it fails, step by step, and every mod
        /// that has hooked those checks (a faction that passes all of vanilla's checks but is still refused
        /// is being refused by one of them).</summary>
        private static void RaidReasons(StringBuilder sb, Map map)
        {
            sb.AppendLine("-- why each hostile faction cannot raid --");
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            var worker = (IncidentWorker_RaidEnemy)IncidentDefOf.RaidEnemy.Worker;
            foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f.IsPlayer || f.Hidden || !f.HostileTo(Faction.OfPlayer)) continue;
                string why = null;
                try
                {
                    if (f.defeated) why = "defeated";
                    else if (f.temporary) why = "temporary";
                    else if (!f.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.OutdoorTemp)
                             || !f.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.SeasonalTemp)) why = "too cold or hot to arrive";
                    else if (map.Tile.Valid && !Find.WorldObjects.AnyFactionSettlementOnLayer(f, map.Tile.Layer)) why = "no settlement on this layer";
                    else if (!f.def.arrivalLayerWhitelist.NullOrEmpty() && !f.def.arrivalLayerWhitelist.Contains(map.Tile.LayerDef)) why = "arrival layer whitelist";
                    else if (!f.def.arrivalLayerBlacklist.NullOrEmpty() && f.def.arrivalLayerBlacklist.Contains(map.Tile.LayerDef)) why = "arrival layer blacklist";
                    else if (map.Tile.LayerDef.onlyAllowWhitelistedArrivals && (f.def.arrivalLayerWhitelist.NullOrEmpty() || !f.def.arrivalLayerWhitelist.Contains(map.Tile.LayerDef))) why = "layer allows only whitelisted arrivals";
                    else if (f.deactivated) why = "deactivated";
                    else if (GenDate.DaysPassedSinceSettle < f.def.earliestRaidDays) why = "earliestRaidDays " + f.def.earliestRaidDays;
                    else if (!f.def.raidArrivalLayerWhitelist.NullOrEmpty() && !f.def.raidArrivalLayerWhitelist.Contains(map.Tile.LayerDef)) why = "raid arrival layer whitelist";
                    else if (!f.def.raidArrivalLayerBlacklist.NullOrEmpty() && f.def.raidArrivalLayerBlacklist.Contains(map.Tile.LayerDef)) why = "raid arrival layer blacklist";
                    else
                    {
                        Faction keep = parms.faction;
                        parms.faction = f;
                        var strat = new StringBuilder();
                        bool any = false;
                        foreach (RaidStrategyDef rs in DefDatabase<RaidStrategyDef>.AllDefs)
                        {
                            string r;
                            if (f.def.disallowedRaidStrategies.NotNullAndContains(rs)) r = "disallowed";
                            else if (rs.Worker.SelectionWeightForFaction(map, f, parms.points) <= 0f) r = "weight 0";
                            else if (map.TileInfo.Mutators.Any(m => m.blacklistedRaidStrategies.NotNullAndContains(rs))) r = "tile mutator";
                            else if (!rs.Worker.CanUseWith(parms, null)) r = "refused (mod?)";
                            else { r = "OK"; any = true; }
                            strat.Append(rs.defName + "=" + r + " ");
                        }
                        parms.faction = keep;
                        bool real = false;
                        try { real = worker.FactionCanBeGroupSource(f, parms); } catch (Exception e) { strat.Append(" [threw " + e.GetType().Name + "]"); }
                        why = (real ? "CAN RAID" : any ? "vanilla checks pass but REFUSED - another mod's patch" : "no usable raid strategy")
                              + " | strategies: " + strat;
                    }
                }
                catch (Exception e) { why = "check threw " + e.GetType().Name + ": " + e.Message; }
                sb.AppendLine("  " + f.Name + " [" + f.def.defName + "]: " + why);
            }
            sb.AppendLine("-- mods hooked into these checks --");
            Hooks(sb, AccessTools.Method(typeof(IncidentWorker_RaidEnemy), "FactionCanBeGroupSource"));
            Hooks(sb, AccessTools.Method(typeof(IncidentWorker_PawnsArrive), "FactionCanBeGroupSource"));
            Hooks(sb, AccessTools.Method(typeof(RaidStrategyWorker), "CanUseWith"));
            Hooks(sb, AccessTools.Method(typeof(RaidStrategyWorker), "SelectionWeightForFaction"));
            Hooks(sb, AccessTools.Method(typeof(IncidentWorker), "CanFireNow"));
            Hooks(sb, AccessTools.Method(typeof(IncidentWorker), "TryExecute"));
            Hooks(sb, AccessTools.Method(typeof(IncidentWorker_Raid), "TryExecuteWorker"));
            Hooks(sb, AccessTools.Method(typeof(StorytellerComp), "UsableIncidentsInCategory", new[] { typeof(IncidentCategoryDef), typeof(IncidentParms) }));
            Hooks(sb, AccessTools.Method(typeof(Storyteller), "TryFire"));
        }

        private static void Hooks(StringBuilder sb, MethodBase m)
        {
            if (m == null) return;
            var info = Harmony.GetPatchInfo(m);
            string who = info == null ? "none" : string.Join(", ", info.Owners);
            sb.AppendLine("  " + m.DeclaringType.Name + "." + m.Name + ": " + who);
        }

        private static void Probe(StringBuilder sb, IncidentDef def, Map map, IncidentCategoryDef cat)
        {
            try
            {
                IncidentParms parms = StorytellerUtility.DefaultParmsNow(cat, map);
                parms.forced = false;
                bool can = def.Worker.CanFireNow(parms);
                sb.Append("  " + def.defName + ": CanFireNow " + can + ", points " + parms.points);
                if (def.Worker is IncidentWorker_PawnsArrive pa)
                {
                    int n = 0;
                    foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
                    {
                        bool ok = false;
                        try { ok = pa.FactionCanBeGroupSource(f, parms); } catch { }
                        if (ok) { n++; sb.Append(n == 1 ? ", sources: " : ", "); sb.Append(f.Name); }
                    }
                    if (n == 0) sb.Append(", NO FACTION CAN BE THE SOURCE");
                }
                sb.AppendLine();
            }
            catch (Exception e) { sb.AppendLine("  " + def.defName + ": probe failed: " + e.Message); }
        }
    }
}
