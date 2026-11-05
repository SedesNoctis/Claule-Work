using RimWorld;
using Verse;

namespace Avir.Linarian;

public class HediffComp_HeavyEyelids : HediffComp
{
	public float yielding = 1f;

	public override void CompExposeData()
	{
		base.CompExposeData();
		Scribe_Values.Look(ref yielding, "yielding", 1f);
	}

	public override void CompPostTickInterval(ref float severityAdjustment, int delta)
	{
		if (base.Pawn.IsHashIntervalTick(250, delta) && base.Pawn.Spawned && !LinarianTactics.Asleep(base.Pawn))
		{
			FleckMaker.ThrowMetaIcon(base.Pawn.Position, base.Pawn.Map, FleckDefOf.SleepZ);
			Pawn pawn = LinarianSource.Of(base.Pawn, parent.def);
			Gene_LinarianSong gene_LinarianSong = Linarian.Song(pawn);
			if (pawn != null && gene_LinarianSong != null && Rand.Chance(LinarianTactics.Chance(pawn, gene_LinarianSong, base.Pawn, callOrSleep: true, "L") * 0.08f * yielding))
			{
				LinarianHediffs.Sleep(base.Pawn, LinarianZone.Ticks(3750f, gene_LinarianSong), pawn);
				base.Pawn.health.RemoveHediff(parent);
			}
		}
	}
}
