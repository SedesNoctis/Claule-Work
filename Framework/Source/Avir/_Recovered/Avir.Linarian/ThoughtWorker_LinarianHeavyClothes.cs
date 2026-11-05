using System.Linq;
using RimWorld;
using Verse;

namespace Avir.Linarian;

public class ThoughtWorker_LinarianHeavyClothes : ThoughtWorker
{
	protected override ThoughtState CurrentStateInternal(Pawn p)
	{
		if (!Linarian.Is(p) || p.apparel == null)
		{
			return ThoughtState.Inactive;
		}
		float num = p.apparel.WornApparel.Sum((Apparel a) => a.GetStatValue(StatDefOf.Mass));
		if (num > 8f)
		{
			return ThoughtState.ActiveAtStage(1);
		}
		if (num > 3f)
		{
			return ThoughtState.ActiveAtStage(0);
		}
		return ThoughtState.Inactive;
	}
}
