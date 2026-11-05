using RimWorld;
using Verse;

namespace Avir;

public class ThoughtWorker_AvirClippedWings : ThoughtWorker
{
	protected override ThoughtState CurrentStateInternal(Pawn p)
	{
		if (!AvirWingParts.Clipped(p))
		{
			return ThoughtState.Inactive;
		}
		return ThoughtState.ActiveAtStage(0);
	}
}
