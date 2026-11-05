using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir.Traits
{
    /// <summary>
    /// Shared lookups and the reconcile that both directions funnel through.
    /// </summary>
    public static class ArchstructrorSync
    {
        public const string TraitName = "Avir_Archstructror";
        public const string HediffName = "Avir_MasterConstructor";

        private static TraitDef traitDef;
        private static HediffDef hediffDef;
        private static bool lookedUp;

        public static void EnsureLookedUp()
        {
            if (lookedUp) return;
            lookedUp = true;
            traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(TraitName);
            hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(HediffName);
        }

        public static TraitDef Trait { get { EnsureLookedUp(); return traitDef; } }
        public static HediffDef Hediff { get { EnsureLookedUp(); return hediffDef; } }

        /// <summary>
        /// Makes the hediff match the trait, in whichever direction is wrong.
        ///
        /// THE BUG THIS FIXES. The grant was one-way: a postfix on GainTrait added the hediff,
        /// and nothing anywhere removed it. Meanwhile Patch_Archstructror_PostProcess strips the
        /// trait off any pawn whose kindDef is not Avir_Architect, which happens constantly -
        /// the shared Avir trait pool has no concept of "restricted to one kind", so the roll
        /// lands on ordinary Avir all the time and then gets cleaned up.
        ///
        /// The order was: pool grants trait -> GainTrait fires -> hediff added -> post-process
        /// sees a non-Architect -> trait removed -> hediff stays. An orphan. Exactly what the
        /// screenshot shows: a Free Nesting pawn with no Archstructror trait and a
        /// master-constructor hediff on the whole body.
        /// </summary>
        public static void Reconcile(Pawn pawn)
        {
            EnsureLookedUp();
            if (traitDef == null || hediffDef == null) return;
            if (pawn == null || pawn.story == null || pawn.story.traits == null) return;
            if (pawn.health == null || pawn.health.hediffSet == null) return;

            // the trait and the Archconstruct path of the control spheres share one set of hediffs and abilities
            Avir.Artifactorics.SpherePassives.Sync(pawn, Avir.Artifactorics.SpherePaths.Of(pawn));
        }
    }

    /// <summary>
    /// Trait gained -> hediff granted. Unchanged in behaviour; it now routes through Reconcile
    /// so there is one place that decides what "correct" means.
    /// </summary>
    [HarmonyPatch(typeof(TraitSet), nameof(TraitSet.GainTrait))]
    public static class Patch_ArchstructrorConditions
    {
        public static void Postfix(Trait trait, Pawn ___pawn)
        {
            try
            {
                if (trait == null || trait.def == null || ___pawn == null) return;
                if (trait.def.defName != ArchstructrorSync.TraitName) return;

                // A postfix runs even when some other prefix vetoed the roll, so the trait being
                // passed in is not proof it was actually added. Reconcile re-reads the truth.
                ArchstructrorSync.Reconcile(___pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Archstructror grant failed: " + e, 0x4A51D3);
            }
        }
    }

    /// <summary>
    /// THE HALF THAT WAS MISSING. Trait lost -> hediff removed.
    ///
    /// The hediff def's own header comment claimed it was "removed the instant it's lost", and
    /// nothing had ever implemented that. This is what makes the claim true.
    /// </summary>
    [HarmonyPatch(typeof(TraitSet), nameof(TraitSet.RemoveTrait))]
    public static class Patch_ArchstructrorConditions_Remove
    {
        public static void Postfix(Trait trait, Pawn ___pawn)
        {
            try
            {
                if (trait == null || trait.def == null || ___pawn == null) return;
                if (trait.def.defName != ArchstructrorSync.TraitName) return;

                ArchstructrorSync.Reconcile(___pawn);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Archstructror revoke failed: " + e, 0x4A51D4);
            }
        }
    }

    /// <summary>
    /// Belt and braces at the end of generation, at Priority.Last so it runs after
    /// Patch_Archstructror_PostProcess has finished deciding whether this pawn keeps the trait.
    ///
    /// The RemoveTrait patch above already handles this deterministically, so in a clean run
    /// this finds nothing to do. It is here for the paths that bypass TraitSet entirely - a mod
    /// clearing allTraits directly, a scenario part, dev tools.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", typeof(PawnGenerationRequest))]
    public static class Patch_ArchstructrorConditions_Reconcile
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn __result)
        {
            try { ArchstructrorSync.Reconcile(__result); }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Archstructror reconcile failed: " + e, 0x4A51D5);
            }
        }
    }

    /// <summary>
    /// Sweeps every pawn once when a save loads, so existing colonies with orphaned hediffs are
    /// corrected without having to regenerate anyone.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class Patch_ArchstructrorConditions_LoadSweep
    {
        public static void Postfix()
        {
            try
            {
                if (ArchstructrorSync.Trait == null || ArchstructrorSync.Hediff == null) return;

                int fixedUp = 0;
                foreach (Pawn p in EveryPawn())
                {
                    if (p == null || p.health == null || p.health.hediffSet == null) continue;

                    bool had = p.health.hediffSet.HasHediff(ArchstructrorSync.Hediff);
                    ArchstructrorSync.Reconcile(p);
                    if (had != p.health.hediffSet.HasHediff(ArchstructrorSync.Hediff)) fixedUp++;
                }

                if (fixedUp > 0)
                    Log.Message("[Avir] Archstructror: corrected " + fixedUp
                        + " pawn(s) whose master-constructor hediff did not match their trait.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Archstructror load sweep failed: " + e);
            }
        }

        /// <summary>Walked by hand rather than through PawnsFinder, whose member names have
        /// already broken this mod once.</summary>
        private static List<Pawn> EveryPawn()
        {
            List<Pawn> result = new List<Pawn>();

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i].mapPawns == null) continue;
                List<Pawn> ps = maps[i].mapPawns.AllPawns;
                for (int j = 0; j < ps.Count; j++) if (ps[j] != null) result.Add(ps[j]);
            }

            if (Find.WorldPawns != null)
            {
                foreach (Pawn p in Find.WorldPawns.AllPawnsAliveOrDead)
                {
                    if (p != null) result.Add(p);
                }
            }

            return result;
        }
    }

}
