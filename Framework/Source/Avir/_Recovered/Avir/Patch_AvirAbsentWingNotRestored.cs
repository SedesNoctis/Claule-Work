using HarmonyLib;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(HealthUtility), "FindBiggestMissingBodyPart")]
public static class Patch_AvirAbsentWingNotRestored
{
	public static void Postfix(Pawn pawn, float minCoverage, ref BodyPartRecord __result)
	{
		if (__result == null || !IsAbsentWing(pawn, __result))
		{
			return;
		}
		__result = null;
		foreach (Hediff_MissingPart missingPartsCommonAncestor in pawn.health.hediffSet.GetMissingPartsCommonAncestors())
		{
			if (missingPartsCommonAncestor.Part != null && !(missingPartsCommonAncestor is Hediff_AvirAbsentWing) && missingPartsCommonAncestor.Part.coverageAbsWithChildren >= minCoverage && !pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(missingPartsCommonAncestor.Part) && (__result == null || missingPartsCommonAncestor.Part.coverageAbsWithChildren > __result.coverageAbsWithChildren))
			{
				__result = missingPartsCommonAncestor.Part;
			}
		}
	}

	private static bool IsAbsentWing(Pawn pawn, BodyPartRecord part)
	{
		foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
		{
			if (hediff is Hediff_AvirAbsentWing && hediff.Part == part)
			{
				return true;
			}
		}
		return false;
	}
}
