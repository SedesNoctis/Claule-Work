using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(Ability), "CanApplyOn", new Type[] { typeof(LocalTargetInfo) })]
public static class Patch_LinarianPsychicImmunity
{
	public static void Postfix(Ability __instance, LocalTargetInfo target, ref bool __result)
	{
		if (__result && target.Thing is Pawn pawn && pawn != __instance.pawn && Linarian.Is(pawn) && Linarian.IsPsychic(__instance.def))
		{
			__result = false;
		}
	}
}
