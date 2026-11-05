using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics;

public static class ArtifactJobUtility
{
	public const float InlayWork = 3000f;

	public const float ExtractWork = 3000f;

	public static Toil WorkToil(JobDriver d, TargetIndex spotIndex, float totalWork, Action onDone)
	{
		float done = 0f;
		Toil toil = ToilMaker.MakeToil("ArtifactWork");
		toil.initAction = delegate
		{
			done = 0f;
		};
		toil.tickAction = delegate
		{
			Pawn pawn = d.pawn;
			Thing thing = d.job.GetTarget(spotIndex).Thing;
			float b = pawn.GetStatValue(StatDefOf.GeneralLaborSpeed) * (thing?.GetStatValue(StatDefOf.WorkTableWorkSpeedFactor) ?? 1f);
			done += Mathf.Max(0.1f, b);
			pawn.skills?.Learn(SkillDefOf.Crafting, 0.05f);
			pawn.skills?.Learn(SkillDefOf.Artistic, 0.05f);
			if (done >= totalWork)
			{
				onDone();
				d.ReadyForNextToil();
			}
		};
		toil.defaultCompleteMode = ToilCompleteMode.Never;
		toil.WithProgressBar(spotIndex, () => done / totalWork);
		toil.FailOnDespawnedNullOrForbidden(spotIndex);
		toil.activeSkill = () => SkillDefOf.Crafting;
		return toil;
	}
}
