using System;
using HarmonyLib;
using RimWorld;

namespace Avir.Linarian;

[HarmonyPatch(typeof(InspectPaneUtility), "DoTabs")]
public static class Patch_BeastPaneTabs
{
	[HarmonyReversePatch(HarmonyReversePatchType.Original)]
	[HarmonyPatch(typeof(InspectPaneUtility), "DoTabs")]
	public static void Vanilla(IInspectPane pane)
	{
		throw new NotImplementedException();
	}

	[HarmonyPriority(800)]
	public static bool Prefix(IInspectPane pane)
	{
		if (!LinarianBeastPane.Hers(pane))
		{
			return true;
		}
		try
		{
			Vanilla(pane);
		}
		catch (NotImplementedException)
		{
			LinarianBeastPane.off = true;
			return true;
		}
		return false;
	}
}
