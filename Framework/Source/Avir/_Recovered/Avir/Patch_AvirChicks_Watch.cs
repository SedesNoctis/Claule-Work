using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(SkillRecord), "Learn")]
public static class Patch_AvirChicks_Watch
{
	private static readonly AccessTools.FieldRef<SkillRecord, Pawn> PawnOf = AccessTools.FieldRefAccess<SkillRecord, Pawn>("pawn");

	private static bool inside;

	public static void Postfix(SkillRecord __instance, float xp)
	{
		if (inside || xp <= 0f || AvirChicks.ChicksOf.Count == 0)
		{
			return;
		}
		Pawn pawn = PawnOf(__instance);
		if (pawn == null || !AvirChicks.ChicksOf.TryGetValue(pawn, out var value))
		{
			return;
		}
		inside = true;
		try
		{
			foreach (Pawn item in value)
			{
				item.skills?.GetSkill(__instance.def)?.Learn(xp * 0.1f, direct: true);
			}
		}
		finally
		{
			inside = false;
		}
	}
}
