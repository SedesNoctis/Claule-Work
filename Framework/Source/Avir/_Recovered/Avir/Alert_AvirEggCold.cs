using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Avir;

public class Alert_AvirEggCold : Alert_Critical
{
	private readonly List<Thing> eggs = new List<Thing>();

	public Alert_AvirEggCold()
	{
		defaultLabel = "Avir egg is freezing";
	}

	private List<Thing> Cold()
	{
		eggs.Clear();
		foreach (Map map in Find.Maps)
		{
			foreach (Thing item in map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamedSilentFail("Avir_Egg") ?? ThingDefOf.Steel))
			{
				if (AvirEggWarmth.IsAvirEgg(item) && AvirEggWarmth.Chilling(item))
				{
					eggs.Add(item);
				}
			}
		}
		return eggs;
	}

	public override TaggedString GetExplanation()
	{
		return "These Avir eggs are colder than their shell can bear and are spoiling:\n\n" + string.Join("\n", eggs.Select((Thing e) => "  - " + e.LabelShort + " (" + e.AmbientTemperature.ToStringTemperature() + ")")) + "\n\nWarm the room, move them to an egg shelf, or let their mother sit by them: a nest or the mother keeps an egg safe down to " + (-40f).ToStringTemperature() + ".";
	}

	public override AlertReport GetReport()
	{
		return AlertReport.CulpritsAre(Cold());
	}
}
