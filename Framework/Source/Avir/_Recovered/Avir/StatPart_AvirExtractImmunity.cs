using RimWorld;
using Verse;

namespace Avir;

public class StatPart_AvirExtractImmunity : StatPart
{
	public const float Bonus = 0.2f;

	private static HediffDef sated;

	private static HediffDef Sated => sated ?? (sated = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_SatedByExtract"));

	private static bool Has(StatRequest req)
	{
		if (req.Thing is Pawn pawn && Sated != null)
		{
			Pawn_HealthTracker health = pawn.health;
			if (health == null)
			{
				return false;
			}
			return health.hediffSet?.HasHediff(Sated) == true;
		}
		return false;
	}

	public override void TransformValue(StatRequest req, ref float val)
	{
		if (Has(req))
		{
			val += 0.2f;
		}
	}

	public override string ExplanationPart(StatRequest req)
	{
		if (!Has(req))
		{
			return null;
		}
		return "Sated by essence extract: +" + 0.2f.ToStringPercent();
	}
}
