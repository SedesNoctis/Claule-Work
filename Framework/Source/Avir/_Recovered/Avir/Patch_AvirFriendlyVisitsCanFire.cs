using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(IncidentWorker_PawnsArrive), "CanFireNowSub")]
public static class Patch_AvirFriendlyVisitsCanFire
{
	[HarmonyPriority(0)]
	public static void Postfix(IncidentWorker_PawnsArrive __instance, IncidentParms parms, ref bool __result)
	{
		if (__result || !(__instance is IncidentWorker_NeutralGroup) || !(parms?.target is Map))
		{
			return;
		}
		try
		{
			if (parms.faction != null)
			{
				if (AvirFactionRelations.IsOrder(parms.faction) && __instance.FactionCanBeGroupSource(parms.faction, parms))
				{
					__result = true;
				}
				return;
			}
			foreach (Faction item in Find.FactionManager.AllFactionsListForReading)
			{
				if (AvirFactionRelations.IsOrder(item) && __instance.FactionCanBeGroupSource(item, parms))
				{
					__result = true;
					break;
				}
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir] order arrival gate failed: " + ex.Message, 4870977);
		}
	}
}
