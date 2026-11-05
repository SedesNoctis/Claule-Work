using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch]
public static class Patch_SongSleepHolds
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		return from m in typeof(RestUtility).GetMethods(BindingFlags.Static | BindingFlags.Public)
			where m.Name == "WakeUp"
			select m;
	}

	public static bool Prefix(Pawn __0)
	{
		if (__0?.health?.hediffSet != null && LinarianHediffs.Avir_SongSleep != null)
		{
			return !__0.health.hediffSet.HasHediff(LinarianHediffs.Avir_SongSleep);
		}
		return true;
	}
}
