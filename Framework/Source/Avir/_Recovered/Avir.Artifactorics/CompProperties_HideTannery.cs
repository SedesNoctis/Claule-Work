namespace Avir.Artifactorics;

public class CompProperties_HideTannery : CompProperties_ArtifactAura
{
	public int batchMin = 3;

	public int batchMax = 5;

	public int queueCapacity = 20;

	public float hoursPerBatch = 6f;

	public string hideCategory = "DankPyon_Hides";

	public CompProperties_HideTannery()
	{
		compClass = typeof(CompHideTannery);
		baseRange = 0f;
		rangePerQuality = 0f;
	}
}
