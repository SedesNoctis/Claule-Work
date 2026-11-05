using System.Linq;
using HarmonyLib;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(Pawn), "Kill")]
public static class Patch_NoScarOnTheDead
{
	public static void Postfix(Pawn __instance)
	{
		if (!__instance.Dead || __instance.health?.hediffSet == null || LinarianHediffs.Avir_PhantomScar == null)
		{
			return;
		}
		foreach (Hediff item in __instance.health.hediffSet.hediffs.Where((Hediff h) => h.def == LinarianHediffs.Avir_PhantomScar || h.def == LinarianHediffs.Avir_ScarFlare).ToList())
		{
			__instance.health.RemoveHediff(item);
		}
	}
}
