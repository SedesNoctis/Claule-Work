using UnityEngine;
using Verse;

namespace Avir.Linarian;

public class HediffComp_DevourFeeds : HediffComp
{
	private static readonly float[] PerHour = new float[4] { 0.006f, 0.01f, 0.015f, 0.022f };

	public int lastNight = -999999;

	public override void CompExposeData()
	{
		base.CompExposeData();
		Scribe_Values.Look(ref lastNight, "lastNight", -999999);
	}

	public override void CompPostTickInterval(ref float severityAdjustment, int delta)
	{
		if (!base.Pawn.IsHashIntervalTick(2500, delta) || base.Pawn.Dead)
		{
			return;
		}
		Pawn pawn = LinarianSource.Of(base.Pawn, LinarianHediffs.Avir_SlowDevour);
		if (pawn != null && !pawn.Dead)
		{
			float num = PerHour[Mathf.Clamp(parent.CurStageIndex, 0, PerHour.Length - 1)];
			if (LinarianTactics.Asleep(base.Pawn))
			{
				num *= 1.5f;
			}
			LinarianFeast.FeedOrStore(pawn, Linarian.Song(pawn), num, 1f);
		}
	}
}
