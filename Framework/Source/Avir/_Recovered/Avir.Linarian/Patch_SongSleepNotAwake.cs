using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(RestUtility), "Awake")]
public static class Patch_SongSleepNotAwake
{
	public static void Postfix(Pawn __0, ref bool __result)
	{
		if (__result && LinarianHediffs.Avir_SongSleep != null && __0 != null && __0.health?.hediffSet?.HasHediff(LinarianHediffs.Avir_SongSleep) == true)
		{
			__result = false;
		}
	}
}
