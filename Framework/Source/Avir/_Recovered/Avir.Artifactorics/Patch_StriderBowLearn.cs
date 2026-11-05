using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(SkillRecord), "Learn")]
public static class Patch_StriderBowLearn
{
	public static void Postfix(SkillRecord __instance, float xp, bool direct, Pawn ___pawn)
	{
		if (direct || xp <= 0f || __instance.def != SkillDefOf.Shooting)
		{
			return;
		}
		CompStriderBow compStriderBow = StriderPaths.Drawn(___pawn);
		if (compStriderBow != null)
		{
			int stones = compStriderBow.Stones;
			compStriderBow.xp += xp;
			int stones2 = compStriderBow.Stones;
			if (stones2 > stones && ___pawn.Faction == Faction.OfPlayer)
			{
				string text = ((compStriderBow.path == null) ? " Choose its path." : ((stones2 == 3 || stones2 == 9 || stones2 == 18) ? (" A new power of " + StriderPaths.Label(compStriderBow.path) + " wakes.") : ""));
				Messages.Message(___pawn.LabelShortCap + "'s bow has gained a stone (" + stones2 + "/" + 18 + ")." + text, ___pawn, MessageTypeDefOf.PositiveEvent, historical: false);
			}
		}
	}
}
