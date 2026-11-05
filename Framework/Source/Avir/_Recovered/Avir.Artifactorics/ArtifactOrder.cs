using Verse;

namespace Avir.Artifactorics;

public class ArtifactOrder : IExposable
{
	public ArtifactOrderKind kind;

	public ArtifactOrderStage stage;

	public Thing artifact;

	public Thing target;

	public Pawn wearer;

	public Building_ArtificerSpot spot;

	public bool allowNonAvir;

	public Pawn artisan;

	public int createdTick;

	public int returnSinceTick;

	public void ExposeData()
	{
		Scribe_Values.Look(ref kind, "kind", ArtifactOrderKind.Inlay);
		Scribe_Values.Look(ref stage, "stage", ArtifactOrderStage.WaitingForWearer);
		Scribe_References.Look(ref artifact, "artifact");
		Scribe_References.Look(ref target, "target");
		Scribe_References.Look(ref wearer, "wearer");
		Scribe_References.Look(ref spot, "spot");
		Scribe_References.Look(ref artisan, "artisan");
		Scribe_Values.Look(ref allowNonAvir, "allowNonAvir", defaultValue: false);
		Scribe_Values.Look(ref createdTick, "createdTick", 0);
		Scribe_Values.Look(ref returnSinceTick, "returnSinceTick", 0);
	}

	public string Describe()
	{
		string text = target?.LabelNoCount ?? "an item";
		string text2 = ((wearer != null) ? (" (" + wearer.LabelShort + "'s)") : "");
		string text3 = stage switch
		{
			ArtifactOrderStage.WaitingForWearer => "waiting for " + (wearer?.LabelShort ?? "the wearer") + " to bring it", 
			ArtifactOrderStage.Ready => "waiting for an artisan", 
			ArtifactOrderStage.ReturnToWearer => "returning to " + (wearer?.LabelShort ?? "the wearer"), 
			_ => "done", 
		};
		if (kind == ArtifactOrderKind.Inlay)
		{
			return "Inlay " + (artifact?.LabelNoCount ?? "artifact") + " into " + text + text2 + " - " + text3;
		}
		return "Extract the artifact from " + text + text2 + (allowNonAvir ? " (anyone, destroys it)" : " (Avir only)") + " - " + text3;
	}
}
