using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(IncidentWorker_NeutralGroup), "TryResolveParmsGeneral")]
public static class Patch_AvirOrderCaravans
{
	public const float OrderShare = 0.35f;

	public static void Prefix(IncidentWorker_NeutralGroup __instance, IncidentParms parms)
	{
		try
		{
			if (parms == null || parms.faction != null)
			{
				return;
			}
			IIncidentTarget target = parms.target;
			Map map = target as Map;
			if (map == null)
			{
				return;
			}
			bool trader = __instance is IncidentWorker_TraderCaravanArrival;
			if ((!trader && !(__instance is IncidentWorker_VisitorGroup)) || !map.mapPawns.FreeColonistsSpawned.Any(AvirBody.IsAvir) || !Rand.Chance(0.35f))
			{
				return;
			}
			PawnGroupKindDef kind = (trader ? PawnGroupKindDefOf.Trader : PawnGroupKindDefOf.Peaceful);
			Faction faction = Find.FactionManager.AllFactionsListForReading.Where((Faction f) => AvirFactionRelations.IsOrder(f) && !f.IsPlayer && !f.defeated && !f.Hidden && !f.temporary && !f.HostileTo(Faction.OfPlayer) && f.def.pawnGroupMakers != null && f.def.pawnGroupMakers.Any((PawnGroupMaker g) => g.kindDef == kind) && (!trader || !f.def.caravanTraderKinds.NullOrEmpty()) && !NeutralGroupIncidentUtility.AnyBlockingHostileLord(map, f)).RandomElementWithFallback();
			if (faction != null)
			{
				parms.faction = faction;
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir] order caravan pick failed: " + ex.Message, 4870976);
		}
	}
}
