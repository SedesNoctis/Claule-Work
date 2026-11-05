using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir
{
    public class HediffCompProperties_ClawConditioning : HediffCompProperties
    {
        public float startSeverity = 0.25f;         // floor / starting conditioning
        public float meleeGrowthMultiplier = 1.5f;  // fighting counts extra - it's rarer in play
        public float plantGrowthMultiplier = 1f;
        public float miningGrowthMultiplier = 1f;
        public float craftGrowthMultiplier = 1f;    // covers Crafting work type: smithing, art, tailoring, etc.
        public int checkInterval = 250;              // ticks between job checks - TickRare cadence, cheap
        // Maps accumulated weighted work-ticks -> severity, with deliberately diminishing
        // returns: each later point requires far more work-ticks than the last, so early stages
        // come quickly and mastery demands sustained, near-relentless use over a long stretch of
        // the pawn's life. Set in XML; see Hediffs_Avir_Claws.xml.
        public SimpleCurve growthCurve;
        // Weighted table rolled once, when the hediff is first created (gene added, including at
        // pawn generation), to decide how adapted this individual already is - a floor under the
        // curve, not a replacement for it. Give bigger severities smaller weights so being born
        // already "mastered" is rare. Leave empty/omit to always start at startSeverity.
        public List<AdaptationSeverityOption> spawnSeverityOptions;

        public HediffCompProperties_ClawConditioning()
        {
            compClass = typeof(HediffComp_ClawConditioning);
        }
    }

    /// <summary>
    /// Lives on the whole-body "essentium claw conditioning" hediff. Every checkInterval ticks
    /// (default 250 - the same cadence vanilla uses for its own "rare tick" checks, so this is
    /// cheap even with several carriers on the map) it looks at the pawn's current job: melee
    /// combat, plant work, mining, and crafting (smithing/art/tailoring/etc, all merged under
    /// WorkTypeDefOf.Crafting since RimWorld 1.1) each add weighted work-ticks to a running
    /// total. That total is mapped to severity through growthCurve, a non-linear curve with
    /// diminishing returns - the gap between successive severity milestones grows sharply, so
    /// reaching "adapting" is quick but "mastered" demands years of a pawn actually using the
    /// claws. Unlike the beak conditioning system this never swaps hediffs on a body part - the
    /// claw/talon combat stats are fixed; only this hediff's own stat-factor stages change.
    ///
    /// spawnFloor is rolled once at creation (see spawnSeverityOptions) and acts as a floor
    /// under the curve's output, so a newly generated pawn can start partway adapted - "already
    /// spent years living with these claws" - without disturbing how the curve itself grows from
    /// there. It is never reduced, and normal use will eventually overtake it via the curve.
    /// </summary>
    public class HediffComp_ClawConditioning : HediffComp
    {
        public HediffCompProperties_ClawConditioning Props => (HediffCompProperties_ClawConditioning)props;

        private double workTicks;
        private float spawnFloor = -1f; // -1 = not yet rolled

        public float Conditioning
        {
            get
            {
                SimpleCurve curve = Props.growthCurve;
                float raw = curve != null ? curve.Evaluate((float)workTicks) : Props.startSeverity;
                float floor = spawnFloor >= 0f ? spawnFloor : Props.startSeverity;
                return Mathf.Clamp(Mathf.Max(raw, floor), Props.startSeverity, 1f);
            }
        }

        public override void CompPostMake()
        {
            base.CompPostMake();
            workTicks = 0;
            spawnFloor = AdaptationRandomizer.RollSeverity(Props.spawnSeverityOptions, Props.startSeverity);
            parent.Severity = Conditioning;
        }

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            if (spawnFloor < 0f)
            {
                spawnFloor = Props.startSeverity;
            }

            // THE AGE CHECK LIVES HERE, NOT IN CompPostMake, AND THAT IS FORCED.
            //
            // CompPostMake runs while the hediff is being built and parent.pawn is not reliably
            // set yet, so the roll cannot ask how old anybody is. PostAdd is the first moment the
            // pawn is known - and it is still before the pawn draws breath, so overwriting the
            // roll here costs nothing and is invisible.
            //
            // A chick hatched an hour ago has no history behind its claws. It climbs the ladder by
            // working, like the mechanic intends.
            if (AdaptationRandomizer.BornUnconditioned(parent.pawn))
            {
                workTicks = 0;
                spawnFloor = Props.startSeverity;
            }

            parent.Severity = Conditioning;
        }

        // How often (in ticks) to check whether the hands/feet need their claws/talons regrown.
        // Deliberately rare (~1 in-game day) and on its own cadence, separate from the existing
        // job-tracking interval above, which is about conditioning growth, not part regrowth.
        private const int RegrowthCheckInterval = 60000;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = parent.pawn;
            if (pawn == null || pawn.Dead)
            {
                return;
            }

            if ((Find.TickManager.TicksGame + pawn.thingIDNumber) % RegrowthCheckInterval == 0
                && ClawsMayBeMissing(pawn))
            {
                RegrowIfNatural(pawn);
            }

            if (Conditioning >= 1f)
            {
                return;
            }

            if (pawn.jobs == null)
            {
                return;
            }

            int interval = Mathf.Max(1, Props.checkInterval);

            // 5x LESS OFTEN for any Avir not in the player's own faction - a visiting trade
            // caravan or a raid shouldn't cost the same continuous tick budget as an equal
            // number of actual colonists, since the player never interacts closely enough with
            // a visitor's own claw conditioning to need full-rate updates for it.
            if (!AvirFaction.IsPlayerFactionPawn(pawn))
            {
                interval *= 5;
            }

            // STAGGERED PER PAWN. This used to be a GLOBAL modulo - TicksGame % interval - which
            // meant every Avir on the map ran the whole job/worktype lookup below ON THE SAME TICK.
            // Thirty Avir, thirty lookups, one frame: a spike that grows linearly with the number
            // of Avir, which is exactly the "more Avir = worse TPS" you are seeing.
            //
            // Offsetting by thingIDNumber spreads the work evenly across the interval instead. The
            // total work is identical; it just stops arriving all at once. HediffComp_SensesByLight
            // and HediffComp_StageBySunlightOrDarkness in this same mod already did it this way.
            if ((Find.TickManager.TicksGame + pawn.thingIDNumber) % interval != 0)
            {
                return;
            }

            Job job = pawn.CurJob;
            if (job == null)
            {
                return;
            }

            float multiplier;
            if (job.def == JobDefOf.AttackMelee)
            {
                multiplier = Props.meleeGrowthMultiplier;
            }
            else
            {
                WorkTypeDef workType = job.workGiverDef?.workType;
                if (workType == WorkTypeDefOf.Growing)
                {
                    multiplier = Props.plantGrowthMultiplier;
                }
                else if (workType == WorkTypeDefOf.Mining)
                {
                    multiplier = Props.miningGrowthMultiplier;
                }
                else if (workType == WorkTypeDefOf.Crafting)
                {
                    multiplier = Props.craftGrowthMultiplier;
                }
                else
                {
                    return;
                }
            }

            if (multiplier <= 0f)
            {
                return;
            }

            workTicks += multiplier * interval;
            parent.Severity = Conditioning;
        }

        // How many claw/talon hediffs this pawn ought to be carrying, as counted by the last
        // full scan. -1 = never scanned, so the first check always runs the scan.
        private int expectedClawCount = -1;

        /// <summary>
        /// Cheap gate in front of the daily regrowth scan. THE SCAN ITSELF IS QUADRATIC: for
        /// every hand and foot it walks the pawn's entire hediff list, and an Avir's list is
        /// long. That work only ever accomplishes something when a claw is genuinely absent,
        /// which for most pawns is never - so it must not be paid on a pawn whose paws are all
        /// where they should be.
        ///
        /// This is ONE linear pass over the hediff list, no nesting and no allocation: count the
        /// claw and talon hediffs and compare against what the last full scan found. Anything
        /// that could remove a claw - amputation, surgery, another mod stripping the hediff -
        /// lowers that count and lets the scan through exactly once, after which the new count
        /// is recorded and the pawn goes quiet again. Amputation is the case worth spelling out:
        /// losing a hand removes its claw hediff and adds a missing-part one, so the list length
        /// is unchanged but the claw count drops, which is why this counts claws rather than
        /// hediffs.
        /// </summary>
        private bool ClawsMayBeMissing(Pawn pawn)
        {
            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return false;
            }

            if (expectedClawCount < 0)
            {
                return true; // never scanned - let the first one through
            }

            int found = 0;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (AllClawDefs.Contains(hediffs[i].def))
                {
                    found++;
                }
            }

            return found != expectedClawCount;
        }

        // Every rung of both ladders plus the A'Shacsaign parallel ladder. A conditioned pawn is
        // carrying Claws_3, not Claws, so counting only the tier-1 defs would make the gate open
        // every single day for exactly the pawns it is meant to spare. Resolved once, silently:
        // any rung the def database doesn't have is simply absent from the set.
        private static HashSet<HediffDef> allClawDefs;

        private static HashSet<HediffDef> AllClawDefs
        {
            get
            {
                if (allClawDefs != null)
                {
                    return allClawDefs;
                }

                allClawDefs = new HashSet<HediffDef>();
                AddIfPresent("Avir_EssentiumClaws");
                AddIfPresent("Avir_EssentiumTalons");
                for (int i = 2; i <= 5; i++)
                {
                    AddIfPresent("Avir_EssentiumClaws_" + i);
                    AddIfPresent("Avir_EssentiumTalons_" + i);
                }
                for (int i = 1; i <= 5; i++)
                {
                    AddIfPresent("Avir_AShacsaignClaws_" + i);
                    AddIfPresent("Avir_AShacsaignTalons_" + i);
                }

                return allClawDefs;
            }
        }

        private static void AddIfPresent(string defName)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            if (def != null)
            {
                allClawDefs.Add(def);
            }
        }

        /// <summary>
        /// For every Hand and Foot on this pawn: if the part is genuinely natural (no foreign
        /// added-part/implant hediff sitting on it) and it's missing its essentium claw/talon,
        /// regrow it. If a foreign implant IS present, that part is skipped entirely - "if they
        /// have a bionic, ticks are skipping".
        /// </summary>
        private void RegrowIfNatural(Pawn pawn)
        {
            if (pawn.health?.hediffSet == null)
            {
                return;
            }

            int present = 0;

            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                HediffDef wanted;
                // The Avir body has claws and talons of their own (AvirBody.cs): the essentium sits there.
                // A body without them (a human given the genes) keeps them on the hands and feet as before.
                if (part.def == AvirBody.Claw || (part.def == EssentiumClawDefOf.Hand && AvirBody.ClawSeat(part) == part))
                {
                    wanted = EssentiumClawDefOf.Avir_EssentiumClaws;
                }
                else if (part.def == AvirBody.Talon || (part.def == EssentiumClawDefOf.Foot && AvirBody.ClawSeat(part) == part))
                {
                    wanted = EssentiumClawDefOf.Avir_EssentiumTalons;
                }
                else
                {
                    continue;
                }

                bool hasWanted = false;
                bool hasForeignImplant = false;

                foreach (Hediff h in pawn.health.hediffSet.hediffs)
                {
                    if (h.Part != part) continue;

                    if (h.def == wanted)
                    {
                        hasWanted = true;
                        break;
                    }

                    if (h is Hediff_AddedPart || h is Hediff_Implant)
                    {
                        hasForeignImplant = true;
                    }
                }

                if (hasWanted)
                {
                    present++;
                    continue;
                }

                if (hasForeignImplant)
                {
                    // A bionic hand, or a higher claw rung, which reads as an added part. Either
                    // way nothing is regrown here - and the gate counts whatever claw hediff is
                    // actually on the body, so an upgraded rung is counted below.
                    continue;
                }

                pawn.health.AddHediff(wanted, part);
                present++;
            }

            // Record what the body looks like now, so the cheap gate can tell "nothing changed"
            // from "something is gone" without walking the parts again. Counted the same way the
            // gate counts, over every ladder rung, so the two numbers are comparable.
            expectedClawCount = CountClawHediffs(pawn);
            if (present > expectedClawCount)
            {
                // Shouldn't happen, but if AddHediff was refused for any reason, trust the body
                // rather than our own bookkeeping - a too-high expectation would reopen the gate
                // every day forever.
                expectedClawCount = present;
            }
        }

        private static int CountClawHediffs(Pawn pawn)
        {
            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return 0;
            }

            int found = 0;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (AllClawDefs.Contains(hediffs[i].def))
                {
                    found++;
                }
            }

            return found;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref workTicks, "avirClawWorkTicks", 0);
            Scribe_Values.Look(ref spawnFloor, "avirClawSpawnFloor", -1f);
        }

        public override string CompDebugString()
        {
            return $"conditioning={Conditioning:0.000} workTicks={workTicks:0} spawnFloor={spawnFloor:0.000}";
        }
    }
}

