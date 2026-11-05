using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(FoodUtility), "IsAcceptablePreyFor")]
public static class Patch_LinarianNotPrey
{
	public static void Postfix(Pawn prey, ref bool __result)
	{
		if (__result && Linarian.Is(prey))
		{
			__result = false;
		}
	}
}
