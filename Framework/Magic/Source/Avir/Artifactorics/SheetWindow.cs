using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    /// <summary>The parchment sheets (artificer's table, artifact spiral, amulet development) can be
    /// blown up to the whole screen for reading, keeping the sheet's proportions, and back.</summary>
    public static class SheetWindow
    {
        public const float Aspect = 1672f / 941f;

        public static Vector2 FitScreen(float margin)
        {
            float w = Verse.UI.screenWidth - margin, h = w / Aspect;
            if (h > Verse.UI.screenHeight - margin) { h = Verse.UI.screenHeight - margin; w = h * Aspect; }
            return new Vector2(w, h);
        }

        private static Texture2D maxTex, restoreTex;
        private static bool texLoaded;

        private static void LoadTex()
        {
            if (texLoaded) return;
            texLoaded = true;
            maxTex = ContentFinder<Texture2D>.Get("UI/Artifactorics/Button_Maximize", false);
            restoreTex = ContentFinder<Texture2D>.Get("UI/Artifactorics/Button_Restore", false) ?? maxTex;
        }

        /// <summary>A dark seal behind a sheet button, so it reads on the parchment.</summary>
        private static void Backing(Rect b, bool over)
        {
            // a soft warm glow only (no dark seal): brighter under the mouse
            Color old = GUI.color;
            GUI.color = over ? new Color(1f, 0.82f, 0.35f, 0.75f) : new Color(1f, 0.93f, 0.75f, 0.55f);
            GUI.DrawTexture(b.ExpandedBy(10f), ArtifactoricsTex.Glow, ScaleMode.StretchToFill);
            GUI.color = old;
        }

        /// <summary>The close button on a dark seal.</summary>
        public static bool CloseButton(Rect close)
        {
            Backing(close, Mouse.IsOver(close));
            return Widgets.CloseButtonFor(close);
        }

        /// <summary>Drawn left of the close button. Returns true when it was clicked (the rect changed).</summary>
        /// <summary>Sheets open on the whole screen; the last choice (maximized or restored) is kept until the game is closed.</summary>
        public static bool PreferMaximized = true;

        public static Rect FullRect(float margin) =>
            new Rect(margin, margin, Verse.UI.screenWidth - 2f * margin, Verse.UI.screenHeight - 2f * margin);

        /// <summary>Called when a window opens: puts it on the whole screen (unless the player restored one earlier).
        /// fillScreen: the whole screen as it is (a window that lays itself out by its size, like the ritual book);
        /// otherwise the largest sheet of the artifact panel's proportions.</summary>
        public static void StartMaximized(Window w, ref bool maximized, ref Rect normal, bool fillScreen = false)
        {
            if (!PreferMaximized || maximized) return;
            normal = w.windowRect;
            if (fillScreen) w.windowRect = FullRect(10f);
            else
            {
                Vector2 s = FitScreen(10f);
                w.windowRect = new Rect((Verse.UI.screenWidth - s.x) / 2f, (Verse.UI.screenHeight - s.y) / 2f, s.x, s.y);
            }
            maximized = true;
        }

        public static bool MaximizeButton(Window w, Rect close, ref bool maximized, ref Rect normal, bool fillScreen = false)
        {
            LoadTex();
            Rect b = new Rect(close.x - 50f, close.y - 6f, 36f, 36f);
            bool over = Mouse.IsOver(b);
            Backing(b, over);
            Color old = GUI.color;
            Texture2D icon = maximized ? restoreTex : maxTex;
            if (icon != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(b, icon, ScaleMode.ScaleToFit);
            }
            else
            {
                // drawn frame: one (whole screen) or two stacked (restore), in light gold on the seal
                GUI.color = new Color(0.98f, 0.88f, 0.60f, 1f);
                if (!maximized) Widgets.DrawBox(b.ContractedBy(4f), 2);
                else
                {
                    Widgets.DrawBox(new Rect(b.x + 7f, b.y + 3f, 13f, 13f), 2);
                    Widgets.DrawBox(new Rect(b.x + 3f, b.y + 8f, 13f, 13f), 2);
                }
            }
            GUI.color = old;
            TooltipHandler.TipRegion(b, maximized ? "Restore the sheet" : "Whole screen");
            if (!Widgets.ButtonInvisible(b)) return false;
            if (!maximized)
            {
                normal = w.windowRect;
                if (fillScreen) w.windowRect = FullRect(10f);
                else
                {
                    Vector2 s = FitScreen(10f);
                    w.windowRect = new Rect((Verse.UI.screenWidth - s.x) / 2f, (Verse.UI.screenHeight - s.y) / 2f, s.x, s.y);
                }
            }
            else w.windowRect = normal;
            maximized = !maximized;
            PreferMaximized = maximized;
            return true;
        }
    }
}
