using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics;

public class WorkGiver_ArtificerBill : WorkGiver_DoBill
{
	[ThreadStatic]
	public static WorkGiverDef current;

	public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
	{
		WorkGiverDef workGiverDef = current;
		current = def;
		try
		{
			return base.JobOnThing(pawn, thing, forced);
		}
		finally
		{
			current = workGiverDef;
		}
	}

	public override bool ShouldSkip(Pawn pawn, bool forced = false)
	{
		WorkGiverDef workGiverDef = current;
		current = def;
		try
		{
			return base.ShouldSkip(pawn, forced);
		}
		finally
		{
			current = workGiverDef;
		}
	}

	public static bool Takes(WorkGiverDef giver, Bill bill)
	{
		bool flag = ArtificerWork.IsDrug(bill.recipe);
		if (giver == ArtificerWork.Drugs)
		{
			if (flag)
			{
				return ArtificerWork.DrugWork != null;
			}
			return false;
		}
		if (flag)
		{
			return ArtificerWork.DrugWork == null;
		}
		return true;
	}
}
