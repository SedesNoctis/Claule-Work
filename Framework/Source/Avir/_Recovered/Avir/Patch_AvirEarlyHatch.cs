using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", new Type[] { typeof(PawnGenerationRequest) })]
public static class Patch_AvirEarlyHatch
{
	public const int HatchAgeDays = 45;

	private const long HatchAgeTicks = 2700000L;

	private const float NewbornThresholdYears = 0.02f;

	public static void Postfix(Pawn __result)
	{
		try
		{
			if (__result != null && !__result.Dead && __result.ageTracker != null && __result.genes != null && __result.genes.HasActiveGene(AvirEarlyHatchDefOf.Avir_EggLaying) && !(__result.ageTracker.AgeBiologicalYearsFloat >= 0.02f) && __result.ageTracker.AgeBiologicalTicks < 2700000)
			{
				__result.ageTracker.AgeBiologicalTicks = 2700000L;
				RefreshLifeStage(__result);
				CoinFlipGender(__result);
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir] Early-hatch age adjustment failed: " + ex, 4870583);
		}
	}

	private static void CoinFlipGender(Pawn pawn)
	{
		try
		{
			XenotypeDef xenotype = pawn.genes.Xenotype;
			if (xenotype != null && (xenotype == Patch_AvirHarpyBirth.Wild || xenotype == Patch_AvirHarpyBirth.Calm))
			{
				return;
			}
			Gender gender = (Rand.Bool ? Gender.Male : Gender.Female);
			if (pawn.gender != gender)
			{
				pawn.gender = gender;
				if (pawn.Drawer != null && pawn.Drawer.renderer != null)
				{
					pawn.Drawer.renderer.SetAllGraphicsDirty();
				}
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir] hatchling gender coin flip failed: " + ex, 4870595);
		}
	}

	public static void AgeHatchling(Pawn pawn)
	{
		try
		{
			if (pawn?.ageTracker != null && AvirFaction.HasFearborn(pawn) && pawn.ageTracker.AgeBiologicalTicks < 2700000)
			{
				pawn.ageTracker.AgeBiologicalTicks = 2700000L;
				RefreshLifeStage(pawn);
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir] hatchling age after hatching failed: " + ex.Message, 4870962);
		}
	}

	private static void RefreshLifeStage(Pawn pawn)
	{
		MethodInfo methodInfo = AccessTools.Method(typeof(Pawn_AgeTracker), "RecalculateLifeStageIndex");
		if (methodInfo != null)
		{
			methodInfo.Invoke(pawn.ageTracker, null);
		}
		if (pawn.Drawer != null && pawn.Drawer.renderer != null)
		{
			pawn.Drawer.renderer.SetAllGraphicsDirty();
		}
	}
}
