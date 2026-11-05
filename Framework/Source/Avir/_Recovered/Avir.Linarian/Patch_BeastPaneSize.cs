using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace Avir.Linarian;

[HarmonyPatch(typeof(InspectPaneUtility), "PaneSizeFor")]
public static class Patch_BeastPaneSize
{
	[HarmonyReversePatch(HarmonyReversePatchType.Original)]
	[HarmonyPatch(typeof(InspectPaneUtility), "PaneSizeFor")]
	public static Vector2 Vanilla(IInspectPane pane)
	{
		throw new NotImplementedException();
	}

	[HarmonyPriority(800)]
	public static bool Prefix(IInspectPane pane, ref Vector2 __result)
	{
		if (!LinarianBeastPane.Hers(pane))
		{
			return true;
		}
		try
		{
			__result = Vanilla(pane);
		}
		catch (NotImplementedException)
		{
			LinarianBeastPane.off = true;
			return true;
		}
		return false;
	}
}
