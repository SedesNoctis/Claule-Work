using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(TendUtility), "DoTend")]
public static class Patch_AvirExtractTend
{
	public static void Prefix(Medicine medicine, out bool __state)
	{
		__state = medicine != null && medicine.def == AvirExtractMedicine.Extract;
	}

	public static void Postfix(Pawn patient, bool __state)
	{
		if (__state && patient?.health != null && AvirExtractMedicine.Sated != null && AvirBody.IsAvir(patient))
		{
			Hediff firstHediffOfDef = patient.health.hediffSet.GetFirstHediffOfDef(AvirExtractMedicine.Sated);
			if (firstHediffOfDef == null)
			{
				patient.health.AddHediff(AvirExtractMedicine.Sated);
			}
			else
			{
				firstHediffOfDef.Severity = 1f;
			}
		}
	}
}
