using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Linarian;

public static class LinarianGods
{
	private static readonly Dictionary<int, int> lastAid = new Dictionary<int, int>();

	private static readonly Dictionary<int, int> scared = new Dictionary<int, int>();

	public static bool Worshipped(Map map, Pawn her)
	{
		return map.mapPawns.FreeColonistsSpawned.Any((Pawn c) => !c.IsSlave && (WildGods.Worships(c) || WildGods.Accepted(c, her)));
	}

	public static void Aid(HeraldRecord r, Map map, int now)
	{
		Pawn her = r.her;
		if (now % 600 != 0 || her == null || !her.Spawned || her.Downed || !Worshipped(map, her))
		{
			return;
		}
		if (r.primal)
		{
			CruelAid(r, map, now);
		}
		else
		{
			if (!GenHostility.AnyHostileActiveThreatToPlayer(map) || (lastAid.TryGetValue(her.thingIDNumber, out var value) && now - value < 2500))
			{
				return;
			}
			Gene_LinarianSong gene_LinarianSong = Linarian.Song(her);
			if (gene_LinarianSong == null)
			{
				return;
			}
			List<Pawn> foes = map.mapPawns.AllPawnsSpawned.Where((Pawn p) => !p.Dead && !p.Downed && p.HostileTo(Faction.OfPlayer) && p.RaceProps.Humanlike).ToList();
			List<Pawn> list = map.mapPawns.FreeColonistsSpawned.Where((Pawn c) => !c.Dead).ToList();
			if (foes.Count == 0 || list.Count == 0)
			{
				return;
			}
			lastAid[her.thingIDNumber] = now;
			Pawn pawn = (from c in list
				where c.health.summaryHealth.SummaryHealthPercent < 0.6f || c.Downed
				orderby c.health.summaryHealth.SummaryHealthPercent
				select c).FirstOrDefault();
			float value2 = Rand.Value;
			if (pawn != null && value2 < 0.4f)
			{
				LinarianVeil.Veil(pawn, 2500);
				Messages.Message("The gods have decided " + pawn.LabelShort + " is not to be seen today. The enemy agrees, apparently.", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
				return;
			}
			Pawn front = list.OrderBy((Pawn c) => foes.Min((Pawn f) => (f.Position - c.Position).LengthHorizontalSquared)).First();
			Pawn pawn2 = foes.OrderBy((Pawn f) => (f.Position - front.Position).LengthHorizontalSquared).First();
			if (value2 < 0.7f)
			{
				LinarianActs.Mirage(her, gene_LinarianSong, front.Position, 6f);
				Messages.Message("The gods lend " + front.LabelShort + " a few spare shadows. The enemy cannot decide which one to shoot.", front, MessageTypeDefOf.PositiveEvent, historical: false);
			}
			else
			{
				LinarianActs.Ecstasy(her, gene_LinarianSong, pawn2.Position, 5f);
				Messages.Message("The enemy has suddenly found religion. The gods are flattered, and so is the enemy.", pawn2, MessageTypeDefOf.PositiveEvent, historical: false);
			}
		}
	}

	private static void CruelAid(HeraldRecord r, Map map, int now)
	{
		Pawn her = r.her;
		if (!GenHostility.AnyHostileActiveThreatToPlayer(map) || (lastAid.TryGetValue(her.thingIDNumber, out var value) && now - value < 5000))
		{
			return;
		}
		List<Pawn> list = map.mapPawns.AllPawnsSpawned.Where((Pawn p) => !p.Dead && !p.Downed && p.HostileTo(Faction.OfPlayer) && !p.RaceProps.Animal).ToList();
		if (list.Count == 0)
		{
			return;
		}
		lastAid[her.thingIDNumber] = now;
		MentalStateDef defend = DefDatabase<MentalStateDef>.GetNamedSilentFail("Avir_SongDefend");
		if (defend != null && Rand.Chance(0.65f))
		{
			List<Pawn> list2 = map.mapPawns.AllPawnsSpawned.Where((Pawn a) => a.RaceProps.Animal && a.Faction == Faction.OfPlayer && !a.Downed && !a.Dead && a.MentalStateDef == null).ToList();
			List<Pawn> list3 = map.mapPawns.AllPawnsSpawned.Where((Pawn a) => a.RaceProps.Animal && a.Faction == null && !a.Downed && !a.Dead && a.MentalStateDef == null).ToList();
			bool flag = list3.Any() && (list2.Count == 0 || Rand.Chance(0.4f));
			List<Pawn> list4 = (flag ? list3 : list2).OrderByDescending((Pawn a) => a.BodySize).Take(Rand.RangeInclusive(3, 8)).ToList();
			if (list4.Count((Pawn a) => LinarianMental.Start(a, her, defend, 15000)) > 0)
			{
				Messages.Message(flag ? "The gods send every wild beast in the area at the raiders. Help is help - do not look it in the mouth." : "The gods have volunteered your herd for the defence. They did not ask whether you could spare it.", list4.First(), MessageTypeDefOf.NeutralEvent, historical: false);
				return;
			}
		}
		Pawn foe = list.RandomElement();
		int num = 0;
		foreach (Pawn item in map.mapPawns.AllPawnsSpawned.Where((Pawn q) => q.RaceProps.Humanlike && !q.Dead && !q.Downed && (q.Position - foe.Position).LengthHorizontal <= 7f && q != her).ToList())
		{
			LinarianHediffs.Sleep(item, 5000, her);
			num++;
		}
		if (num > 0)
		{
			Messages.Message("The gods end the fight the simple way: everyone in it falls asleep. Yours too. Fair is fair.", foe, MessageTypeDefOf.NeutralEvent, historical: false);
		}
	}

	public static bool Tribute(HeraldRecord r, Map map)
	{
		Pawn her = r.her;
		if (!r.primal || her == null || !Worshipped(map, her))
		{
			return false;
		}
		if (LinarianManners.Loot(map, precious: true) == null && LinarianManners.Loot(map, precious: false) == null)
		{
			return false;
		}
		int num = 0;
		foreach (Pawn item in map.mapPawns.FreeColonistsSpawned.Where((Pawn c) => !c.IsSlave && (WildGods.Worships(c) || WildGods.Accepted(c, her)) && LinarianTactics.Takeable(c)).InRandomOrder().Take(3)
			.ToList())
		{
			Thing thing = LinarianManners.Loot(map, Rand.Bool) ?? LinarianManners.Loot(map, precious: true) ?? LinarianManners.Loot(map, precious: false);
			if (thing != null && LinarianManners.Bear(item, her, thing, r.lair.IsValid ? r.lair : her.Position, 10000))
			{
				num++;
			}
		}
		if (num > 0)
		{
			Messages.Message("The faithful walk out into the night with gifts for the gods. The gods are always hungry.", MessageTypeDefOf.NeutralEvent, historical: false);
		}
		return true;
	}

	public static bool ScaredOff(Pawn her)
	{
		if (her != null && scared.TryGetValue(her.thingIDNumber, out var value))
		{
			return Find.TickManager.TicksGame - value < 20000;
		}
		return false;
	}

	public static void Scare(Pawn her, Thing totem, float range)
	{
		scared[her.thingIDNumber] = Find.TickManager.TicksGame;
		IntVec3 fleeDest = CellFinderLoose.GetFleeDest(her, new List<Thing> { totem }, range + 15f);
		if (fleeDest.IsValid && fleeDest != her.Position)
		{
			her.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Flee, fleeDest, totem), JobCondition.InterruptForced);
		}
		FleckMaker.ThrowMicroSparks(her.DrawPos, her.Map);
	}

	public static void SeenQuietly(Pawn her)
	{
		HeraldRecord heraldRecord = LinarianPresence.Record(her);
		if (heraldRecord != null)
		{
			if (LinarianVeil.Veiled(her))
			{
				LinarianVeil.Unveil(her);
			}
			if (LinarianVeil.InForm(her) != null)
			{
				LinarianVeil.ClearForm(her);
			}
			heraldRecord.revealedUntil = Mathf.Max(heraldRecord.revealedUntil, Find.TickManager.TicksGame + 400);
		}
	}
}
