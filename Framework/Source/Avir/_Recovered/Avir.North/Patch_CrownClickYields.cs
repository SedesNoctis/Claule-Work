using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.North;

[HarmonyPatch(typeof(GenUI), "ThingsUnderMouse")]
public static class Patch_CrownClickYields
{
	public static void Postfix(Vector3 clickPos, List<Thing> __result)
	{
		if (__result == null || __result.Count < 2)
		{
			return;
		}
		IntVec3 intVec = IntVec3.FromVector3(clickPos);
		bool flag = false;
		for (int i = 0; i < __result.Count; i++)
		{
			Thing thing = __result[i];
			if (thing.Position == intVec && !IsGroundCover(thing) && (!(thing is Plant_AvirNorth plant_AvirNorth) || !plant_AvirNorth.def.plant.IsTree))
			{
				flag = true;
				break;
			}
			if (thing.Position == intVec && thing is Plant plant && plant.def.plant.IsTree)
			{
				return;
			}
		}
		if (!flag)
		{
			return;
		}
		int num = 0;
		for (int num2 = __result.Count - 1 - num; num2 >= 0; num2--)
		{
			if (__result[num2] is Plant_AvirNorth plant_AvirNorth2 && plant_AvirNorth2.def.plant.IsTree && plant_AvirNorth2.Position != intVec)
			{
				__result.RemoveAt(num2);
				__result.Add(plant_AvirNorth2);
				num++;
			}
		}
	}

	private static bool IsGroundCover(Thing t)
	{
		if (t is Plant plant && !plant.def.plant.IsTree)
		{
			return plant.def.plant.maxMeshCount > 1;
		}
		return false;
	}
}
