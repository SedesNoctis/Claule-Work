using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Spire;

[HarmonyPatch(typeof(GravshipUtility), "GetPlayerGravEngine_NewTemp")]
public static class Patch_SpireIsThePlayersEngine
{
	public static void Postfix(Map map, ref Building_GravEngine __result)
	{
		if (__result == null && map != null)
		{
			__result = SpireAsGravship.On(map);
		}
	}
}
