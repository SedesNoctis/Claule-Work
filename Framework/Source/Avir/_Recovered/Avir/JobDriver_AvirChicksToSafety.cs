using System.Collections.Generic;
using FlightGene;
using Verse.AI;

namespace Avir;

public class JobDriver_AvirChicksToSafety : JobDriver
{
	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		return true;
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		yield return Toils_General.Do(delegate
		{
			if (AvirFlightUtility.CanTakeOff(pawn))
			{
				AvirFlightUtility.TakeOff(pawn, automatic: true);
			}
		});
		yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
		yield return Toils_General.Do(delegate
		{
			AvirChicks.DismountAll(pawn, job.targetA.Cell);
		});
	}

	public override string GetReport()
	{
		return "flying the chicks to safety.";
	}
}
