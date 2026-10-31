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

namespace Avir.Magic
{
    /// <summary>The drawn effects of the elements - a bolt between two points, a strike, a fissure, a ring of haze, a
    /// splash of mud, a cloud or a devil of dust: each a sprite laid on the map for a moment, fading as it goes, some of
    /// them turning. One drawer for all of them.</summary>
    public class MapComponent_SpellSprites : Verse.MapComponent
    {
        private class Sprite { public UnityEngine.Material mat; public UnityEngine.Vector3 at; public float w, h, angle, spin; public int born, life; }
        private readonly System.Collections.Generic.List<Sprite> sprites = new System.Collections.Generic.List<Sprite>();
        private static readonly System.Collections.Generic.Dictionary<string, UnityEngine.Material> mats = new System.Collections.Generic.Dictionary<string, UnityEngine.Material>();
        private static readonly UnityEngine.MaterialPropertyBlock block = new UnityEngine.MaterialPropertyBlock();

        public MapComponent_SpellSprites(Verse.Map map) : base(map) { }

        private static UnityEngine.Material Mat(string path)
        {
            if (mats.TryGetValue(path, out var m)) return m;
            m = Verse.ContentFinder<UnityEngine.Texture2D>.Get("Things/Mote/Avir/Magic/" + path, false) != null
                ? Verse.MaterialPool.MatFrom("Things/Mote/Avir/Magic/" + path, Verse.ShaderDatabase.MoteGlow) : null;
            mats[path] = m;
            return m;
        }

        /// <summary>A sprite at a point: its size in cells, how long it lasts (ticks), its angle and its turning.</summary>
        public static void Put(Verse.Map map, string path, UnityEngine.Vector3 at, float size, int life, float angle = -1f, float spin = 0f, float aspect = 1f)
        {
            var c = map?.GetComponent<MapComponent_SpellSprites>();
            UnityEngine.Material m = Mat(path);
            if (c == null || m == null) return;
            c.sprites.Add(new Sprite { mat = m, at = at, w = size, h = size / aspect, angle = angle < 0f ? Verse.Rand.Range(0f, 360f) : angle, spin = spin, born = Verse.Find.TickManager.TicksGame, life = life });
        }

        /// <summary>A bolt stretched from one point to another.</summary>
        public static void Bolt(Verse.Map map, UnityEngine.Vector3 from, UnityEngine.Vector3 to, int life = 18)
        {
            UnityEngine.Vector3 d = to - from; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.2f) return;
            Put(map, "Storm/Bolt_Line", (from + to) / 2f, len, life, d.AngleFlat() - 90f, 0f, len / UnityEngine.Mathf.Max(0.6f, len * 0.22f));
        }

        public override void MapComponentUpdate()
        {
            if (sprites.Count == 0 || Verse.Find.CurrentMap != map) return;
            int now = Verse.Find.TickManager.TicksGame;
            sprites.RemoveAll(s => now - s.born > s.life);
            float y = Verse.AltitudeLayer.MoteOverhead.AltitudeFor();
            foreach (Sprite s in sprites)
            {
                float t = (now - s.born) / (float)UnityEngine.Mathf.Max(1, s.life);
                float alpha = t < 0.15f ? t / 0.15f : t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f;
                block.SetColor(Verse.ShaderPropertyIDs.Color, new UnityEngine.Color(1f, 1f, 1f, alpha));
                UnityEngine.Vector3 at = new UnityEngine.Vector3(s.at.x, y, s.at.z);
                var q = UnityEngine.Quaternion.AngleAxis(s.angle + s.spin * (now - s.born), UnityEngine.Vector3.up);
                UnityEngine.Graphics.DrawMesh(Verse.MeshPool.plane10, UnityEngine.Matrix4x4.TRS(at, q, new UnityEngine.Vector3(s.w, 1f, s.h)), s.mat, 0, null, 0, block);
            }
        }
    }
}
