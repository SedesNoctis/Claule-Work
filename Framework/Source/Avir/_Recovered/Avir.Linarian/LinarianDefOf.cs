using RimWorld;
using Verse;

namespace Avir.Linarian;

[DefOf]
public static class LinarianDefOf
{
	public static GeneDef Avir_LinarianSong;

	public static XenotypeDef Avir_Linarian;

	public static TraitDef Avir_ArcaneDependence;

	static LinarianDefOf()
	{
		DefOfHelper.EnsureInitializedInCtor(typeof(LinarianDefOf));
	}
}
