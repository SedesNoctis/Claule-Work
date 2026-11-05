using Avir.Artifactorics;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>The frame of the parchment windows (ritual book, editor, inlay): a dark binding with a gold
    /// edge, a seal in every corner and a minor rune in the middle of every side.</summary>
    public static class ParchmentFrame
    {
        private static Color Binding => FlowStyle.Binding;
        private static Color Gold => FlowStyle.Gold;
        private static Color Ink => FlowStyle.InkSoft;
        public const float Width = 30f;

        /// <summary>The wrapper around the panel's edges (UI/Ritualistics/Book/Frame): a square picture, its border
        /// a fixed share of it (WrapperCut); corners drawn as they are, sides and middle stretched.</summary>
        public static readonly Texture2D Wrapper = ContentFinder<Texture2D>.Get("UI/Ritualistics/Book/Frame", false);
        private const float WrapperCut = 0.22f;   // the wing-tip corners reach a fifth in; the band itself is a tenth
        private const float WrapperEdge = 72f;

        private static void NineSlice(Rect r, Texture2D t, float edge, float cut)
        {
            GUI.color = Color.white;
            float[] xs = { r.x, r.x + edge, r.xMax - edge, r.xMax };
            float[] ys = { r.y, r.y + edge, r.yMax - edge, r.yMax };
            float[] us = { 0f, cut, 1f - cut, 1f };
            // texture v runs bottom-up
            float[] vs = { 1f, 1f - cut, cut, 0f };
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    if (i == 1 && j == 1) continue;   // the middle stays parchment
                    Rect screen = new Rect(xs[i], ys[j], xs[i + 1] - xs[i], ys[j + 1] - ys[j]);
                    Rect uv = new Rect(us[i], vs[j + 1], us[i + 1] - us[i], vs[j] - vs[j + 1]);
                    GUI.DrawTextureWithTexCoords(screen, t, uv);
                }
        }

        /// <summary>Draws the parchment and the frame; returns the area inside it.</summary>
        public static Rect Draw(Rect box)
        {
            GUI.color = Color.white;
            if (FlowStyle.Page != null) GUI.DrawTexture(box, FlowStyle.Page, ScaleMode.StretchToFill);
            else ArtifactoricsTex.TileParchment(box);
            FlowStyle.Wash(box);
            // the artist's wrapper, if there is one: a 9-slice frame around the edges
            if (Wrapper != null)
            {
                NineSlice(box, Wrapper, WrapperEdge, WrapperCut);
                GUI.color = Color.white;
                return box.ContractedBy(WrapperEdge * 0.5f);
            }
            // the binding
            Widgets.DrawBoxSolid(new Rect(box.x, box.y, box.width, 8f), Binding);
            Widgets.DrawBoxSolid(new Rect(box.x, box.yMax - 8f, box.width, 8f), Binding);
            Widgets.DrawBoxSolid(new Rect(box.x, box.y, 8f, box.height), Binding);
            Widgets.DrawBoxSolid(new Rect(box.xMax - 8f, box.y, 8f, box.height), Binding);
            GUI.color = Gold;
            Widgets.DrawBox(box.ContractedBy(8f), 2);
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.7f);
            Widgets.DrawBox(box.ContractedBy(15f), 1);
            // seals in the corners, minor runes on the sides
            float cs = 46f;
            GUI.color = Ink;
            foreach (Vector2 c in new[] { new Vector2(box.x + 15f, box.y + 15f), new Vector2(box.xMax - 15f, box.y + 15f),
                                          new Vector2(box.x + 15f, box.yMax - 15f), new Vector2(box.xMax - 15f, box.yMax - 15f) })
            {
                Rect r = new Rect(c.x - cs / 2f, c.y - cs / 2f, cs, cs);
                GUI.color = new Color(0.93f, 0.86f, 0.70f);
                GUI.DrawTexture(r.ContractedBy(9f), RitualTex.Disc, ScaleMode.StretchToFill);
                GUI.color = Ink;
                if (FlowStyle.Corner != null) { GUI.color = Color.white; GUI.DrawTexture(r.ExpandedBy(6f), FlowStyle.Corner, ScaleMode.ScaleToFit); }
                else GUI.DrawTexture(r, RitualOrnament.CoreInk, ScaleMode.ScaleToFit);
            }
            Texture2D side = RitualOrnament.Connector(0, 0, 1, false);
            float ms = 40f;
            Side(new Vector2(box.center.x, box.y + 12f), 90f, ms, side);
            Side(new Vector2(box.center.x, box.yMax - 12f), 90f, ms, side);
            Side(new Vector2(box.x + 12f, box.center.y), 0f, ms, side);
            Side(new Vector2(box.xMax - 12f, box.center.y), 0f, ms, side);
            GUI.color = Color.white;
            return box.ContractedBy(Width);
        }

        private static void Side(Vector2 c, float turn, float size, Texture2D tex)
        {
            Rect r = new Rect(c.x - size / 2f, c.y - size / 2f, size, size);
            GUI.color = new Color(0.93f, 0.86f, 0.70f);
            Widgets.DrawBoxSolid(new Rect(c.x - (turn == 0f ? 6f : size * 0.4f), c.y - (turn == 0f ? size * 0.4f : 6f),
                turn == 0f ? 12f : size * 0.8f, turn == 0f ? size * 0.8f : 12f), new Color(0.93f, 0.86f, 0.70f));
            GUI.color = Ink;
            Matrix4x4 keep = GUI.matrix;
            if (turn != 0f) AvirLine.Rotate(turn, c);
            GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            GUI.matrix = keep;
        }
    }
}
