using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public static class LinarianManners
{
	public const string Favourite = "favourite";

	public const string Gathers = "gathers";

	public const string Haunts = "haunts";

	public const string Kind = "kind";

	public const string Cult = "cult";

	private static readonly Dictionary<int, int> guarded = new Dictionary<int, int>();

	public static readonly string[] AllHabits = new string[3] { "favourite", "gathers", "haunts" };

	public static bool IsKind(HeraldRecord r)
	{
		if (r.habits != null)
		{
			return r.habits.Contains("kind");
		}
		return false;
	}

	public static void Guard(HeraldRecord r, Map map, int now)
	{
		if (now % 60 != 0)
		{
			return;
		}
		Pawn her = r.her;
		Gene_LinarianSong gene_LinarianSong = Linarian.Song(her);
		if (gene_LinarianSong == null)
		{
			return;
		}
		foreach (Pawn item in map.mapPawns.AllPawnsSpawned.ToList())
		{
			if (!item.Dead && !item.Downed && item != her && LinarianZone.InZone(her, gene_LinarianSong, item.Position) && (!guarded.TryGetValue(item.thingIDNumber, out var value) || now - value >= 1800))
			{
				bool flag = item.CurJob?.targetA.Thing is Pawn pawn && pawn.Faction == Faction.OfPlayer;
				bool flag2 = item.RaceProps.Animal && item.MentalStateDef != null && item.MentalStateDef.IsAggro;
				if (item.RaceProps.Animal && (flag2 || (flag && (item.CurJobDef == JobDefOf.PredatorHunt || item.CurJobDef == JobDefOf.AttackMelee))))
				{
					guarded[item.thingIDNumber] = now;
					item.MentalState?.RecoverFromState();
					item.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.PanicFlee, "a Singer's song", forced: true);
				}
				else if (item.RaceProps.Humanlike && item.HostileTo(Faction.OfPlayer) && flag && LinarianTactics.Takeable(item))
				{
					guarded[item.thingIDNumber] = now;
					LinarianHediffs.Sleep(item, 2500, her);
				}
			}
		}
	}

	public static void BreakOut(Pawn a)
	{
		Building building = (from x in GenRadial.RadialDistinctThingsAround(a.Position, a.Map, 8f, useCenter: true).OfType<Building>()
			where x.def.IsFence || x is Building_Door
			orderby (x.Position - a.Position).LengthHorizontalSquared
			select x).FirstOrDefault();
		if (building != null)
		{
			Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, building);
			job.expiryInterval = 600;
			a.jobs.StartJob(job, JobCondition.InterruptForced);
		}
	}

	public static List<string> RollHabits()
	{
		return AllHabits.InRandomOrder().Take(Rand.RangeInclusive(1, 2)).ToList();
	}

	public static IntVec3 FindLair(Map map, IntVec3 near)
	{
		IntVec3 home = (map.areaManager.Home.ActiveCells.Any() ? CenterOf(map.areaManager.Home.ActiveCells) : map.Center);
		Func<IntVec3, bool>[] array = new Func<IntVec3, bool>[3]
		{
			(IntVec3 c) => c.Roofed(map) && c.GetRoof(map).isNatural,
			(IntVec3 c) => GenRadial.RadialDistinctThingsAround(c, map, 2.9f, useCenter: true).Count((Thing t) => t.def.plant?.IsTree ?? false) >= 3,
			(IntVec3 c) => true
		};
		foreach (Func<IntVec3, bool> pref in array)
		{
			if (CellFinder.TryFindRandomCellNear(map.Center, map, map.Size.x, (IntVec3 c) => Ok(c) && pref(c), out var result))
			{
				return result;
			}
		}
		return near;
		bool Ok(IntVec3 c)
		{
			if (c.Standable(map) && !map.areaManager.Home[c] && (c - home).LengthHorizontal >= 40f && !c.Fogged(map))
			{
				return !SongShelter.ShieldedCell(map, c);
			}
			return false;
		}
	}

	private static IntVec3 CenterOf(IEnumerable<IntVec3> cells)
	{
		int num = 0;
		long num2 = 0L;
		long num3 = 0L;
		foreach (IntVec3 cell in cells)
		{
			num2 += cell.x;
			num3 += cell.z;
			num++;
		}
		if (num != 0)
		{
			return new IntVec3((int)(num2 / num), 0, (int)(num3 / num));
		}
		return IntVec3.Invalid;
	}

	public static bool Spared(HeraldRecord r, Pawn c)
	{
		if (r.favourite == null || r.favourite != c)
		{
			return WildGods.Accepted(c, r.her);
		}
		return true;
	}

	public static Thing Loot(Map map, bool precious)
	{
		return (precious ? map.listerThings.ThingsOfDef(ThingDefOf.Silver).Concat(map.listerThings.ThingsOfDef(ThingDefOf.Gold)) : (from x in map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree)
			where !(x is Pawn) && x.def.IsNutritionGivingIngestible && x.def.ingestible.HumanEdible
			select x)).Where((Thing x) => x.Spawned && map.areaManager.Home[x.Position] && !SongShelter.ShieldedCell(map, x.Position)).RandomElementWithFallback();
	}

	public static bool Bear(Pawn bearer, Pawn her, Thing item, IntVec3 lair, int ticks)
	{
		if (bearer == null || item == null || !LinarianMental.Start(bearer, her, LinarianMental.Bearer, ticks))
		{
			return false;
		}
		if (bearer.MentalState is MentalState_SongBearer mentalState_SongBearer)
		{
			mentalState_SongBearer.item = item;
			mentalState_SongBearer.lair = lair;
		}
		return true;
	}

	public static int Circle(Pawn her, Map map, IntVec3 near, float radius)
	{
		bool colonyToo = her.Faction != Faction.OfPlayer;
		int count = Mathf.Clamp(2 + Mathf.RoundToInt((Linarian.Song(her)?.Power ?? 1f) * 4f), 2, 20);
		IGrouping<ThingDef, Pawn> grouping = (from a in map.mapPawns.AllPawnsSpawned
			where a.RaceProps.Animal && (a.Faction == null || (colonyToo && a.Faction == Faction.OfPlayer)) && !a.Dead && !a.Downed && a.MentalStateDef == null && (a.Position - near).LengthHorizontal <= radius
			group a by a.def into g
			where g.Count() >= 2
			select g).ToList().RandomElementWithFallback();
		if (grouping == null)
		{
			return 0;
		}
		if (!CellFinder.TryFindRandomCellNear(grouping.First().Position, map, 15, (IntVec3 c) => c.Standable(map) && (colonyToo || !map.areaManager.Home[c]), out var result))
		{
			return 0;
		}
		int num = 0;
		foreach (Pawn item in grouping.Take(count))
		{
			if (LinarianMental.Start(item, her, LinarianMental.Circling, 60000) && item.MentalState is MentalState_SongCircle mentalState_SongCircle)
			{
				mentalState_SongCircle.centre = result;
				mentalState_SongCircle.angle = (float)num * 1.1f;
				num++;
			}
		}
		return num;
	}

	public static void Night(HeraldRecord r, Map map, Gene_LinarianSong g, List<Pawn> colonists)
	{
		if (LinarianGods.ScaredOff(r.her))
		{
			return;
		}
		Pawn her = r.her;
		IntVec3 lair = (r.lair.IsValid ? r.lair : her.Position);
		List<Pawn> source = colonists.Where((Pawn c) => !Spared(r, c)).ToList();
		if (r.habits != null && r.habits.Contains("cult"))
		{
			int count = Mathf.Clamp(LinarianTactics.Count(g), 2, 5);
			foreach (Pawn item in source.Where(LinarianTactics.Asleep).InRandomOrder().Take(count)
				.ToList())
			{
				LinarianActs.Adore(her, g, item, 0.15f);
			}
		}
		if (g.Has("L1") && g.Has("S1") && r.peckTarget == null && Rand.Chance(0.35f))
		{
			Pawn pawn = source.Where((Pawn x) => !x.Downed).RandomElementWithFallback();
			if (pawn != null)
			{
				r.peckTarget = pawn;
				r.peckUntil = Find.TickManager.TicksGame + 15000;
			}
		}
		if (g.Has("L1") && g.Has("C1"))
		{
			foreach (Pawn item2 in source.Where(LinarianTactics.Asleep).InRandomOrder().Take(2)
				.ToList())
			{
				LinarianActs.Adore(her, g, item2, 0.05f);
			}
			foreach (Pawn item3 in source.Where((Pawn x) => LinarianSource.Of(x, LinarianHediffs.Avir_Adoration) == her && !x.Downed && x.MentalStateDef == null).InRandomOrder().Take(2)
				.ToList())
			{
				Thing thing = Stockpiled(map);
				if (thing != null)
				{
					RestUtility.WakeUp(item3);
					Bear(item3, her, thing, lair, 10000);
				}
			}
		}
		if (g.Has("D1") && g.Has("S1"))
		{
			foreach (Pawn item4 in source.Where((Pawn x) => x == r.grudge || x == r.peckTarget || LinarianSource.Of(x, LinarianHediffs.Avir_StrangeAche) == her || x.health.hediffSet.HasHediff(LinarianHediffs.PhantomScar)).ToList())
			{
				Insomniac(item4);
			}
		}
		if (g.Has("C1") && g.Has("P1") && Rand.Chance(0.15f))
		{
			Pawn pawn2 = source.Where((Pawn x) => LinarianActs.Worship(x) >= 0.8f && !x.Downed).RandomElementWithFallback();
			if (pawn2 != null)
			{
				GoFeral(pawn2);
			}
		}
		if (g.Has("P1") && g.Has("D1") && Rand.Chance(0.4f))
		{
			Pawn pawn3 = (from x in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
				where x.RaceProps.Animal && !x.Downed && x.MentalStateDef == null
				select x).RandomElementWithFallback();
			if (pawn3 != null)
			{
				LinarianMental.StartCalled(pawn3, her, 7500);
			}
		}
		if (r.main == "L" && Rand.Chance(0.25f))
		{
			Pawn pawn4 = source.Where((Pawn x) => LinarianTactics.Asleep(x) && !x.Downed).RandomElementWithFallback();
			Thing thing2 = Loot(map, precious: true);
			if (pawn4 != null && thing2 != null)
			{
				RestUtility.WakeUp(pawn4);
				Bear(pawn4, her, thing2, lair, 10000);
			}
		}
		if (r.main == "P")
		{
			Pawn pawn5 = map.mapPawns.AllPawnsSpawned.Where((Pawn x) => x.RaceProps.Animal && x.Faction == null && !x.Downed && x.MentalStateDef == null).RandomElementWithFallback();
			Thing thing3 = Loot(map, Rand.Chance(0.3f));
			if (pawn5 != null && thing3 != null && Rand.Chance(0.5f))
			{
				Bear(pawn5, her, thing3, lair, 15000);
			}
			if (Rand.Chance(0.3f))
			{
				Circle(her, map, her.Position, 60f);
			}
		}
	}

	public static Thing Stockpiled(Map map)
	{
		return (from x in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
			where x.Spawned && !(x is Pawn) && x.def.EverStorable(willMinifyIfPossible: false) && map.areaManager.Home[x.Position] && !SongShelter.ShieldedCell(map, x.Position) && x.MarketValue * (float)x.stackCount > 20f
			select x).RandomElementWithFallback();
	}

	public static void Insomniac(Pawn c)
	{
		if (c.InBed() && !c.Awake())
		{
			RestUtility.WakeUp(c);
			c.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Wander_Sad, "a bad dream", forced: true);
			ThoughtDef namedSilentFail = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_SongInsomnia");
			if (namedSilentFail != null)
			{
				c.needs?.mood?.thoughts?.memories?.TryGainMemory(namedSilentFail);
			}
		}
	}

	public static void GoFeral(Pawn c)
	{
		PawnKindDef namedSilentFail = DefDatabase<PawnKindDef>.GetNamedSilentFail("WildMan");
		if (namedSilentFail != null)
		{
			if (c.Faction != null)
			{
				c.SetFaction(null);
			}
			c.ChangeKind(namedSilentFail);
			Messages.Message(c.LabelShortCap + " has gone feral, and wanders off like a beast.", c, MessageTypeDefOf.NegativeEvent, historical: false);
		}
	}

	public static void Habits(HeraldRecord r, Map map, int now)
	{
		Pawn her = r.her;
		Gene_LinarianSong gene_LinarianSong = Linarian.Song(her);
		if (gene_LinarianSong != null && gene_LinarianSong.shadow && gene_LinarianSong.Has("L1") && now % 2500 == 0 && LinarianShadow.EternalNightOn(map) && !IsKind(r))
		{
			foreach (Pawn item in map.mapPawns.FreeColonistsSpawned.Where((Pawn x) => x.InBed() && !x.Awake() && x.needs?.rest != null).ToList())
			{
				item.needs.rest.CurLevel = Mathf.Min(item.needs.rest.CurLevel, 0.75f);
			}
		}
		if (r.habits == null)
		{
			return;
		}
		if (r.habits.Contains("gathers") && now % 2500 == 0)
		{
			foreach (Pawn item2 in map.mapPawns.AllPawnsSpawned.Where((Pawn x) => x.RaceProps.Animal && x.Faction == null && !x.Downed && x.MentalStateDef == null && (x.Position - her.Position).LengthHorizontal < 30f).Take(3).ToList())
			{
				if (CellFinder.TryFindRandomCellNear(her.Position, map, 4, (IntVec3 c) => c.Standable(map), out var result))
				{
					item2.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, result), JobCondition.InterruptForced);
				}
			}
		}
		if (r.habits.Contains("haunts") && now % 7500 == 0 && Rand.Chance(0.3f))
		{
			IntVec3 root = map.areaManager.Home.ActiveCells.Where((IntVec3 c) => !c.Roofed(map)).RandomElementWithFallback(IntVec3.Invalid);
			if (root.IsValid && CellFinder.TryFindRandomCellNear(root, map, 12, (IntVec3 c) => c.Standable(map) && !map.areaManager.Home[c] && !SongShelter.ShieldedCell(map, c), out var result2))
			{
				r.lair = result2;
			}
		}
	}

	public static void Amuse(Pawn her, Gene_LinarianSong g)
	{
		Need_Joy need_Joy = her.needs?.joy;
		if (need_Joy != null && !(need_Joy.CurLevel > 0.35f) && !her.Drafted && !her.InMentalState && !her.Downed && g.Has("P1") && Circle(her, her.Map, her.Position, Mathf.Min(g.ZoneRadius, 60f)) > 0)
		{
			need_Joy.GainJoy(0.3f, JoyKindDefOf.Social);
			LinarianTactics.Notify(her, "she sets the beasts circling, and it amuses her.");
		}
	}
}
