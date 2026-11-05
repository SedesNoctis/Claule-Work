using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// One configurable StatPart that powers every *stat* effect of the Psychic Nature gene.
    /// Attached to several stats from AvirHarmony (one instance per stat), each told how much to
    /// contribute per "step" of Psychic sensitivity.
    ///
    ///   step size = 0.15   (one step per +15% Psychic sensitivity, counted from 0)
    ///   steps     = PsychicSensitivity / 0.15
    ///
    /// Three contribution modes:
    ///   - additive (default):     val += perStep * steps
    ///   - factor (asFactor):      val *= 1 + perStep * steps          (used for a % move-speed buff)
    ///   - base-scaled (baseValue): val += baseValue * (1 + perStep*steps)
    ///         used for CE natural armour: a flat base value that itself grows a little with psy,
    ///         computed here so ONLY the natural armour scales - never the pawn's worn apparel.
    ///
    /// maxAbsOffset (>0) clamps the additive offset magnitude (used to cap melee dodge at +50%).
    /// lightConditional picks perStep (sunlight) vs perStepDark, using the mod's own sunlight rule.
    ///
    /// Runs on every read of its target stat, so cheap early-outs come first. It reads
    /// PsychicSensitivity, a DIFFERENT stat, so there is no recursion.
    /// </summary>
    public class StatPart_PsychicNature : StatPart
    {
        public const float StepSize = 0.15f;

        public float perStep;
        public float perStepDark;
        public bool lightConditional = false;
        public bool asFactor = false;
        public float baseValue = 0f;
        public float maxAbsOffset = 0f; // 0 = no cap
        public string effectLabel = "Psychic nature";

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (TryCompute(req, out float offset, out float factorMul))
            {
                // One of the two is always neutral (offset 0 or factorMul 1), so this applies
                // whichever mode is active.
                val = (val * factorMul) + offset;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!TryCompute(req, out float offset, out float factorMul))
            {
                return null;
            }

            if (asFactor)
            {
                return effectLabel + ": x" + factorMul.ToString("0.###");
            }
            if (baseValue != 0f)
            {
                // Armour values aren't percentages - show the raw contribution.
                string s = offset >= 0f ? "+" : "";
                return effectLabel + ": " + s + offset.ToString("0.###");
            }
            string sign = offset >= 0f ? "+" : "";
            return effectLabel + ": " + sign + offset.ToStringPercent();
        }

        private bool TryCompute(StatRequest req, out float offset, out float factorMul)
        {
            offset = 0f;
            factorMul = 1f;

            Pawn pawn = req.Thing as Pawn;
            if (pawn == null)
            {
                return false;
            }

            // Single combined lookup: bails cheaply for non-carriers (returns false), and hands
            // back the cached step count for carriers - no second gene scan, no second cache pass.
            if (!PsychicNatureCache.TryGetSteps(pawn, out float steps))
            {
                return false;
            }

            if (steps <= 0f)
            {
                // base-scaled effects still apply their flat base even at 0 psy.
                if (baseValue == 0f)
                {
                    return false;
                }
                steps = 0f;
            }

            float per = lightConditional
                ? (InSunlight(pawn) ? perStep : perStepDark)
                : perStep;

            if (asFactor)
            {
                factorMul = 1f + (per * steps);
                return factorMul != 1f;
            }

            offset = (baseValue != 0f)
                ? baseValue * (1f + (per * steps))
                : per * steps;

            if (maxAbsOffset > 0f)
            {
                offset = Mathf.Clamp(offset, -maxAbsOffset, maxAbsOffset);
            }

            return offset != 0f;
        }

        // Real sunlight only: unroofed while the sky is bright. Mirrors HediffComp_SensesByLight,
        // so artificial light never counts as "sun". Off-map / unspawned counts as darkness.
        private static bool InSunlight(Pawn pawn)
        {
            if (!pawn.Spawned || pawn.Map == null)
            {
                return false;
            }

            return !pawn.Position.Roofed(pawn.Map)
                   && pawn.Map.skyManager.CurSkyGlow >= 0.5f;
        }
    }
}
