using RimWorld;
using UnityEngine;
using Verse;

namespace Avir;

public class ThoughtWorker_AvirLordsPresent : ThoughtWorker_Precept
{
	protected override ThoughtState ShouldHaveThought(Pawn p)
	{
		if (!p.Spawned || !AvirRhythmUtility.IsAvir(p) || !AvirSisters.IsSisters(p.Ideo))
		{
			return false;
		}
		int num = 0;
		foreach (Precept item in p.Ideo.PreceptsListForReading)
		{
			if (item is Precept_RoleSingle { Active: not false } precept_RoleSingle && AvirSistersRoles.IsLordRole(precept_RoleSingle.def))
			{
				Pawn chosenPawnValue = precept_RoleSingle.ChosenPawnValue;
				if (chosenPawnValue != null && chosenPawnValue != p && chosenPawnValue.Spawned && chosenPawnValue.Map == p.Map)
				{
					num += AvirSistersRoles.Social(chosenPawnValue);
				}
			}
		}
		if (num <= 0)
		{
			return false;
		}
		return ThoughtState.ActiveAtStage(Mathf.Clamp(num / 10, 0, 5));
	}
}
