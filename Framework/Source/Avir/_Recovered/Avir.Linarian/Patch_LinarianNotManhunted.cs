using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

[HarmonyPatch(typeof(MentalState_Manhunter), "ForceHostileTo", new Type[] { typeof(Thing) })]
public static class Patch_LinarianNotManhunted
{
	public static void Postfix(MentalState_Manhunter __instance, Thing t, ref bool __result)
	{
		if (__result && __instance.pawn.Faction == null && t is Pawn p && Linarian.Is(p))
		{
			__result = false;
		}
	}
}
