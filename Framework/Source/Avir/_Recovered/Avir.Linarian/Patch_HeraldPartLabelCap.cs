using HarmonyLib;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(BodyPartRecord), "LabelCap", MethodType.Getter)]
public static class Patch_HeraldPartLabelCap
{
	public static void Postfix(BodyPartRecord __instance, ref string __result)
	{
		__result = HeraldParts.Rename(__instance, __result);
	}
}
