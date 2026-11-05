using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    // WorkGiver_DoBill is vanilla core - always present, so this is a normal (not soft/reflection)
    // patch, picked up automatically by AvirHarmony's harmony.PatchAll().
    //
    // WHY THIS EXISTS: our separate Avir_TelekineticHaul job only ever runs if the pawn is
    // actually assigned Hauling-type work. WorkGiver_DoBill doesn't wait for that - the instant a
    // pawn is free for Crafting work, it can already see an ingredient anywhere on the map and
    // hand back a job that walks there and carries it back itself. For a dedicated crafter (often
    // with Hauling low-priority or off), that Crafting job wins every time and our Hauling job
    // never gets a turn - the amulet-wearer just walks like nobody was wearing anything.
    //
    // Fix: don't try to out-schedule DoBill. Instead, the instant DoBill hands back a job, check
    // whether any of its queued ingredients are within the pawn's telekinesis radius, and if so,
    // relocate them to right next to the bench before the pawn takes a single step. The pawn's
    // own (still vanilla) fetch toils then have little or nothing left to walk for.
    [HarmonyPatch(typeof(WorkGiver_DoBill), "JobOnThing")]
    public static class Patch_AvirDoBillIngredientTeleport
    {
        // Anything already closer than this to the bench isn't worth teleporting - avoids
        // pointless micro-relocations for ingredients that were basically already there.
        private const float SkipIfAlreadyWithinDistSq = 4f; // ~2 tiles

        [HarmonyPostfix]
        public static void Postfix(Pawn pawn, bool forced, Job __result)
        {
            if (__result == null || pawn == null || pawn.Map == null)
            {
                return;
            }

            if (__result.def != JobDefOf.DoBill)
            {
                return;
            }

            if (!AvirTelekinesisUtility.TryGetRadius(pawn, out float radius))
            {
                return;
            }

            List<LocalTargetInfo> ingredientQueue = __result.targetQueueB;
            if (ingredientQueue == null || ingredientQueue.Count == 0)
            {
                return;
            }

            Thing bench = __result.targetA.Thing;
            IntVec3 origin = bench != null ? bench.Position : pawn.Position;
            float radiusSq = radius * radius;
            int ownerKey = pawn.thingIDNumber;

            MapComponent_AvirTelekinesisCache cache = pawn.Map.GetComponent<MapComponent_AvirTelekinesisCache>();
            if (cache == null || bench == null)
            {
                return;
            }

            List<int> countQueue = __result.countQueue;

            for (int i = 0; i < ingredientQueue.Count; i++)
            {
                Thing ingredient = ingredientQueue[i].Thing;
                if (ingredient == null || !ingredient.Spawned || ingredient.Map != pawn.Map)
                {
                    continue;
                }

                if (AvirTelekinesisClaims.IsClaimedByOther(ingredient, ownerKey))
                {
                    continue;
                }

                // In flight already - see MapComponent_AvirTelekinesisCache.IsAwaitingTeleport.
                if (MapComponent_AvirTelekinesisCache.IsAwaitingTeleport(ingredient))
                {
                    continue;
                }

                float distSq = (ingredient.Position - origin).LengthHorizontalSquared;
                if (distSq <= SkipIfAlreadyWithinDistSq || distSq > radiusSq)
                {
                    continue;
                }

                // countQueue[i] is the exact amount DoBill itself intends to take from this
                // stack - move only that much, not the whole pile, and leave the remainder where
                // it was for whoever/whatever needs it next.
                int neededCount = (countQueue != null && i < countQueue.Count && countQueue[i] > 0)
                    ? countQueue[i]
                    : -1;

                // Never touch the world synchronously here (see MapComponent_AvirTelekinesisCache
                // for why) - queue it for next tick instead.
                cache.EnqueueTeleportNearBench(ingredient, bench, neededCount);
            }
        }
    }
}
