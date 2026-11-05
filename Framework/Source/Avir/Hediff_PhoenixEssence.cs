using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// ESSENCE OF PHOENIX. Runs backwards compared to every other drug in the mod: it is applied
    /// at FULL severity and the peak is the first thing that happens. From there it decays for
    /// 72 hours, and what it does to the body inverts on the way down.
    ///
    /// STAGE BANDS. Deliberately uneven, so the healing window is exactly 48 hours rather than
    /// the 43.2 that five equal 0.20 bands would give:
    ///
    ///   5/5  Grace of the First Healer   1.000 - 0.778   16h   heal + regrow + immunise
    ///   4/5  The Long Mending            0.778 - 0.556   16h   heal + immunise, no regrowth
    ///   3/5  The Waning Grace            0.556 - 0.333   16h   heal only
    ///   2/5  The Judgement               0.333 - 0.167   12h   external burns
    ///   1/5  The Immolation              0.167 - 0.000   12h   internal burns, body alight
    ///
    /// severityPerDay is -0.3333, i.e. 1.0 -> 0.0 across three days.
    ///
    /// The hediff NEVER self-removes at zero (ShouldRemove is forced false). Hitting zero is the
    /// finale, not the end of the effect: the pawn dies covered in burns and
    /// GameComponent_AvirPhoenixRebirth brings it back. Letting the vanilla remove-at-zero fire
    /// would silently cancel the whole point of the drug.
    /// </summary>
    public class Hediff_PhoenixEssence : HediffWithComps
    {
        // Band floors. Kept here as well as in XML so the code and the stage list cannot drift.
        public const float Stage5Floor = 0.778f;
        public const float Stage4Floor = 0.556f;
        public const float Stage3Floor = 0.333f;
        public const float Stage2Floor = 0.167f;

        private const int WorkIntervalTicks = 60;

        // Per-interval budgets. Tuned against a 60-tick cadence (one real second at 1x).
        //
        // HEALING IS SCALED BY STAGE, and stage 5 is deliberately absurd. At 40 points per
        // injury per second the pawn out-heals essentially any sustained damage the game can
        // apply, which is what makes the Grace read as near-invulnerability rather than as
        // fast recovery. It is not a damage-resistance stat and it is not immunity: a single
        // blow big enough to destroy a vital part still kills. She just cannot be worn down.
        private const float InjuryHealStage5 = 40f;
        private const float InjuryHealStage4 = 12f;
        private const float InjuryHealStage3 = 4f;
        private const float BloodLossHealPerWork = 0.012f;
        private const float ImmunityGainPerWork = 0.010f;
        private const float BurnSeverityPerWork = 1.1f;

        private bool finaleFired;
        private int workCounter;
        private int implantCounter;

        /// <summary>
        /// Parts the fire has taken and not yet given back.
        ///
        /// THE BURN AND THE REGROWTH ARE NOW SEPARATE EVENTS. The first version called
        /// RestorePart on the bionic, which removed the machinery and regrew the limb under it
        /// in the same instant - so a bionic arm silently became a flesh arm with no gap. There
        /// was nothing to notice and nothing to regret.
        ///
        /// Now the implant is REMOVED, which leaves the part missing, and this counter schedules
        /// it to grow back on its own clock. A pawn who loses a bionic leg at hour two spends the
        /// next two hours on one leg. That gap is the entire point: it is where the player finds
        /// out what they did.
        /// </summary>
        private int pendingRegrowths;
        private int regrowCounter;

        /// <summary>How long a part the fire took stays gone before it grows back. Two hours.</summary>
        private const int RegrowIntervalTicks = 5000;

        /// <summary>
        /// How long the fire takes to reach the next piece of machinery. One in-game hour.
        ///
        /// THIS USED TO HAPPEN ALL AT ONCE, AT POSTADD. Gradual is the better design and the
        /// reason is entirely about information: a player who sends an augmented pawn into a
        /// fight after dosing them should not be told immediately that it was a mistake. The
        /// first hour is fine. The second one takes an arm. By the time the pattern is obvious
        /// the fight is already happening.
        ///
        /// One hour also means a pawn wearing five bionics is stripped well inside the healing
        /// window, so the machinery is always gone long before the Judgement starts.
        /// </summary>
        private const int BurnIntervalTicks = 2500;

        /// <summary>
        /// HediffDefOf has no Burn field - it is a plain HediffDef, resolved by name. Trying to
        /// reach it as HediffDefOf.Burn is CS0117. Cached statically so the lookup happens once.
        /// </summary>
        private static HediffDef burnDef;
        private static bool burnResolved;
        private static HediffDef BurnDef
        {
            get
            {
                if (!burnResolved)
                {
                    burnResolved = true;
                    burnDef = DefDatabase<HediffDef>.GetNamedSilentFail("Burn");
                    if (burnDef == null) Log.Warning("[Avir] Phoenix: HediffDef 'Burn' not found - no burns will be applied.");
                }
                return burnDef;
            }
        }

        public override string LabelBase => CurStage?.label ?? def.label;
        public override string LabelInBrackets => null;

        /// <summary>Never removed by severity. See the class note - zero is the finale.</summary>
        public override bool ShouldRemove => false;

        public bool Healing => Severity >= Stage3Floor;      // 5/5, 4/5, 3/5
        public bool RegrowsParts => Severity >= Stage5Floor;  // 5/5 only
        public bool Immunising => Severity >= Stage4Floor;    // 5/5, 4/5
        public bool Judging => Severity < Stage3Floor && Severity >= Stage2Floor;  // 2/5
        public bool Immolating => Severity < Stage2Floor;     // 1/5
        public bool InAgony => Judging || Immolating;

        /// <summary>Healing per injury per work interval. Falls off hard as the grace wanes.</summary>
        private float CurrentHealRate
        {
            get
            {
                if (RegrowsParts) return InjuryHealStage5;
                if (Immunising) return InjuryHealStage4;
                return InjuryHealStage3;
            }
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);

            // Nothing burns yet. The first implant goes one hour in - see BurnIntervalTicks.
            implantCounter = 0;
        }

        public override void Tick()
        {
            base.Tick();

            if (pawn == null || pawn.Dead) return;

            if (Severity <= 0.0001f)
            {
                FireTheFinale();
                return;
            }

            implantCounter++;
            if (implantCounter >= BurnIntervalTicks)
            {
                implantCounter = 0;
                BurnOutOneImplant();
            }

            if (pendingRegrowths > 0)
            {
                regrowCounter++;
                if (regrowCounter >= RegrowIntervalTicks)
                {
                    regrowCounter = 0;
                    RegrowOneBurnedPart();
                }
            }

            workCounter++;
            if (workCounter < WorkIntervalTicks) return;
            workCounter = 0;

            if (Healing) DoHealingWork();
            else if (Judging) DoJudgementWork();
            else DoImmolationWork();
        }

        // ============================================================
        //  STAGES 5-3: THE HEALING WINDOW
        // ============================================================
        private void DoHealingWork()
        {
            HealInjuriesAndBleeding();
            ClearAddictionsAndTolerances();
            SatisfyNeeds();

            if (Immunising) AccelerateImmunity();
            if (RegrowsParts) RegrowOneMissingPart();
        }

        /// <summary>
        /// Injuries shrink, bleeding stops outright, blood loss refills. Bleeding is closed by
        /// zeroing the injury's bleed flag rather than by tending, so it works on pawns nobody
        /// is available to treat - which is most of the cases this drug exists for.
        /// </summary>
        private void HealInjuriesAndBleeding()
        {
            List<Hediff> all = pawn.health.hediffSet.hediffs;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Hediff_Injury injury = all[i] as Hediff_Injury;
                if (injury != null)
                {
                    if (injury.Bleeding) injury.Tended(1f, 1f);
                    injury.Heal(CurrentHealRate);
                    continue;
                }

                if (all[i].def == HediffDefOf.BloodLoss)
                {
                    all[i].Severity -= BloodLossHealPerWork;
                }
            }
        }

        /// <summary>
        /// Every chemical dependency and every built-up tolerance goes. Addictions are removed
        /// outright rather than eased down, so there is no withdrawal on the far side - the body
        /// being rebuilt has simply never taken the drug.
        /// </summary>
        private void ClearAddictionsAndTolerances()
        {
            List<Hediff> all = pawn.health.hediffSet.hediffs;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                // the white barrenness the heartwing draught leaves goes with the old body
                if (all[i].def.defName == "Avir_WhiteBarrenness")
                {
                    pawn.health.RemoveHediff(all[i]);
                    continue;
                }
                if (all[i] is Hediff_Addiction || all[i].def.IsAddiction)
                {
                    pawn.health.RemoveHediff(all[i]);
                    continue;
                }

                // Tolerance hediffs are plain HediffWithComps carrying a chemical; the reliable
                // marker across vanilla and modded drugs is the def's own chemical link.
                if (all[i].def.hediffGivers == null && all[i].def.defName != null
                    && all[i].def.defName.EndsWith("Tolerance"))
                {
                    pawn.health.RemoveHediff(all[i]);
                }
            }
        }

        /// <summary>
        /// Rest and the desire to sleep go to full, ordinary hunger is held just above the point
        /// where it would start nagging, and an Avir's Arcane Hunger is filled to the brim. The
        /// NATURAL drain of the Arcane Hunger is suppressed separately and entirely in XML, via
        /// an Avir_ArcaneHungerFallRateFactor of 0 on every stage - Gene_ArcaneHunger multiplies
        /// its loss-per-day by that stat, so zero stops it dead without touching the gene.
        /// </summary>
        private void SatisfyNeeds()
        {
            if (pawn.needs != null)
            {
                if (pawn.needs.rest != null) pawn.needs.rest.CurLevel = pawn.needs.rest.MaxLevel;
                if (pawn.needs.food != null && pawn.needs.food.CurLevelPercentage < 0.90f)
                {
                    pawn.needs.food.CurLevelPercentage = 0.90f;
                }
            }

            Gene_ArcaneHunger hunger = Gene_ArcaneHunger.Of(pawn);
            if (hunger != null && hunger.Value < 1f) hunger.Value = 1f;
        }

        private void AccelerateImmunity()
        {
            ImmunityHandler handler = pawn.health.immunity;
            if (handler == null) return;

            List<ImmunityRecord> records = handler.ImmunityListForReading;
            for (int i = 0; i < records.Count; i++)
            {
                records[i].immunity = Mathf.Min(1f, records[i].immunity + ImmunityGainPerWork);
            }
        }

        /// <summary>One part per interval, so a pawn missing several rebuilds visibly rather
        /// than popping back whole in a single frame.</summary>
        private void RegrowOneMissingPart()
        {
            List<Hediff_MissingPart> missing = pawn.health.hediffSet.GetMissingPartsCommonAncestors();
            if (missing == null || missing.Count == 0) return;

            pawn.health.RestorePart(missing[0].Part);
        }

        // ============================================================
        //  STAGE 2: THE JUDGEMENT - external burns only
        // ============================================================
        private void DoJudgementWork()
        {
            SatisfyNeeds();
            BurnRandomPart(BodyPartDepth.Outside);
        }

        // ============================================================
        //  STAGE 1: THE IMMOLATION - internal burns, and the body alight
        // ============================================================
        private void DoImmolationWork()
        {
            SatisfyNeeds();
            BurnRandomPart(BodyPartDepth.Inside);

            if (!pawn.Dead && pawn.Spawned && !pawn.IsBurning() && Rand.Chance(0.10f))
            {
                pawn.TryAttachFire(0.6f, null);
            }
        }

        /// <summary>
        /// Adds a burn directly as a hediff rather than dealing damage.
        ///
        /// THIS IS THE WHOLE REASON THE BURNS CANNOT KILL. Routing through the damage system
        /// would run vanilla's lethality checks and could take the pawn out early, which would
        /// skip the finale entirely. Adding the hediff by hand lets the severity be clamped so
        /// the part is always left with at least one point of health, on every part, every time.
        /// </summary>
        private void BurnRandomPart(BodyPartDepth depth)
        {
            IEnumerable<BodyPartRecord> parts =
                pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, depth);

            List<BodyPartRecord> usable = new List<BodyPartRecord>();
            foreach (BodyPartRecord p in parts)
            {
                if (pawn.health.hediffSet.GetPartHealth(p) > 2f) usable.Add(p);
            }
            if (usable.Count == 0) return;

            BodyPartRecord part = usable[Rand.Range(0, usable.Count)];
            float headroom = pawn.health.hediffSet.GetPartHealth(part) - 1f;
            float amount = Mathf.Min(BurnSeverityPerWork, headroom);
            if (amount <= 0f) return;

            if (BurnDef == null) return;
            Hediff burn = HediffMaker.MakeHediff(BurnDef, pawn, part);
            burn.Severity = amount;
            pawn.health.AddHediff(burn, part);
        }

        // ============================================================
        //  ZERO: THE PYRE
        // ============================================================
        /// <summary>
        /// Survived all 72 hours. The body is burned everywhere and the pawn dies - but the
        /// rebirth hediff goes on FIRST, because that is the flag Patch_AvirPhoenixDeath reads
        /// inside Pawn.Kill to register the resurrection. Applying it after the kill would be
        /// too late.
        /// </summary>
        private void FireTheFinale()
        {
            if (finaleFired) return;
            finaleFired = true;

            if (!AvirPhoenixUtil.HasRebirth(pawn))
            {
                pawn.health.AddHediff(AvirPhoenixDefOf.Avir_PhoenixRebirth);
            }

            IEnumerable<BodyPartRecord> parts =
                pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined);

            foreach (BodyPartRecord p in new List<BodyPartRecord>(parts))
            {
                if (BurnDef == null) break;
                float headroom = pawn.health.hediffSet.GetPartHealth(p) - 1f;
                if (headroom <= 0f) continue;

                Hediff burn = HediffMaker.MakeHediff(BurnDef, pawn, p);
                burn.Severity = headroom;
                pawn.health.AddHediff(burn, p);
            }

            if (!pawn.Dead) pawn.Kill(null, this);
        }

        /// <summary>
        /// Takes ONE piece of machinery, and gives the body part underneath back.
        ///
        /// RestorePart rather than RemoveHediff, for the same reason Patch_AvirBionics.Strip
        /// uses it: an installed bionic leg has already destroyed the natural leg, so the
        /// hediff IS the leg. Removing it outright leaves the pawn with nothing there. The
        /// Phoenix rebuilds - it does not amputate.
        ///
        /// Whole-body implants with no part (joywires, tattoos) have nothing to regrow and are
        /// simply removed.
        /// </summary>
        private void BurnOutOneImplant()
        {
            List<Hediff> doomed = AvirPhoenixUtil.DoomedImplants(pawn);
            if (doomed.Count == 0) return;

            Hediff target = doomed[0];
            bool hadPart = target.Part != null;

            // REMOVE, do not RestorePart. An installed bionic leg IS the leg, so removing the
            // hediff leaves the part missing - and that is now deliberate. The gap is scheduled
            // to close on its own timer rather than closing in the same frame.
            pawn.health.RemoveHediff(target);

            if (hadPart)
            {
                pendingRegrowths++;
                regrowCounter = 0;
            }
        }

        /// <summary>
        /// Gives back one part the fire took. Independent of stage, unlike the natural limb
        /// regrowth in DoHealingWork, which is 5/5 only - a bionic burned out at hour twenty
        /// still has to come back, and by then the Grace is long past.
        ///
        /// Restores whatever is missing rather than tracking which specific part was taken.
        /// BodyPartRecord does not survive a save/load cleanly as a stored reference, and the
        /// distinction does not matter: anything missing on this pawn during the essence is
        /// something the fire is responsible for putting back.
        /// </summary>
        private void RegrowOneBurnedPart()
        {
            List<Hediff_MissingPart> missing = pawn.health.hediffSet.GetMissingPartsCommonAncestors();
            if (missing == null || missing.Count == 0)
            {
                pendingRegrowths = 0;
                return;
            }

            pawn.health.RestorePart(missing[0].Part);
            pendingRegrowths--;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref finaleFired, "avirPhoenixFinaleFired", false);
            Scribe_Values.Look(ref workCounter, "avirPhoenixWorkCounter", 0);
            Scribe_Values.Look(ref implantCounter, "avirPhoenixImplantCounter", 0);
            Scribe_Values.Look(ref pendingRegrowths, "avirPhoenixPendingRegrowths", 0);
            Scribe_Values.Look(ref regrowCounter, "avirPhoenixRegrowCounter", 0);
        }
    }
}
