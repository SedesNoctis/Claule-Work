using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(PawnUIOverlay), "DrawPawnGUIOverlay")]
public static class Patch_HeraldNoNameplate
{
	public static bool Prefix(Pawn ___pawn)
	{
		if (!LinarianPresence.Disguised(___pawn))
		{
			if (LinarianVeil.Veiled(___pawn))
			{
				return ___pawn.Faction == Faction.OfPlayer;
			}
			return true;
		}
		return false;
	}
}
