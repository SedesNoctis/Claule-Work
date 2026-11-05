using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", new Type[] { typeof(PawnGenerationRequest) })]
public static class Patch_AvirNoBionicsAtBirth
{
	[HarmonyPriority(0)]
	public static void Postfix(Pawn __result)
	{
		try
		{
			if (__result != null && AvirBody.IsAvir(__result))
			{
				Strip(__result);
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir] removing generated bionics failed: " + ex.Message, 4870960);
		}
	}

	public static int Strip(Pawn p)
	{
		if (p?.health?.hediffSet == null)
		{
			return 0;
		}
		List<Hediff> list = p.health.hediffSet.hediffs.Where((Hediff h) => h is Hediff_AddedPart && h.def.spawnThingOnRemoved != null && !h.def.defName.StartsWith("Avir")).ToList();
		foreach (Hediff item in list)
		{
			p.health.RemoveHediff(item);
		}
		if (list.Count > 0)
		{
			Log.Message("[Avir] " + p.LabelShort + ": removed " + string.Join(", ", list.Select((Hediff h) => h.LabelCap)) + " - an Avir is not made with bionics.");
		}
		return list.Count;
	}

	[DebugAction("Avir", "Fix hatched chicks (selected): no bionics, 45 days", false, false, false, false, false, 0, false, allowedGameStates = AllowedGameStates.PlayingOnMap)]
	private static void StripSelected()
	{
		foreach (object selectedObject in Find.Selector.SelectedObjects)
		{
			if (selectedObject is Pawn pawn && AvirBody.IsAvir(pawn))
			{
				Strip(pawn);
				Patch_AvirEarlyHatch.AgeHatchling(pawn);
			}
		}
	}
}
