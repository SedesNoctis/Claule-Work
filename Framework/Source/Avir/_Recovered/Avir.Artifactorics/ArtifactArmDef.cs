using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

public class ArtifactArmDef : Def
{
	public List<ArtifactCellStat> cellStats = new List<ArtifactCellStat>();

	public ArtifactCarrier carrier;

	public int armNumber;

	public ArtifactPathKind kind;

	public string exclusionGroup;

	public string iconPath;

	private Texture2D iconInt;

	private bool iconLoaded;

	[MustTranslate]
	public string nodeSummary;

	public List<ArtifactMilestoneInfo> milestones = new List<ArtifactMilestoneInfo>();

	public bool requiresPsychicHeart;

	public Texture2D Icon
	{
		get
		{
			if (!iconLoaded)
			{
				iconLoaded = true;
				if (!iconPath.NullOrEmpty())
				{
					iconInt = ContentFinder<Texture2D>.Get(iconPath, reportFailure: false);
				}
			}
			return iconInt;
		}
	}

	public bool IsCombat => kind == ArtifactPathKind.Combat;

	public bool Excludes(ArtifactArmDef other)
	{
		if (other != null && other != this && !exclusionGroup.NullOrEmpty() && exclusionGroup == other.exclusionGroup)
		{
			return carrier == other.carrier;
		}
		return false;
	}

	public ArtifactMilestoneInfo MilestoneAt(int node)
	{
		for (int i = 0; i < milestones.Count; i++)
		{
			if (milestones[i].node == node)
			{
				return milestones[i];
			}
		}
		return null;
	}

	public override IEnumerable<string> ConfigErrors()
	{
		foreach (string item in base.ConfigErrors())
		{
			yield return item;
		}
		if (armNumber <= 0)
		{
			yield return "armNumber must be 1 or more";
		}
		ArtifactoricsSettingsDef s = DefDatabase<ArtifactoricsSettingsDef>.GetNamedSilentFail("Artifactorics_Settings");
		if (s == null)
		{
			yield break;
		}
		foreach (ArtifactMilestoneInfo milestone in milestones)
		{
			if (!s.milestoneNodes.Contains(milestone.node))
			{
				yield return "milestone text for node " + milestone.node + ", which is not a milestone node in settings";
			}
		}
	}
}
