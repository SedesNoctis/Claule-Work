using Avir.Linarian;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir;

public class HediffComp_FurShed : HediffComp
{
	private const int AccrualIntervalTicks = 60;

	private int nextShedTick = -1;

	private float loose;

	public HediffCompProperties_FurShed Props => (HediffCompProperties_FurShed)props;

	public int LooseCount => Mathf.FloorToInt(loose);

	public bool WorthGrooming
	{
		get
		{
			float num = Props.minLooseToGroom * AvirGroomingTraits.GroomThresholdFactor(parent.pawn);
			return loose >= num;
		}
	}

	public float LooseFraction
	{
		get
		{
			float poolCapacity = Props.poolCapacity;
			if (!(poolCapacity <= 0f))
			{
				return Mathf.Clamp01(loose / poolCapacity);
			}
			return 0f;
		}
	}

	public override string CompTipStringExtra
	{
		get
		{
			int looseCount = LooseCount;
			if (looseCount <= 0)
			{
				return null;
			}
			return "Loose feathers: " + looseCount;
		}
	}

	public override void CompPostTick(ref float severityAdjustment)
	{
		base.CompPostTick(ref severityAdjustment);
		Pawn pawn = parent.pawn;
		if (pawn == null || pawn.Dead)
		{
			return;
		}
		int num = AvirSimTier.ScaleInterval(pawn, 60);
		if ((Find.TickManager.TicksGame + (pawn.thingIDNumber & 0x7FFFFFFF)) % num != 0)
		{
			return;
		}
		float num2 = Props.feathersPerDay * AvirGroomingTraits.CoatFactor(pawn);
		if (num2 > 0f)
		{
			loose += num2 * (float)num / 60000f;
			if (loose > Props.poolCapacity)
			{
				loose = Props.poolCapacity;
			}
		}
		if (!pawn.Spawned || pawn.Map == null)
		{
			return;
		}
		int ticksGame = Find.TickManager.TicksGame;
		if (nextShedTick < 0)
		{
			nextShedTick = ticksGame + Rand.Range(Props.minTicksBetween, Props.maxTicksBetween);
		}
		else
		{
			if (ticksGame < nextShedTick)
			{
				return;
			}
			nextShedTick = ticksGame + Rand.Range(Props.minTicksBetween, Props.maxTicksBetween);
			if (pawn.Faction == Faction.OfPlayer || !Avir.Linarian.Linarian.Is(pawn) || Rand.Chance(0.12f))
			{
				int num3 = Mathf.Max(1, Props.amountPerShed);
				if (!(loose < (float)num3))
				{
					loose -= num3;
					Drop(pawn, num3);
				}
			}
		}
	}

	public int HarvestLoose(Pawn actor)
	{
		return HarvestLoose(actor, 1f);
	}

	public int HarvestLoose(Pawn actor, float fraction)
	{
		Pawn pawn = parent.pawn;
		if (pawn == null || !pawn.Spawned || pawn.Map == null)
		{
			return 0;
		}
		int num = Mathf.FloorToInt((float)LooseCount * Mathf.Clamp01(fraction));
		if (num <= 0)
		{
			return 0;
		}
		loose -= num;
		if (loose < 0f)
		{
			loose = 0f;
		}
		int num2 = num;
		if (Patch_AvirAShacsaignSpawn.IsSubrace(pawn))
		{
			num2 = num / 2;
			if (num2 <= 0)
			{
				return 0;
			}
		}
		Drop(pawn, num2);
		return num2;
	}

	private static void Drop(Pawn pawn, int amount)
	{
		if (FeatheredFurDefOf.Leather_AvirFeatheredFur != null)
		{
			Thing thing = ThingMaker.MakeThing(FeatheredFurDefOf.Leather_AvirFeatheredFur);
			thing.stackCount = Mathf.Max(1, amount);
			GenPlace.TryPlaceThing(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near);
		}
	}

	public override void CompExposeData()
	{
		base.CompExposeData();
		Scribe_Values.Look(ref nextShedTick, "avirFurNextShed", -1);
		Scribe_Values.Look(ref loose, "avirFurLoose", 0f);
	}
}
