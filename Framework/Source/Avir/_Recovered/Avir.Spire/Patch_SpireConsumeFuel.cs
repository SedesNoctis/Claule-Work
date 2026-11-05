using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;

namespace Avir.Spire;

[HarmonyPatch(typeof(Building_GravEngine), "ConsumeFuel")]
public static class Patch_SpireConsumeFuel
{
	public static bool Prefix(Building_GravEngine __instance, PlanetTile tile)
	{
		if (!(__instance is Building_SpireCradle { Spire: not null } building_SpireCradle))
		{
			return true;
		}
		if (!GravshipUtility.TryGetPathFuelCost(building_SpireCradle.Map.Tile, tile, out var cost, out var _))
		{
			return true;
		}
		float num = building_SpireCradle.Spire.Take(cost * 5f) / 5f;
		float num2 = cost - num;
		if (num2 <= 0.01f)
		{
			return false;
		}
		List<CompRefuelable> list = (from g in building_SpireCradle.GravshipComponents
			where g.CanBeActive && g.Props.providesFuel
			select g.parent.GetComp<CompRefuelable>() into r
			where r != null
			select r).ToList();
		float num3 = list.Sum((CompRefuelable r) => r.Fuel);
		if (num3 <= 0f)
		{
			return false;
		}
		float num4 = Mathf.Min(1f, num2 / num3);
		foreach (CompRefuelable item in list)
		{
			item.ConsumeFuel(item.Fuel * num4);
		}
		return false;
	}
}
