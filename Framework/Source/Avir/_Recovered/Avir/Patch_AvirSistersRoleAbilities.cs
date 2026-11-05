using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir;

[HarmonyPatch(typeof(Precept_Role), "FillOrUpdateAbilityList")]
public static class Patch_AvirSistersRoleAbilities
{
	public static bool Prefix(Precept_Role __instance, Pawn forPawn, List<Ability> abilities, ref List<Ability> __result)
	{
		if (!AvirSisters.IsSisters(__instance.ideo))
		{
			return true;
		}
		List<AbilityDef> defs = AvirSistersRoles.AbilitiesFor(__instance.def);
		if (defs == null)
		{
			return true;
		}
		if (abilities != null)
		{
			abilities.RemoveAll((Ability a) => a == null || a.def == null);
			if (abilities.Count != defs.Count || abilities.Any((Ability a) => !defs.Contains(a.def)))
			{
				abilities = null;
			}
		}
		if (abilities == null)
		{
			abilities = new List<Ability>();
			foreach (AbilityDef item in defs)
			{
				abilities.Add(AbilityUtility.MakeAbility(item, forPawn, __instance));
			}
		}
		else
		{
			foreach (Ability ability in abilities)
			{
				ability.pawn = forPawn;
				ability.verb.caster = forPawn;
				ability.sourcePrecept = __instance;
			}
		}
		__result = abilities;
		return false;
	}
}
