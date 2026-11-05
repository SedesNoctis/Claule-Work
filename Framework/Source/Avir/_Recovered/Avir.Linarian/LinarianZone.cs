using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Linarian;

public static class LinarianZone
{
	private class Field
	{
		public Map map;

		public Pawn her;

		public IntVec3 at;

		public float r;

		public int until;

		public Dictionary<Pawn, int> held = new Dictionary<Pawn, int>();
	}

	public const float WholeMap = 120f;

	private static readonly List<Field> fields = new List<Field>();

	public static bool InZone(Pawn her, Gene_LinarianSong g, IntVec3 c)
	{
		if (!(g.ZoneRadius >= float.MaxValue))
		{
			return (c - her.Position).LengthHorizontal <= g.ZoneRadius;
		}
		return true;
	}

	public static float Area(string t, Gene_LinarianSong g)
	{
		float num;
		switch (t)
		{
		case "L1":
			num = 5f;
			break;
		case "L3":
			num = 9f;
			break;
		case "L4":
			num = 6f;
			break;
		case "L5":
			num = 11f;
			break;
		case "C3":
			num = 9f;
			break;
		case "C1":
			num = 6f;
			break;
		case "C4":
			num = 5f;
			break;
		case "D4":
			num = 6f;
			break;
		case "D2":
			num = 6f;
			break;
		case "S1":
			num = 6f;
			break;
		case "P2":
			num = 8f;
			break;
		case "D5":
			num = 7f;
			break;
		case "W1":
			num = (g.Has("W5") ? 10f : 7f);
			break;
		case "W2":
			num = 8f;
			break;
		case "W3":
			num = 6f;
			break;
		case "W4":
			num = 5f;
			break;
		case "W5":
			num = 11f;
			break;
		case "F4":
			num = 9f;
			break;
		case "F5":
			num = 6f;
			break;
		case "S3":
			num = 7f;
			break;
		case "G3":
			num = 6f;
			break;
		case "G5":
			num = 5f;
			break;
		case "T3":
			num = 7f;
			break;
		case "T4":
			num = 12f;
			break;
		default:
			return 0f;
		}
		return Mathf.Min(num * g.Reach, GenRadial.MaxRadialPatternRadius - 1f);
	}

	public static IEnumerable<Pawn> Around(Map map, IntVec3 c, float r)
	{
		return (from q in GenRadial.RadialDistinctThingsAround(c, map, r, useCenter: true).OfType<Pawn>()
			where !q.Dead
			select q).ToList();
	}

	public static List<Pawn> FoesAt(Pawn her, IntVec3 c, float r)
	{
		return (from q in Around(her.Map, c, r)
			where q != her && (q.HostileTo(her) || LinarianPackSiphon.Held(her, q) || (LinarianCult.SingerOf(q) == her && q.Faction != her.Faction)) && !q.RaceProps.Animal
			orderby (q.Position - c).LengthHorizontalSquared
			select q).ToList();
	}

	public static List<Pawn> OwnAt(Pawn her, IntVec3 c, float r)
	{
		return (from q in Around(her.Map, c, r)
			where q != her && q.Faction != null && q.Faction == her.Faction && q.RaceProps.Humanlike
			select q).ToList();
	}

	public static IEnumerable<Pawn> InHerZone(Pawn her, Gene_LinarianSong g)
	{
		if (her.Map != null)
		{
			return her.Map.mapPawns.AllPawnsSpawned.Where((Pawn q) => !q.Dead && InZone(her, g, q.Position)).ToList();
		}
		return Enumerable.Empty<Pawn>();
	}

	public static int Ticks(float baseTicks, Gene_LinarianSong g)
	{
		return Mathf.RoundToInt(baseTicks * g.Reach);
	}

	public static void DrowsyField(Pawn her, Gene_LinarianSong g, IntVec3 c, float r)
	{
		fields.Add(new Field
		{
			map = her.Map,
			her = her,
			at = c,
			r = r,
			until = Find.TickManager.TicksGame + Ticks(2500f, g)
		});
		FleckMaker.Static(c, her.Map, FleckDefOf.PsycastAreaEffect, r);
	}

	public static void Tick(Map map, int now)
	{
		fields.RemoveAll((Field f) => f.until < now || f.her == null || f.her.Dead || (f.map != map && f.map.Disposed()));
		foreach (Field item in fields.Where((Field f) => f.map == map))
		{
			Gene_LinarianSong gene_LinarianSong = Linarian.Song(item.her);
			if (gene_LinarianSong == null)
			{
				continue;
			}
			foreach (Pawn item2 in FoesAt(item.her, item.at, item.r).Where(LinarianTactics.Takeable))
			{
				if (!LinarianTactics.Asleep(item2))
				{
					LinarianHediffs.Add(item2, LinarianHediffs.Avir_TimeSlow, 150);
					item.held.TryGetValue(item2, out var value);
					value = (item.held[item2] = value + 1);
					if (value >= 6 && Rand.Chance(LinarianTactics.Chance(item.her, gene_LinarianSong, item2, callOrSleep: true, "L") * 0.35f))
					{
						LinarianHediffs.Sleep(item2, Ticks(3750f, gene_LinarianSong), item.her);
					}
				}
			}
			if (now % 240 == 0 && Find.CurrentMap == map)
			{
				FleckMaker.ThrowDustPuffThick(item.at.ToVector3Shifted(), map, item.r * 0.6f, new Color(0.6f, 0.55f, 0.85f, 0.4f));
			}
		}
	}

	private static bool Disposed(this Map m)
	{
		if (m != null)
		{
			return Find.Maps.IndexOf(m) < 0;
		}
		return true;
	}
}
