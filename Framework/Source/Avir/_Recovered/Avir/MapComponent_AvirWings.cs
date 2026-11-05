using System.Linq;
using FlightGene;
using RimWorld;
using Verse;

namespace Avir;

public class MapComponent_AvirWings : MapComponent
{
	public MapComponent_AvirWings(Map map)
		: base(map)
	{
	}

	public override void FinalizeInit()
	{
		base.FinalizeInit();
		foreach (Pawn item in map.mapPawns.AllPawns.ToList())
		{
			RaceProperties raceProps = item.RaceProps;
			if (raceProps != null && raceProps.Humanlike)
			{
				AvirWingParts.Ensure(item);
			}
		}
	}

	public override void MapComponentTick()
	{
		if (Find.TickManager.TicksGame % 120 != 0)
		{
			return;
		}
		foreach (Pawn item in map.mapPawns.AllPawnsSpawned)
		{
			if (AvirFlightUtility.IsFlying(item) && AvirWingParts.Clipped(item))
			{
				AvirFlightUtility.Land(item);
				if (item.Faction == Faction.OfPlayer)
				{
					Messages.Message(item.LabelShort + " cannot stay in the air: a wing is broken.", item, MessageTypeDefOf.NegativeEvent, historical: false);
				}
			}
		}
	}
}
