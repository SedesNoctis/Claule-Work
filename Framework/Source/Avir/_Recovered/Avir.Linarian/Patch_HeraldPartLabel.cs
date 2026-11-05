using HarmonyLib;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(BodyPartRecord), "Label", MethodType.Getter)]
public static class Patch_HeraldPartLabel
{
	public static void Postfix(BodyPartRecord __instance, ref string __result)
	{
		__result = HeraldParts.Rename(__instance, __result);
	}
}
