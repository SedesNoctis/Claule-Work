using System.Collections.Generic;
using UnityEngine;
using Verse;
using Avir.Artifactorics;

namespace Avir.Ritualistics
{
    /// <summary>Plaque art drawn in slices: the ornamented parts keep their shape, only the plain stretches between them
    /// widen. Measures are in the picture's own pixels.</summary>
    public static class PlaqueArt
    {
        /// <summary>cuts: the slice edges across the picture (0 ... width); stretch: which slices may widen.
        /// Returns the scale the picture was drawn at.</summary>
        public static float Draw(Rect r, Texture2D t, float[] cuts, bool[] stretch)
        {
            float s = r.height / t.height;
            float fixedW = 0f, freeW = 0f;
            for (int i = 0; i < stretch.Length; i++)
            {
                float pw = (cuts[i + 1] - cuts[i]) * s;
                if (stretch[i]) freeW += pw; else fixedW += pw;
            }
            if (fixedW >= r.width || freeW <= 0f)
            {
                GUI.DrawTexture(r, t, ScaleMode.StretchToFill);
                return s;
            }
            float k = (r.width - fixedW) / freeW;
            // the stretched slices first, a little wider than their place; the ornamented ones over them - no seam shows
            var rects = new Rect[stretch.Length];
            float x = r.x;
            for (int i = 0; i < stretch.Length; i++)
            {
                float w = (cuts[i + 1] - cuts[i]) * s * (stretch[i] ? k : 1f);
                rects[i] = new Rect(Mathf.Round(x), Mathf.Round(r.y), Mathf.Round(x + w) - Mathf.Round(x), Mathf.Round(r.height));
                x += w;
            }
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < stretch.Length; i++)
                {
                    if (stretch[i] != (pass == 0)) continue;
                    Rect scr = stretch[i] ? new Rect(rects[i].x - 3f, rects[i].y, rects[i].width + 6f, rects[i].height) : rects[i];
                    Rect uv = new Rect(cuts[i] / t.width, 0f, (cuts[i + 1] - cuts[i]) / t.width, 1f);
                    GUI.DrawTextureWithTexCoords(scr, t, uv);
                }
            return s;
        }

        /// <summary>Where a point of the picture (its own pixels across) lands on screen when drawn by Draw into r.</summary>
        public static float MapX(Rect r, Texture2D t, float[] cuts, bool[] stretch, float px)
        {
            float s = r.height / t.height, fixedW = 0f, freeW = 0f;
            for (int i = 0; i < stretch.Length; i++) { float pw = (cuts[i + 1] - cuts[i]) * s; if (stretch[i]) freeW += pw; else fixedW += pw; }
            if (fixedW >= r.width || freeW <= 0f) return r.x + r.width * px / t.width;
            float k = (r.width - fixedW) / freeW, x = r.x;
            for (int i = 0; i < stretch.Length; i++)
            {
                float m = s * (stretch[i] ? k : 1f);
                if (px <= cuts[i + 1] || i == stretch.Length - 1) return x + (px - cuts[i]) * m;
                x += (cuts[i + 1] - cuts[i]) * m;
            }
            return x;
        }
    }
}

namespace Avir.Ritualistics
{
    /// <summary>The ritual windows' own art, where it is present: a frame for each column, a card under each block of
    /// text (what it does, the details), a strip under the chosen row of a list. Without the files nothing changes.</summary>
    public static class RitualArt
    {
        private static Texture2D panel, card, row;
        private static bool looked;

        private static void Look()
        {
            if (looked) return;
            looked = true;
            panel = ContentFinder<Texture2D>.Get("UI/Ritualistics/Panel", false);
            card = ContentFinder<Texture2D>.Get("UI/Ritualistics/Card", false);
            row = ContentFinder<Texture2D>.Get("UI/Ritualistics/RowSelected", false);
        }

        public static bool HasCard { get { Look(); return card != null; } }
        public static bool HasPanel { get { Look(); return panel != null; } }

        /// <summary>Nine slices: corners keep their size (texCorner of the picture drawn screenCorner wide), edges
        /// stretch one way, the middle both.</summary>
        public static void Nine(Rect r, Texture2D t, float texCorner, float screenCorner)
        {
            float c = Mathf.Min(screenCorner, r.width / 2f, r.height / 2f);
            float u = texCorner / t.width, v = texCorner / t.height;
            float[] xs = { r.x, r.x + c, r.xMax - c, r.xMax };
            float[] ys = { r.y, r.y + c, r.yMax - c, r.yMax };
            float[] us = { 0f, u, 1f - u, 1f };
            float[] vs = { 1f, 1f - v, v, 0f };   // the texture's v runs upward
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    Rect scr = new Rect(Mathf.Round(xs[i]), Mathf.Round(ys[j]), Mathf.Round(xs[i + 1]) - Mathf.Round(xs[i]), Mathf.Round(ys[j + 1]) - Mathf.Round(ys[j]));
                    if (scr.width <= 0f || scr.height <= 0f) continue;
                    GUI.DrawTextureWithTexCoords(scr, t, new Rect(us[i], vs[j + 1], us[i + 1] - us[i], vs[j] - vs[j + 1]));
                }
        }

        /// <summary>A card under a block of text (the plaque of a spell's effect).</summary>
        public static void CardAt(Rect r)
        {
            Look();
            if (card == null) return;
            Color keep = GUI.color;
            GUI.color = Color.white;
            Nine(r, card, card.width * 0.25f, 28f);
            GUI.color = keep;
        }

        /// <summary>The frame round a column.</summary>
        public static void Panel(Rect r)
        {
            Look();
            if (panel == null) return;
            Color keep = GUI.color;
            GUI.color = Color.white;
            Nine(r, panel, panel.width * 0.25f, 56f);
            GUI.color = keep;
        }

        /// <summary>The chosen row of a list; false if there is no art (the caller draws its own).</summary>
        public static bool Selected(Rect r)
        {
            Look();
            if (row == null) return false;
            Color keep = GUI.color;
            // a quiet wash under the text: no finial at the start, where the name stands - only at the far end
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            PlaqueArt.Draw(r, row, new[] { 170f, row.width - 165f, row.width }, new[] { true, false });
            GUI.color = keep;
            return true;
        }

        private const float Pad = 14f, Gap = 12f;

        private static List<string> Blocks(string text)
        {
            int i = text.IndexOf("DETAILS\n");
            if (i <= 0 && (i = text.IndexOf("DETAILS\r\n")) <= 0) return new List<string> { text.TrimEnd() };
            return new List<string> { text.Substring(0, i).TrimEnd(), text.Substring(i).TrimEnd() };
        }

        /// <summary>How tall the text stands, in cards if there is the art.</summary>
        public static float TextHeight(string text, float w)
        {
            if (!HasCard) return Text.CalcHeight(text, w);
            float h = 0f;
            foreach (string b in Blocks(text)) h += Text.CalcHeight(b, w - Pad * 2f) + Pad * 2f + Gap;
            return h;
        }

        /// <summary>The text, each block (what it does; the details) on its own card if there is the art.</summary>
        public static void TextIn(Rect view, string text)
        {
            if (!HasCard) { ArtInk.Label(view, text); return; }
            float y = view.y;
            Color ink = GUI.color;
            foreach (string b in Blocks(text))
            {
                float h = Text.CalcHeight(b, view.width - Pad * 2f) + Pad * 2f;
                Rect c = new Rect(view.x, y, view.width, h);
                GUI.color = new Color(0.92f, 0.84f, 0.68f, 0.75f);   // toned into the page: not a white sheet on yellow
                Nine(c, card, card.width * 0.2f, 22f);
                GUI.color = ink;
                ArtInk.Label(c.ContractedBy(Pad), b);
                y += h + Gap;
            }
        }
    }
}
