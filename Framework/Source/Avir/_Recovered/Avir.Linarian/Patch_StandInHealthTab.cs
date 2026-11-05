using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch]
public static class Patch_StandInHealthTab
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.PropertyGetter(typeof(ITab_Pawn_Health), "PawnForHealth");
	}

	private static bool Prepare()
	{
		return TargetMethod() != null;
	}

	public static void Postfix(ref Pawn __result)
	{
		if (__result != null && LinarianPresence.Disguised(__result))
		{
			__result = LinarianStandIn.For(__result) ?? __result;
		}
	}
}
