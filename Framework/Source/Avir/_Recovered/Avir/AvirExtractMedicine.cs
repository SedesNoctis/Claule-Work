using System;
using Verse;

namespace Avir;

[StaticConstructorOnStartup]
public static class AvirExtractMedicine
{
	public static readonly ThingDef Extract = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_EssenceExtract");

	public static readonly HediffDef Sated = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_SatedByExtract");

	[ThreadStatic]
	public static Pawn patient;

	[ThreadStatic]
	public static bool excludeExtract;

	public static bool NeedsImmunity(Pawn p)
	{
		if (p?.health?.hediffSet != null && (Sated == null || !p.health.hediffSet.HasHediff(Sated)))
		{
			return p.health.hediffSet.hediffs.Any(delegate(Hediff h)
			{
				HediffComp_Immunizable hediffComp_Immunizable = h.TryGetComp<HediffComp_Immunizable>();
				return hediffComp_Immunizable != null && !hediffComp_Immunizable.FullyImmune;
			});
		}
		return false;
	}
}
