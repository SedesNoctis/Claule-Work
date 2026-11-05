using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir;

[StaticConstructorOnStartup]
public static class AvirHungerStatInjector
{
	static AvirHungerStatInjector()
	{
		StatDef psychicSensitivity = StatDefOf.PsychicSensitivity;
		if (psychicSensitivity == null)
		{
			return;
		}
		if (psychicSensitivity.parts == null)
		{
			psychicSensitivity.parts = new List<StatPart>();
		}
		for (int i = 0; i < psychicSensitivity.parts.Count; i++)
		{
			if (psychicSensitivity.parts[i] is StatPart_ArcaneHunger)
			{
				return;
			}
		}
		psychicSensitivity.parts.Add(new StatPart_ArcaneHunger
		{
			parentStat = psychicSensitivity
		});
	}
}
