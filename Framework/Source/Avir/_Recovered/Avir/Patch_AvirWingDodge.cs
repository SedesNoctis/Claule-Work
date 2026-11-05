using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch]
public static class Patch_AvirWingDodge
{
	public const float DodgeChance = 0.6f;

	public static IEnumerable<MethodBase> TargetMethods()
	{
		foreach (Type item in typeof(DamageWorker_AddInjury).AllSubclasses().Concat(new Type[1] { typeof(DamageWorker_AddInjury) }))
		{
			MethodInfo methodInfo = AccessTools.DeclaredMethod(item, "ChooseHitPart", new Type[2]
			{
				typeof(DamageInfo),
				typeof(Pawn)
			});
			if (methodInfo != null && !methodInfo.IsAbstract)
			{
				yield return methodInfo;
			}
		}
	}

	public static void Postfix(DamageInfo dinfo, Pawn pawn, ref BodyPartRecord __result)
	{
		if (__result == null || (__result.def != AvirWingParts.Wing && (AvirBody.Tail == null || __result.def != AvirBody.Tail)) || pawn == null || pawn.Downed || !pawn.Awake() || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving) || !Rand.Chance(0.6f))
		{
			return;
		}
		for (int i = 0; i < 6; i++)
		{
			BodyPartRecord randomNotMissingPart = pawn.health.hediffSet.GetRandomNotMissingPart(dinfo.Def, dinfo.Height, dinfo.Depth);
			if (randomNotMissingPart != null && randomNotMissingPart.def != AvirWingParts.Wing && randomNotMissingPart.def != AvirBody.Tail)
			{
				__result = randomNotMissingPart;
				break;
			}
		}
	}
}
