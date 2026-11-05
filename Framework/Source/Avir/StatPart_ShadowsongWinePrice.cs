using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Shadowsong Wine needs specific, hand-set prices per quality tier (Legendary 64, Masterwork
    /// 52, Excellent 40, etc. - see AvirQualityUtility.MarketValueFor) rather than vanilla's own
    /// quality-price curve, which would badly overshoot those numbers. This StatPart is injected
    /// onto the global MarketValue StatDef (see AvirHarmony.InjectShadowsongWinePriceStatPart)
    /// and, for this one ThingDef only, replaces whatever value came before it in the chain with
    /// the exact table value - a full override, not an offset or factor.
    /// </summary>
    public class StatPart_ShadowsongWinePrice : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!Applies(req)) return;

            QualityCategory quality = AvirQualityUtility.GetQuality(req.Thing);
            val = AvirQualityUtility.MarketValueFor(quality);
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!Applies(req)) return null;

            QualityCategory quality = AvirQualityUtility.GetQuality(req.Thing);
            return "Shadowsong vintage (" + quality.GetLabel() + "): " + AvirQualityUtility.MarketValueFor(quality).ToString("F0");
        }

        private bool Applies(StatRequest req)
        {
            return req.Thing != null && req.Thing.def.defName == "Avir_ShadowsongWine";
        }
    }
}
