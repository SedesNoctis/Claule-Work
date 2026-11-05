using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace Avir.Linarian;

[HarmonyPatch(typeof(InspectPaneUtility), "InspectPaneOnGUI")]
public static class Patch_BeastPaneOnGUI
{
	[HarmonyReversePatch(HarmonyReversePatchType.Original)]
	[HarmonyPatch(typeof(InspectPaneUtility), "InspectPaneOnGUI")]
	public static void Vanilla(Rect inRect, IInspectPane pane)
	{
		throw new NotImplementedException();
	}

	[HarmonyPriority(800)]
	public static bool Prefix(Rect inRect, IInspectPane pane)
	{
		if (!LinarianBeastPane.Hers(pane))
		{
			return true;
		}
		try
		{
			Vanilla(inRect, pane);
		}
		catch (NotImplementedException)
		{
			LinarianBeastPane.off = true;
			return true;
		}
		return false;
	}
}
