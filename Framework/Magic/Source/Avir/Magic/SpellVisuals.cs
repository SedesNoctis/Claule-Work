using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>What a spell left on a body, seen: drops on the soaked, wind round a weapon of air, dust at the feet of
    /// the heavy, sparks round a charged weapon... One comp for all of them, set in each hediff's def.</summary>
    public class HediffCompProperties_SpellMotes : HediffCompProperties
    {
        public string kind = "dust";          // dust, glow, sparks, splash, heat, lightning, stars
        public Color color = new Color(1f, 1f, 1f, 0.5f);
        public int interval = 60;
        public float scale = 1f;
        public float height = 0.2f;           // above the feet: 0 - at the feet, 0.5 - the head
        public HediffCompProperties_SpellMotes() { compClass = typeof(HediffComp_SpellMotes); }
    }

    public class HediffComp_SpellMotes : HediffComp
    {
        public HediffCompProperties_SpellMotes Props => (HediffCompProperties_SpellMotes)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn p = parent.pawn;
            if (p == null || !p.Spawned || p.Map != Find.CurrentMap || !p.IsHashIntervalTick(Mathf.Max(5, Props.interval))) return;
            Map map = p.Map;
            Vector3 at = p.DrawPos + new Vector3(Rand.Range(-0.25f, 0.25f), 0f, Props.height - 0.3f + Rand.Range(-0.1f, 0.1f));
            switch (Props.kind)
            {
                case "glow": FleckMaker.ThrowFireGlow(at, map, 0.6f * Props.scale); break;
                case "sparks": FleckMaker.ThrowMicroSparks(at, map); break;
                case "splash": FleckMaker.Static(at.ToIntVec3(), map, FleckDefOf.WaterSplash, 0.8f * Props.scale); break;
                case "heat": FleckMaker.ThrowHeatGlow(p.Position, map, 0.8f * Props.scale); break;
                case "lightning": FleckMaker.ThrowLightningGlow(at, map, 0.6f * Props.scale); break;
                case "stars":
                    // circling over the head
                    float a = (Find.TickManager.TicksGame % 120) / 120f * Mathf.PI * 2f;
                    FleckMaker.ThrowDustPuffThick(p.DrawPos + new Vector3(Mathf.Cos(a) * 0.3f, 0f, 0.45f + Mathf.Sin(a) * 0.1f), map, 0.5f * Props.scale, Props.color);
                    break;
                default: FleckMaker.ThrowDustPuffThick(at, map, 0.8f * Props.scale, Props.color); break;
            }
        }
    }
}
