using System;
using System.Linq;
using HarmonyLib;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(Pawn_HealthTracker), "AddHediff", new Type[]
{
	typeof(Hediff),
	typeof(BodyPartRecord),
	typeof(DamageInfo?),
	typeof(DamageWorker.DamageResult)
})]
public static class Patch_LinarianMindHediffs
{
	private static readonly string[] Words = new string[9] { "Thrall", "Dominion", "Dominated", "Mindcontrol", "MindControl", "Suppress", "Intruded", "PsychicSleep", "Enthralled" };

	public static bool Prefix(Pawn_HealthTracker __instance, Hediff hediff, Pawn ___pawn)
	{
		if (hediff?.def == null || !Linarian.Is(___pawn))
		{
			return true;
		}
		string i = hediff.def.defName;
		return !Words.Any((string w) => i.Contains(w));
	}
}
