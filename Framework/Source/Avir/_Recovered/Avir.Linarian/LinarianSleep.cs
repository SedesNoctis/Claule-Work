using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian;

public static class LinarianSleep
{
	public static HediffDef Drowsy => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_HeavyEyelids");

	public static HediffDef Eternal => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_EternalSleep");

	public static void HeavyEyelids(Pawn p, Gene_LinarianSong g, IntVec3 c, float r, float yielding)
	{
		int num = 0;
		foreach (Pawn item in from q in LinarianZone.FoesAt(p, c, r).Where(LinarianTactics.Takeable)
			where !LinarianTactics.Asleep(q)
			select q)
		{
			if (Rand.Chance(LinarianTactics.Chance(p, g, item, callOrSleep: true, "L")) && MakeDrowsy(p, item, LinarianZone.Ticks(5000f, g), yielding))
			{
				num++;
			}
		}
		FleckMaker.Static(c, p.Map, FleckDefOf.PsycastAreaEffect, r);
		LinarianTactics.Notify(p, (num > 0) ? (num + " grow drowsy.") : "No one feels drowsy.");
	}

	public static bool MakeDrowsy(Pawn p, Pawn q, int ticks, float yielding)
	{
		if (Drowsy == null)
		{
			return false;
		}
		Hediff hediff = LinarianHediffs.Add(q, Drowsy, ticks, 1f, p);
		HediffComp_HeavyEyelids hediffComp_HeavyEyelids = hediff?.TryGetComp<HediffComp_HeavyEyelids>();
		if (hediffComp_HeavyEyelids != null)
		{
			hediffComp_HeavyEyelids.yielding = Mathf.Max(hediffComp_HeavyEyelids.yielding, yielding);
		}
		return hediff != null;
	}

	public static void Field(Pawn p, Gene_LinarianSong g, IntVec3 c, float r)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int ticks = LinarianZone.Ticks(3750f, g);
		foreach (Pawn item in (from q in LinarianZone.FoesAt(p, c, r).Where(LinarianTactics.Takeable)
			where !LinarianTactics.Asleep(q)
			select q).ToList())
		{
			float num4 = LinarianTactics.Chance(p, g, item, callOrSleep: true, "L");
			if (Rand.Chance(num4))
			{
				if (Rand.Chance(0.12f) && Eternal != null)
				{
					LinarianHediffs.Add(item, Eternal, -1, 1f, p);
					num3++;
				}
				else if (num4 >= 0.6f || !g.Has("L1"))
				{
					LinarianHediffs.Sleep(item, ticks, p);
					num++;
				}
				else if (MakeDrowsy(p, item, LinarianZone.Ticks(5000f, g), 3f))
				{
					num2++;
				}
			}
		}
		FleckMaker.Static(c, p.Map, FleckDefOf.PsycastAreaEffect, r);
		LinarianTactics.Notify(p, (num + num2 + num3 == 0) ? "The field stays awake." : (num + " fall asleep, " + num2 + " grow drowsy" + ((num3 > 0) ? (", and " + num3 + " sink into a sleep with no waking.") : ".")));
	}

	public static void ReleaseEternal(Pawn p, IntVec3 c, float r)
	{
		if (Eternal == null || p.Map == null)
		{
			return;
		}
		foreach (Pawn item in LinarianZone.Around(p.Map, c, r).ToList())
		{
			Hediff hediff = item.health?.hediffSet?.GetFirstHediffOfDef(Eternal);
			if (hediff != null && LinarianSource.Of(item, Eternal) == p)
			{
				item.health.RemoveHediff(hediff);
			}
		}
	}

	public static bool SongAsleep(Pawn q)
	{
		HediffSet hediffSet = q?.health?.hediffSet;
		if (hediffSet != null)
		{
			if (LinarianHediffs.Avir_SongSleep == null || !hediffSet.HasHediff(LinarianHediffs.Avir_SongSleep))
			{
				if (Eternal != null)
				{
					return hediffSet.HasHediff(Eternal);
				}
				return false;
			}
			return true;
		}
		return false;
	}
}
