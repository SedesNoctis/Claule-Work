using RimWorld;

namespace Avir;

public class CompProperties_AbilityAvirInspire : CompProperties_AbilityEffect
{
	public InspirationDef inspiration;

	public CompProperties_AbilityAvirInspire()
	{
		compClass = typeof(CompAbilityEffect_AvirInspire);
	}
}
