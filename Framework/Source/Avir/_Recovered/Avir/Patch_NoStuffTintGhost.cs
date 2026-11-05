using HarmonyLib;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(GhostUtility), "GhostGraphicFor")]
public static class Patch_NoStuffTintGhost
{
	public static void Prefix(ThingDef thingDef, ref ThingDef stuff)
	{
		if (stuff != null && NoStuffTint.On(thingDef))
		{
			stuff = null;
		}
	}
}
