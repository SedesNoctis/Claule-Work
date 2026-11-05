using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(RecipeWorker), "AvailableOnNow")]
public static class Patch_AvirExtractAdministerAvirOnly
{
	public static void Postfix(RecipeWorker __instance, Thing thing, ref bool __result)
	{
		if (__result && __instance is Recipe_AdministerIngestible && AvirExtractMedicine.Extract != null)
		{
			RecipeDef recipe = __instance.recipe;
			if (recipe != null && recipe.fixedIngredientFilter?.Allows(AvirExtractMedicine.Extract) == true && (!(thing is Pawn p) || !AvirBody.IsAvir(p)))
			{
				__result = false;
			}
		}
	}
}
