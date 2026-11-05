using RimWorld;
using Verse;

namespace Avir;

public class ThoughtWorker_AvirArcaneHunger : ThoughtWorker
{
	protected override ThoughtState CurrentStateInternal(Pawn p)
	{
		Gene_ArcaneHunger gene_ArcaneHunger = Gene_ArcaneHunger.Of(p);
		if (gene_ArcaneHunger == null || !gene_ArcaneHunger.Active)
		{
			return ThoughtState.Inactive;
		}
		float value = gene_ArcaneHunger.Value;
		if (value >= 0.6f)
		{
			return ThoughtState.Inactive;
		}
		if (value >= 0.35f)
		{
			return ThoughtState.ActiveAtStage(0);
		}
		if (value >= 0.1f)
		{
			return ThoughtState.ActiveAtStage(1);
		}
		return ThoughtState.ActiveAtStage(2);
	}
}
