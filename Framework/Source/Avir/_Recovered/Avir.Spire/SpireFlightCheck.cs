using Verse;

namespace Avir.Spire;

[StaticConstructorOnStartup]
public static class SpireFlightCheck
{
	static SpireFlightCheck()
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_SpireCradle");
		if (ModsConfig.OdysseyActive && namedSilentFail != null)
		{
			if (namedSilentFail.thingClass != typeof(Building_SpireCradle))
			{
				Log.Warning("[Avir] Odyssey is active but the cradle of the spire is " + namedSilentFail.thingClass?.ToString() + " - the flight patch did not apply.");
			}
			else
			{
				Log.Message("[Avir] The spire can fly (Odyssey): the cradle is a grav engine.");
			}
		}
	}
}
