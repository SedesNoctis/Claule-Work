using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(Pawn), "GetGizmos")]
public static class Patch_AvirChicks_Gizmos
{
	private static Texture2D icon;

	public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn __instance)
	{
		foreach (Gizmo gizmo in gizmos)
		{
			yield return gizmo;
		}
		if (__instance.Faction != Faction.OfPlayer || !__instance.Spawned || !AvirChicks.IsGrownAvir(__instance))
		{
			yield break;
		}
		if (icon == null)
		{
			icon = ContentFinder<Texture2D>.Get("UI/Commands/Avir/PutChicksDown", reportFailure: false) ?? ContentFinder<Texture2D>.Get("UI/Commands/DropCarriedPawn", reportFailure: false) ?? BaseContent.BadTex;
		}
		AvirChicks.ChicksOf.TryGetValue(__instance, out var mine);
		if (mine == null || mine.Count <= 0)
		{
			yield break;
		}
		yield return new Command_Action
		{
			defaultLabel = "Put the chicks down",
			defaultDesc = "Set the chicks on the back down here.",
			icon = icon,
			action = delegate
			{
				foreach (Pawn item in mine.ToList())
				{
					AvirChicks.SetDownByHand(item);
				}
			}
		};
	}
}
