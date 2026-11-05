using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    /// <summary>
    /// An artifact is worth what it is made of and how well: the def's base value times its quality (the rim's
    /// and the heart's together, on the game's own quality scale) times its metals (a precious rim or heart costs
    /// more). A legendary rim with a masterwork essentium heart is no longer priced like a plain setting.
    /// </summary>
    public class StatPart_ArtifactValue : StatPart
    {
        public static float Factor(Thing t, out string why)
        {
            why = null;
            CompAvirAmulet am = t?.TryGetComp<CompAvirAmulet>();
            if (am != null && am.necklaceMetal != null)
            {
                // the amulet's quality is already priced by the game (its quality comp follows necklace and core); here only the metals
                float mm = 1f + Metal(am.necklaceMetal) + (am.coreMetal != null ? Metal(am.coreMetal) : 0f);
                why = "metals x" + mm.ToString("0.##");
                return mm;
            }
            CompArtifact c = t?.TryGetComp<CompArtifact>();
            if (c == null || c.rimMetal == null) return 1f;
            float q = QualityValue(c.settingQuality);
            if (c.heartMetal != null) q = (q + QualityValue(c.heartQuality)) / 2f;
            float m = 1f + Metal(c.rimMetal) + (c.heartMetal != null ? Metal(c.heartMetal) : 0f);
            why = "Quality x" + q.ToString("0.##") + ", metals x" + m.ToString("0.##");
            return q * m;
        }

        /// <summary>The game's own market-value factors for quality.</summary>
        private static float QualityValue(QualityCategory q)
        {
            switch (q)
            {
                case QualityCategory.Awful: return 0.5f;
                case QualityCategory.Poor: return 0.75f;
                case QualityCategory.Good: return 1.25f;
                case QualityCategory.Excellent: return 1.5f;
                case QualityCategory.Masterwork: return 2.5f;
                case QualityCategory.Legendary: return 5f;
                default: return 1f;
            }
        }

        /// <summary>What a metal adds: 6% of its own unit price (silver +6%, gold +60%, essentium about +140%).</summary>
        private static float Metal(ArtifactMetalDef m) => m?.material == null ? 0f : 0.06f * m.material.BaseMarketValue;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (req.HasThing) val *= Factor(req.Thing, out _);
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!req.HasThing) return null;
            float f = Factor(req.Thing, out string why);
            return f == 1f || why == null ? null : "Artifact: " + why + " = x" + f.ToString("0.##");
        }
    }

    [StaticConstructorOnStartup]
    public static class ArtifactValueInstaller
    {
        static ArtifactValueInstaller()
        {
            StatDef s = StatDefOf.MarketValue;
            if (s.parts == null) s.parts = new List<StatPart>();
            if (!s.parts.Any(p => p is StatPart_ArtifactValue)) s.parts.Add(new StatPart_ArtifactValue { parentStat = s });
        }
    }

    /// <summary>Finished artifacts for an Avir artificer caravan: a rim and a heart from the lists, a quality of
    /// normal to masterwork (rarely legendary), undeveloped and charged.</summary>
    public class StockGenerator_AvirArtifacts : StockGenerator
    {
        public List<ArtifactMetalDef> rims = new List<ArtifactMetalDef>();
        public List<ArtifactMetalDef> hearts = new List<ArtifactMetalDef>();

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            int n = countRange.RandomInRange;
            for (int i = 0; i < n; i++)
            {
                Thing t = ThingMaker.MakeThing(ArtifactoricsDefOf.Avir_Artifact);
                CompArtifact c = t.TryGetComp<CompArtifact>();
                if (c == null) continue;
                c.rimMetal = rims.Where(m => m != null && m.allowedAsRim).RandomElementWithFallback()
                             ?? ArtifactoricsDatabase.Metals.Where(m => m.allowedAsRim).RandomElementWithFallback();
                c.heartMetal = hearts.Where(m => m != null && m.allowedAsHeart).RandomElementWithFallback()
                               ?? ArtifactoricsDatabase.Metals.Where(m => m.allowedAsHeart).RandomElementWithFallback();
                c.settingQuality = Roll();
                c.heartQuality = Roll();
                yield return t;
            }
        }

        private static QualityCategory Roll()
        {
            float r = Rand.Value;
            return r < 0.35f ? QualityCategory.Normal : r < 0.65f ? QualityCategory.Good : r < 0.87f ? QualityCategory.Excellent
                 : r < 0.98f ? QualityCategory.Masterwork : QualityCategory.Legendary;
        }

        public override bool HandlesThingDef(ThingDef thingDef) => thingDef == ArtifactoricsDefOf.Avir_Artifact || thingDef == ArtifactoricsDefOf.Avir_ArtifactSetting;
    }
}
