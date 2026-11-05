using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// Fearborn pawns (the predator mindset was merged into Fearborn) never flee in panic - the hunt is always closer to
    /// the surface than fear is. This blocks the "PanicFlee" mental state directly at the source
    /// (MentalStateHandler.TryStartMentalState), so it doesn't matter whether vanilla or Combat
    /// Extended's suppression/pain system is the one trying to trigger it - the break is simply
    /// never allowed to start for these pawns.
    ///
    /// PanicFlee is looked up by defName ("PanicFlee") rather than via a DefOf, since it's a
    /// vanilla-core def that should always exist, but this keeps the mod from erroring out if it
    /// is ever renamed or removed upstream - it just silently stops blocking anything.
    /// </summary>
    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class Patch_PredatorMindset_NoPanicFlee
    {
        private static MentalStateDef panicFleeDef;
        private static bool lookedUp;

        public static bool Prefix(MentalStateHandler __instance, MentalStateDef stateDef, ref bool __result)
        {
            if (stateDef == null)
            {
                return true;
            }

            if (!lookedUp)
            {
                panicFleeDef = DefDatabase<MentalStateDef>.GetNamedSilentFail("PanicFlee");
                lookedUp = true;
            }

            if (panicFleeDef == null || stateDef != panicFleeDef)
            {
                return true; // Not PanicFlee (or it's missing entirely) - nothing for us to do.
            }

            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn == null || pawn.genes == null || !pawn.genes.HasActiveGene(AvirDefOf.Fearborn))
            {
                return true; // Doesn't have the gene - let vanilla/CE handle it as normal.
            }

            // Block the mental state outright.
            __result = false;
            return false;
        }
    }
}
