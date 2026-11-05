using System;
using HarmonyLib;
using RimWorld;

namespace Avir.Linarian;

[HarmonyPatch(typeof(InspectPaneUtility), "PaneWidthFor")]
public static class Patch_BeastPaneWidth
{
	[HarmonyReversePatch(HarmonyReversePatchType.Original)]
	[HarmonyPatch(typeof(InspectPaneUtility), "PaneWidthFor")]
	public static float Vanilla(IInspectPane pane)
	{
		throw new NotImplementedException();
	}

	[HarmonyPriority(800)]
	public static bool Prefix(IInspectPane pane, ref float __result)
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
