using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Hooks Pawn_ApparelTracker.Wear directly (same choke point every apparel-equip call goes
    /// through - manual dress-up, pawn generation via Patch_AvirAmuletAssignment, auto-dress,
    /// everything) rather than the Notify_ApparelAdded pattern used elsewhere in this mod: that
    /// hook fires AFTER the apparel is already worn, too late to refuse it cleanly. A Prefix here
    /// can block the wear from ever happening at all.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Wear))]
    public static class Patch_AvirAdaptedTo
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn_ApparelTracker __instance, Apparel newApparel, out bool __state)
        {
            __state = false; // true = we blocked it, so the postfix must not then bind it

            Comp_AdaptedTo comp = newApparel?.TryGetComp<Comp_AdaptedTo>();
            if (comp == null || comp.adaptedPawn == null)
            {
                return true; // not an adaptable item, or not yet bound to anyone - proceed normally
            }

            Pawn wouldBeWearer = __instance.pawn;
            if (comp.adaptedPawn == wouldBeWearer)
            {
                return true; // already theirs - fine
            }

            __state = true;

            if (wouldBeWearer != null)
            {
                Messages.Message(
                    "Avir_AdaptedToRefusal".Translate(newApparel.Label, comp.adaptedPawn.Named("OWNER"), wouldBeWearer.Named("PAWN")),
                    wouldBeWearer,
                    MessageTypeDefOf.RejectInput,
                    false);
            }

            return false; // skip the actual Wear call entirely - the item is never equipped
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn_ApparelTracker __instance, Apparel newApparel, bool __state)
        {
            if (__state)
            {
                return; // the prefix blocked this wear - do not bind an item that was just refused
            }

            Comp_AdaptedTo comp = newApparel?.TryGetComp<Comp_AdaptedTo>();
            if (comp == null || comp.adaptedPawn != null)
            {
                return;
            }

            comp.adaptedPawn = __instance.pawn;
        }
    }
}
