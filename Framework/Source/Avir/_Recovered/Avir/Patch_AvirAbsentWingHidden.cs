using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(HealthCardUtility), "VisibleHediffs")]
public static class Patch_AvirAbsentWingHidden
{
	public static IEnumerable<Hediff> Postfix(IEnumerable<Hediff> __result)
	{
		foreach (Hediff item in __result)
		{
			if (!(item is Hediff_AvirAbsentWing))
			{
				yield return item;
			}
		}
	}
}
