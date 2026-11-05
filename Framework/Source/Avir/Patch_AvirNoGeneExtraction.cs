using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// AVIR GENES DO NOT COME OUT. The gene extractor refuses an Avir outright: their genes are woven into
    /// the essence that holds them together, and a xenogerm cut from one would be dead tissue. (It also
    /// keeps the subspecies genes - which decide the very shape of the body - from ever reaching a
    /// xenogerm.)
    /// </summary>
    [HarmonyPatch(typeof(Building_GeneExtractor), nameof(Building_GeneExtractor.CanAcceptPawn))]
    public static class Patch_AvirNoGeneExtraction
    {
        public static void Postfix(Pawn pawn, ref AcceptanceReport __result)
        {
            if (__result.Accepted && pawn != null && AvirBody.IsAvir(pawn))
                __result = "Avir genes cannot be extracted.";
        }
    }
}
