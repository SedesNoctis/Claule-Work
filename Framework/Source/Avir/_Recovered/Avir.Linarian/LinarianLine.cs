using System;
using System.Linq;
using RimWorld;
using Verse;

namespace Avir.Linarian;

public static class LinarianLine
{
	public static void Apply(Pawn child, Pawn mother, Pawn father)
	{
		try
		{
			if (child?.genes == null || !Linarian.Is(mother))
			{
				return;
			}
			GeneDef song = LinarianDefOf.Avir_LinarianSong;
			XenotypeDef avir_Linarian = LinarianDefOf.Avir_Linarian;
			if (child.gender == Gender.Female)
			{
				if (child.genes.GetGene(song) == null)
				{
					child.genes.AddGene(song, xenogene: false);
				}
				child.genes.SetXenotypeDirect(avir_Linarian);
				return;
			}
			foreach (Gene item in child.genes.GenesListForReading.Where((Gene g) => g.def == song).ToList())
			{
				child.genes.RemoveGene(item);
			}
			XenotypeDef xenotypeDef = father?.genes?.Xenotype;
			if (xenotypeDef != null && xenotypeDef != avir_Linarian)
			{
				child.genes.SetXenotypeDirect(xenotypeDef);
				return;
			}
			XenotypeDef namedSilentFail = DefDatabase<XenotypeDef>.GetNamedSilentFail("Avir");
			if (namedSilentFail != null)
			{
				child.genes.SetXenotypeDirect(namedSilentFail);
			}
		}
		catch (Exception ex)
		{
			Log.WarningOnce("[Avir] a Singer's line failed: " + ex.Message, 4874497);
		}
	}
}
