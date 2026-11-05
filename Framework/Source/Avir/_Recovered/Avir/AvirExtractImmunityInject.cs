using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir;

[StaticConstructorOnStartup]
public static class AvirExtractImmunityInject
{
	static AvirExtractImmunityInject()
	{
		StatDef namedSilentFail = DefDatabase<StatDef>.GetNamedSilentFail("ImmunityGainSpeed");
		if (namedSilentFail != null)
		{
			if (namedSilentFail.parts == null)
			{
				namedSilentFail.parts = new List<StatPart>();
			}
			if (!namedSilentFail.parts.Exists((StatPart p) => p is StatPart_AvirExtractImmunity))
			{
				namedSilentFail.parts.Add(new StatPart_AvirExtractImmunity
				{
					parentStat = namedSilentFail
				});
			}
		}
	}
}
