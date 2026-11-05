using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Traits
{
    /// <summary>
    /// Split prefix/postfix:
    ///   - Salvation death and normal crow death both resurrect/affect a DIFFERENT pawn than the
    ///     one currently dying, so reading their hediffs/position before Kill() runs (prefix) is
    ///     both safe and preferable, same reasoning as the Cinder Volley CE patch.
    ///   - The Crow Watcher's own self-resurrection needs Dead == true first, which is only true
    ///     AFTER Kill() actually executes - so that specific case is a postfix.
    ///   - Corpse-preservation (cross-map fix) ALSO has to be a postfix: a pawn's Corpse doesn't
    ///     exist until after Kill() runs, so we can't grab it in the prefix even though the
    ///     Salvation-transformation logic that triggers it does run there.
    /// Data captured in the prefix is passed through via Harmony's standard __state mechanism.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), "Kill")]
    public static class Patch_CrowDeath_Effects
    {
        private class WatcherDeathState
        {
            public List<Pawn> crowsSnapshot;
            public IntVec3 position;
            public Map map;
        }

        private class PatchState
        {
            public WatcherDeathState watcherState;
            public Pawn corpseOwnerForSalvation;
        }

        public static void Prefix(Pawn __instance, out object __state)
        {
            __state = null;

            try
            {
                if (__instance == null || __instance.Dead) return;

                PatchState state = new PatchState();

                bool transformed = HandleWatchedPawnDeath(__instance);
                if (transformed) state.corpseOwnerForSalvation = __instance;

                HandleSalvationDeath(__instance);
                HandleNormalCrowDeath(__instance);

                state.watcherState = CaptureWatcherDeathState(__instance);

                if (state.watcherState != null || state.corpseOwnerForSalvation != null) __state = state;
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Patch_CrowDeath_Effects prefix failed: " + e);
            }
        }

        public static void Postfix(Pawn __instance, object __state)
        {
            try
            {
                if (!(__state is PatchState state)) return;

                if (state.corpseOwnerForSalvation != null)
                {
                    CaptureAndHoldCorpseIfAtRisk(state.corpseOwnerForSalvation);
                }

                if (state.watcherState != null)
                {
                    foreach (Pawn crow in state.watcherState.crowsSnapshot)
                    {
                        if (crow != null && !crow.Dead)
                        {
                            crow.Kill(null, null); // re-enters this same patch for each crow individually
                        }
                    }

                    ResurrectionEffectUtility.PerformResurrection(
                        __instance, state.watcherState.position, state.watcherState.map, null,
                        DefDatabase<HediffDef>.GetNamedSilentFail("Avir_WatchedOver"),
                        DefDatabase<HediffDef>.GetNamedSilentFail("Avir_ResurrectionComa"),
                        DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_RestoredToLife"));
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Patch_CrowDeath_Effects postfix failed: " + e);
            }
        }

        // THE ACTUAL TRIGGER: a watched-over pawn dies -> their crow's WatchingOver becomes
        // Salvation. Returns true only on success, so the postfix knows whether to also protect
        // this pawn's corpse from map cleanup.
        private static bool HandleWatchedPawnDeath(Pawn pawn)
        {
            HediffDef watchedOverDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_WatchedOver");
            if (watchedOverDef == null)
            {
                Log.Warning("[Avir] HandleWatchedPawnDeath: Avir_WatchedOver HediffDef not found at all - check the def is loaded.");
                return false;
            }

            Hediff h = pawn.health?.hediffSet?.GetFirstHediffOfDef(watchedOverDef);
            if (h == null) return false; // this pawn just isn't bonded - normal, not an error

            HediffComp_WatchedOver comp = h.TryGetComp<HediffComp_WatchedOver>();
            if (comp == null)
            {
                Log.Warning("[Avir] HandleWatchedPawnDeath: " + pawn.LabelShortCap + " has Avir_WatchedOver but no HediffComp_WatchedOver comp on it - XML/comp mismatch.");
                return false;
            }

            Log.Message("[Avir] HandleWatchedPawnDeath: " + pawn.LabelShortCap + " died with Avir_WatchedOver. crow="
                + (comp.crow != null ? comp.crow.LabelShortCap + " (dead=" + comp.crow.Dead + ")" : "NULL"));

            if (comp.crow == null || comp.crow.Dead) return false;

            Pawn crow = comp.crow;
            Pawn crowWatcher = comp.crowWatcher;

            HediffDef watchingOverDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_WatchingOver");
            HediffDef salvationDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Salvation");
            if (watchingOverDef == null || salvationDef == null)
            {
                Log.Warning("[Avir] HandleWatchedPawnDeath: Avir_WatchingOver or Avir_Salvation HediffDef not found - can't transform the crow's hediff.");
                return false;
            }
            if (crow.health?.hediffSet == null) return false;

            Hediff watchingHediff = crow.health.hediffSet.GetFirstHediffOfDef(watchingOverDef);
            if (watchingHediff != null) crow.health.RemoveHediff(watchingHediff);

            Hediff salvationHediff = HediffMaker.MakeHediff(salvationDef, crow);

            HediffComp_Salvation salvationComp = salvationHediff.TryGetComp<HediffComp_Salvation>();
            if (salvationComp == null)
            {
                Log.Warning("[Avir] HandleWatchedPawnDeath: Avir_Salvation has no HediffComp_Salvation on it - XML/comp mismatch, countdown will never fire.");
                return false;
            }

            salvationComp.Initialize(pawn, crowWatcher);
            crow.health.AddHediff(salvationHediff);
            Log.Message("[Avir] HandleWatchedPawnDeath: " + crow.LabelShortCap + " transformed to Salvation, watching for " + pawn.LabelShortCap + ".");
            return true;
        }

        // Cross-map fix: if the pawn who just died has no colonists left on their map, that map
        // is a candidate for RimWorld's own cleanup, which would silently destroy the corpse
        // before the 3-hour wait finishes. Despawning it and holding a direct reference makes it
        // immune to that - it just sits as a plain saved object until resurrection respawns it.
        // Same-map deaths with colonists present are untouched entirely.
        private static void CaptureAndHoldCorpseIfAtRisk(Pawn pawn)
        {
            Corpse corpse = pawn.Corpse;
            if (corpse == null)
            {
                Log.Warning("[Avir] CaptureAndHoldCorpseIfAtRisk: " + pawn.LabelShortCap + " has no Corpse right after Kill() - can't protect it if the map gets cleaned up.");
                return;
            }

            Map map = corpse.Map;
            if (map == null)
            {
                Log.Message("[Avir] CaptureAndHoldCorpseIfAtRisk: corpse for " + pawn.LabelShortCap + " has no Map already - nothing to do.");
                return;
            }

            bool anyColonistsHere = map.mapPawns.FreeColonistsSpawnedCount > 0 || map.mapPawns.ColonistsSpawnedCount > 0;
            if (anyColonistsHere)
            {
                Log.Message("[Avir] CaptureAndHoldCorpseIfAtRisk: " + pawn.LabelShortCap + "'s map still has colonists present - not at risk, leaving the corpse alone.");
                return;
            }

            HediffDef watchedOverDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_WatchedOver");
            Hediff h = pawn.health?.hediffSet?.GetFirstHediffOfDef(watchedOverDef);
            HediffComp_WatchedOver watchedComp = h?.TryGetComp<HediffComp_WatchedOver>();
            Pawn crow = watchedComp?.crow;
            if (crow == null)
            {
                Log.Warning("[Avir] CaptureAndHoldCorpseIfAtRisk: couldn't find " + pawn.LabelShortCap + "'s crow to store the held corpse on.");
                return;
            }

            HediffDef salvationDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Salvation");
            Hediff salvationHediff = crow.health?.hediffSet?.GetFirstHediffOfDef(salvationDef);
            HediffComp_Salvation salvationComp = salvationHediff?.TryGetComp<HediffComp_Salvation>();
            if (salvationComp == null)
            {
                Log.Warning("[Avir] CaptureAndHoldCorpseIfAtRisk: crow " + crow.LabelShortCap + " has no active Salvation comp - can't store the held corpse.");
                return;
            }

            if (corpse.Spawned)
            {
                corpse.DeSpawn(DestroyMode.Vanish);
            }
            salvationComp.heldCorpse = corpse;

            Log.Message("[Avir] CaptureAndHoldCorpseIfAtRisk: " + pawn.LabelShortCap + " died alone on an empty map - despawned and holding their corpse until resurrection.");
        }

        private static void HandleSalvationDeath(Pawn crow)
        {
            HediffDef salvationDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Salvation");
            if (salvationDef == null) return;

            Hediff h = crow.health?.hediffSet?.GetFirstHediffOfDef(salvationDef);
            if (h == null) return; // this crow just isn't in Salvation - normal, not an error

            HediffComp_Salvation comp = h.TryGetComp<HediffComp_Salvation>();

            Log.Message("[Avir] HandleSalvationDeath: " + crow.LabelShortCap + " is dying with Avir_Salvation present. comp found: "
                + (comp != null) + " | watchedPawn: " + (comp?.watchedPawn != null ? comp.watchedPawn.LabelShortCap + " (dead=" + comp.watchedPawn.Dead + ")" : "NULL")
                + " | heldCorpse: " + (comp?.heldCorpse != null ? "present" : "none"));

            if (comp == null || comp.watchedPawn == null) return;

            Pawn watchedPawn = comp.watchedPawn;
            Pawn crowWatcher = comp.crowWatcher;
            Corpse heldCorpse = comp.heldCorpse;

            IntVec3 position = crow.PositionHeld;
            Map map = crow.MapHeld;

            Log.Message("[Avir] HandleSalvationDeath: attempting resurrection of " + watchedPawn.LabelShortCap
                + " at " + position + " on map " + (map != null ? map.ToString() : "NULL"));

            ResurrectionEffectUtility.PerformResurrection(
                watchedPawn, position, map, heldCorpse,
                DefDatabase<HediffDef>.GetNamedSilentFail("Avir_WatchedOver"),
                DefDatabase<HediffDef>.GetNamedSilentFail("Avir_ResurrectionComa"),
                DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_RestoredToLife"));

            RemoveFromWatcherBonds(crowWatcher, crow);
        }

        private static void HandleNormalCrowDeath(Pawn crow)
        {
            HediffDef watchingOverDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_WatchingOver");
            if (watchingOverDef == null) return;

            Hediff h = crow.health?.hediffSet?.GetFirstHediffOfDef(watchingOverDef);
            HediffComp_WatchingOver comp = h?.TryGetComp<HediffComp_WatchingOver>();
            if (comp == null) return;

            Pawn watchedPawn = comp.watchedPawn;
            Pawn crowWatcher = comp.crowWatcher;

            HediffDef watchedOverDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_WatchedOver");
            if (watchedPawn != null && watchedOverDef != null && watchedPawn.health?.hediffSet != null)
            {
                Hediff wo = watchedPawn.health.hediffSet.GetFirstHediffOfDef(watchedOverDef);
                if (wo != null) watchedPawn.health.RemoveHediff(wo);
            }

            RemoveFromWatcherBonds(crowWatcher, crow);
        }

        private static WatcherDeathState CaptureWatcherDeathState(Pawn watcher)
        {
            HediffDef bondsDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_CrowWatcherBonds");
            if (bondsDef == null) return null;

            Hediff h = watcher.health?.hediffSet?.GetFirstHediffOfDef(bondsDef);
            HediffComp_CrowWatcherBonds comp = h?.TryGetComp<HediffComp_CrowWatcherBonds>();
            if (comp == null || comp.Count == 0) return null;

            Pawn firstCrow = comp.FirstCrow;
            return new WatcherDeathState
            {
                crowsSnapshot = new List<Pawn>(comp.BondedCrows),
                position = firstCrow != null ? firstCrow.PositionHeld : watcher.PositionHeld,
                map = firstCrow != null ? firstCrow.MapHeld : watcher.MapHeld
            };
        }

        private static void RemoveFromWatcherBonds(Pawn crowWatcher, Pawn crow)
        {
            if (crowWatcher == null) return;

            HediffDef bondsDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_CrowWatcherBonds");
            if (bondsDef == null) return;

            Hediff h = crowWatcher.health?.hediffSet?.GetFirstHediffOfDef(bondsDef);
            HediffComp_CrowWatcherBonds comp = h?.TryGetComp<HediffComp_CrowWatcherBonds>();
            comp?.RemoveCrow(crow);
        }
    }
}
