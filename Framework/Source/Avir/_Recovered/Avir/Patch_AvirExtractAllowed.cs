using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(MedicalCareUtility), "AllowsMedicine")]
public static class Patch_AvirExtractAllowed
{
	public static void Postfix(ThingDef meds, ref bool __result)
	{
		if (!__result || meds == null || meds != AvirExtractMedicine.Extract)
		{
			return;
		}
		if (AvirExtractMedicine.excludeExtract)
		{
			__result = false;
			return;
		}
		Pawn patient = AvirExtractMedicine.patient;
		if (patient != null && !AvirBody.IsAvir(patient))
		{
			__result = false;
		}
	}
}
