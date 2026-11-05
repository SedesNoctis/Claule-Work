using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    [StaticConstructorOnStartup]
    public static class FlowTex
    {
        public static readonly Texture2D Frame = ContentFinder<Texture2D>.Get("UI/Ritualistics/FlowGaugeFrame", false);
        public static readonly Texture2D Fill = ContentFinder<Texture2D>.Get("UI/Ritualistics/FlowGaugeFill", false);
        public static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("UI/Ritualistics/FlowIcon", false);
        public static readonly Texture2D[] Saturation =
        {
            ContentFinder<Texture2D>.Get("UI/Ritualistics/FlowSaturation_1", false),
            ContentFinder<Texture2D>.Get("UI/Ritualistics/FlowSaturation_2", false),
            ContentFinder<Texture2D>.Get("UI/Ritualistics/FlowSaturation_3", false),
        };
        public static readonly Texture2D Dome = ContentFinder<Texture2D>.Get("Things/Mote/Avir/BarrierDome", false);
        public static readonly Texture2D Vortex = ContentFinder<Texture2D>.Get("Things/Mote/Avir/Vortex", false);
        public static readonly Texture2D Thread = ContentFinder<Texture2D>.Get("Things/Mote/Avir/Thread", false);
        public static readonly Texture2D Engraved = ContentFinder<Texture2D>.Get("Things/Mote/Avir/EngravedRing", false);
    }

    /// <summary>The ritual effects (flecks in Flecks_Avir_Ritual.xml).</summary>
    public static class RitualFx
    {
        private static FleckDef Def(string n) => DefDatabase<FleckDef>.GetNamedSilentFail(n);

        public static void Spawn(string def, Vector3 at, Map map, float size, float rotation = -1f, Color? tint = null)
        {
            FleckDef d = Def(def);
            if (d == null || map == null) return;
            FleckCreationData data = FleckMaker.GetDataStatic(at, map, d, size);
            data.rotation = rotation >= 0f ? rotation : Rand.Range(0f, 360f);
            if (tint.HasValue) data.instanceColor = tint.Value;
            map.flecks.CreateFleck(data);
        }

        public static void Ripple(Vector3 at, Map map, float size = 3f) => Spawn("Avir_Fleck_BarrierRipple", at, map, size);
        public static void Blast(Vector3 at, Map map, float radius) => Spawn("Avir_Fleck_ReleaseBlast", at, map, radius * 2f);
        public static void Wave(Vector3 at, Map map, float radius) => Spawn("Avir_Fleck_LiftWave", at, map, radius * 1.6f);
        public static void Mote(Vector3 at, Map map, float size = 0.6f, Color? tint = null) => Spawn("Avir_Fleck_FlowMote", at, map, size, 0f, tint);
    }

    /// <summary>The map's external flow at a glance: a violet stream in a frame with four moon phases at the
    /// thresholds; it deepens and burns brighter as the flow climbs toward a cataclysm.</summary>
    public static class FlowGauge
    {
        private static readonly Color Calm = new Color(0.55f, 0.45f, 0.70f);
        private static readonly Color Peak = new Color(0.78f, 0.18f, 1f);

        /// <summary>Where the stream runs inside the frame texture (fractions of its size).</summary>
        private const float InX0 = 0.072f, InX1 = 0.928f, InY0 = 0.363f, InY1 = 0.855f;
        /// <summary>Frame texture width / height.</summary>
        private const float FrameAspect = 1916f / 193f;

        /// <summary>0 ... 1 along the stream, the four thresholds under the four moons of the frame.</summary>
        public static float Level(float flow)
        {
            // each threshold sits under its moon on the frame (new, crescent, half, full)
            float[] at = { 0f, MapComponent_ExternalFlow.Calm, MapComponent_ExternalFlow.Restless, MapComponent_ExternalFlow.Stormy, MapComponent_ExternalFlow.Tempest, MapComponent_ExternalFlow.Tempest * 2f };
            float[] lv = { 0f, 0.152f, 0.372f, 0.616f, 0.843f, 1f };
            for (int i = 1; i < at.Length; i++)
                if (flow <= at[i]) return Mathf.Lerp(lv[i - 1], lv[i], (flow - at[i - 1]) / (at[i] - at[i - 1]));
            return 1f;
        }

        public static Color ColorOf(float flow)
        {
            int tier = MapComponent_ExternalFlow.TierIndex(flow);
            Color c = Color.Lerp(Calm, Peak, tier / 4f);
            if (tier >= 3) c = Color.Lerp(c, Color.white, 0.15f * (0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * (tier == 4 ? 6f : 3f))));
            return c;
        }

        /// <summary>Label on top, the framed stream under it.</summary>
        public static void Draw(Rect r, Map map)
        {
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(map);
            if (ext == null) return;
            float f = ext.Total;
            int tier = MapComponent_ExternalFlow.TierIndex(f);
            Color c = ColorOf(f);
            // the label, with the flow's own sign
            float lh = 22f;
            if (FlowTex.Icon != null) { GUI.color = Color.white; GUI.DrawTexture(new Rect(r.x, r.y, lh, lh), FlowTex.Icon, ScaleMode.ScaleToFit); }
            GUI.color = new Color(c.r * 0.55f, c.g * 0.35f, c.b * 0.6f);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(r.x + lh + 4f, r.y + 1f, r.width - lh - 4f, lh), "External stream: " + f.ToString("0") + "% - " + MapComponent_ExternalFlow.Tier(f));
            // the frame keeps its proportions
            float fw = Mathf.Min(r.width, (r.height - lh) * FrameAspect), fh = fw / FrameAspect;
            Rect frame = new Rect(r.x + (r.width - fw) / 2f, r.y + lh, fw, fh);
            Rect inner = new Rect(frame.x + fw * InX0, frame.y + fh * InY0, fw * (InX1 - InX0), fh * (InY1 - InY0));
            float level = Level(f);
            if (FlowTex.Fill != null && level > 0f)
            {
                GUI.BeginGroup(new Rect(inner.x, inner.y, inner.width * level, inner.height));
                float pulse = tier >= 3 ? 0.15f * Mathf.Sin(Time.realtimeSinceStartup * (tier == 4 ? 6f : 3f)) : 0f;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(0.65f + 0.1f * tier + pulse));
                // the stream is taller than the bar: its wisps spill a little past the edges
                float sh = inner.height * 1.9f;
                GUI.DrawTexture(new Rect(0f, (inner.height - sh) / 2f, inner.width, sh), FlowTex.Fill, ScaleMode.StretchToFill);
                GUI.EndGroup();
            }
            else if (FlowTex.Fill == null)
                Widgets.DrawBoxSolid(new Rect(inner.x, inner.y, inner.width * level, inner.height), new Color(c.r, c.g, c.b, 0.9f));
            GUI.color = Color.white;
            if (FlowTex.Frame != null) GUI.DrawTexture(frame, FlowTex.Frame, ScaleMode.StretchToFill);
            TooltipHandler.TipRegion(r, "The energy of everything on this map: colonists' psychic sensitivity, psyfocus, charged artifacts and batteries, essentium, crystals, pylons, storms, drones, running rituals and barriers.\n\n"
                                        + "The four moons mark the tiers: calm below 375 000%; restless to 750 000 (storms, blizzards); stormy to 1 500 000 (cold snaps, lightning near energy); "
                                        + "tempest to 3 750 000 (lightning on artifact buildings, artifacts drain, sensitive minds suffer); then cataclysm.\n\nFocus runes draw on it: more energy, more danger.");
            GUI.color = Color.white;
        }
    }
}
