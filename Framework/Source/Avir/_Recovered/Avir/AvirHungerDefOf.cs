using RimWorld;
using Verse;

namespace Avir;

[DefOf]
public static class AvirHungerDefOf
{
	public static GeneDef Avir_ArcaneHunger;

	static AvirHungerDefOf()
	{
		DefOfHelper.EnsureInitializedInCtor(typeof(AvirHungerDefOf));
	}
}
