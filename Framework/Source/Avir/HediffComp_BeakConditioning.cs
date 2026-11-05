using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    public class HediffCompProperties_BeakConditioning : HediffCompProperties
    {
        public float perStrike = 0.001f;   // +severity per confirmed beak hit
        public float startSeverity = 0.1f; // floor / starting conditioning
        // Weighted table rolled once, when the hediff is first created (gene added, including at
        // pawn generation), to decide how honed this individual's beak already is. Give bigger
        // severities smaller weights so being born already "tempered" is rare. Leave empty/omit
        // to always start at startSeverity. Shares AdaptationRandomizer with the claw gene.
        public List<AdaptationSeverityOption> spawnSeverityOptions;

        public HediffCompProperties_BeakConditioning()
        {
            compClass = typeof(HediffComp_BeakConditioning);
        }
    }

    /// <summary>
    /// Lives on the whole-body "essentium beak conditioning" hediff. Counts confirmed beak
    /// strikes, drives this hediff's severity 0.1 -> 1.0, and keeps the correct beak TIER hediff
    /// installed on the Jaw (tier 1 below 0.5, tier 2 below 1.0, tier 3 at 1.0). Each tier's tool
    /// carries its own penetration (vanilla fraction; ToolCE mm under Combat Extended).
    /// </summary>
    public class HediffComp_BeakConditioning : HediffComp
    {
        public HediffCompProperties_BeakConditioning Props => (HediffCompProperties_BeakConditioning)props;

        private int strikes;
        private int installedTier = -1;

        public float Conditioning =>
            Mathf.Clamp(Props.startSeverity + strikes * Props.perStrike, Props.startSeverity, 1f);

        private static int TierFor(float c) => c < 0.5f ? 1 : (c < 1f ? 2 : 3);

        private int lastStrikeTick = -1;

        // Called by the melee-hit Harmony postfixes. De-duplicated per game tick so multiple
        // hooks firing for the same swing only count once.
        public void RegisterStrike()
        {
            int t = Find.TickManager.TicksGame;
            if (t == lastStrikeTick)
            {
                return;
            }
            lastStrikeTick = t;

            if (Conditioning < 1f)
            {
                strikes++;
            }
            parent.Severity = Conditioning;
            UpdateTier();
        }

        public override void CompPostMake()
        {
            base.CompPostMake();

            // Roll how honed this individual's beak already is, then back-convert that target
            // severity into an equivalent strike count so the rest of the class (RegisterStrike,
            // UpdateTier, saving) doesn't need to know anything changed.
            float target = AdaptationRandomizer.RollSeverity(Props.spawnSeverityOptions, Props.startSeverity);
            float perStrike = Mathf.Max(Props.perStrike, 0.0001f);
            strikes = Mathf.Max(0, Mathf.RoundToInt((target - Props.startSeverity) / perStrike));

            parent.Severity = Conditioning;
        }

        // Install the jaw beak the instant the conditioning hediff is added (i.e. when the gene
        // is applied at generation), so a pawn already has it before spawning onto the map.
        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);

            // Same rule as the claws, and for the same reason: CompPostMake cannot see the pawn,
            // PostAdd can. This beak only READ as correct on Maximus's hatchling because the die
            // happened to land low - it could have rolled honed just as easily.
            if (AdaptationRandomizer.BornUnconditioned(parent.pawn))
            {
                strikes = 0;
            }

            parent.Severity = Conditioning;
            UpdateTier();
        }

        // How often (in ticks) to fully re-verify the jaw's beak, not just the "never installed
        // yet" case CompPostTick already covered. ~1 in-game day - deliberately rare, per spec.
        private const int RareRecheckInterval = 60000;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            // Ensure a beak is installed shortly after spawn/load.
            if (installedTier < 0)
            {
                UpdateTier();
                return;
            }

            // Very rare full re-check, staggered per pawn so many Avir don't all re-verify on the
            // same tick. Catches a jaw that lost its beak through some means other than this comp
            // (another mod, dev tools, a save-compat edge case) and regrows it - but only if the
            // jaw is genuinely natural; see UpdateTier's own bionic guard.
            Pawn pawn = parent.pawn;
            if (pawn == null) return;
            if ((Find.TickManager.TicksGame + pawn.thingIDNumber) % RareRecheckInterval != 0) return;

            UpdateTier();
        }

        private static HediffDef TierDef(int t)
        {
            switch (t)
            {
                case 1: return EssentiumBeakDefOf.Avir_EssentiumBeak_1;
                case 2: return EssentiumBeakDefOf.Avir_EssentiumBeak_2;
                default: return EssentiumBeakDefOf.Avir_EssentiumBeak_3;
            }
        }

        private void UpdateTier()
        {
            Pawn pawn = parent.pawn;
            if (pawn == null || pawn.health == null)
            {
                return;
            }

            BodyPartRecord jaw = pawn.health.hediffSet.GetNotMissingParts()
                .FirstOrDefault(p => p.def == EssentiumBeakDefOf.Jaw);
            if (jaw == null)
            {
                return; // jaw amputated - nothing to install a beak onto
            }

            // BIONIC GUARD: if the jaw is carrying some OTHER added-part hediff (a bionic/
            // archotech jaw from another mod), this is not a natural jaw - leave it alone
            // entirely rather than layering a beak tier on top of someone else's implant.
            foreach (Hediff h in pawn.health.hediffSet.hediffs)
            {
                if (h.Part != jaw) continue;
                if (h.def == EssentiumBeakDefOf.Avir_EssentiumBeak_1
                 || h.def == EssentiumBeakDefOf.Avir_EssentiumBeak_2
                 || h.def == EssentiumBeakDefOf.Avir_EssentiumBeak_3) continue;

                if (h is Hediff_AddedPart || h is Hediff_Implant)
                {
                    return; // a foreign implant sits here - skip, don't touch it
                }
            }

            int tier = TierFor(Conditioning);
            if (tier == installedTier && pawn.health.hediffSet.HasHediff(TierDef(tier)))
            {
                return;
            }

            // Remove any tier that isn't the one we want, then install the wanted tier.
            for (int t = 1; t <= 3; t++)
            {
                if (t == tier) continue;
                Hediff other = pawn.health.hediffSet.GetFirstHediffOfDef(TierDef(t));
                if (other != null) pawn.health.RemoveHediff(other);
            }
            if (!pawn.health.hediffSet.HasHediff(TierDef(tier)))
            {
                pawn.health.AddHediff(TierDef(tier), jaw);
            }
            installedTier = tier;
        }

        // Clean up the jaw beak when the gene/conditioning is removed.
        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            Pawn pawn = parent.pawn;
            if (pawn == null || pawn.health == null) return;
            for (int t = 1; t <= 3; t++)
            {
                Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(TierDef(t));
                if (h != null) pawn.health.RemoveHediff(h);
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref strikes, "avirBeakStrikes", 0);
            Scribe_Values.Look(ref installedTier, "avirBeakTier", -1);
        }

        public override string CompDebugString()
        {
            return $"strikes={strikes} conditioning={Conditioning:0.000} tier={installedTier}";
        }
    }
}
