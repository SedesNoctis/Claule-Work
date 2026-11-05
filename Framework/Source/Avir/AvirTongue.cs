using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// AN AVIR SPEAKS WITH ITS TONGUE. The beak is not the voice: the tongue inside it is. No tongue - mute, though the
    /// beak still eats; no beak - no tongue either; a metal beak has no tongue at all.
    /// </summary>
    [HarmonyPatch(typeof(PawnCapacityWorker_Talking), nameof(PawnCapacityWorker_Talking.CalculateCapacityLevel))]
    public static class Patch_AvirTongueTalking
    {
        public static void Postfix(HediffSet diffSet, ref float __result)
        {
            if (__result <= 0f) return;
            Pawn p = diffSet?.pawn;
            if (p == null || !AvirBody.IsAvirBody(p)) return;
            BodyPartRecord beak = AvirBeakless.Beak;
            if (beak == null) return;
            if (diffSet.PartIsMissing(beak) || diffSet.HasDirectlyAddedPartFor(beak)) { __result = 0f; return; }
            foreach (BodyPartRecord t in beak.parts)
                if (t.def == AvirBody.Tongue && diffSet.PartIsMissing(t)) { __result = 0f; return; }
        }
    }
}
