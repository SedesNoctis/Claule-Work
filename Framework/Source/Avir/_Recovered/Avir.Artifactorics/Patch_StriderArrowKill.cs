using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(Pawn), "Kill")]
public static class Patch_StriderArrowKill
{
	public static void Prefix(Pawn __instance, DamageInfo? dinfo, out Pawn __state)
	{
		__state = null;
		if (dinfo.HasValue && StriderHit.IsArrow(dinfo.Value, out var archer, out var bow) && bow.path == "Flame" && bow.Stones >= 9 && __instance.Spawned)
		{
			__state = archer;
		}
	}

	public static void Postfix(Pawn __instance, Pawn __state)
	{
		if (__state == null || __instance.Corpse?.Map == null)
		{
			return;
		}
		CompStriderBow compStriderBow = StriderPaths.Drawn(__state);
		if (compStriderBow == null)
		{
			return;
		}
		float num = StriderPaths.Power(compStriderBow, __state);
		StriderHit.inner = true;
		try
		{
			IntVec3 position = __instance.Corpse.Position;
			Map map = __instance.Corpse.Map;
			DamageDef flame = DamageDefOf.Flame;
			int damAmount = Mathf.RoundToInt(8f * num);
			List<Thing> ignoredThings = new List<Thing> { __state };
			GenExplosion.DoExplosion(position, map, 1.9f, flame, __state, damAmount, -1f, null, null, null, null, null, 0f, 1, null, null, 255, applyDamageToExplosionCellsNeighbors: false, null, 0f, 1, 0f, damageFalloff: false, null, ignoredThings);
		}
		finally
		{
			StriderHit.inner = false;
		}
	}
}
