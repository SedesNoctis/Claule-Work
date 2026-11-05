using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Spire;

[HarmonyPatch(typeof(RitualOutcomeEffectWorker_GravshipLaunch), "Apply")]
public static class Patch_SpireCleanLanding
{
	public static void Prefix(LordJob_Ritual jobRitual, out CompSpireHelm __state)
	{
		__state = jobRitual?.selectedTarget.Thing?.TryGetComp<CompSpireHelm>();
	}

	public static void Postfix(CompSpireHelm __state)
	{
		if (__state?.engine?.launchInfo != null)
		{
			__state.engine.launchInfo.doNegativeOutcome = false;
		}
	}
}
