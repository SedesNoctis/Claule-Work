using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(Thing), "GetInspectTabs")]
public static class Patch_HeraldNoTabs
{
	public static void Postfix(Thing __instance, ref IEnumerable<InspectTabBase> __result)
	{
		if (__instance is Pawn p && LinarianPresence.Disguised(p))
		{
			__result = (__result ?? Enumerable.Empty<InspectTabBase>()).Where((InspectTabBase t) => t is ITab_Pawn_Log || t is ITab_Pawn_Social || t is ITab_Pawn_Health).ToList();
		}
	}
}
