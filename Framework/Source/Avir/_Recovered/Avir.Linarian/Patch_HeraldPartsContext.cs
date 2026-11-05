using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(HealthCardUtility), "DrawHediffListing")]
public static class Patch_HeraldPartsContext
{
	public static void Prefix(Pawn pawn)
	{
		HeraldParts.current = pawn;
	}

	public static void Finalizer()
	{
		HeraldParts.current = null;
	}
}
