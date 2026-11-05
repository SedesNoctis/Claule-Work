using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Counts a beak strike per melee swing for pawns that have the essentium-beak conditioning.
    /// CE routes melee damage differently from vanilla, so we hook every likely per-swing method
    /// (vanilla + CE candidates). The comp de-duplicates by game tick, so even if several of these
    /// fire for one swing it only counts once. Dev-mode logging shows which path actually fires.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BeakPatches
    {
        static BeakPatches()
        {
            Harmony h = new Harmony("yourname.avir.beak");
            HarmonyMethod post = new HarmonyMethod(typeof(BeakPatches), nameof(Post));

            // vanilla
            TryPatch(h, AccessTools.Method(typeof(Verb_MeleeAttackDamage), "ApplyMeleeDamageToTarget"), post, "vanilla.ApplyMeleeDamageToTarget");
            // CE candidates
            TryPatch(h, AccessTools.Method("CombatExtended.Verb_MeleeAttackCE:ApplyMeleeDamageToTarget"), post, "CE.ApplyMeleeDamageToTarget");
            TryPatch(h, AccessTools.Method("CombatExtended.Verb_MeleeAttackCE:DamageInfosToApply"), post, "CE.DamageInfosToApply");
            TryPatch(h, AccessTools.Method("CombatExtended.Verb_MeleeAttackCE:TryCastShot"), post, "CE.TryCastShot");
        }

        private static void TryPatch(Harmony h, MethodBase target, HarmonyMethod post, string label)
        {
            if (target == null)
            {
                Log.Message($"[AvirBeak] (skip - not found) {label}");
                return;
            }
            try { h.Patch(target, postfix: post); Log.Message($"[AvirBeak] patched {label}"); }
            catch (Exception e) { Log.Warning($"[AvirBeak] FAILED {label}: {e.Message}"); }
        }

        // Works for any of the hooked methods (only needs the verb instance).
        public static void Post(Verb __instance)
        {
            if (__instance == null) return;
            if (__instance.EquipmentSource != null) return; // ignore held-weapon attacks

            Pawn caster = __instance.CasterPawn;
            if (caster == null || caster.health == null) return;

            Hediff cond = caster.health.hediffSet.GetFirstHediffOfDef(EssentiumBeakDefOf.Avir_EssentiumBeakConditioning);
            if (cond == null) return;

            if (Prefs.DevMode)
            {
                Log.Message($"[AvirBeak] strike registered for {caster.LabelShort}");
            }
            cond.TryGetComp<HediffComp_BeakConditioning>()?.RegisterStrike();
        }
    }
}
