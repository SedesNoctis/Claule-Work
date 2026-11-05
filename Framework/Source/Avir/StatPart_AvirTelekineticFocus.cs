using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Injected onto WorkSpeedGlobal by AvirHarmony (same pattern as StatPart_PsychicNature's own
    /// injection - see InjectTelekineticFocusStatPart). Reads whichever Avir amulet the pawn
    /// currently wears and multiplies work speed by that amulet's own workSpeedFactor. A pawn can
    /// only ever wear one Avir amulet at a time (shared accessory layer), so there's no stacking
    /// question - the loop just returns the first (only) one found.
    /// </summary>
    public class StatPart_AvirTelekineticFocus : StatPart
    {
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
                return "Avir amulet (telekinetic focus): x" + factor.ToString("0.##");
            }

            return null;
        }

        private static bool TryGetFactor(StatRequest req, out float factor)
        {
            factor = 1f;

            if (!(req.Thing is Pawn pawn) || pawn.apparel == null)
            {
                return false;
            }

            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                Comp_Telekinesis comp = worn[i].TryGetComp<Comp_Telekinesis>();
                if (comp != null)
                {
                    factor = comp.Props.workSpeedFactor;
                    return factor != 1f;
                }
            }

            return false;
        }
    }
}
