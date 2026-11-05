using RimWorld;
using Verse;

namespace Avir.Artifactorics;

[DefOf]
public static class ArtifactoricsJobDefOf
{
	public static JobDef Avir_ArtifactBringGarment;

	public static JobDef Avir_ArtifactInlay;

	public static JobDef Avir_ArtifactExtract;

	public static JobDef Avir_ArtifactAttune;

	static ArtifactoricsJobDefOf()
	{
		DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactoricsJobDefOf));
	}
}
