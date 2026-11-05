using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// The Avir orders come to a colony they are at peace with, whatever a tech-level filter says. Mods like
    /// Ignorance Is Bliss turn away factions far from the colony's calculated tech level; the orders are Ultra by def,
    /// so a young or medieval colony never saw their visitors or caravans. Run last, after any such filter: a
    /// non-hostile order that passes the game's own basic arrival checks is let back in. Raids are untouched - a
    /// raid needs a hostile faction, and this never lets a hostile one through.
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker_PawnsArrive), nameof(IncidentWorker_PawnsArrive.FactionCanBeGroupSource))]
    public static class Patch_AvirFriendlyVisits
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Faction f, IncidentParms parms, bool desperate, ref bool __result)
        {
            if (__result || f == null || !AvirFactionRelations.IsOrder(f)) return;
            if (f.IsPlayer || f.defeated || f.temporary || f.deactivated || f.HostileTo(Faction.OfPlayer)) return;
            if (!(parms?.target is Map map)) return;
            if (!desperate && (!f.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.OutdoorTemp)
                               || !f.def.allowedArrivalTemperatureRange.Includes(map.mapTemperature.SeasonalTemp))) return;
            __result = true;
        }
    }
}
