using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir;

[HarmonyPatch(typeof(HealthAIUtility), "FindBestMedicine")]
public static class Patch_AvirExtractFindMedicine
{
	public static void Prefix(Pawn patient, out Pawn __state)
	{
		__state = AvirExtractMedicine.patient;
		AvirExtractMedicine.patient = patient;
	}

	public static void Postfix(Pawn healer, Pawn patient, bool onlyUseInventory, Pawn __state, ref Thing __result)
	{
		try
		{
			ThingDef extract = AvirExtractMedicine.Extract;
			if (extract == null || healer == null || patient == null)
			{
				return;
			}
			bool flag = AvirBody.IsAvir(patient);
			if (__result != null && __result.def == extract && (!flag || !(__result is Medicine)) && !AvirExtractMedicine.excludeExtract)
			{
				AvirExtractMedicine.excludeExtract = true;
				try
				{
					__result = HealthAIUtility.FindBestMedicine(healer, patient, onlyUseInventory);
				}
				finally
				{
					AvirExtractMedicine.excludeExtract = false;
				}
			}
			if (!(!flag || onlyUseInventory) && AvirExtractMedicine.NeedsImmunity(patient) && patient.MapHeld != null && (patient.playerSettings == null || patient.playerSettings.medCare.AllowsMedicine(extract)))
			{
				Thing thing = GenClosest.ClosestThing_Global_Reachable(patient.PositionHeld, patient.MapHeld, patient.MapHeld.listerThings.ThingsOfDef(extract), PathEndMode.ClosestTouch, TraverseParms.For(healer), 9999f, (Thing t) => t is Medicine && !t.IsForbidden(healer) && healer.CanReserve(t, 10, 1));
				if (thing != null)
				{
					__result = thing;
				}
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir] extract medicine choice failed: " + ex.Message, 4870992);
		}
		finally
		{
			AvirExtractMedicine.patient = __state;
		}
	}
}
