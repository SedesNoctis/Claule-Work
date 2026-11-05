using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch]
public static class Patch_StandInLogAndSocial
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		MethodBase methodBase = AccessTools.PropertyGetter(typeof(ITab_Pawn_Log), "SelPawnForCombatInfo");
		MethodBase b = AccessTools.PropertyGetter(typeof(ITab_Pawn_Social), "SelPawnForSocialInfo");
		if (methodBase != null)
		{
			yield return methodBase;
		}
		if (b != null)
		{
			yield return b;
		}
	}

	public static void Postfix(ref Pawn __result)
	{
		if (__result != null && LinarianPresence.Disguised(__result))
		{
			__result = LinarianStandIn.For(__result) ?? __result;
		}
	}
}
