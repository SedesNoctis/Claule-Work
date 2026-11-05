using Verse;

namespace Avir;

public static class AvirMeleeInstinct
{
	public const float AvirFactor = 1.75f;

	public const float SingerFactor = 0.75f;

	public static float Factor(Pawn p)
	{
		string text = p?.genes?.Xenotype?.defName;
		if (!(text == "Avir"))
		{
			if (!(text == "Avir_Linarian"))
			{
				return 1f;
			}
			return 0.75f;
		}
		return 1.75f;
	}

	public static void DropOldAptitude(Pawn p)
	{
		if (p?.genes == null || Factor(p) == 1f)
		{
			return;
		}
		string[] array = ((Factor(p) != 0.75f) ? new string[1] { "AptitudeStrong_Melee" } : new string[2] { "AptitudeStrong_Melee", "MeleeDamage_Strong" });
		for (int i = 0; i < array.Length; i++)
		{
			GeneDef namedSilentFail = DefDatabase<GeneDef>.GetNamedSilentFail(array[i]);
			Gene gene = ((namedSilentFail == null) ? null : p.genes.GetGene(namedSilentFail));
			if (gene != null)
			{
				p.genes.RemoveGene(gene);
			}
		}
	}
}
