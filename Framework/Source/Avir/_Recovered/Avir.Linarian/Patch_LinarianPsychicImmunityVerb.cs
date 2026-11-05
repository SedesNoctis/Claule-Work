using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(Verb_CastAbility), "ValidateTarget")]
public static class Patch_LinarianPsychicImmunityVerb
{
	public static void Postfix(Verb_CastAbility __instance, LocalTargetInfo target, bool showMessages, ref bool __result)
	{
		if (__result && target.Thing is Pawn pawn && pawn != __instance.CasterPawn && Linarian.Is(pawn) && Linarian.IsPsychic(__instance.ability?.def))
		{
			__result = false;
			if (showMessages)
			{
				Messages.Message(pawn.LabelShortCap + " is a Singer: no power of the mind takes hold of her.", pawn, MessageTypeDefOf.RejectInput, historical: false);
			}
		}
	}
}
