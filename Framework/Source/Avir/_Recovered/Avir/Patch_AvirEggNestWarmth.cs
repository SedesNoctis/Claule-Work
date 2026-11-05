using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(CompTemperatureRuinable), "CompTick")]
public static class Patch_AvirEggNestWarmth
{
	public static bool Prefix(CompTemperatureRuinable __instance)
	{
		Thing parent = __instance.parent;
		if (!AvirEggWarmth.IsAvirEgg(parent) || !parent.Spawned)
		{
			return true;
		}
		float ambientTemperature = parent.AmbientTemperature;
		if (ambientTemperature < __instance.Props.minSafeTemperature && ambientTemperature >= -40f && AvirEggWarmth.Nested(parent))
		{
			return false;
		}
		return true;
	}
}
