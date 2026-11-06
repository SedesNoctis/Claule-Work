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
        private class Sprite { public UnityEngine.Material mat; public UnityEngine.Vector3 at; public float w, h, angle, spin, grow; public int born, life; }
        private readonly System.Collections.Generic.List<Sprite> sprites = new System.Collections.Generic.List<Sprite>();
        private static readonly System.Collections.Generic.Dictionary<string, UnityEngine.Material> mats = new System.Collections.Generic.Dictionary<string, UnityEngine.Material>();
        private static readonly UnityEngine.MaterialPropertyBlock block = new UnityEngine.MaterialPropertyBlock();

        public MapComponent_SpellSprites(Verse.Map map) : base(map) { }

        private static UnityEngine.Material Mat(string path)
        {
            if (mats.TryGetValue(path, out var m)) return m;
            m = Verse.ContentFinder<UnityEngine.Texture2D>.Get("Things/Mote/Avir/Magic/" + path, false) != null
                ? Verse.MaterialPool.MatFrom("Things/Mote/Avir/Magic/" + path, Painted(path) ? Verse.ShaderDatabase.Transparent : Verse.ShaderDatabase.MoteGlow) : null;
            mats[path] = m;
            return m;
        }

        /// <summary>The sprites redrawn as pictures with their own light and alpha (Fix136): drawn as they are, so their
        /// dark parts show; the older ones stay glow.</summary>
        private static bool Painted(string path) => path.StartsWith("Life/") || path.StartsWith("Second/") || path.StartsWith("Storm/Bolt_Fork") || path.StartsWith("Storm/Bolt_Line") || path.StartsWith("Storm/Strike") || path.StartsWith("Haze/Shimmer");

        /// <summary>A sprite at a point: its size in cells, how long it lasts (ticks), its angle and its turning.</summary>
        /// <summary>A sprite in a colour of its own (Fix166): a Life picture recoloured for a meeting.</summary>
        public static void PutTinted(Verse.Map map, string path, UnityEngine.Color tint, UnityEngine.Vector3 at, float size, int life, float angle = -1f, float spin = 0f, float aspect = 1f)
        {
            var c = map?.GetComponent<MapComponent_SpellSprites>();
            string key = path + "#" + UnityEngine.ColorUtility.ToHtmlStringRGBA(tint);
            if (!mats.TryGetValue(key, out var m))
            {
                m = Verse.ContentFinder<UnityEngine.Texture2D>.Get("Things/Mote/Avir/Magic/" + path, false) != null
                    ? Verse.MaterialPool.MatFrom("Things/Mote/Avir/Magic/" + path, Verse.ShaderDatabase.Transparent, tint) : null;
                mats[key] = m;
            }
            if (c == null || m == null) return;
            c.sprites.Add(new Sprite { mat = m, at = at, w = size, h = size / aspect, angle = angle < 0f ? Verse.Rand.Range(0f, 360f) : angle, spin = spin, born = Verse.Find.TickManager.TicksGame, life = life });
        }

        public static void Put(Verse.Map map, string path, UnityEngine.Vector3 at, float size, int life, float angle = -1f, float spin = 0f, float aspect = 1f)
        {
            var c = map?.GetComponent<MapComponent_SpellSprites>();
            UnityEngine.Material m = Mat(path);
            if (c == null || m == null) return;
            c.sprites.Add(new Sprite { mat = m, at = at, w = size, h = size / aspect, angle = angle < 0f ? Verse.Rand.Range(0f, 360f) : angle, spin = spin, born = Verse.Find.TickManager.TicksGame, life = life });
        }

        /// <summary>A sprite that spreads as it fades (Fix180): a ripple on water.</summary>
        public static void PutGrowing(Verse.Map map, string path, UnityEngine.Vector3 at, float size, int life, float grow)
        {
            var c = map?.GetComponent<MapComponent_SpellSprites>();
            UnityEngine.Material m = Mat(path);
            if (c == null || m == null) return;
            c.sprites.Add(new Sprite { mat = m, at = at, w = size, h = size, angle = Verse.Rand.Range(0f, 360f), spin = 0f, grow = grow, born = Verse.Find.TickManager.TicksGame, life = life });
        }

        /// <summary>A coloured sprite that spreads as it fades (Fix183: thunder's violet rings).</summary>
        public static void PutTintedGrowing(Verse.Map map, string path, UnityEngine.Color tint, UnityEngine.Vector3 at, float size, int life, float grow)
        {
            var c = map?.GetComponent<MapComponent_SpellSprites>();
            string key = path + "#" + UnityEngine.ColorUtility.ToHtmlStringRGBA(tint) + "#glow";
            if (!mats.TryGetValue(key, out var m))
            {
                m = Verse.ContentFinder<UnityEngine.Texture2D>.Get("Things/Mote/Avir/Magic/" + path, false) != null
                    ? Verse.MaterialPool.MatFrom("Things/Mote/Avir/Magic/" + path, Verse.ShaderDatabase.MoteGlow, tint) : null;
                mats[key] = m;
            }
            if (c == null || m == null) return;
            c.sprites.Add(new Sprite { mat = m, at = at, w = size, h = size, angle = Verse.Rand.Range(0f, 360f), spin = 0f, grow = grow, born = Verse.Find.TickManager.TicksGame, life = life });
        }

        /// <summary>A bolt stretched from one point to another.</summary>
        public static void Bolt(Verse.Map map, UnityEngine.Vector3 from, UnityEngine.Vector3 to, int life = 18)
        {
            UnityEngine.Vector3 d = to - from; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.2f) return;
            Put(map, "Storm/Bolt_Line", (from + to) / 2f, len, life, d.AngleFlat() - 90f, 0f, len / UnityEngine.Mathf.Max(0.6f, len * 0.22f));
        }

        /// <summary>A living vein of Life stretched from one point to another (a beam of Life, taking or giving).</summary>
        public static void Vein(Verse.Map map, UnityEngine.Vector3 from, UnityEngine.Vector3 to, int life = 40)
        {
            UnityEngine.Vector3 d = to - from; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.3f) return;
            Put(map, "Life/Vein", (from + to) / 2f, len, life, d.AngleFlat() - 90f, 0f, len / UnityEngine.Mathf.Max(0.6f, len * 0.25f));
        }

        /// <summary>A curving stream laid along a line in short pieces (Fix158: the blood stream is not a thin strip).</summary>
        public static void Stream(Verse.Map map, string path, UnityEngine.Vector3 from, UnityEngine.Vector3 to, int life = 50)
        {
            UnityEngine.Vector3 d = to - from; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.5f) return;
            int n = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.CeilToInt(len / 1.4f));
            for (int i = 0; i < n; i++) Put(map, path, from + d * ((i + 0.5f) / n), 1.9f, life + 3 * i, d.AngleFlat() - 90f, 0f, 1.32f);
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
                float k = 1f + s.grow * t;   // a ripple spreads as it fades
                UnityEngine.Graphics.DrawMesh(Verse.MeshPool.plane10, UnityEngine.Matrix4x4.TRS(at, q, new UnityEngine.Vector3(s.w * k, 1f, s.h * k)), s.mat, 0, null, 0, block);
            }
        }
    }
}
