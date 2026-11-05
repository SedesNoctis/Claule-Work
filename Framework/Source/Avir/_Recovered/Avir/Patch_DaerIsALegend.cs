using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(BiomeDef), "CommonalityOfAnimal")]
public static class Patch_DaerIsALegend
{
	public static void Postfix(PawnKindDef animalDef, ref float __result)
	{
		if (animalDef != null && animalDef.defName == "DankPyon_Daer")
		{
			__result *= 0.2f;
		}
	}
}
