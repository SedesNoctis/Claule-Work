using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", new Type[] { typeof(PawnGenerationRequest) })]
public static class Patch_AvirRaceFromXenotype
{
	private static PawnKindDef avirKind;

	private static PawnKindDef harpyKind;

	public static bool hatchingCalmHarpy;

	public static bool hatchingAvir;

	[HarmonyPriority(800)]
	public static void Prefix(ref PawnGenerationRequest request)
	{
		if (avirKind == null)
		{
			avirKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Avir_Colonist");
		}
		if (harpyKind == null)
		{
			harpyKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Avir_HarpyCalmKind");
		}
		string text = request.KindDef?.race?.defName;
		if (text == null)
		{
			return;
		}
		XenotypeDef forcedXenotype = request.ForcedXenotype;
		if ((forcedXenotype != null && forcedXenotype.defName == "Avir_HarpyCalm") || hatchingCalmHarpy)
		{
			if (harpyKind != null && text != "Avir_HarpyRace")
			{
				request.KindDef = harpyKind;
			}
		}
		else if ((text == "Human" || text == "Avir_HarpyRace") && AvirBody.IsAvirXenotype(forcedXenotype) && avirKind != null)
		{
			request.KindDef = avirKind;
		}
		else if (hatchingAvir && text == "Human" && avirKind != null)
		{
			request.KindDef = avirKind;
			if (request.ForcedXenotype == null && request.ForcedEndogenes != null)
			{
				request.ForcedXenotype = XenotypeDefOf.Baseliner;
			}
		}
		else if (text == "Human" && forcedXenotype == null && request.ForcedEndogenes == null && request.ForcedXenogenes == null && request.Faction != null && request.Faction.IsPlayer && avirKind != null && request.Faction.def.basicMemberKind?.race == avirKind.race)
		{
			request.KindDef = avirKind;
		}
	}
}
