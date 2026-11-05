using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Avir.Artifactorics;

[StaticConstructorOnStartup]
public static class Patch_StriderBowCE
{
	static Patch_StriderBowCE()
	{
		Type type = AccessTools.TypeByName("CombatExtended.Verb_ShootCE");
		if (!(type == null))
		{
			Harmony harmony = new Harmony("avir.striderbow.ce");
			MethodInfo methodInfo = AccessTools.Method(type, "Available");
			MethodInfo methodInfo2 = AccessTools.Method(type, "TryCastShot");
			if (methodInfo != null)
			{
				harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(Patch_StriderBowCE), "AvailablePostfix"));
			}
			if (methodInfo2 != null)
			{
				harmony.Patch(methodInfo2, null, new HarmonyMethod(typeof(Patch_StriderBowCE), "CastPostfix"));
			}
		}
	}

	public static void AvailablePostfix(Verb __instance, ref bool __result)
	{
		if (__result && CompStriderBow.Of(__instance.EquipmentSource) != null && !StriderBow.CanFire(__instance.EquipmentSource))
		{
			__result = false;
		}
	}

	public static void CastPostfix(Verb __instance, bool __result)
	{
		if (__result && CompStriderBow.Of(__instance.EquipmentSource) != null)
		{
			StriderBow.Spend(__instance.EquipmentSource, __instance.CasterPawn);
		}
	}
}
