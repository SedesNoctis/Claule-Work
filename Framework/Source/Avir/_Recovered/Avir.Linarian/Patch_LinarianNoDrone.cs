using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(ThoughtWorker_PsychicDrone), "CurrentStateInternal")]
public static class Patch_LinarianNoDrone
{
	public static void Postfix(Pawn p, ref ThoughtState __result)
	{
		if (__result.Active && Linarian.Is(p))
		{
			__result = ThoughtState.Inactive;
		}
	}
}
