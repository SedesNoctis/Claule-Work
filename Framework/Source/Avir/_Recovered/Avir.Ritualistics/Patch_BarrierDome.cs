using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics;

[HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
public static class Patch_BarrierDome
{
	public static void Postfix(Projectile __instance, Vector3 lastExactPos, Vector3 newExactPos, ref bool __result)
	{
		if (__result || BarrierDome.All.Count == 0 || !__instance.Spawned)
		{
			return;
		}
		Map map = __instance.Map;
		Thing launcher = __instance.Launcher;
		foreach (BarrierDome item in BarrierDome.All.ToList())
		{
			if (item.map == map && !item.InGroup(lastExactPos) && item.Inside(newExactPos) && (launcher == null || launcher.Faction != Faction.OfPlayer) && (item.hit == null || item.hit(__instance)))
			{
				RitualFx.Ripple(newExactPos, map, 2.5f);
				FleckMaker.ThrowMicroSparks(newExactPos, map);
				__instance.Destroy();
				__result = true;
				break;
			}
		}
	}
}
