using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(MainTabWindow_Inspect), "PaneTopY", MethodType.Getter)]
public static class Patch_BeastPaneTop
{
	[HarmonyPriority(800)]
	public static bool Prefix(ref float __result)
	{
		if (!LinarianBeastPane.Hers())
		{
			return true;
		}
		__result = (float)Verse.UI.screenHeight - 165f - 35f;
		return false;
	}
}
