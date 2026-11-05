using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", new Type[] { typeof(PawnGenerationRequest) })]
public static class Patch_AvirNestingJoinersAreAvir
{
	public static void Prefix(ref PawnGenerationRequest request)
	{
		Faction faction = request.Faction;
		PawnKindDef kindDef = request.KindDef;
		if (kindDef?.race?.race == null || !kindDef.race.race.Humanlike || faction?.def == null || (!(faction.def.defName == "Avir_FreeNesting") && !AvirFactionRelations.IsOrder(faction)))
		{
			return;
		}
		string defName = kindDef.race.defName;
		if (!(defName == "Avir_Race") && !(defName == "Avir_HarpyRace") && defName.IndexOf("Illithid", StringComparison.OrdinalIgnoreCase) < 0 && !request.IsCreepJoiner && (request.AllowedDevelopmentalStages & DevelopmentalStage.Newborn) == 0)
		{
			PawnKindDef pawnKindDef = faction.def.basicMemberKind;
			if (pawnKindDef?.race == null || pawnKindDef.race.defName != "Avir_Race")
			{
				pawnKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail("Avir_Colonist");
			}
			if (pawnKindDef != null)
			{
				request.KindDef = pawnKindDef;
			}
		}
	}
}
