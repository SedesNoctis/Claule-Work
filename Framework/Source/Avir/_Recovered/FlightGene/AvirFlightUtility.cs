using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avir;
using Avir.Linarian;
using RimWorld;
using Verse;

namespace FlightGene;

public static class AvirFlightUtility
{
	private class CarrierBox
	{
		public bool carries;

		public bool everChecked;

		public int checkedAtTick;
	}

	public const float AutoTakeoffRestThreshold = 0.25f;

	private const int RecheckIntervalTicks = 2500;

	private static readonly ConditionalWeakTable<Pawn, CarrierBox> carrierTable = new ConditionalWeakTable<Pawn, CarrierBox>();

	private static GeneDef flightGene;

	private static HediffDef flightHediff;

	private static bool lookedUp;

	public const float WaterCrossingRestThreshold = 0.1f;

	private static readonly Dictionary<int, int> landedAtTick = new Dictionary<int, int>();

	public static GeneDef FlightGeneDef
	{
		get
		{
			EnsureLookedUp();
			return flightGene;
		}
	}

	public static HediffDef FlightHediff
	{
		get
		{
			EnsureLookedUp();
			return flightHediff;
		}
	}

	private static void EnsureLookedUp()
	{
		if (!lookedUp)
		{
			lookedUp = true;
			flightGene = DefDatabase<GeneDef>.GetNamedSilentFail("Flight_Gene");
			flightHediff = DefDatabase<HediffDef>.GetNamedSilentFail("Flight_Hediff");
		}
	}

	public static bool IsFlying(Pawn pawn)
	{
		if (AvirFlightRegistry.NobodyFlying)
		{
			return false;
		}
		if (pawn == null)
		{
			return false;
		}
		if (AvirFlightRegistry.Contains(pawn))
		{
			return true;
		}
		if (!AvirFlightCarriers.MayCarry(pawn))
		{
			return false;
		}
		EnsureLookedUp();
		if (flightHediff == null || pawn.health?.hediffSet == null)
		{
			return false;
		}
		return pawn.health.hediffSet.HasHediff(flightHediff);
	}

	public static bool CanTakeOff(Pawn pawn)
	{
		return CanTakeOff(pawn, 0.25f);
	}

	public static bool CanTakeOff(Pawn pawn, float restFloor)
	{
		EnsureLookedUp();
		if (flightGene == null || pawn == null)
		{
			return false;
		}
		if (!AvirFlightCarriers.MayCarry(pawn))
		{
			return false;
		}
		if (pawn.Dead || pawn.Downed)
		{
			return false;
		}
		if (GroundedByForm(pawn))
		{
			return false;
		}
		if (!CarriesFlightGene(pawn))
		{
			return false;
		}
		if (AvirWingParts.Clipped(pawn))
		{
			return false;
		}
		Need_Rest need_Rest = pawn.needs?.rest;
		if (need_Rest != null)
		{
			return need_Rest.CurLevel > restFloor;
		}
		return true;
	}

	public static bool CarriesFlightGene(Pawn pawn)
	{
		EnsureLookedUp();
		if (flightGene == null || pawn == null)
		{
			return false;
		}
		if (pawn.genes == null)
		{
			return false;
		}
		int num = Find.TickManager?.TicksGame ?? 0;
		CarrierBox value = carrierTable.GetValue(pawn, (Pawn _) => new CarrierBox());
		if (!value.everChecked || num - value.checkedAtTick >= 2500)
		{
			value.everChecked = true;
			value.carries = pawn.genes.HasActiveGene(flightGene);
			value.checkedAtTick = num;
		}
		return value.carries;
	}

	public static void Land(Pawn pawn)
	{
		HediffDef hediffDef = FlightHediff;
		if (hediffDef != null && pawn?.health?.hediffSet != null)
		{
			Hediff firstHediffOfDef = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
			if (firstHediffOfDef != null)
			{
				pawn.health.RemoveHediff(firstHediffOfDef);
			}
			NoteLanded(pawn);
		}
	}

	public static void NoteLanded(Pawn pawn)
	{
		if (pawn != null && Find.TickManager != null)
		{
			landedAtTick[pawn.thingIDNumber] = Find.TickManager.TicksGame;
		}
	}

	public static int TicksSinceLanded(Pawn pawn)
	{
		if (pawn == null || Find.TickManager == null)
		{
			return int.MaxValue;
		}
		if (!landedAtTick.TryGetValue(pawn.thingIDNumber, out var value))
		{
			return int.MaxValue;
		}
		return Find.TickManager.TicksGame - value;
	}

	public static bool GroundedByForm(Pawn pawn)
	{
		string text = LinarianVeil.InForm(pawn);
		if (text != null)
		{
			return text != "Crow";
		}
		return false;
	}

	public static void TakeOff(Pawn pawn, bool automatic)
	{
		EnsureLookedUp();
		if (flightHediff == null || pawn?.health == null || IsFlying(pawn) || GroundedByForm(pawn))
		{
			return;
		}
		Hediff hediff = HediffMaker.MakeHediff(flightHediff, pawn);
		pawn.health.AddHediff(hediff);
		if (automatic)
		{
			HediffComp_FlightMonitor hediffComp_FlightMonitor = (hediff as HediffWithComps)?.GetComp<HediffComp_FlightMonitor>();
			if (hediffComp_FlightMonitor != null)
			{
				hediffComp_FlightMonitor.autoTriggered = true;
			}
		}
	}
}
