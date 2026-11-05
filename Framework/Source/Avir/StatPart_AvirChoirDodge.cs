using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// Injected onto VEF_RangedDodgeChance by AvirHarmony. +5% per Fearborn member currently in
    /// the pawn's Choir, capped at +50% - same cap for every member regardless of caste (unlike
    /// the sensitivity part below, which doubles the cap for Matriarchs/Priestesses).
    /// </summary>
    public class StatPart_AvirChoirDodge : StatPart
    {
        private const float PerFearbornMember = 0.05f;
        private const float Cap = 0.50f;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (TryGetOffset(req, out float offset))
            {
                val += offset;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (TryGetOffset(req, out float offset) && offset != 0f)
            {
                return "In the Choir: +" + offset.ToStringPercent();
            }

            return null;
        }

        private static bool TryGetOffset(StatRequest req, out float offset)
        {
            offset = 0f;

            if (!(req.Thing is Pawn pawn))
            {
                return false;
            }

            if (!GameComponent_AvirChoir.TryGetChoirFor(pawn, out AvirChoirState state))
            {
                return false;
            }

            offset = Mathf.Min(PerFearbornMember * state.fearbornCount, Cap);
            return offset > 0f;
        }
    }
}
