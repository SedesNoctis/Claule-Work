using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Avir;

public class JobDriver_AvirRideOnBack : JobDriver
{
	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		return true;
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		Toil toil = ToilMaker.MakeToil("AvirRide");
		toil.defaultCompleteMode = ToilCompleteMode.Never;
		toil.tickIntervalAction = delegate
		{
			if (!AvirChicks.CarrierOf.ContainsKey(pawn))
			{
				ReadyForNextToil();
			}
		};
		yield return toil;
	}

	public override string GetReport()
	{
		if (!AvirChicks.CarrierOf.TryGetValue(pawn, out var value))
		{
			return base.GetReport();
		}
		return "riding on " + value.LabelShort + "'s back.";
	}
}
