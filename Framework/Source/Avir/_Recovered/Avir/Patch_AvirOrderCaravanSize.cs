using HarmonyLib;
using RimWorld;

namespace Avir;

[HarmonyPatch(typeof(IncidentWorker_TraderCaravanArrival), "ResolveParmsPoints")]
public static class Patch_AvirOrderCaravanSize
{
	public const float Divisor = 1.5f;

	public static void Postfix(IncidentParms parms)
	{
		if (parms?.faction != null && AvirFactionRelations.IsOrder(parms.faction))
		{
			parms.points /= 1.5f;
		}
	}
}
