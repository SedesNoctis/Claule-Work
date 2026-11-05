using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir;

public static class AvirChicks
{
	public const int MaxChicks = 2;

	public const float MaxAge = 3f;

	public const float XpShare = 0.1f;

	public static readonly Dictionary<Pawn, Pawn> CarrierOf = new Dictionary<Pawn, Pawn>();

	public static readonly Dictionary<Pawn, List<Pawn>> ChicksOf = new Dictionary<Pawn, List<Pawn>>();

	public static readonly HashSet<Pawn> AutoOff = new HashSet<Pawn>();

	public static readonly Dictionary<Pawn, int> LeaveUntil = new Dictionary<Pawn, int>();

	public const float SleepyBelow = 0.3f;

	public const float RestedAbove = 0.85f;

	public const float HungryBelow = 0.3f;

	public const int LeaveAloneTicks = 30000;

	public const float SeekRange = 40f;

	public static JobDef RideDef => DefDatabase<JobDef>.GetNamedSilentFail("Avir_RideOnBack");

	public static JobDef TakeDef => DefDatabase<JobDef>.GetNamedSilentFail("Avir_TakeChick");

	public static JobDef PutToBedDef => DefDatabase<JobDef>.GetNamedSilentFail("Avir_PutChickToBed");

	public static bool IsChick(Pawn p)
	{
		if (p?.ageTracker != null && p.RaceProps.Humanlike && p.ageTracker.AgeBiologicalYearsFloat < 3f)
		{
			return !p.Dead;
		}
		return false;
	}

	public static bool IsGrownAvir(Pawn p)
	{
		if (p != null && AvirBody.IsAvir(p) && p.ageTracker != null)
		{
			return p.DevelopmentalStage.Adult();
		}
		return false;
	}

	public static bool Family(Pawn carrier, Pawn chick)
	{
		if (carrier?.relations == null || chick?.relations == null)
		{
			return false;
		}
		if (carrier.relations.FamilyByBlood.Contains(chick))
		{
			return true;
		}
		foreach (Pawn item in from r in chick.relations.DirectRelations
			where r.def == PawnRelationDefOf.Parent
			select r.otherPawn)
		{
			if (item != null && (carrier.relations.DirectRelationExists(PawnRelationDefOf.Spouse, item) || carrier.relations.DirectRelationExists(PawnRelationDefOf.Lover, item) || carrier.relations.DirectRelationExists(PawnRelationDefOf.Fiance, item)))
			{
				return true;
			}
		}
		return false;
	}

	public static string CannotCarry(Pawn carrier, Pawn chick)
	{
		if (!IsGrownAvir(carrier))
		{
			return "only a grown Avir carries chicks";
		}
		if (!IsChick(chick))
		{
			return "only a chick under three";
		}
		if (CarrierOf.ContainsKey(chick))
		{
			return "already carried";
		}
		if (ChicksOf.TryGetValue(carrier, out var value) && value.Count >= 2)
		{
			return "two chicks already";
		}
		if (!Family(carrier, chick))
		{
			return "not family";
		}
		if (carrier.Downed)
		{
			return "cannot now";
		}
		return null;
	}

	public static void Mount(Pawn carrier, Pawn chick)
	{
		if (CannotCarry(carrier, chick) == null && chick.Spawned)
		{
			CarrierOf[chick] = carrier;
			if (!ChicksOf.TryGetValue(carrier, out var value))
			{
				value = (ChicksOf[carrier] = new List<Pawn>());
			}
			value.Add(chick);
			Follow(carrier, chick);
			HoldStill(chick);
		}
	}

	public static void Dismount(Pawn chick, IntVec3? at = null)
	{
		if (chick == null || !CarrierOf.TryGetValue(chick, out var value))
		{
			return;
		}
		CarrierOf.Remove(chick);
		if (ChicksOf.TryGetValue(value, out var value2))
		{
			value2.Remove(chick);
			if (value2.Count == 0)
			{
				ChicksOf.Remove(value);
			}
		}
		if (chick.Spawned)
		{
			Map map = chick.Map;
			IntVec3 intVec = at ?? value.Position;
			if (!intVec.InBounds(map) || !intVec.Standable(map))
			{
				intVec = CellFinder.StandableCellNear(value.Position, map, 3f);
			}
			if (intVec.IsValid)
			{
				chick.Position = intVec;
				chick.Notify_Teleported(endCurrentJob: true, resetTweenedPos: false);
			}
			if (chick.CurJobDef == RideDef)
			{
				chick.jobs.EndCurrentJob(JobCondition.InterruptForced);
			}
		}
	}

	public static void DismountAll(Pawn carrier, IntVec3? at = null)
	{
		if (!ChicksOf.TryGetValue(carrier, out var value))
		{
			return;
		}
		foreach (Pawn item in value.ToList())
		{
			Dismount(item, at);
		}
	}

	private static void Follow(Pawn carrier, Pawn chick)
	{
		if (chick.Position != carrier.Position)
		{
			chick.Position = carrier.Position;
			chick.pather?.StopDead();
		}
		chick.Rotation = carrier.Rotation;
	}

	private static void HoldStill(Pawn chick)
	{
		JobDef rideDef = RideDef;
		if (rideDef != null && chick.jobs != null && chick.CurJobDef != rideDef)
		{
			chick.jobs.StartJob(JobMaker.MakeJob(rideDef), JobCondition.InterruptForced);
		}
	}

	public static void CarrierTick(Pawn carrier, int delta)
	{
		if (!ChicksOf.TryGetValue(carrier, out var value) || !carrier.Spawned)
		{
			return;
		}
		if (carrier.Dead || carrier.Downed)
		{
			DismountAll(carrier);
			return;
		}
		for (int num = value.Count - 1; num >= 0; num--)
		{
			Pawn pawn = value[num];
			if (pawn == null || !pawn.Spawned || pawn.Map != carrier.Map || pawn.Dead || !IsChick(pawn) || pawn.CarriedBy != null)
			{
				Dismount(pawn);
			}
			else if (carrier.IsHashIntervalTick(250, delta) && NeedsBedRest(pawn))
			{
				Dismount(pawn);
			}
			else
			{
				Follow(carrier, pawn);
				if (carrier.IsHashIntervalTick(60, delta))
				{
					HoldStill(pawn);
				}
				if (carrier.IsHashIntervalTick(250, delta) && AvirTail.Health(carrier) > 0f)
				{
					pawn.needs?.play?.Play(0.012f * AvirTail.Health(carrier));
				}
			}
		}
	}

	public static Vector3 SeatPos(Pawn carrier, Pawn chick)
	{
		Vector3 drawPos = carrier.DrawPos;
		List<Pawn> value;
		int num = (ChicksOf.TryGetValue(carrier, out value) ? Mathf.Max(0, value.IndexOf(chick)) : 0);
		float num2 = ((value == null || value.Count <= 1) ? 0f : ((num == 0) ? (-0.14f) : 0.14f));
		Rot4 rotation = carrier.Rotation;
		float num3 = ((value == null || value.Count <= 1) ? 0.28f : ((num == 0) ? (-0.3f) : 0.3f));
		Vector3 vector = ((rotation == Rot4.South) ? new Vector3(num3, 0.3f, 0.14f) : ((rotation == Rot4.North) ? new Vector3(0f - num3, 0.3f, 0.12f) : ((!(rotation == Rot4.East)) ? new Vector3(0.1f - num2 * 0.3f, 0.3f, 0.16f) : new Vector3(-0.1f + num2 * 0.3f, 0.3f, 0.16f))));
		return drawPos + vector;
	}

	public static IntVec3 SafeCell(Pawn carrier)
	{
		Map map = carrier.Map;
		if (ChicksOf.TryGetValue(carrier, out var value))
		{
			foreach (Pawn item in value)
			{
				Building_Bed building_Bed = item.ownership?.OwnedBed;
				if (building_Bed != null && building_Bed.Spawned && building_Bed.Map == map)
				{
					return building_Bed.Position;
				}
			}
		}
		List<Pawn> list = (from p in map.attackTargetsCache.TargetsHostileToColony.OfType<Pawn>()
			where p.Spawned && !p.Downed
			select p).ToList();
		IntVec3 result = IntVec3.Invalid;
		float num = -1f;
		Area home = map.areaManager.Home;
		IEnumerable<IntVec3> source = ((home != null && home.TrueCount > 0) ? home.ActiveCells : GenRadial.RadialCellsAround(carrier.Position, 30f, useCenter: true));
		int num2 = 0;
		foreach (IntVec3 c in source.InRandomOrder())
		{
			if (++num2 > 300)
			{
				break;
			}
			if (!c.InBounds(map) || !c.Roofed(map) || !c.Standable(map))
			{
				continue;
			}
			Room room = c.GetRoom(map);
			if (room != null && !room.PsychologicallyOutdoors)
			{
				float num3 = ((list.Count == 0) ? 999f : ((float)list.Min((Pawn f) => (f.Position - c).LengthHorizontalSquared)));
				if (num3 > num)
				{
					num = num3;
					result = c;
				}
			}
		}
		return result;
	}

	public static void Clear()
	{
		CarrierOf.Clear();
		ChicksOf.Clear();
		AutoOff.Clear();
		LeaveUntil.Clear();
	}

	public static void SetDownByHand(Pawn chick)
	{
		LeaveUntil[chick] = Find.TickManager.TicksGame + 30000;
		Dismount(chick);
	}

	public static bool NeedsBedRest(Pawn chick)
	{
		Building_Bed building_Bed = chick.CurrentBed();
		if ((building_Bed == null || !building_Bed.Medical) && !HealthAIUtility.ShouldSeekMedicalRest(chick))
		{
			return HealthAIUtility.ShouldBeTendedNowByPlayer(chick);
		}
		return true;
	}

	private static bool Busy(Pawn a)
	{
		if (!a.Drafted && !a.Downed && !a.InMentalState && a.Awake() && !AutoOff.Contains(a) && a.CurJob != null && !a.CurJob.playerForced && a.CurJobDef != TakeDef && a.CurJobDef != PutToBedDef)
		{
			return a.CurJobDef == JobDefOf.LayDown;
		}
		return true;
	}

	private static void Give(Pawn a, Job job)
	{
		Job curJob = a.CurJob;
		bool resumeCurJobAfterwards = curJob != null && curJob.def?.suspendable == true;
		a.jobs.StartJob(job, JobCondition.InterruptForced, null, resumeCurJobAfterwards);
	}

	public static void AutoTick(Map map)
	{
		if (TakeDef == null || PutToBedDef == null || GenHostility.AnyHostileActiveThreatToPlayer(map))
		{
			return;
		}
		int ticksGame = Find.TickManager.TicksGame;
		foreach (KeyValuePair<Pawn, List<Pawn>> item in ChicksOf.ToList())
		{
			Pawn key = item.Key;
			if (!key.Spawned || key.Map != map || Busy(key))
			{
				continue;
			}
			Pawn pawn = item.Value.FirstOrDefault((Pawn c) => c?.needs?.rest != null && c.needs.rest.CurLevel < 0.3f);
			if (pawn != null)
			{
				Building_Bed building_Bed = pawn.ownership?.OwnedBed;
				if (building_Bed == null || !building_Bed.Spawned || building_Bed.Map != map)
				{
					building_Bed = RestUtility.FindBedFor(pawn, key, checkSocialProperness: false);
				}
				if (building_Bed == null || !key.CanReach(building_Bed, PathEndMode.Touch, Danger.Deadly))
				{
					Dismount(pawn);
				}
				else
				{
					Give(key, JobMaker.MakeJob(PutToBedDef, pawn, building_Bed));
				}
			}
		}
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
		foreach (Pawn item2 in list)
		{
			if (!IsChick(item2) || CarrierOf.ContainsKey(item2) || item2.Downed || item2.InMentalState || !item2.Awake() || item2.CarriedBy != null || NeedsBedRest(item2))
			{
				continue;
			}
			if (LeaveUntil.TryGetValue(item2, out var value))
			{
				if (value > ticksGame)
				{
					continue;
				}
				LeaveUntil.Remove(item2);
			}
			if ((item2.needs?.rest != null && item2.needs.rest.CurLevel < 0.85f) || (item2.needs?.food != null && item2.needs.food.CurLevelPercentage < 0.3f) || map.reservationManager.IsReservedByAnyoneOf(item2, Faction.OfPlayer))
			{
				continue;
			}
			Pawn pawn2 = null;
			float num = 1600f;
			foreach (Pawn item3 in list)
			{
				if (item3 != item2 && IsGrownAvir(item3) && !Busy(item3) && CannotCarry(item3, item2) == null)
				{
					float num2 = (item3.Position - item2.Position).LengthHorizontalSquared;
					if (num2 < num && item3.CanReach(item2, PathEndMode.Touch, Danger.Deadly))
					{
						pawn2 = item3;
						num = num2;
					}
				}
			}
			if (pawn2 != null)
			{
				Give(pawn2, JobMaker.MakeJob(TakeDef, item2));
			}
		}
	}
}
