using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Avir.Artifactorics
{
    [DefOf]
    public static class ArtifactChargeDefOf
    {
        public static HediffDef Avir_ArtifactDrained;
        public static JobDef Avir_ArtifactRecharge;

        static ArtifactChargeDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactChargeDefOf)); }
    }

    /// <summary>
    /// Stage 10 - charge.
    ///   Charge is kept as a share of the artifact's capacity (0..1). Capacity in units =
    ///   100 x setting-quality factor x the wearer's psychic sensitivity (0.5..3), so the same
    ///   effect costs a sensitive wearer a smaller share.
    ///   At peace the artifact barely spends: chargeDailyBase per day once a cell is open, plus
    ///   chargeDailyPerMilestoneArm for every working milestone arm. In a fight (drafted, or
    ///   attacked / attacking in the last 10 s) the active milestones cost charge: shield hits,
    ///   the aura, blinks, dissolve, wall passage, the veil. Peaceful milestones never cost more
    ///   than the daily upkeep.
    ///   Empty: milestones stop. Cell bonuses, telekinesis and experience go on.
    ///   Recharge: the wearer's own work, 1 hour where it stands, every 3 days. All worn
    ///   artifacts are filled at once; 1% psychic sensitivity given = 0.75% charge, never below
    ///   10% of the pawn's own sensitivity. What was given is lost for a day (hediff).
    ///   Automatic recharge below the threshold the player sets per pawn; a button orders it now.
    ///   A psycaster's psyfocus trickles into an unfilled artifact, but never below the floor.
    /// </summary>
    public static class ArtifactCharge
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public static float QualityFactor(QualityCategory q)
        {
            List<float> l = S?.chargeQualityFactors;
            int i = (int)q;
            return l != null && i >= 0 && i < l.Count ? l[i] : 1f;
        }

        public static float Capacity(ArtifactState st, Pawn wearer)
        {
            float sens = wearer != null ? Mathf.Clamp(ArtifactFocus.Sensitivity(wearer), 0.5f, 3f) : 1f;
            return 100f * QualityFactor(st.settingQuality) * sens;
        }

        /// <summary>Whether the passive powers that only make sense in a fight (veil, phasing, blink on the walk, the
        /// hunter's ambush, battle telekinesis) may wake: a drafted pawn, one fighting now, or one that has picked out an
        /// enemy to go for. A trader walking about the colony or a colonist hauling stone does not turn invisible.</summary>
        public static bool CombatReady(Pawn p)
        {
            if (p == null) return false;
            if (InCombat(p)) return true;
            Pawn_MindState m = p.mindState;
            return m != null && (m.enemyTarget is Pawn e && !e.Dead && !e.Downed && e.HostileTo(p) || m.anyCloseHostilesRecently && p.Faction != Faction.OfPlayer && p.HostileTo(Faction.OfPlayer));
        }

        public static bool InCombat(Pawn p)
        {
            if (p == null) return false;
            if (p.Drafted) return true;
            int now = Find.TickManager.TicksGame;
            Pawn_MindState m = p.mindState;
            return m != null && (now - m.lastEngageTargetTick < 600 || now - m.lastHarmTick < 600 || now - m.lastAttackTargetTick < 600);
        }

        /// <summary>Spends `units` from the artifact that gives `arm` its milestone. Returns false
        /// (nothing spent) when that artifact is empty. Outside a fight active effects are free -
        /// the daily upkeep covers them - unless `always` is set.</summary>
        public static bool Spend(Pawn p, ArtifactArmDef arm, float units, bool always = false)
        {
            if (S == null || p == null) return true;
            MilestoneArm best = null;
            foreach (MilestoneArm m in ArtifactMilestones.For(p))
                if (m.active && m.arm == arm && (best == null || m.reached > best.reached)) best = m;
            if (best == null) return true;
            ArtifactState st = best.state;
            if (st.charge <= 0f) return false;
            if (!always && !InCombat(p)) return true;
            st.charge = Mathf.Max(0f, st.charge - units / Capacity(st, p));
            if (st.charge <= 0f)
            {
                ArtifactMilestones.Invalidate(p);
                if (p.Faction == Faction.OfPlayer)
                    Messages.Message(p.LabelShort + "'s artifact in " + best.socket.parent.LabelNoCount + " is out of charge.",
                        p, MessageTypeDefOf.NegativeEvent, false);
            }
            return true;
        }

        public static float LowestCharge(Pawn p)
        {
            float low = 1f;
            foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p))
                if (s.inlaid != null && s.inlaid.HasHeart) low = Mathf.Min(low, s.inlaid.charge);
            return low;
        }

        /// <summary>Psychic sensitivity a full recharge would take now, and how much it may give.</summary>
        public static void RechargeCost(Pawn p, out float wanted, out float allowed)
        {
            wanted = 0f;
            foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p))
                if (s.inlaid != null && s.inlaid.HasHeart) wanted += (1f - s.inlaid.charge) / S.chargePerSensitivity;
            float sens = ArtifactFocus.Sensitivity(p);
            float given = (p.health.hediffSet.GetFirstHediffOfDef(ArtifactChargeDefOf.Avir_ArtifactDrained) as Hediff_ArtifactDrained)?.given ?? 0f;
            float own = sens + given;   // the pawn's own sensitivity, before today's donation
            allowed = Mathf.Max(0f, sens - own * S.rechargeSensitivityFloor);
        }

        /// <summary>A mage pours their Arcana in first (the author, Nov 2026); only what it does not cover is taken from
        /// psychic sensitivity. Returns the Arcana spent.</summary>
        private static float FromArcana(Pawn p)
        {
            Magic.Hediff_ArcaneCore core = Magic.AvirMagic.On ? Magic.Mages.Core(p) : null;
            if (core == null || core.arcana <= 0.01f) return 0f;
            float spent = 0f;
            foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p))
            {
                ArtifactState st = s.inlaid;
                if (st == null || !st.HasHeart || st.charge >= 0.999f || core.arcana <= 0.01f) continue;
                float cap = Mathf.Max(1f, Capacity(st, p));
                float g = Mathf.Min(core.arcana, (1f - st.charge) * cap);
                st.charge = Mathf.Min(1f, st.charge + g / cap);
                core.arcana -= g; spent += g;
            }
            return spent;
        }

        public static void DoRecharge(Pawn p)
        {
            float arc = FromArcana(p);
            if (arc > 0f && p.Faction == Faction.OfPlayer)
                Messages.Message(p.LabelShort + " poured " + Mathf.RoundToInt(arc) + " Arcana into " + p.Possessive() + " artifacts.", p, MessageTypeDefOf.TaskCompletion, false);
            RechargeCost(p, out float wanted, out float allowed);
            if (wanted <= 0.0001f) { GameComponent_ArtifactCharge.Get?.MarkRecharged(p); ArtifactMilestones.Invalidate(p); ArtifactBonusCache.Invalidate(p); return; }
            if (wanted <= 0.0001f) return;
            float give = Mathf.Min(wanted, allowed);
            float share = wanted > 0f ? give / wanted : 0f;
            foreach (CompArtifactSocket s in ArtifactWearCache.Worn(p))
            {
                ArtifactState st = s.inlaid;
                if (st == null || !st.HasHeart) continue;
                st.charge = Mathf.Min(1f, st.charge + (1f - st.charge) * share);
            }
            if (give > 0f)
            {
                var h = p.health.hediffSet.GetFirstHediffOfDef(ArtifactChargeDefOf.Avir_ArtifactDrained) as Hediff_ArtifactDrained;
                if (h == null)
                {
                    h = (Hediff_ArtifactDrained)HediffMaker.MakeHediff(ArtifactChargeDefOf.Avir_ArtifactDrained, p);
                    p.health.AddHediff(h);
                }
                h.given += give;
                h.ticksLeft = GenDate.TicksPerDay;
                h.Refresh();
            }
            GameComponent_ArtifactCharge.Get?.MarkRecharged(p);
            ArtifactMilestones.Invalidate(p);
            ArtifactBonusCache.Invalidate(p);
            if (p.Faction == Faction.OfPlayer)
                Messages.Message(p.LabelShort + " poured " + give.ToStringPercent() + " psychic sensitivity into " + p.Possessive()
                                 + " artifacts" + (share < 0.999f ? " - not enough to fill them" : "") + ".",
                    p, MessageTypeDefOf.TaskCompletion, false);
        }

        public static bool CanRecharge(Pawn p, out string why)
        {
            why = null;
            var gc = GameComponent_ArtifactCharge.Get;
            int left = gc?.CooldownLeft(p) ?? 0;
            if (left > 0) { why = "ready in " + left.ToStringTicksToPeriod(); return false; }
            if (LowestCharge(p) >= 0.999f) { why = "the artifacts are full"; return false; }
            RechargeCost(p, out _, out float allowed);
            bool arcana = Magic.AvirMagic.On && (Magic.Mages.Core(p)?.arcana ?? 0f) > 0.5f;
            if (allowed <= 0.001f && !arcana) { why = "too little psychic sensitivity left to give"; return false; }
            return true;
        }

        public static void OrderRecharge(Pawn p, bool forced)
        {
            Job job = JobMaker.MakeJob(ArtifactChargeDefOf.Avir_ArtifactRecharge);
            job.playerForced = forced;
            if (forced) p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            else p.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
        }
    }

    /// <summary>The psychic sensitivity given to a recharge, lost for one day.</summary>
    public class Hediff_ArtifactDrained : Hediff_ArtifactTimed
    {
        public float given;
        [Unsaved] private HediffStage stage;

        public void Refresh() => stage = null;

        public override HediffStage CurStage
        {
            get
            {
                if (stage == null)
                {
                    stage = new HediffStage();
                    stage.statOffsets = new List<StatModifier> { new StatModifier { stat = StatDefOf.PsychicSensitivity, value = -given } };
                }
                return stage;
            }
        }

        public override string LabelInBrackets => "-" + given.ToStringPercent() + ", " + base.LabelInBrackets;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref given, "given");
        }
    }

    /// <summary>Per pawn: the auto-recharge threshold and the last recharge.</summary>
    public class GameComponent_ArtifactCharge : GameComponent
    {
        private Dictionary<int, float> thresholds = new Dictionary<int, float>();
        private Dictionary<int, int> lastRecharge = new Dictionary<int, int>();
        private List<int> k1, k2; private List<float> v1; private List<int> v2;

        public GameComponent_ArtifactCharge(Game game) { }

        public static GameComponent_ArtifactCharge Get => Current.Game?.GetComponent<GameComponent_ArtifactCharge>();

        public float Threshold(Pawn p)
        {
            return thresholds.TryGetValue(p.thingIDNumber, out float t) ? t : (ArtifactoricsSettingsDef.Main?.autoRechargeDefault ?? 0.3f);
        }

        public void SetThreshold(Pawn p, float t) => thresholds[p.thingIDNumber] = t;

        // ---- stage 12c: auto-used stones work only while the artifact holds at least this much ----
        private Dictionary<int, float> stoneThresholds = new Dictionary<int, float>();
        private List<int> k3; private List<float> v3;

        public float StoneThreshold(Pawn p)
        {
            return stoneThresholds.TryGetValue(p.thingIDNumber, out float t) ? t : (ArtifactoricsSettingsDef.Main?.autoStoneDefault ?? 0.25f);
        }

        public void SetStoneThreshold(Pawn p, float t) => stoneThresholds[p.thingIDNumber] = t;

        public void MarkRecharged(Pawn p) => lastRecharge[p.thingIDNumber] = Find.TickManager.TicksGame;

        public int CooldownLeft(Pawn p)
        {
            if (!lastRecharge.TryGetValue(p.thingIDNumber, out int last)) return 0;
            int cd = Mathf.RoundToInt((ArtifactoricsSettingsDef.Main?.rechargeCooldownDays ?? 3f) * GenDate.TicksPerDay);
            return Mathf.Max(0, last + cd - Find.TickManager.TicksGame);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref thresholds, "thresholds", LookMode.Value, LookMode.Value, ref k1, ref v1);
            Scribe_Collections.Look(ref lastRecharge, "lastRecharge", LookMode.Value, LookMode.Value, ref k2, ref v2);
            Scribe_Collections.Look(ref stoneThresholds, "stoneThresholds", LookMode.Value, LookMode.Value, ref k3, ref v3);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (thresholds == null) thresholds = new Dictionary<int, float>();
                if (lastRecharge == null) lastRecharge = new Dictionary<int, int>();
                if (stoneThresholds == null) stoneThresholds = new Dictionary<int, float>();
            }
        }
    }

    /// <summary>The wearer recharges its artifacts where it stands (1 hour).</summary>
    public class JobDriver_ArtifactRecharge : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !ArtifactCharge.CanRecharge(pawn, out _));
            Toil wait = Toils_General.Wait(ArtifactoricsSettingsDef.Main?.rechargeTicks ?? 2500);
            wait.WithProgressBarToilDelay(TargetIndex.None);
            yield return wait;
            yield return new Toil
            {
                initAction = () => ArtifactCharge.DoRecharge(pawn),
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    /// <summary>Daily upkeep, psyfocus trickle, aura upkeep in fights, and automatic recharge.</summary>
    public class MapComponent_ArtifactCharge : MapComponent
    {
        private const int Interval = 250;
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public MapComponent_ArtifactCharge(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (S == null || Find.TickManager.TicksGame % Interval != 0) return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            float dayShare = Interval / (float)GenDate.TicksPerDay;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.apparel == null || p.Dead) continue;
                List<CompArtifactSocket> worn = ArtifactWearCache.Worn(p);
                if (worn.Count == 0) continue;

                // daily upkeep
                List<MilestoneArm> ms = ArtifactMilestones.For(p);
                foreach (CompArtifactSocket s in worn)
                {
                    ArtifactState st = s.inlaid;
                    if (st == null || !st.HasHeart || st.charge <= 0f) continue;
                    if (st.opened == null || st.opened.Values.All(v => v <= 0)) continue;
                    int arms = ms.Count(m => m.active && m.state == st);
                    float daily = S.chargeDailyBase + S.chargeDailyPerMilestoneArm * arms;
                    st.charge = Mathf.Max(0f, st.charge - daily * dayShare);
                    if (st.charge <= 0f) ArtifactMilestones.Invalidate(p);
                }

                // Command aura upkeep in a fight
                if (ArtifactMilestones.Level(p, ArtifactArmDefOf.ArtifactArm_Command) >= 3 && ArtifactCharge.InCombat(p))
                    ArtifactCharge.Spend(p, ArtifactArmDefOf.ArtifactArm_Command, S.chargeAuraPerSecond * Interval / 60f);

                // psyfocus trickle (psycasters only, never below the floor)
                if (p.HasPsylink && p.psychicEntropy != null)
                {
                    CompArtifactSocket low = worn.Where(s => s.inlaid != null && s.inlaid.HasHeart && s.inlaid.charge < 1f)
                        .OrderBy(s => s.inlaid.charge).FirstOrDefault();
                    float focus = p.psychicEntropy.CurrentPsyfocus;
                    if (low != null && focus > S.psyfocusFloor)
                    {
                        float take = Mathf.Min(S.psyfocusPerHour * Interval / 2500f, focus - S.psyfocusFloor);
                        p.psychicEntropy.OffsetPsyfocusDirectly(-take);
                        low.inlaid.charge = Mathf.Min(1f, low.inlaid.charge + take * S.chargePerPsyfocus);
                    }
                }

                // automatic recharge
                if (p.Faction == Faction.OfPlayer && p.IsColonist)
                {
                    var gc = GameComponent_ArtifactCharge.Get;
                    float threshold = gc?.Threshold(p) ?? 0f;
                    // a carried crystal battery first; the wearer's own psychic recharge only without one
                    if (threshold > 0f && ArtifactCharge.LowestCharge(p) < threshold && ArtifactBattery.HasUsable(p)) ArtifactBattery.Use(p, false);
                    else if (threshold > 0f && ArtifactCharge.LowestCharge(p) < threshold && p.CurJobDef != ArtifactChargeDefOf.Avir_ArtifactRecharge
                        && p.CurJobDef != ArtifactoricsJobDefOf.Avir_ArtifactAttune
                        && MapComponent_ArtifactOrders.Available(p, map) && ArtifactCharge.CanRecharge(p, out _))
                        ArtifactCharge.OrderRecharge(p, false);
                }
            }
        }
    }
}
