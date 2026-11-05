using RimWorld;
using Verse;

namespace Avir;

public class StatPart_ArcaneHunger : StatPart
{
	public override void TransformValue(StatRequest req, ref float val)
	{
		Gene_ArcaneHunger gene_ArcaneHunger = GeneFor(req);
		if (gene_ArcaneHunger != null)
		{
			val *= gene_ArcaneHunger.PsychicFactor;
		}
	}

	public override string ExplanationPart(StatRequest req)
	{
		Gene_ArcaneHunger gene_ArcaneHunger = GeneFor(req);
		if (gene_ArcaneHunger == null)
		{
			return null;
		}
		float psychicFactor = gene_ArcaneHunger.PsychicFactor;
		if (psychicFactor >= 0.999f)
		{
			return null;
		}
		return "Arcane hunger: x" + psychicFactor.ToStringPercent();
	}

	private static Gene_ArcaneHunger GeneFor(StatRequest req)
	{
		return ArcaneHungerCache.For(req.Thing as Pawn);
	}
}
