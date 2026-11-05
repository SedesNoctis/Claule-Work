using RimWorld;
using Verse;

namespace Avir;

[DefOf]
public static class AvirTonicDefOf
{
	public static JobDef Avir_GiveTonic;

	static AvirTonicDefOf()
	{
		DefOfHelper.EnsureInitializedInCtor(typeof(AvirTonicDefOf));
	}
}
