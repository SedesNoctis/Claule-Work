using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Keeps a Prideful pawn's per-garment mood memories (Avir_PridefulApparelQuality) and their
    /// "wearing nothing" memory (Avir_PridefulNudity) in sync with what's actually worn.
    /// Updated in real time via the equip/unequip patches below; slow-developing state (a
    /// garment decaying into "tattered") is caught by GameComponent_PridefulApparelSync's
    /// periodic pass instead. Picked up automatically by AvirHarmony's PatchAll() - no other
    /// wiring needed.
    /// </summary>
    public static class PridefulApparelSync
    {
        /// <summary>
        /// NO LONGER GRANTS ANYTHING - it only cleans up, exactly like SyncNudity below.
        ///
        /// Apparel quality is now Avir_PridefulApparel, a single situational thought over the whole
        /// outfit (ThoughtWorker_AvirApparelQuality). The per-garment memories this used to create
        /// would otherwise sit in existing saves forever, paying a second time for a state the
        /// situational thought already covers.
        ///
        /// The apparel argument is ignored: every stale memory goes, whichever garment produced it.
        /// </summary>
        public static void SyncOne(Pawn pawn, Apparel apparel)
        {
            if (pawn?.needs?.mood == null || AvirDefOf.Avir_PridefulApparelQuality == null) return;

            MemoryThoughtHandler memories = pawn.needs.mood.thoughts.memories;
            List<Thought_Memory> memList = memories.Memories;

            for (int i = memList.Count - 1; i >= 0; i--)
            {
                if (memList[i].def != AvirDefOf.Avir_PridefulApparelQuality) continue;
                memories.RemoveMemory(memList[i]);
            }
        }

        public static void SyncNudity(Pawn pawn)
        {
            if (pawn?.needs?.mood == null || AvirDefOf.Avir_PridefulNudity == null) return;

            MemoryThoughtHandler memories = pawn.needs.mood.thoughts.memories;
            List<Thought_Memory> memList = memories.Memories;

            for (int i = memList.Count - 1; i >= 0; i--)
            {
                if (memList[i].def != AvirDefOf.Avir_PridefulNudity) continue;

                memories.RemoveMemory(memList[i]);
                Report(pawn, "cleared the old nudity memory; the bonus is situational now");
            }
        }

        private static readonly HashSet<int> reported = new HashSet<int>();

        /// <summary>
        /// ONE LINE PER PAWN PER SESSION, and it exists because guessing failed twice.
        ///
        /// A prideful Avir with every slot empty and no +10 has several possible explanations that
        /// look identical from outside the game: the equip/unequip hooks never fired because the
        /// pawn was stripped some other way, IsPrideful is somehow false for them, or the memory is
        /// being granted and then discarded by something else. Reading the code cannot distinguish
        /// them; one log line can.
        ///
        /// Prints what was actually seen at the moment of the check - worn count, the verdict, and
        /// what was done about it - so the answer arrives without anyone having to sit and watch.
        /// </summary>
        private static void Report(Pawn pawn, string action)
        {
            if (pawn == null || !reported.Add(pawn.thingIDNumber)) return;

            Log.Message("[Avir nudity] " + pawn.LabelShortCap + ": " + action + ".");
        }

    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelAdded))]
    public static class Patch_Prideful_ApparelAdded
    {
        public static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            Pawn pawn = __instance.pawn;
            if (!PridefulApparelUtility.IsPrideful(pawn))
            {
                return;
            }
            PridefulApparelSync.SyncOne(pawn, apparel);
            PridefulApparelSync.SyncNudity(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelRemoved))]
    public static class Patch_Prideful_ApparelRemoved
    {
        public static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            Pawn pawn = __instance.pawn;
            if (pawn?.needs?.mood == null)
            {
                return;
            }
            // SyncOne rather than the old RemoveOne. Both were per-garment; there is no longer any
            // such thing, so both hooks do the same job - clear whatever stale per-item memories
            // this pawn still carries. RemoveOne died with the per-garment design.
            PridefulApparelSync.SyncOne(pawn, apparel);
            if (PridefulApparelUtility.IsPrideful(pawn))
            {
                PridefulApparelSync.SyncNudity(pawn);
            }
        }
    }

}
