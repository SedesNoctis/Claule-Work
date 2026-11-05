using Avir.Artifactorics;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>The ritual windows soak up the map's flow: from tier to tier the parchment, the ink, the binding,
    /// the gold and the buttons change, and veins of energy creep over the page. A window sets the tier once a
    /// frame (Begin); textures that are not drawn yet fall back to the calm ones, colours shift anyway.
    /// Textures: UI/Ritualistics/Flow/{Parchment,Veins,Corner,Button_Normal,Button_Hover,Button_Pressed}_T1..T4.</summary>
    [StaticConstructorOnStartup]
    public static class FlowStyle
    {
        /// <summary>0 calm ... 4 cataclysm.</summary>
        public static int Tier;
        /// <summary>A slow breath, stronger at higher tiers (0..1).</summary>
        public static float Breath => Tier == 0 ? 0f : 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * (0.7f + 0.6f * Tier));

        private static readonly Texture2D[] parchment = Load("Parchment"), veins = Load("Veins"), corner = Load("Corner"),
            bNormal = Load("Button_Normal"), bHover = Load("Button_Hover"), bPressed = Load("Button_Pressed");

        private static Texture2D[] Load(string name)
        {
            var a = new Texture2D[5];
            for (int i = 1; i <= 4; i++) a[i] = ContentFinder<Texture2D>.Get("UI/Ritualistics/Flow/" + name + "_T" + i, false);
            return a;
        }

        public static void Begin(Map map)
        {
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(map ?? Find.CurrentMap);
            Tier = ext == null ? 0 : MapComponent_ExternalFlow.TierIndex(ext.Total);
            Scrollbar(true);
            Inside = true;
        }

        public static void End() { Tier = 0; Scrollbar(false); Inside = false; }

        /// <summary>A ritual window is being drawn (its words go in italics, if the setting asks).</summary>
        public static bool Inside;

        // the ritual windows' own scrollbar, where its art is present: a track and a thumb, their ends kept, the middle stretched
        private static Texture2D track, thumb, keepTrack, keepThumb;
        private static RectOffset keepTrackB, keepThumbB;
        private static bool scrollLooked, swapped;

        private static void Scrollbar(bool on)
        {
            if (!scrollLooked)
            {
                scrollLooked = true;
                track = ContentFinder<Texture2D>.Get("UI/Ritualistics/ScrollTrack", false);
                thumb = ContentFinder<Texture2D>.Get("UI/Ritualistics/ScrollThumb", false);
            }
            if (track == null || thumb == null) return;
            GUIStyle tr = GUI.skin.verticalScrollbar, th = GUI.skin.verticalScrollbarThumb;
            if (on && !swapped)
            {
                keepTrack = tr.normal.background; keepThumb = th.normal.background; keepTrackB = tr.border; keepThumbB = th.border;
                tr.normal.background = track; tr.border = new RectOffset(0, 0, 18, 18);
                th.normal.background = thumb; th.border = new RectOffset(0, 0, 30, 30);
                swapped = true;
            }
            else if (!on && swapped)
            {
                tr.normal.background = keepTrack; tr.border = keepTrackB;
                th.normal.background = keepThumb; th.border = keepThumbB;
                swapped = false;
            }
        }

        private static float T => Tier / 4f;
        private static readonly Color Violet = new Color(0.42f, 0.12f, 0.62f);
        private static readonly Color Glow = new Color(0.78f, 0.35f, 1f);

        /// <summary>At a cataclysm the page burns dark: the ink turns to light.</summary>
        public static bool Light => Tier >= 4;
        public static Texture2D Pick(Texture2D ink, Texture2D glow) => Light && glow != null ? glow : ink;

        private static readonly Color[] ink = { new Color(0.03f, 0.018f, 0.008f), new Color(0.06f, 0.02f, 0.06f), new Color(0.10f, 0.02f, 0.14f), new Color(0.07f, 0f, 0.11f), new Color(1f, 0.90f, 1f) };
        private static readonly Color[] inkSoft = { new Color(0.10f, 0.06f, 0.025f), new Color(0.14f, 0.07f, 0.12f), new Color(0.18f, 0.06f, 0.24f), new Color(0.13f, 0.02f, 0.19f), new Color(0.94f, 0.80f, 1f) };
        private static readonly Color[] inkGrey = { new Color(0.22f, 0.17f, 0.12f), new Color(0.26f, 0.18f, 0.24f), new Color(0.30f, 0.20f, 0.36f), new Color(0.26f, 0.16f, 0.32f), new Color(0.72f, 0.60f, 0.80f) };

        public static Color Ink => ink[Tier];
        public static Color InkSoft => inkSoft[Tier];
        public static Color InkGrey => inkGrey[Tier];
        public static Color Gold => Color.Lerp(new Color(0.70f, 0.50f, 0.12f), Color.Lerp(Glow, Color.white, 0.25f * Breath), T);
        /// <summary>Button labels: dark ink, light on the burning plum button of a cataclysm.</summary>
        public static Color ButtonText => Light && bNormal[4] != null ? new Color(1f, 0.9f, 1f) : Ink;
        public static Color Binding => Color.Lerp(new Color(0.17f, 0.10f, 0.05f), new Color(0.14f, 0.04f, 0.22f), T);

        /// <summary>The page of this tier, drawn whole (null - the calm parchment, tiled).</summary>
        public static Texture2D Page => parchment[Tier];
        public static Texture2D Veins => veins[Tier] ?? (Tier >= 2 ? veins[3] : null);
        public static Texture2D Corner => corner[Tier];
        public static Texture2D ButtonNormal => bNormal[Tier] ?? ArtifactoricsTex.ButtonNormal;
        public static Texture2D ButtonHover => bHover[Tier] ?? ArtifactoricsTex.ButtonHover;
        public static Texture2D ButtonPressed => bPressed[Tier] ?? ArtifactoricsTex.ButtonPressed;

        /// <summary>A dark plate behind a glyph from the stormy tier on, so it reads on the burning page.</summary>
        public static void Backing(Rect glyph)
        {
            if (Tier < 3) return;
            Color keep = GUI.color;
            GUI.color = Light ? new Color(0.06f, 0.0f, 0.10f, 0.72f) : new Color(0.98f, 0.92f, 1f, 0.55f);
            GUI.DrawTexture(glyph.ContractedBy(glyph.width * 0.06f), RitualTex.Disc, ScaleMode.StretchToFill);
            GUI.color = keep;
        }

        /// <summary>A violet wash over the page when no tier art exists yet - so the change shows from the start.
        /// Above it, the storm of the stream presses in from the window's edges (the saturation vignette).</summary>
        public static void Wash(Rect box)
        {
            if (Tier == 0) return;
            Texture2D sat = FlowTex.Saturation[Mathf.Clamp(Tier - 2, 0, 2)];
            if (sat != null)
            {
                float a = (Tier == 1 ? 0.3f : Tier == 2 ? 0.55f : 0.75f) * (0.85f + 0.15f * Breath);
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.DrawTexture(box, sat, ScaleMode.StretchToFill);
                GUI.color = Color.white;
            }
            Texture2D v = Veins;
            if (v != null)
            {
                float a = veins[Tier] != null ? 1f : Tier / 3f;   // one drawing serves the tiers that lack their own
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(a * (0.75f + 0.25f * Breath)));
                GUI.DrawTexture(box, v, ScaleMode.StretchToFill);
            }
            else if (parchment[Tier] == null)
            {
                GUI.color = new Color(Violet.r, Violet.g, Violet.b, 0.04f + 0.035f * Tier + 0.02f * Breath);
                GUI.DrawTexture(box, BaseContent.WhiteTex);
            }
            GUI.color = Color.white;
        }
    }
}
