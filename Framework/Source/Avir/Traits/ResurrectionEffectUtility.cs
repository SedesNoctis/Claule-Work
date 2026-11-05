using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir.Traits
{
    /// <summary>
    /// Cross-map handling: if the watched pawn died alone on a map RimWorld might tear down
    /// before the 3-hour wait finishes, Patch_CrowDeath_Effects despawns their corpse immediately
    /// after death and holds a direct reference on the crow's Salvation comp - a despawned Thing
    /// referenced directly like that isn't subject to map cleanup at all. This method respawns it
    /// at the target location before attempting resurrection. Same-map deaths are untouched -
    /// heldCorpse is simply null there, and this behaves exactly as before.
    /// </summary>
    public static class ResurrectionEffectUtility
    {
        public static void PerformResurrection(Pawn pawn, IntVec3 position, Map map, Corpse heldCorpse, HediffDef watchedOverHediff, HediffDef comaHediff, ThoughtDef restoredThought)
        {
            if (pawn == null) return;

            try
            {
                Log.Message("[Avir] PerformResurrection called for " + pawn.LabelShortCap + " - currently dead: " + pawn.Dead
                    + " | heldCorpse: " + (heldCorpse != null ? "present (spawned=" + heldCorpse.Spawned + ")" : "none"));

                if (!pawn.Dead)
                {
                    Log.Message("[Avir] PerformResurrection: " + pawn.LabelShortCap + " isn't dead - nothing to do, skipping.");
                    return;
                }

                // Cross-map fix: if we're holding a corpse we deliberately despawned to protect
                // it from map cleanup, respawn it at the target location FIRST so
                // ResurrectionUtility.TryResurrect has an actual spawned corpse to work with,
                // same as the already-working same-map case.
                if (heldCorpse != null && !heldCorpse.Destroyed && !heldCorpse.Spawned && map != null && position.IsValid)
                {
                    IntVec3 respawnCell = CellFinder.RandomClosewalkCellNear(position, map, 3);
                    if (!respawnCell.IsValid) respawnCell = position;
                    GenSpawn.Spawn(heldCorpse, respawnCell, map);
                    Log.Message("[Avir] PerformResurrection: respawned held corpse for " + pawn.LabelShortCap + " at " + respawnCell);
                }

                ResurrectionUtility.TryResurrect(pawn);

                if (pawn.Dead)
                {
                    Log.Warning("[Avir] Resurrection failed for " + pawn.LabelShortCap
                        + " - ResurrectionUtility.TryResurrect did not bring them back (corpse may be missing, destroyed, buried, or too decayed).");
                    return;
                }

                Log.Message("[Avir] PerformResurrection: " + pawn.LabelShortCap + " is alive again, proceeding with teleport/heal/strip.");

                TeleportPawn(pawn, position, map);
                RestoreBodyToNatural(pawn);

                pawn.apparel?.DestroyAll();

                if (watchedOverHediff != null && pawn.health?.hediffSet != null)
                {
                    Hediff wo = pawn.health.hediffSet.GetFirstHediffOfDef(watchedOverHediff);
                    if (wo != null) pawn.health.RemoveHediff(wo);
                }

                if (comaHediff != null && pawn.health != null)
                {
                    Hediff coma = HediffMaker.MakeHediff(comaHediff, pawn);
                    pawn.health.AddHediff(coma);
                }

                if (restoredThought != null)
                {
                    pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(restoredThought);
                }

                Log.Message("[Avir] PerformResurrection: fully completed for " + pawn.LabelShortCap + ".");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] PerformResurrection threw for " + pawn.LabelShortCap + ": " + e);
            }
        }

        private static void TeleportPawn(Pawn pawn, IntVec3 position, Map map)
        {
            if (map == null || !position.IsValid) return;

            try
            {
                if (pawn.Spawned)
                {
                    if (pawn.Map == map)
                    {
                        TrySpawnSkipEffect(pawn.Position, map);
                    }
                    pawn.DeSpawn(DestroyMode.Vanish);
                }

                IntVec3 spawnCell = CellFinder.RandomClosewalkCellNear(position, map, 3);
                if (!spawnCell.IsValid) spawnCell = position;

                GenSpawn.Spawn(pawn, spawnCell, map);
                TrySpawnSkipEffect(spawnCell, map);
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Teleport during resurrection failed: " + e);
            }
        }

        // Best-effort cosmetic only - wrapped so a bad guess here can never break the actual
        // resurrection. Tries Royalty's real Skip flecks first, falls back to a generic vanilla
        // spark effect that's virtually guaranteed to exist either way.
        private static void TrySpawnSkipEffect(IntVec3 cell, Map map)
        {
            try
            {
                string[] candidates = { "PsycastSkipFlashExit", "PsycastSkipOuterRingExit", "PsycastSkipInnerExit", "SkipFlashExit" };
                foreach (string name in candidates)
                {
                    FleckDef def = DefDatabase<FleckDef>.GetNamedSilentFail(name);
                    if (def != null)
                    {
                        FleckMaker.Static(cell, map, def, 1f);
                        return;
                    }
                }

                FleckMaker.ThrowMicroSparks(cell.ToVector3Shifted(), map);
            }
            catch
            {
                // Cosmetic only - silently skip if even the fallback doesn't match this version's API.
            }
        }

        private static void RestoreBodyToNatural(Pawn pawn)
        {
            if (pawn.health?.hediffSet == null) return;

            List<Hediff> toRemove = new List<Hediff>();
            foreach (Hediff h in pawn.health.hediffSet.hediffs)
            {
                if (h is Hediff_Injury || h is Hediff_MissingPart || h is Hediff_AddedPart)
                {
                    toRemove.Add(h);
                }
            }

            foreach (Hediff h in toRemove)
            {
                try { pawn.health.RemoveHediff(h); }
                catch (Exception e) { Log.Warning("[Avir] Failed removing hediff " + h.def.defName + " during body restoration: " + e); }
            }
        }
    }
}
