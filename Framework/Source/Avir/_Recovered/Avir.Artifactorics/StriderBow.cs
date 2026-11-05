using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

public static class StriderBow
{
	public static float ShotCharge => ArtifactoricsSettingsDef.Main?.bowShotCharge ?? 0.01f;

	public static ArtifactState Quiver(ThingWithComps bow)
	{
		ArtifactState artifactState = bow?.Socket()?.inlaid;
		if (artifactState == null || !artifactState.HasHeart)
		{
			return null;
		}
		return artifactState;
	}

	public static void Spend(ThingWithComps bow, Pawn archer)
	{
		ArtifactState artifactState = Quiver(bow);
		if (artifactState != null)
		{
			artifactState.charge = Mathf.Max(0f, artifactState.charge - ShotCharge);
			if (artifactState.charge < ShotCharge && archer?.Faction == Faction.OfPlayer)
			{
				Messages.Message(archer.LabelShortCap + "'s bow has drawn its last arrow: the artifact in it is out of charge.", archer, MessageTypeDefOf.NegativeEvent, historical: false);
			}
			if (artifactState.charge <= 0f && archer != null)
			{
				ArtifactMilestones.Invalidate(archer);
			}
		}
	}

	public static bool CanFire(ThingWithComps bow)
	{
		ArtifactState artifactState = Quiver(bow);
		if (artifactState != null)
		{
			return artifactState.charge >= ShotCharge;
		}
		return false;
	}
}
