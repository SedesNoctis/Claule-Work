using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(EquipmentUtility), "CanEquip", new Type[]
{
	typeof(Thing),
	typeof(Pawn),
	typeof(string),
	typeof(bool)
}, new ArgumentType[]
{
	ArgumentType.Normal,
	ArgumentType.Normal,
	ArgumentType.Out,
	ArgumentType.Normal
})]
public static class Patch_LinarianNoArtifacts
{
	public static void Postfix(Thing thing, Pawn pawn, ref string cantReason, ref bool __result)
	{
		if (__result && Linarian.Is(pawn))
		{
			if (thing?.def != null && thing.def.IsRangedWeapon)
			{
				__result = false;
				cantReason = "a Singer does not shoot: she has her aria";
			}
			else if (Linarian.IsArtifactGear(thing))
			{
				__result = false;
				cantReason = "a Singer makes artifacts, but never wears them";
			}
		}
	}
}
