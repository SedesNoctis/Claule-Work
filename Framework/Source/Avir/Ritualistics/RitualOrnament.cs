using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>The pattern of a circle beyond its runes: every ring is cut into slots, minor runes join
    /// the slots to one another, an empty slot is sealed. Minor runes are only design - they do nothing.
    /// One layout for the editor, the book and the chalk on the floor.</summary>
    [StaticConstructorOnStartup]
    public static class RitualOrnament
    {
        private static readonly string[] Names = { "Talons", "Twist", "Quill", "Diamond", "Beads", "Eye", "Crescents", "Spindle" };
        private static readonly Texture2D[] Ink = new Texture2D[Names.Length], Glow = new Texture2D[Names.Length];
        public static readonly Texture2D SlotInk, SlotGlow, CoreInk, CoreGlow, FrameInk, FrameGlow, SealInk, SealGlow;
        /// <summary>A rune sits inside its slot frame at this share of the frame's size.</summary>
        public const float RuneInFrame = 0.5f;
        /// <summary>A slot frame's share of its section's width. A square frame, turned to any angle of the ring, reaches
        /// 0.71 of its size past its centre at the corners: 0.7 keeps even those inside the section lines (and, in the
        /// outermost ring, inside the circle's rim) - the runes no longer spill over the edge.</summary>
        public const float FrameInBand = 0.7f;

        static RitualOrnament()
        {
            for (int i = 0; i < Names.Length; i++)
            {
                Ink[i] = Load("Ink", Names[i]);
                Glow[i] = Load("Glow", Names[i]);
            }
            SlotInk = Load("Ink", "Slot");
            SlotGlow = Load("Glow", "Slot");
            CoreInk = Load("Ink", "Core");
            CoreGlow = Load("Glow", "Core");
            FrameInk = Load("Ink", "Frame");
            FrameGlow = Load("Glow", "Frame");
            SealInk = Load("Ink", "Seal");
            SealGlow = Load("Glow", "Seal");
        }

        private static Texture2D Load(string kind, string name) =>
            ContentFinder<Texture2D>.Get("UI/Ritualistics/Runes/Minor/" + kind + "/Minor_" + name, false) ?? BaseContent.BadTex;

        /// <summary>Angle of slot k of ring i, degrees clockwise from the top (screen space).</summary>
        public static float SlotAngle(int ring, int k, int per) => -90f + k * 360f / per + ring * 25f;

        /// <summary>The minor rune between slot k and k+1: two per ring, alternating, so the pattern repeats.</summary>
        public static Texture2D Connector(int ring, int k, int per, bool glow)
        {
            int a = (ring * 2) % Names.Length, b = (ring * 2 + 1) % Names.Length;
            int i = per % 2 == 0 && k % 2 == 1 ? b : a;
            return glow ? Glow[i] : Ink[i];
        }

        /// <summary>Size of a slot frame: fits both the section's width and the arc of one slot.
        /// The rune inside is RuneInFrame of it.</summary>
        public static float GlyphSize(float inner, float outer, int per, float cap)
        {
            float mid = (inner + outer) / 2f;
            float arc = 2f * Mathf.PI * mid / per;
            // the frame keeps inside its section: across the band and along its arc (a square frame - mind its corners)
            return Mathf.Min((outer - inner) * FrameInBand, arc * 0.58f, cap * 1.6f);
        }

        /// <summary>Size of a minor rune: it lies along the ring, across the gap between two slots.</summary>
        public static float ConnectorSize(float inner, float outer, int per)
        {
            float mid = (inner + outer) / 2f;
            return Mathf.Min(2f * Mathf.PI * mid / per * 0.3f, (outer - inner) * 0.9f);
        }

        public static Rect GlyphRect(Vector2 c, float radius, float angleDeg, float size)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Rect(c.x + Mathf.Cos(a) * radius - size / 2f, c.y + Mathf.Sin(a) * radius - size / 2f, size, size);
        }

        /// <summary>Slot dividers, minor runes and sealed empty slots for a circle drawn in the UI.
        /// filled(i) is how many runes ring i holds.</summary>
        public static void DrawUI(Vector2 c, float R, float core, int n, System.Func<int, int> slotsOf, System.Func<int, int> filled, Color ink)
        {
            Color faint = new Color(ink.r, ink.g, ink.b, ink.a * 0.55f);
            // a second, thin line inside the rim and around the core
            GUI.color = faint;
            Circle(c, R - 6f);
            Circle(c, core - 5f);
            GUI.color = new Color(ink.r, ink.g, ink.b, ink.a * 0.5f);
            GUI.color = ink;
            GUI.DrawTexture(new Rect(c.x - core * 0.8f, c.y - core * 0.8f, core * 1.6f, core * 1.6f), FlowStyle.Pick(CoreInk, CoreGlow), ScaleMode.ScaleToFit);
            for (int i = 0; i < n; i++)
            {
                float inner = core + (R - core) * i / n, outer = core + (R - core) * (i + 1) / n, mid = (inner + outer) / 2f;
                int per = slotsOf(i);
                float size = GlyphSize(inner, outer, per, 90f), band = outer - inner;
                int has = filled(i);
                for (int k = 0; k < per; k++)
                {
                    // every slot is a frame; a written rune sits inside it
                    Rect fr = GlyphRect(c, mid, SlotAngle(i, k, per), size);
                    FlowStyle.Backing(fr);
                    GUI.color = k >= has ? faint : ink;
                    GUI.DrawTexture(fr, FlowStyle.Pick(FrameInk, FrameGlow), ScaleMode.ScaleToFit);
                    if (per < 2) continue;   // one slot: nothing to join
                    float div = SlotAngle(i, k, per) + 180f / per, a = div * Mathf.Deg2Rad;
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    // the cut between two slots: short strokes from both lines, a minor rune between them
                    AvirLine.Draw(c + dir * (inner + 2f), c + dir * (inner + band * 0.24f), faint, 2f);
                    AvirLine.Draw(c + dir * (outer - band * 0.24f), c + dir * (outer - 2f), faint, 2f);
                    // the minor rune lies along the ring, joining the two slots
                    GUI.color = ink;
                    Rect cr = GlyphRect(c, mid, div, ConnectorSize(inner, outer, per));
                    Matrix4x4 keep = GUI.matrix;
                    AvirLine.Rotate(div, cr.center);
                    GUI.DrawTexture(cr, Connector(i, k, per, FlowStyle.Light), ScaleMode.ScaleToFit);
                    GUI.matrix = keep;
                }
            }
            GUI.color = Color.white;
        }

        private static void Circle(Vector2 c, float rad) =>
            GUI.DrawTexture(new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f), RitualTex.Ring, ScaleMode.StretchToFill);
    }
}
