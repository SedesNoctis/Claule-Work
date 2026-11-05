using Avir.Ritualistics;
using RimWorld;
using Verse;

namespace Avir.Linarian;

public static class SongShelter
{
	public static bool ShieldedCell(Map map, IntVec3 c)
	{
		if (map == null || !c.IsValid)
		{
			return false;
		}
		foreach (CompPsychicPylon item in CompPsychicPylon.All)
		{
			if (item.parent.Map == map && item.charge > 0f && item.Takes(drone: true) && (float)(c - item.parent.Position).LengthHorizontalSquared <= item.Range * item.Range)
			{
				return true;
			}
		}
		return false;
	}

	public static bool ShieldedFrom(Pawn q, Pawn her)
	{
		if (her != null && q != null && (her.Faction == null || q.Faction != her.Faction))
		{
			return Shielded(q);
		}
		return false;
	}

	public static bool Shielded(Pawn q)
	{
		if (q?.health?.hediffSet == null)
		{
			return false;
		}
		if (PsychicDefOf.Avir_StormCharm != null && q.health.hediffSet.HasHediff(PsychicDefOf.Avir_StormCharm))
		{
			return true;
		}
		Map mapHeld = q.MapHeld;
		if (mapHeld == null || q.HostileTo(Faction.OfPlayer))
		{
			return false;
		}
		foreach (CompPsychicPylon item in CompPsychicPylon.All)
		{
			if (item.parent.Map == mapHeld && item.charge > 0f && item.Takes(drone: true) && (float)(q.PositionHeld - item.parent.Position).LengthHorizontalSquared <= item.Range * item.Range)
			{
				return true;
			}
		}
		return false;
	}
}
