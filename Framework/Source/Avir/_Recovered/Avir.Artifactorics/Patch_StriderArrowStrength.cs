using HarmonyLib;
using Verse;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(Pawn), "PreApplyDamage")]
public static class Patch_StriderArrowStrength
{
	public static void Prefix(Pawn __instance, ref DamageInfo dinfo)
	{
		if (StriderHit.IsArrow(dinfo, out var archer, out var bow))
		{
			float num = StriderPaths.Power(bow, archer);
			float num2 = 1f;
			int stones = bow.Stones;
			if (bow.path == "Hunt" && stones >= 9 && (__instance.RaceProps.Animal || __instance.health.summaryHealth.SummaryHealthPercent < 0.999f))
			{
				num2 += 0.3f * num;
			}
			if (bow.path == "Moon" && stones >= 3 && archer.Spawned && archer.Map.glowGrid.GroundGlowAt(archer.Position) < 0.3f)
			{
				num2 += 0.25f * num;
			}
			if (num2 > 1f)
			{
				dinfo.SetAmount(dinfo.Amount * num2);
			}
		}
	}
}
