using System.Collections.Generic;
using System.Linq;
using Avir.Artifactorics;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Magic
{
    /// <summary>Spell signs: the frames (art in UI/Magic/Icon/Rim_* and Plate_* when present, plain shapes until then)
    /// and the drawing of a sign wherever a spell is shown - the hand, the book, the cast button.</summary>
    [StaticConstructorOnStartup]
    public static class SpellIconArt
    {
        private static readonly Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();

        public static Texture2D Rim(SpellIcon.Shape s) => Get(s, true);
        public static Texture2D Plate(SpellIcon.Shape s) => Get(s, false);
        public static bool HasArt(SpellIcon.Shape s) => ContentFinder<Texture2D>.Get("UI/Magic/Icon/Rim_" + s, false) != null;

        private static Texture2D Get(SpellIcon.Shape s, bool rim)
        {
            if (s == SpellIcon.Shape.None) return null;
            int key = (int)s * 2 + (rim ? 1 : 0);
            if (cache.TryGetValue(key, out Texture2D t)) return t;
            t = ContentFinder<Texture2D>.Get("UI/Magic/Icon/" + (rim ? "Rim_" : "Plate_") + s, false) ?? Make(s, rim);
            cache[key] = t;
            return t;
        }

        /// <summary>Inside the shape? x, y from -1 to 1, y up.</summary>
        private static bool In(SpellIcon.Shape s, float x, float y)
        {
            switch (s)
            {
                case SpellIcon.Shape.Circle: return x * x + y * y <= 0.86f * 0.86f;
                case SpellIcon.Shape.Diamond: return Mathf.Abs(x) + Mathf.Abs(y) <= 0.92f;
                case SpellIcon.Shape.Shield:
                    if (y > 0.82f) return false;
                    if (y >= -0.15f) return Mathf.Abs(x) <= 0.76f;
                    return y >= -0.95f && Mathf.Abs(x) <= 0.76f * Mathf.Sqrt((y + 0.95f) / 0.8f);
            }
            return false;
        }

        private static Texture2D Make(SpellIcon.Shape s, bool rim)
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // four samples a pixel, for soft edges
                    int hit = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        float fx = (x + 0.25f + 0.5f * (k % 2)) / n * 2f - 1f, fy = (y + 0.25f + 0.5f * (k / 2)) / n * 2f - 1f;
                        bool o = In(s, fx, fy);
                        if (rim ? o && !In(s, fx / 0.87f, fy / 0.87f) : In(s, fx / 0.9f, fy / 0.9f)) hit++;
                    }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 4));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        private static bool Dark(Color c) => 0.3f * c.r + 0.59f * c.g + 0.11f * c.b < 0.35f;

        public static RuneDef DefaultGlyph(RitualSchema s) => s == null ? null : Spells.Element(s) ?? RitualEvaluator.Read(s).verbs.FirstOrDefault();

        /// <summary>The spell's sign: the one the player made, else its element on a dark disc.</summary>
        public static void Draw(Rect r, RitualSchema s)
        {
            if (s?.icon == null) { SpellGlyph.Draw(r.ContractedBy(r.width * 0.06f), DefaultGlyph(s)); return; }
            Draw(r, s.icon, DefaultGlyph(s));
        }

        public static void Draw(Rect r, SpellIcon ic, RuneDef fallback)
        {
            Color keep = GUI.color;
            if (ic.shape != SpellIcon.Shape.None)
            {
                GUI.color = ic.plate; GUI.DrawTexture(r, Plate(ic.shape), ScaleMode.ScaleToFit);
                GUI.color = ic.rim; GUI.DrawTexture(r, Rim(ic.shape), ScaleMode.ScaleToFit);
            }
            RuneDef main = ic.main ?? fallback;
            if (main != null)
            {
                float k = ic.shape == SpellIcon.Shape.None ? 0.06f : ic.shape == SpellIcon.Shape.Shield ? 0.27f : 0.24f;
                Rect g = r.ContractedBy(r.width * k);
                if (ic.shape == SpellIcon.Shape.Shield) g.y -= r.height * 0.04f;
                GUI.color = ic.glyph; GUI.DrawTexture(g, Dark(ic.glyph) ? main.Ink : main.Glow, ScaleMode.ScaleToFit);
            }
            if (ic.second != null)
            {
                float w = r.width * 0.36f;
                Rect c = new Rect(r.xMax - w, r.yMax - w, w, w);
                GUI.color = Dark(ic.glyph2) ? new Color(0.95f, 0.9f, 0.82f, 0.95f) : new Color(0.08f, 0.06f, 0.10f, 0.9f);
                GUI.DrawTexture(c, RitualTex.Disc, ScaleMode.StretchToFill);
                GUI.color = ic.glyph2; GUI.DrawTexture(c.ContractedBy(w * 0.14f), Dark(ic.glyph2) ? ic.second.Ink : ic.second.Glow, ScaleMode.ScaleToFit);
            }
            GUI.color = keep;
        }
    }

    /// <summary>The sign editor: a background (diamond, shield, circle or none), a main glyph, a small corner glyph,
    /// a colour for each - and the sign at three sizes, as it will be seen.</summary>
    public class Dialog_SpellIcon : Window
    {
        private readonly RitualSchema s;
        private readonly SpellIcon ic;
        private readonly System.Action<SpellIcon> done;
        private Vector2 scrollMain, scrollSecond;
        private int colourOf = 2;   // 0 plate, 1 rim, 2 main glyph, 3 corner glyph

        private static readonly Color[] Palette =
        {
            Color.white, new Color(0.95f, 0.86f, 0.6f), new Color(1f, 0.72f, 0.25f), new Color(1f, 0.45f, 0.12f), new Color(0.85f, 0.15f, 0.1f),
            new Color(0.55f, 0.05f, 0.12f), new Color(1f, 0.55f, 0.75f), new Color(0.62f, 0.35f, 0.95f), new Color(0.2f, 0.3f, 0.85f), new Color(0.45f, 0.75f, 1f),
            new Color(0.2f, 0.75f, 0.7f), new Color(0.4f, 0.84f, 0.34f), new Color(0.5f, 0.55f, 0.2f), new Color(0.45f, 0.3f, 0.18f), new Color(0.6f, 0.6f, 0.62f),
            new Color(0.25f, 0.24f, 0.28f), new Color(0.12f, 0.08f, 0.14f), new Color(0.04f, 0.03f, 0.05f),
        };

        public Dialog_SpellIcon(RitualSchema s, System.Action<SpellIcon> done)
        {
            this.s = s;
            this.done = done;
            ic = s.icon?.Copy() ?? new SpellIcon { main = SpellIconArt.DefaultGlyph(s) };
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1100f, Verse.UI.screenWidth - 40f), Mathf.Min(760f, Verse.UI.screenHeight - 40f));

        private static Color Ink => FlowStyle.Ink;
        private static Color InkSoft => FlowStyle.InkSoft;

        public override void DoWindowContents(Rect box)
        {
            FlowStyle.Begin(null);
            Rect inner = ParchmentFrame.Draw(box).ContractedBy(10f);
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(inner.x, inner.y, inner.width, 34f), "The sign of " + s.name);
            Text.Font = GameFont.Small;
            float lw = 300f;
            Rect left = new Rect(inner.x, inner.y + 42f, lw, inner.height - 42f);
            Rect right = new Rect(left.xMax + 20f, left.y, inner.xMax - left.xMax - 20f, left.height);
            DrawPreview(left);
            DrawChoices(right);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            FlowStyle.End();
        }

        private void DrawPreview(Rect r)
        {
            Rect big = new Rect(r.x + (r.width - 240f) / 2f, r.y, 240f, 240f);
            Widgets.DrawBoxSolid(big.ExpandedBy(6f), new Color(0.16f, 0.13f, 0.20f, 0.9f));
            SpellIconArt.Draw(big, ic, SpellIconArt.DefaultGlyph(s));
            // as it is seen: on the cast button and in the book
            float y = big.yMax + 20f;
            GUI.color = InkSoft; ArtInk.Label(new Rect(r.x, y, r.width, 24f), "As it is seen:"); y += 26f;
            Rect g1 = new Rect(r.x + 20f, y, 75f, 75f), g2 = new Rect(g1.xMax + 30f, y + 14f, 48f, 48f), g3 = new Rect(g2.xMax + 30f, y + 22f, 32f, 32f);
            Widgets.DrawBoxSolid(g1, new Color(0.2f, 0.2f, 0.22f)); SpellIconArt.Draw(g1.ContractedBy(4f), ic, SpellIconArt.DefaultGlyph(s));
            SpellIconArt.Draw(g2, ic, SpellIconArt.DefaultGlyph(s));
            SpellIconArt.Draw(g3, ic, SpellIconArt.DefaultGlyph(s));
            y += 100f;
            if (!SpellIconArt.HasArt(SpellIcon.Shape.Diamond))
            {
                GUI.color = InkSoft;
                ArtInk.Label(new Rect(r.x, y, r.width, 60f), "The frames are plain shapes until their art is drawn.");
            }
            float by = r.yMax - 40f;
            if (RitualUI.Button(new Rect(r.x, by - 44f, r.width, 38f), "Back to the element alone")) { done?.Invoke(null); Close(); }
            if (RitualUI.Button(new Rect(r.x, by, r.width, 38f), "Keep this sign")) { done?.Invoke(ic.Copy()); Close(); }
        }

        private void DrawChoices(Rect r)
        {
            float y = r.y;
            GUI.color = Ink; ArtInk.Label(new Rect(r.x, y, 160f, 30f), "Background");
            float x = r.x + 160f;
            foreach (SpellIcon.Shape sh in new[] { SpellIcon.Shape.Diamond, SpellIcon.Shape.Shield, SpellIcon.Shape.Circle, SpellIcon.Shape.None })
            {
                Rect b = new Rect(x, y, 120f, 32f);
                if (RitualUI.Button(b, sh == SpellIcon.Shape.None ? "None" : sh.ToString(), true, ic.shape == sh)) ic.shape = sh;
                x += 126f;
            }
            y += 44f;
            List<RuneDef> runes = RuneDef.All.Where(x2 => x2.Glow != BaseContent.BadTex).ToList();
            float gridH = (r.yMax - y - 170f) / 2f;
            GUI.color = Ink; ArtInk.Label(new Rect(r.x, y, r.width, 26f), "Main sign"); y += 28f;
            RuneGrid(new Rect(r.x, y, r.width, gridH - 30f), runes, ic.main ?? SpellIconArt.DefaultGlyph(s), d => ic.main = d, ref scrollMain, false);
            y += gridH;
            GUI.color = Ink; ArtInk.Label(new Rect(r.x, y, r.width, 26f), "Corner sign (small, lower right)"); y += 28f;
            RuneGrid(new Rect(r.x, y, r.width, gridH - 30f), runes, ic.second, d => ic.second = d, ref scrollSecond, true);
            y += gridH;
            // colours: choose what to paint, then the colour
            GUI.color = Ink; ArtInk.Label(new Rect(r.x, y, 120f, 30f), "Colour of");
            string[] what = { "Plate", "Rim", "Main sign", "Corner sign" };
            x = r.x + 120f;
            for (int i = 0; i < what.Length; i++)
            {
                if (RitualUI.Button(new Rect(x, y, 130f, 32f), what[i], true, colourOf == i)) colourOf = i;
                x += 136f;
            }
            y += 42f;
            float sw = Mathf.Min(40f, (r.width - (Palette.Length - 1) * 6f) / Palette.Length);
            Color cur = colourOf == 0 ? ic.plate : colourOf == 1 ? ic.rim : colourOf == 2 ? ic.glyph : ic.glyph2;
            for (int i = 0; i < Palette.Length; i++)
            {
                Rect c = new Rect(r.x + i * (sw + 6f), y, sw, sw);
                Widgets.DrawBoxSolid(c, Palette[i]);
                bool on = (Palette[i] - cur).maxColorComponent < 0.01f && (cur - Palette[i]).maxColorComponent < 0.01f;
                GUI.color = on ? FlowStyle.Gold : new Color(0f, 0f, 0f, 0.5f);
                Widgets.DrawBox(c, on ? 3 : 1);
                if (Widgets.ButtonInvisible(c))
                {
                    if (colourOf == 0) ic.plate = Palette[i]; else if (colourOf == 1) ic.rim = Palette[i]; else if (colourOf == 2) ic.glyph = Palette[i]; else ic.glyph2 = Palette[i];
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }
            }
            GUI.color = Color.white;
        }

        private void RuneGrid(Rect r, List<RuneDef> runes, RuneDef chosen, System.Action<RuneDef> pick, ref Vector2 scroll, bool allowNone)
        {
            const float cell = 52f;
            int per = Mathf.Max(1, Mathf.FloorToInt((r.width - 16f) / cell));
            int count = runes.Count + (allowNone ? 1 : 0);
            Rect view = new Rect(0f, 0f, r.width - 16f, Mathf.CeilToInt(count / (float)per) * cell);
            Widgets.DrawBoxSolid(r, new Color(0.16f, 0.13f, 0.20f, 0.85f));
            RitualScroll.Begin(r, ref scroll, view);
            for (int i = 0; i < count; i++)
            {
                Rect c = new Rect((i % per) * cell + 3f, (i / per) * cell + 3f, cell - 6f, cell - 6f);
                RuneDef d = allowNone ? (i == 0 ? null : runes[i - 1]) : runes[i];
                bool on = d == chosen;
                if (on) Widgets.DrawBoxSolid(c, new Color(1f, 0.85f, 0.4f, 0.35f));
                else if (Mouse.IsOver(c)) Widgets.DrawBoxSolid(c, new Color(1f, 1f, 1f, 0.12f));
                if (d == null)
                {
                    GUI.color = new Color(0.95f, 0.88f, 0.7f); Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(c, "none"); Text.Anchor = TextAnchor.UpperLeft;
                }
                else
                {
                    GUI.color = d.role == RuneRole.Element ? Color.Lerp(d.color, Color.white, 0.2f) : new Color(0.95f, 0.9f, 0.8f);
                    GUI.DrawTexture(c.ContractedBy(5f), d.Glow, ScaleMode.ScaleToFit);
                    TooltipHandler.TipRegion(c, d.LabelCap);
                }
                if (Widgets.ButtonInvisible(c)) { pick(d); SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); }
            }
            GUI.color = Color.white;
            Widgets.EndScrollView();
        }
    }
}
