using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// Injected onto PsychicSensitivity by AvirHarmony. Factor, not offset (consistent with every
    /// other PsychicSensitivity effect in this mod) - +5% per Fearborn member currently in the
    /// pawn's Choir, capped at 150% - the SAME cap, needing the SAME headcount, for every member.
    /// Matriarch/Priestess recipients then simply double that shared result (up to 300%) - a
    /// property of who's RECEIVING it, not a higher cap that needs more Fearborn to reach.
    /// </summary>
    public class StatPart_AvirChoirSensitivity : StatPart
    {
        private const float PerFearbornMember = 0.05f;
        private const float SharedCap = 1.50f;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (TryGetFactor(req, out float factor))
            {
                val *= factor;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (TryGetFactor(req, out float factor) && factor != 1f)
            {
                return "In the Choir: x" + factor.ToString("0.##");
            }

            return null;
        }

        private static bool TryGetFactor(StatRequest req, out float factor)
        {
            factor = 1f;

            if (!(req.Thing is Pawn pawn))
            {
                return false;
            }

            if (!GameComponent_AvirChoir.TryGetChoirFor(pawn, out AvirChoirState state))
            {
                return false;
            }

            float sharedBonus = Mathf.Min(PerFearbornMember * state.fearbornCount, SharedCap);
            float bonus = IsMatriarchOrPriestess(pawn) ? sharedBonus * 2f : sharedBonus;
            factor = 1f + bonus;
            return bonus > 0f;
        }

        private static bool IsMatriarchOrPriestess(Pawn pawn)
        {
            string kind = pawn.kindDef?.defName;
            return kind == "Avir_Matriarch" || kind == "Avir_Priestess";
        }
    }
}
