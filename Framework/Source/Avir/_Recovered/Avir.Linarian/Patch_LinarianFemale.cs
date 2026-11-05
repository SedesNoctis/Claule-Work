using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(PawnBioAndNameGenerator), "GiveAppropriateBioAndNameTo")]
public static class Patch_LinarianFemale
{
	public static void Prefix(Pawn pawn, XenotypeDef xenotype)
	{
		if (pawn != null && xenotype != null && xenotype == LinarianDefOf.Avir_Linarian && pawn.gender == Gender.Male)
		{
			pawn.gender = Gender.Female;
		}
	}
}
