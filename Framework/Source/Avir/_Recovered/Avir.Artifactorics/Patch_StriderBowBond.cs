using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Artifactorics;

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
public static class Patch_StriderBowBond
{
	public static void Postfix(Thing thing, Pawn pawn, ref string cantReason, ref bool __result)
	{
		if (!__result)
		{
			return;
		}
		CompStriderBow compStriderBow = CompStriderBow.Of(thing);
		if (compStriderBow != null)
		{
			if (compStriderBow.BondedToOther(pawn))
			{
				__result = false;
				cantReason = "bonded to " + compStriderBow.bonded.LabelShort;
			}
			else if (compStriderBow.bonded == null && !CompStriderBow.CanBond(pawn))
			{
				__result = false;
				cantReason = "only an Avir can bond with it";
			}
		}
	}
}
