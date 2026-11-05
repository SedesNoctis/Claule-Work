using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(Pawn), "GetGizmos")]
public static class Patch_HeraldGizmos
{
	private static Texture2D hunt;

	private static Texture2D tame;

	[HarmonyPriority(0)]
	public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
	{
		if (LinarianPresence.Disguised(__instance))
		{
			Pawn her = __instance;
			hunt = hunt ?? ContentFinder<Texture2D>.Get("UI/Designators/Hunt", reportFailure: false);
			tame = tame ?? ContentFinder<Texture2D>.Get("UI/Designators/Tame", reportFailure: false);
			__result = new List<Gizmo>
			{
				new Command_Action
				{
					defaultLabel = "DesignatorHunt".Translate(),
					defaultDesc = "DesignatorHuntDesc".Translate(),
					icon = hunt,
					action = delegate
					{
						HeraldDesignations.Set(her, DesignationDefOf.Hunt);
					}
				},
				new Command_Action
				{
					defaultLabel = "DesignatorTame".Translate(),
					defaultDesc = "DesignatorTameDesc".Translate(),
					icon = tame,
					action = delegate
					{
						HeraldDesignations.Set(her, DesignationDefOf.Tame);
					}
				}
			};
		}
	}
}
