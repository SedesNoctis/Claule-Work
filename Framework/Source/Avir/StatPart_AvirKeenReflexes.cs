using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Avir fight by ear. Every Fearborn carrier converts hearing above the human baseline into
    /// melee accuracy and evasion - they hear the swing coming before they see it.
    ///
    /// THE CURVE. bonus = a * (1.25^n - 1), where n is the number of full 10% steps of Hearing
    /// above 100%. The (r^n - 1) shape is zero at n=0 by construction, so an ordinary-eared pawn
    /// gets exactly nothing and no special case is needed for them. The coefficient a is pinned
    /// by the design anchor at 180% hearing (n=8): dodge x1.45, hit x1.75.
    ///
    ///     hearing   dodge     hit (vanilla scale)
    ///      110%     102.3%     103.8%
    ///      140%     113.1%     121.8%
    ///      160%     125.5%     142.6%
    ///      180%     145.0%     175.0%   <- anchor, and the ceiling
    ///
    /// CEILING AT n=8. Hard clamp, no plateau creep. Hearing is genuinely hard to raise in
    /// RimWorld without implants, and Avir will not touch implants - so 180% is where the design
    /// tops out and everything beyond it is flavour. Without the clamp a 1.25 base reaches 223%
    /// dodge at 220% hearing, which is not a build, it is a bug with a story attached.
    ///
    /// WHY THERE ARE TWO MODES. Combat Extended does not merely retune melee, it changes what
    /// the numbers MEAN, so one set of coefficients cannot be correct in both worlds:
    ///
    ///   MeleeDodgeChance - CE sets defaultBaseValue to 1 and displays it as a percentage, so a
    ///     multiplier reads exactly as designed: x1.45 shows up as 145%. Multiplying works in
    ///     both worlds, so dodge uses the same code path either way.
    ///
    ///   MeleeHitChance - vanilla treats this as a factor near 1.0, so x1.75 is meaningful. CE
    ///     replaces the stat's postProcessCurve with (-20, 0.10) (0, 0.60) (10, 0.90) (20, 1.00)
    ///     and sets noSkillOffset to 6.7, turning the raw value into a skill-like -20..+20 scale
    ///     that a curve maps to a probability CAPPED AT 1.00. Under CE, "175% hit chance" is not
    ///     merely unbalanced, it is unrepresentable. So the CE path adds RAW POINTS instead:
    ///     +8 at the anchor, which lifts a baseline fighter from 60% to 84% actual hits.
    ///
    /// The two paths meet at the same place in fiction and diverge only in arithmetic.
    ///
    /// COST. No caching, deliberately. Capacity levels are already cached by the pawn's health
    /// tracker, so this is a dictionary read plus a Math.Pow on a value clamped to nine discrete
    /// steps. These stats are evaluated per melee swing and by the stat UI, never per tick.
    /// </summary>
    public class StatPart_AvirKeenReflexes : StatPart
    {
        public enum Mode { Dodge, Hit }

        public Mode mode = Mode.Dodge;

        private const float Base = 1.25f;          // growth per 10% of hearing
        private const float MaxSteps = 8f;         // ceiling: 180% hearing
        private const float AnchorDodge = 0.45f;   // x1.45 at the anchor
        private const float AnchorHit = 0.75f;     // x1.75 at the anchor, vanilla scale
        private const float AnchorHitRawCE = 8f;   // +8 raw points at the anchor, CE scale

        private static readonly float Denominator = (float)Math.Pow(Base, MaxSteps) - 1f;

        private static bool ceChecked;
        private static bool ceActive;

        private static PawnCapacityDef hearingCached;
        private static bool hearingResolved;

        /// <summary>Resolved once. If Combat Extended is absent the vanilla arithmetic applies.</summary>
        private static bool CombatExtendedActive
        {
            get
            {
                if (!ceChecked)
                {
                    ceChecked = true;
                    ceActive = ModsConfig.IsActive("CETeam.CombatExtended");
                }
                return ceActive;
            }
        }

        private static PawnCapacityDef HearingCapacity
        {
            get
            {
                if (!hearingResolved)
                {
                    hearingResolved = true;
                    hearingCached = DefDatabase<PawnCapacityDef>.GetNamedSilentFail("Hearing");
                    if (hearingCached == null)
                        Log.Warning("[Avir] Hearing capacity not found - keen reflexes are inert.");
                }
                return hearingCached;
            }
        }

        /// <summary>Steps of 10% hearing above baseline, clamped to the design ceiling.</summary>
        private static float Steps(Pawn pawn)
        {
            PawnCapacityDef hearing = HearingCapacity;
            if (hearing == null) return 0f;

            float level = pawn.health.capacities.GetLevel(hearing);
            float steps = (level - 1f) / 0.1f;
            if (steps <= 0f) return 0f;
            return steps > MaxSteps ? MaxSteps : steps;
        }

        private bool TryGetBonus(StatRequest req, out float bonus, out bool additive)
        {
            bonus = 0f;
            additive = false;

            if (!req.HasThing) return false;
            Pawn pawn = req.Thing as Pawn;
            if (pawn == null || pawn.health == null) return false;
            if (!AvirFaction.HasFearborn(pawn)) return false;

            float steps = Steps(pawn);
            if (steps <= 0f) return false;

            float shape = ((float)Math.Pow(Base, steps) - 1f) / Denominator;   // 0 at baseline, 1 at anchor

            if (mode == Mode.Dodge)
            {
                bonus = AnchorDodge * shape;      // multiplicative in both worlds
                return true;
            }

            if (CombatExtendedActive)
            {
                // CE: raw points on a -20..+20 scale, added, never multiplied.
                bonus = AnchorHitRawCE * shape;
                additive = true;
                return true;
            }

            bonus = AnchorHit * shape;            // vanilla: plain factor
            return true;
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!TryGetBonus(req, out float bonus, out bool additive)) return;
            if (additive) val += bonus;
            else val *= 1f + bonus;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!TryGetBonus(req, out float bonus, out bool additive)) return null;
            if (additive)
                return "Keen reflexes (hearing): +" + bonus.ToString("0.0");
            return "Keen reflexes (hearing): x" + (1f + bonus).ToString("0.00");
        }
    }

    /// <summary>
    /// Self-registering injector. Deliberately NOT folded into AvirHarmony's InjectChoirPart
    /// block: this needs no Harmony patch at all, only two StatDefs decorated after defs load,
    /// and keeping it here means the whole mechanic - curve, modes and wiring - lives in one
    /// readable file. StaticConstructorOnStartup runs after DefDatabase is populated, which is
    /// exactly the window required.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirKeenReflexesInjector
    {
        static AvirKeenReflexesInjector()
        {
            Inject("MeleeDodgeChance", StatPart_AvirKeenReflexes.Mode.Dodge);
            Inject("MeleeHitChance", StatPart_AvirKeenReflexes.Mode.Hit);
        }

        private static void Inject(string statDefName, StatPart_AvirKeenReflexes.Mode mode)
        {
            StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(statDefName);
            if (stat == null)
            {
                Log.Warning("[Avir] Stat " + statDefName + " not found - keen reflexes not attached.");
                return;
            }

            if (stat.parts == null) stat.parts = new List<StatPart>();

            for (int i = 0; i < stat.parts.Count; i++)
            {
                StatPart_AvirKeenReflexes existing = stat.parts[i] as StatPart_AvirKeenReflexes;
                if (existing != null && existing.mode == mode) return;   // idempotent
            }

            stat.parts.Add(new StatPart_AvirKeenReflexes { parentStat = stat, mode = mode });
        }
    }
}
