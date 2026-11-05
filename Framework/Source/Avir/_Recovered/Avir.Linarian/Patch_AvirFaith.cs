using System;
using HarmonyLib;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", new Type[] { typeof(PawnGenerationRequest) })]
public static class Patch_AvirFaith
{
	public static void Postfix(Pawn __result)
	{
		AvirFaith.Settle(__result);
	}
}
