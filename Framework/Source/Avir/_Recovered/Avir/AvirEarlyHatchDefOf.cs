using RimWorld;
using Verse;

namespace Avir;

[DefOf]
public static class AvirEarlyHatchDefOf
{
	public static GeneDef Avir_EggLaying;

	static AvirEarlyHatchDefOf()
	{
		DefOfHelper.EnsureInitializedInCtor(typeof(AvirEarlyHatchDefOf));
	}
}
