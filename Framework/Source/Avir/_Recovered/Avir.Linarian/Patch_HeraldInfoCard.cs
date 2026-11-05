using HarmonyLib;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(WindowStack), "Add")]
public static class Patch_HeraldInfoCard
{
	[HarmonyPriority(800)]
	public static bool Prefix(Window window, WindowStack __instance)
	{
		if (!(window is Dialog_InfoCard))
		{
			return true;
		}
		if (!(Traverse.Create(window).Field("thing").GetValue() is Pawn pawn) || !LinarianPresence.Disguised(pawn))
		{
			return true;
		}
		Pawn pawn2 = LinarianStandIn.For(pawn);
		if (pawn2 != null)
		{
			__instance.Add(new Dialog_InfoCard(pawn2));
		}
		return false;
	}
}
