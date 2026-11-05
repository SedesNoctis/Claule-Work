using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", new Type[] { typeof(PawnGenerationRequest) })]
public static class Patch_LinarianArcaneDependence
{
	public static void Postfix(Pawn __result)
	{
		Gene_LinarianSong gene_LinarianSong = Linarian.Song(__result);
		if (gene_LinarianSong != null && __result.story?.traits != null && LinarianDefOf.Avir_ArcaneDependence != null && Rand.Chance(0.03f))
		{
			if (!__result.story.traits.HasTrait(LinarianDefOf.Avir_ArcaneDependence))
			{
				__result.story.traits.GainTrait(new Trait(LinarianDefOf.Avir_ArcaneDependence));
			}
			gene_LinarianSong.shadow = true;
		}
	}
}
