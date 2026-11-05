using HarmonyLib;
using Verse;

namespace Avir.Linarian;

[HarmonyPatch(typeof(PawnRenderNode), "TexPathFor")]
public static class Patch_LinarianTail
{
	private const string Tail = "Things/Pawn/Humanlike/Avir/BodyAttachments/Tails/AvirTail";

	public static void Postfix(Pawn pawn, ref string __result)
	{
		if (__result == "Things/Pawn/Humanlike/Avir/BodyAttachments/Tails/AvirTail" && Linarian.Is(pawn))
		{
			__result = "Things/Pawn/Humanlike/Avir/BodyAttachments/Tails/AvirTailDouble";
		}
	}
}
