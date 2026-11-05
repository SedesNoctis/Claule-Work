using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(RaceProperties), "CanEverEat", new Type[] { typeof(ThingDef) })]
public static class Patch_AvirAnimalsSpareCrops
{
	private static readonly Dictionary<ThingDef, bool> spared = new Dictionary<ThingDef, bool>();

	public static bool Spared(ThingDef plant)
	{
		if (plant?.plant == null)
		{
			return false;
		}
		if (spared.TryGetValue(plant, out var value))
		{
			return value;
		}
		ThingDef harvestedThingDef = plant.plant.harvestedThingDef;
		value = !plant.plant.IsTree && harvestedThingDef != null && (harvestedThingDef.IsDrug || harvestedThingDef.IsMedicine || !harvestedThingDef.IsNutritionGivingIngestible);
		spared[plant] = value;
		return value;
	}

	public static void Postfix(RaceProperties __instance, ThingDef t, ref bool __result)
	{
		if (__result && __instance.Animal && Spared(t))
		{
			__result = false;
		}
	}
}
