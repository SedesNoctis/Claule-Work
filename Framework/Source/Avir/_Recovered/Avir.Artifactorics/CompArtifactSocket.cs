using System.Linq;
using RimWorld;
using Verse;

namespace Avir.Artifactorics;

public class CompArtifactSocket : ThingComp
{
	public ArtifactState inlaid;

	public bool HasArtifact => inlaid != null;

	public bool Self => ArtifactCarrierUtility.IsSelfArtifact(parent);

	public ArtifactCarrier Carrier => ArtifactCarrierUtility.CarrierOf(parent);

	public Pawn Holder => ArtifactCarrierUtility.HolderOf(parent);

	public override void PostPostMake()
	{
		base.PostPostMake();
		EnsureSelf();
	}

	public override void PostSpawnSetup(bool respawningAfterLoad)
	{
		base.PostSpawnSetup(respawningAfterLoad);
		EnsureSelf();
	}

	public void EnsureSelf()
	{
		if (inlaid == null && Self)
		{
			ArtifactMetalDef namedSilentFail = DefDatabase<ArtifactMetalDef>.GetNamedSilentFail("ArtifactMetal_Essentium");
			ArtifactMetalDef artifactMetalDef = DefDatabase<ArtifactMetalDef>.GetNamedSilentFail("ArtifactMetal_Silver") ?? ArtifactMetalChoice.Rims.FirstOrDefault();
			if (namedSilentFail != null && artifactMetalDef != null)
			{
				QualityCategory q = parent.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal;
				inlaid = ArtifactMetalChoice.NewArtifact(namedSilentFail, artifactMetalDef, q);
				inlaid.charge = 1f;
			}
		}
	}

	public bool Uses(ArtifactArmDef arm)
	{
		if (arm != null && arm.carrier == Carrier)
		{
			return !Self;
		}
		return false;
	}

	public override void PostExposeData()
	{
		base.PostExposeData();
		if (Scribe.mode != LoadSaveMode.Saving || inlaid != null)
		{
			Scribe_Deep.Look(ref inlaid, "artifact");
		}
	}

	public override string TransformLabel(string label)
	{
		if (inlaid != null)
		{
			return label + " (" + (inlaid.customName.NullOrEmpty() ? "artifact" : inlaid.customName) + ")";
		}
		return label;
	}

	public override string CompInspectStringExtra()
	{
		if (inlaid == null)
		{
			return null;
		}
		return "Artifact inlaid: " + inlaid.ShortLabel() + ".\n" + inlaid.InspectLines();
	}
}
