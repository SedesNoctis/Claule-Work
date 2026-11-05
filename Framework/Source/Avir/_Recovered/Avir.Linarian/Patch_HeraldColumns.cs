using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch]
public static class Patch_HeraldColumns
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		MethodBase methodBase = AccessTools.Method(typeof(PawnColumnWorker_Tame), "HasCheckbox");
		MethodBase b = AccessTools.Method(typeof(PawnColumnWorker_Hunt), "HasCheckbox");
		if (methodBase != null)
		{
			yield return methodBase;
		}
		if (b != null)
		{
			yield return b;
		}
	}

	public static void Postfix(Pawn pawn, ref bool __result)
	{
		if (!__result && LinarianPresence.Disguised(pawn))
		{
			__result = pawn.Spawned;
		}
	}
}
