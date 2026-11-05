using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    public class HediffCompProperties_AvirClawTier : HediffCompProperties
    {
        /// <summary>Ticks between tier checks. Tier changes are rare - severity crawls up over
        /// hundreds of in-game hours - so this can be lazy without anyone noticing.</summary>
        public int checkInterval = 2000;

        public HediffCompProperties_AvirClawTier()
        {
            compClass = typeof(HediffComp_AvirClawTier);
        }
    }

    /// <summary>
    /// Keeps the claw and talon hediffs in step with how far conditioning has progressed.
    ///
    /// WHY THIS IS A SEPARATE COMP. HediffComp_ClawConditioning already owns severity: it counts
    /// work ticks, weights melee higher than farming, rolls a spawn floor and regrows claws that
    /// were cut off. All this comp does is read the severity that comp publishes and make sure
    /// the right tier is on the body. Splitting it this way means the growth logic is never
    /// touched - the two comps share nothing but parent.Severity, which is already public state.
    ///
    /// WHY TIERS AT ALL. Damage scales fine from a pawn stat (MeleeDamageFactor), but armour
    /// penetration does not: under CE the penetration figure lives on the tool and the only
    /// pawn-facing multiplier, MeleePenetrationFactor, is categorised as a WEAPON stat. So the
    /// only way to make penetration improve with practice is to swap the tool itself - which
    /// means swapping the hediff that carries it. The beak has done exactly this since it was
    /// written (Avir_EssentiumBeak_1/2/3); the claws simply never got the same treatment, which
    /// is why they arrived at ultratech penetration on day one and never moved.
    ///
    /// TIER 1 KEEPS THE ORIGINAL DEFNAMES. Avir_EssentiumClaws and Avir_EssentiumTalons stay as
    /// the first rung rather than being renamed to _1. The gene applies them through VEF's
    /// hediffsToBodyParts, and implant exemptions, strip filters and the regrowth routine all
    /// name them; renaming would scatter breakage across files that have nothing to do with this
    /// change. The ladder is therefore Claws, Claws_2 .. Claws_5.
    /// </summary>
    public class HediffComp_AvirClawTier : HediffComp
    {
        public HediffCompProperties_AvirClawTier Props => (HediffCompProperties_AvirClawTier)props;

        private int lastAppliedTier = -1;
        private bool lastAppliedSubrace;

        private static List<HediffDef> clawLadder;
        private static List<HediffDef> talonLadder;
        private static List<HediffDef> subraceClawLadder;
        private static List<HediffDef> subraceTalonLadder;
        private static bool laddersResolved;

        /// <summary>
        /// Boundaries follow the conditioning stages declared on the hediff (0.25 / 0.40 / 0.55 /
        /// 0.70 / 0.85), so the weapon tier and the label the player reads always change together.
        /// </summary>
        private static int TierFor(float severity)
        {
            if (severity < 0.40f) return 1;
            if (severity < 0.55f) return 2;
            if (severity < 0.70f) return 3;
            if (severity < 0.85f) return 4;
            return 5;
        }

        private static void ResolveLadders()
        {
            if (laddersResolved) return;
            laddersResolved = true;

            clawLadder = new List<HediffDef>();
            talonLadder = new List<HediffDef>();

            string[] clawNames =
            {
                "Avir_EssentiumClaws", "Avir_EssentiumClaws_2", "Avir_EssentiumClaws_3",
                "Avir_EssentiumClaws_4", "Avir_EssentiumClaws_5"
            };
            string[] talonNames =
            {
                "Avir_EssentiumTalons", "Avir_EssentiumTalons_2", "Avir_EssentiumTalons_3",
                "Avir_EssentiumTalons_4", "Avir_EssentiumTalons_5"
            };

            for (int i = 0; i < clawNames.Length; i++)
            {
                HediffDef c = DefDatabase<HediffDef>.GetNamedSilentFail(clawNames[i]);
                HediffDef t = DefDatabase<HediffDef>.GetNamedSilentFail(talonNames[i]);
                if (c == null || t == null)
                {
                    Log.Warning("[Avir] Claw tier ladder incomplete at rung " + (i + 1)
                                + " - claw tiering disabled.");
                    clawLadder = null;
                    talonLadder = null;
                    return;
                }
                clawLadder.Add(c);
                talonLadder.Add(t);
            }

            // A'Shacsaign run a parallel ladder at x1.3. If it is missing - someone stripped the
            // subrace files - they silently fall back to the common ladder rather than losing
            // their claws entirely.
            subraceClawLadder = new List<HediffDef>();
            subraceTalonLadder = new List<HediffDef>();
            for (int i = 0; i < 5; i++)
            {
                HediffDef c = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_AShacsaignClaws_" + (i + 1));
                HediffDef t = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_AShacsaignTalons_" + (i + 1));
                if (c == null || t == null)
                {
                    subraceClawLadder = null;
                    subraceTalonLadder = null;
                    Log.Warning("[Avir] A'Shacsaign claw ladder incomplete - subrace falls back to standard claws.");
                    break;
                }
                subraceClawLadder.Add(c);
                subraceTalonLadder.Add(t);
            }
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = parent.pawn;
            if (pawn == null || !pawn.Spawned) return;
            if (!pawn.IsHashIntervalTick(Props.checkInterval)) return;

            SyncTiers(pawn);
        }

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            lastAppliedTier = -1;
            if (parent.pawn != null) SyncTiers(parent.pawn);
        }

        private void SyncTiers(Pawn pawn)
        {
            ResolveLadders();
            if (clawLadder == null) return;

            bool subrace = Patch_AvirAShacsaignSpawn.IsSubrace(pawn) && subraceClawLadder != null;

            // Belt and braces: echolocation must never coexist with the subrace, and this runs on
            // a lazy interval, so a late re-grant by the gene cannot survive.
            if (subrace) Patch_AvirAShacsaignSpawn.StripEcholocation(pawn);

            int tier = TierFor(parent.Severity);
            if (tier == lastAppliedTier && subrace == lastAppliedSubrace) return;

            // Every rung of BOTH ladders counts as "ours" when searching, so a pawn who becomes
            // A'Shacsaign after the fact is migrated across rather than ending up with two sets.
            List<HediffDef> clawSearch = new List<HediffDef>(clawLadder);
            List<HediffDef> talonSearch = new List<HediffDef>(talonLadder);
            if (subraceClawLadder != null)
            {
                clawSearch.AddRange(subraceClawLadder);
                talonSearch.AddRange(subraceTalonLadder);
            }

            HediffDef wantedClaw = (subrace ? subraceClawLadder : clawLadder)[tier - 1];
            HediffDef wantedTalon = (subrace ? subraceTalonLadder : talonLadder)[tier - 1];

            // EssentiumClawDefOf, not BodyPartDefOf: vanilla's BodyPartDefOf declares Hand but not
            // Foot, and the mod already keeps both here for RegrowIfNatural.
            SwapOnParts(pawn, EssentiumClawDefOf.Hand, clawSearch, wantedClaw);
            SwapOnParts(pawn, EssentiumClawDefOf.Foot, talonSearch, wantedTalon);
            // the Avir body: claws and talons are parts of their own (AvirBody.cs)
            if (AvirBody.Claw != null) SwapOnParts(pawn, AvirBody.Claw, clawSearch, wantedClaw);
            if (AvirBody.Talon != null) SwapOnParts(pawn, AvirBody.Talon, talonSearch, wantedTalon);

            lastAppliedTier = tier;
            lastAppliedSubrace = subrace;
        }

        /// <summary>
        /// For every matching body part that currently carries some rung of the ladder, replace it
        /// with the wanted rung. Parts carrying nothing from the ladder are left alone - a pawn
        /// whose claws were cut off stays clawless here, because regrowth is
        /// HediffComp_ClawConditioning's job and duplicating it would race against it.
        /// </summary>
        private static void SwapOnParts(Pawn pawn, BodyPartDef partDef, List<HediffDef> ladder, HediffDef wanted)
        {
            List<BodyPartRecord> parts = pawn.health.hediffSet.GetNotMissingParts() as List<BodyPartRecord>
                                         ?? new List<BodyPartRecord>(pawn.health.hediffSet.GetNotMissingParts());

            for (int p = 0; p < parts.Count; p++)
            {
                BodyPartRecord part = parts[p];
                if (part.def != partDef) continue;

                Hediff present = null;
                for (int i = 0; i < ladder.Count; i++)
                {
                    Hediff found = pawn.health.hediffSet.hediffs.Find(h => h.def == ladder[i] && h.Part == part);
                    if (found != null) { present = found; break; }
                }

                if (present == null) continue;          // nothing of ours on this part
                if (present.def == wanted) continue;    // already correct

                pawn.health.RemoveHediff(present);
                pawn.health.AddHediff(wanted, part);
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref lastAppliedTier, "avirClawTier", -1);
            Scribe_Values.Look(ref lastAppliedSubrace, "avirClawSubrace", false);
        }

        public override string CompDebugString()
        {
            return "claw tier: " + TierFor(parent.Severity) + " (applied " + lastAppliedTier + ")";
        }
    }
}
