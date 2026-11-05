using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch]
public static class Patch_HeraldInspectPane
{
	private static MethodBase TargetMethod()
	{
		return AccessTools.Method(typeof(InspectPaneFiller), "DoPaneContentsFor");
	}

	private static bool Prepare()
	{
		return TargetMethod() != null;
	}

	public static bool Prefix(ISelectable sel, Rect rect)
	{
		if (!(sel is Pawn pawn) || !LinarianPresence.Disguised(pawn))
		{
			return true;
		}
		Rect rect2 = rect.ContractedBy(4f);
		float y = rect2.y;
		Text.Font = GameFont.Small;
		Widgets.Label(new Rect(rect2.x, y, rect2.width, 22f), "Female " + HeraldLabels.Beast(pawn) + ", wild");
		y += 22f;
		float f = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
		Widgets.Label(new Rect(rect2.x, y, rect2.width, 22f), "Health: " + f.ToStringPercent());
		y += 22f;
		Widgets.Label(new Rect(rect2.x, y, rect2.width, 22f), LinarianDisguise.Activity(pawn));
		y += 26f;
		GUI.color = new Color(0.8f, 0.8f, 0.8f);
		Text.Font = GameFont.Tiny;
		Widgets.Label(new Rect(rect2.x, y, rect2.width, rect2.yMax - y), LinarianDisguise.Description(pawn));
		Text.Font = GameFont.Small;
		GUI.color = Color.white;
		return false;
	}
}
