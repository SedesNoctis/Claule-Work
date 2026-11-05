using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(SkillRecord), "Learn")]
public static class Patch_AvirMeleeInstinct
{
	public static void Prefix(SkillRecord __instance, ref float xp, bool direct, Pawn ___pawn)
	{
		if (!direct && !(xp <= 0f) && __instance.def == SkillDefOf.Melee)
		{
			float num = AvirMeleeInstinct.Factor(___pawn);
			if (num != 1f)
			{
				xp *= num;
			}
		}
	}
}
