using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>The wind that is seen (Fix253, the author: ours showed only as arcane gusts, or not at all). The map's own
    /// wind - the game's, the very one that drifts our gas (MapComponent_GasDrift: its direction from the weather wind, its
    /// strength from the wind manager) - is drawn wherever it blows in view: thin strands running with it, now and then one
    /// that winds into a curl, bundles of them when it blows hard, and what a strong wind carries - leaves, sand, snow, by
    /// the land. A gust (the weather's, an arcane one) quickens and thickens it where it passes. It breaks at walls and does
    /// not enter under a roof, so the lee of a house is seen as it is.
    /// Only a picture: nothing is ticked, nothing is saved, no things are made - a small pool of strands moved and drawn each
    /// frame, in the camera's view only. With Atmosphere Overhaul loaded its own wind is drawn by it, and this is off.</summary>
    public class MapComponent_AmbientWind : MapComponent
    {
        private struct P { public Vector3 pos; public float age, life, speed, len, wid, alpha, phase, rot, spin; public byte kind; public byte tex; public Color col; }
        private const int Max = 160;
        private readonly P[] ps = new P[Max];
        private int count;
        private float skyGlow = 1f; private int skyAt = -999;

        private const string Dir = "Things/Mote/Avir/Magic/Wind/Ambient/";
        private static Material[] lines, curls, gusts, leaves; private static Material speck; private static bool loaded;
        private static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        /// <summary>Developer mode: the wind's strength forced for the look (-1 - the game's own).</summary>
        public static float DevForce = -1f;
        public static int LastCount;

        public MapComponent_AmbientWind(Map map) : base(map) { }

        private static Material M(string n) => ContentFinder<Texture2D>.Get(Dir + n, false) != null ? MaterialPool.MatFrom(Dir + n, ShaderDatabase.Transparent, Color.white) : null;
        private static void Load()
        {
            loaded = true;
            lines = new[] { M("Line_A"), M("Line_B"), M("Line_C") }.Where(m => m != null).ToArray();
            curls = new[] { M("Curl_A"), M("Curl_B"), M("Curl_C") }.Where(m => m != null).ToArray();
            gusts = new[] { M("Gust_A"), M("Gust_B") }.Where(m => m != null).ToArray();
            leaves = new[] { M("Leaf_A"), M("Leaf_B") }.Where(m => m != null).ToArray();
            speck = M("Speck");
        }

        /// <summary>The wind's strength for the look, 0..1: a breath is not drawn, a gale is drawn thick.</summary>
        private float Strength(out float raw)
        {
            raw = DevForce >= 0f ? DevForce : map.windManager.WindSpeed;
            return Mathf.Clamp01((raw - 0.15f) / 1.6f);
        }

        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map) { count = 0; return; }
            if (!AvirMagic.On || AvirMod.Settings != null && !AvirMod.Settings.windLook || AtmosphereBridge.On && DevForce < 0f) { count = 0; LastCount = 0; return; }
            if (Find.CameraDriver == null || WorldRendererUtility.WorldSelected) return;
            if (!loaded) Load();
            if (lines == null || lines.Length == 0) return;
            float s = Strength(out float raw);
            var weather = MapComp<MapComponent_WeatherWind>.Of(map);
            float angle = weather != null && weather.angle >= 0f ? weather.angle : 90f;
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
            Vector3 side = new Vector3(dir.z, 0f, -dir.x);
            CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(3).ClipInsideMap(map);
            float density = AvirMod.Settings?.windLookDensity ?? 1f;
            int want = raw < 0.15f ? 0 : Mathf.Clamp(Mathf.RoundToInt(view.Area / 60f * (0.2f + 1.3f * s) * density), 3, Max);
            float dt = Mathf.Min(0.1f, Time.deltaTime) * Mathf.Min(2.5f, Find.TickManager.Paused ? 0f : Find.TickManager.TickRateMultiplier);
            int now = Find.TickManager.TicksGame;
            if (now - skyAt > 120 || now < skyAt) { skyAt = now; skyGlow = Mathf.Lerp(0.45f, 1f, map.skyManager.CurSkyGlow); }

            // new strands, a few a frame, where the wind is: out of doors, in sight
            if (dt > 0f)
            {
                var gustsNow = MapComp<MapComponent_MagicWind>.Of(map);
                for (int n = 0; n < 5 && count < want; n++)
                {
                    IntVec3 c = new IntVec3(Rand.Range(view.minX, view.maxX + 1), 0, Rand.Range(view.minZ, view.maxZ + 1));
                    if (!c.InBounds(map) || c.Roofed(map) || c.Fogged(map)) continue;
                    Vector3 at = c.ToVector3Shifted() + new Vector3(Rand.Range(-0.5f, 0.5f), 0f, Rand.Range(-0.5f, 0.5f));
                    ps[count++] = Make(at, s, gustsNow != null && gustsNow.GustCount > 0 ? gustsNow.BoostAt(at) : 1f, c);
                }
            }

            float y = AltitudeLayer.MoteOverhead.AltitudeFor();
            Quaternion along = Quaternion.AngleAxis(angle, Vector3.up);
            for (int i = count - 1; i >= 0; i--)
            {
                P p = ps[i];
                p.age += dt;
                if (p.age >= p.life) { ps[i] = ps[--count]; continue; }
                p.pos += dir * (p.speed * dt) + side * (Mathf.Sin(p.age * 2.6f + p.phase) * 0.35f * dt);
                // it breaks at a wall and does not go in under a roof: the strand fades there
                if (dt > 0f && (i + now) % 4 == 0)
                {
                    IntVec3 head = (p.pos + dir * (p.len * 0.45f)).ToIntVec3();
                    if (!head.InBounds(map) || head.Roofed(map) || head.Filled(map)) p.life = Mathf.Min(p.life, p.age + 0.16f);
                }
                ps[i] = p;
                float fade = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(p.age / p.life)), 0.8f);
                Color col = p.col; col.a = p.alpha * fade * skyGlow;
                if (col.a < 0.01f) continue;
                block.SetColor(ShaderPropertyIDs.Color, col);
                Vector3 pos = p.pos; pos.y = y;
                Material m; Quaternion rot; Vector3 scale;
                switch (p.kind)
                {
                    case 1: m = curls[p.tex % curls.Length]; rot = along; scale = new Vector3(p.wid, 1f, p.len); break;
                    case 2: m = gusts[p.tex % gusts.Length]; rot = along; scale = new Vector3(p.wid, 1f, p.len); break;
                    case 3: m = leaves[p.tex % leaves.Length]; p.rot += p.spin * dt; ps[i] = p; rot = Quaternion.AngleAxis(p.rot, Vector3.up); scale = new Vector3(p.wid, 1f, p.wid); break;
                    case 4: m = speck; rot = Quaternion.identity; scale = new Vector3(p.wid, 1f, p.wid); break;
                    default: m = lines[p.tex % lines.Length]; rot = along; scale = new Vector3(p.wid, 1f, p.len); break;
                }
                if (m == null) continue;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, rot, scale), m, 0, null, 0, block);
            }
            LastCount = count;
        }

        private P Make(Vector3 at, float s, float boost, IntVec3 c)
        {
            var p = new P { pos = at, phase = Rand.Range(0f, 6.28f), tex = (byte)Rand.Range(0, 6) };
            float r = Rand.Value;
            bool hard = s > 0.5f || boost > 1.3f;
            // what the land gives the wind to carry
            string biome = map.Biome?.defName ?? "", wdef = map.weatherManager.curWeather?.defName ?? "";
            bool snowy = map.snowGrid.GetDepth(c) > 0.3f || wdef.Contains("Snow") || wdef.Contains("Blizzard");
            bool sandy = biome.Contains("Desert") || biome.Contains("Arid") || wdef.Contains("Sand") || wdef.Contains("Dust");
            bool rainy = wdef.Contains("Rain") || wdef.Contains("Storm") || wdef.Contains("Thunder");
            // on bright ground a white strand is lost: over snow the wind is drawn as its blue shadow, over sand - warm white
            Color air = snowy ? new Color(0.47f, 0.57f, 0.75f) : sandy ? new Color(1f, 0.98f, 0.92f) : rainy ? new Color(0.82f, 0.89f, 1f) : Color.white;
            float carry = (s > 0.3f ? 0.14f + 0.26f * s : 0f) * (boost > 1.3f ? 1.5f : 1f);
            if (r < carry && (leaves.Length > 0 || speck != null))
            {
                bool leaf = !snowy && !sandy && leaves.Length > 0 && map.Biome != null && map.Biome.plantDensity > 0.1f;
                p.kind = (byte)(leaf ? 3 : 4);
                p.wid = leaf ? Rand.Range(0.32f, 0.48f) : Rand.Range(0.16f, 0.3f);
                p.len = p.wid;
                p.speed = (4.5f + 7f * s) * Rand.Range(0.85f, 1.3f) * boost;
                p.life = Rand.Range(1.4f, 2.6f);
                p.alpha = leaf ? Rand.Range(0.75f, 0.95f) : Rand.Range(0.5f, 0.85f);
                p.spin = Rand.Range(-420f, 420f); p.rot = Rand.Range(0f, 360f);
                p.col = leaf ? Leaf() : snowy ? new Color(0.59f, 0.67f, 0.8f) : sandy ? new Color(0.96f, 0.89f, 0.7f) : new Color(0.75f, 0.72f, 0.66f);
                return p;
            }
            r = Rand.Value;
            if (hard && r < 0.28f && gusts.Length > 0)
            {
                p.kind = 2; p.len = Rand.Range(4.5f, 7f) * (0.8f + 0.5f * s); p.wid = p.len * 0.375f * Rand.Range(0.8f, 1.1f);
                p.speed = (5f + 7f * s) * Rand.Range(0.9f, 1.2f) * boost; p.life = Rand.Range(0.9f, 1.6f);
                p.alpha = (0.26f + 0.4f * s) * Rand.Range(0.8f, 1.2f);
            }
            else if (r < 0.46f && curls.Length > 0)
            {
                p.kind = 1; p.len = Rand.Range(3f, 5f) * (0.75f + 0.4f * s); p.wid = p.len * 0.375f;
                p.speed = (2.2f + 4f * s) * Rand.Range(0.8f, 1.15f) * boost; p.life = Rand.Range(1.5f, 2.6f);
                p.alpha = (0.26f + 0.4f * s) * Rand.Range(0.8f, 1.2f);
            }
            else
            {
                p.kind = 0; p.len = Rand.Range(3f, 6f) * (0.7f + 0.6f * s); p.wid = p.len * 0.1875f * Rand.Range(0.8f, 1.15f);
                p.speed = (3f + 6f * s) * Rand.Range(0.8f, 1.25f) * boost; p.life = Rand.Range(1.1f, 2.2f);
                p.alpha = (0.22f + 0.42f * s) * Rand.Range(0.75f, 1.2f);
            }
            if (boost > 1.3f) p.alpha = Mathf.Min(0.9f, p.alpha * 1.4f);
            p.col = air;
            return p;
        }

        private static Color Leaf()
        {
            float r = Rand.Value;
            return r < 0.4f ? new Color(0.42f, 0.55f, 0.24f) : r < 0.7f ? new Color(0.62f, 0.56f, 0.22f) : new Color(0.52f, 0.36f, 0.2f);
        }

        [DebugAction("Magic", "Wind look: force strength (calm / breeze / wind / gale / off)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevForceCycle()
        {
            DevForce = DevForce < 0f ? 0.1f : DevForce < 0.2f ? 0.5f : DevForce < 0.6f ? 1.1f : DevForce < 1.2f ? 2.2f : -1f;
            Messages.Message(DevForce < 0f ? "Wind look: the game's own wind again." : "Wind look: forced to " + DevForce.ToString("0.0") + " (the game's runs 0 to about 2).", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Magic", "Wind look: readout (log)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DevReadout()
        {
            Map m = Find.CurrentMap; if (m == null) return;
            var w = m.GetComponent<MapComponent_WeatherWind>();
            Log.Message("[Avir] wind look: the game's wind " + m.windManager.WindSpeed.ToString("0.00") + (DevForce >= 0f ? " (forced " + DevForce.ToString("0.0") + ")" : "") + ", blowing towards " + (w?.angle ?? -1f).ToString("0") + " degrees; strands drawn now: " + LastCount
                + "; setting " + (AvirMod.Settings?.windLook == true ? "on, x" + AvirMod.Settings.windLookDensity.ToString("0.0") : "off") + (AtmosphereBridge.On ? "; Atmosphere Overhaul is loaded - its own wind is drawn, ours is off" : ""));
        }
    }
}
