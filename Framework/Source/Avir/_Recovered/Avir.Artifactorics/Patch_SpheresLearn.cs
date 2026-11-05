using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Artifactorics;

[HarmonyPatch(typeof(SkillRecord), "Learn")]
public static class Patch_SpheresLearn
{
	public static void Postfix(SkillRecord __instance, float xp, bool direct, Pawn ___pawn)
	{
		if (direct || xp <= 0f || ___pawn == null)
		{
			return;
		}
		SkillDef def = __instance.def;
		if (def != SkillDefOf.Construction && def != SkillDefOf.Crafting && def != SkillDefOf.Artistic && def != SkillDefOf.Intellectual)
		{
			return;
		}
		CompArchitectSpheres compArchitectSpheres = SpherePaths.Of(___pawn);
		if (compArchitectSpheres != null)
		{
			int stones = compArchitectSpheres.Stones;
			compArchitectSpheres.xp += xp;
			int stones2 = compArchitectSpheres.Stones;
			if (stones2 > stones && ___pawn.Faction == Faction.OfPlayer)
			{
				string text = ((compArchitectSpheres.path == null) ? " Choose their path." : (SpherePaths.Nodes.Contains(stones2) ? (" A new power of " + SpherePaths.Label(compArchitectSpheres.path) + " wakes.") : ""));
				Messages.Message(___pawn.LabelShortCap + "'s spheres have gained a stone (" + stones2 + "/" + 12 + ")." + text, ___pawn, MessageTypeDefOf.PositiveEvent, historical: false);
				compArchitectSpheres.Sync(___pawn);
			}
		}
	}
}
