using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(TendUtility), "DoTend")]
public static class Patch_AvirTendHediff
{
	public static void Prefix(Medicine medicine, out HediffDef __state)
	{
		__state = medicine?.def.GetModExtension<AvirTendHediff>()?.hediff;
	}

	public static void Postfix(Pawn patient, HediffDef __state)
	{
		if (__state != null && patient?.health != null)
		{
			Hediff firstHediffOfDef = patient.health.hediffSet.GetFirstHediffOfDef(__state);
			if (firstHediffOfDef == null)
			{
				patient.health.AddHediff(__state);
			}
			else
			{
				firstHediffOfDef.TryGetComp<HediffComp_Disappears>()?.ResetElapsedTicks();
			}
		}
	}
}
