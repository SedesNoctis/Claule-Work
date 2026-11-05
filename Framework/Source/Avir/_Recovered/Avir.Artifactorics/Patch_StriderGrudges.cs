using HarmonyLib;
using Verse;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(Pawn), "PostApplyDamage")]
public static class Patch_StriderGrudges
{
	public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
	{
		if (!(totalDamageDealt <= 0f) && __instance.Faction != null && dinfo.Instigator is Pawn pawn && pawn.Faction != __instance.Faction)
		{
			GameComponent_StriderGrudges.Instance?.Add(pawn, __instance.Faction, totalDamageDealt);
		}
	}
}
