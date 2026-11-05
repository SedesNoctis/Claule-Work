using UnityEngine;
using Verse;

namespace Avir
{
    public class HediffCompProperties_ChoirStatusTip : HediffCompProperties
    {
        public HediffCompProperties_ChoirStatusTip()
        {
            compClass = typeof(HediffComp_ChoirStatusTip);
        }
    }

    /// <summary>Shows the CURRENT live dodge/sensitivity bonus in the hediff's tooltip - these
    /// numbers move continuously as Fearborn singers enter/leave the Choir, so a static
    /// description can't show them; this reads the same live state the two Choir StatParts do.</summary>
    public class HediffComp_ChoirStatusTip : HediffComp
    {
        private const float PerFearbornMember = 0.05f;
        private const float DodgeCap = 0.50f;
        private const float SharedSensitivityCap = 1.50f;

        public override string CompTipStringExtra
        {
            get
            {
                Pawn pawn = parent.pawn;
                if (pawn == null || !GameComponent_AvirChoir.TryGetChoirFor(pawn, out AvirChoirState state))
                {
                    return null;
                }

                float dodge = Mathf.Min(PerFearbornMember * state.fearbornCount, DodgeCap);

                float sharedSensitivity = Mathf.Min(PerFearbornMember * state.fearbornCount, SharedSensitivityCap);
                string kind = pawn.kindDef?.defName;
                bool isMatriarchOrPriestess = kind == "Avir_Matriarch" || kind == "Avir_Priestess";
                float sensitivity = isMatriarchOrPriestess ? sharedSensitivity * 2f : sharedSensitivity;

                return "Avir_ChoirStatusTip".Translate(
                    state.fearbornCount,
                    dodge.ToStringPercent(),
                    sensitivity.ToStringPercent());
            }
        }
    }
}
