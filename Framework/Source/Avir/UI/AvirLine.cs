using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>A straight line for the mod's windows: a thin strip turned about its middle. Widgets.DrawLine went
    /// astray on 1.7 inside zoomed or scrolled areas (the ritual circle's strokes flew off in every direction); the
    /// turned strip is the same way the circle's minor runes are drawn, and those stay where they belong.</summary>
    public static class AvirLine
    {

        /// <summary>Turns what is drawn next about a point given in the current group's coordinates; restore GUI.matrix
        /// afterwards. GUI.matrix acts on SCREEN coordinates, but GUIClip.Unclip (what Unity's own RotateAroundPivot uses,
        /// and what this used before) stops at the window: inside a RimWorld window the pivot came out short by exactly the
        /// window's own position on the screen, and every turned thing in a window - the ritual editor's minor runes - was
        /// swung about the wrong point (measured from the author's screenshot: about (34, 36) px, the editor's content
        /// corner). On the map, with no window, nothing was off, which is why the circle itself always looked right.
        /// GUIToScreenPoint goes all the way to the screen; it is read with an identity matrix so the UI scale is applied
        /// once, by the matrix itself.</summary>
        public static void Rotate(float angle, Vector2 pivot)
        {
            Matrix4x4 m = GUI.matrix;
            Vector3 ps = ScreenOf(pivot, m);
            GUI.matrix = Matrix4x4.TRS(ps, Quaternion.Euler(0f, 0f, angle), Vector3.one) * Matrix4x4.TRS(-ps, Quaternion.identity, Vector3.one) * m;
        }

        /// <summary>Where a point of the current group lands on the screen. Taken from the mouse, which the game knows in
        /// both spaces (the event's position is the group's, the input's is the screen's) and which clicks in every window
        /// prove to be mapped exactly as drawing is - so no guess about windows, clips or the UI matrix is needed. The
        /// matrix here is never turned (every turn is undone right after its drawing), only scaled and moved.</summary>
        private static Vector3 ScreenOf(Vector2 p, Matrix4x4 m)
        {
            Event e = Event.current;
            if (e != null)
            {
                Vector2 local = e.mousePosition;
                Vector2 screen = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                Vector2 at = screen + new Vector2(m.m00 * (p.x - local.x), m.m11 * (p.y - local.y));
                return new Vector3(at.x, at.y, 0f);
            }
            Matrix4x4 keep = m;
            GUI.matrix = Matrix4x4.identity;
            Vector2 origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
            GUI.matrix = keep;
            return m.MultiplyPoint3x4(new Vector3(origin.x + p.x, origin.y + p.y, 0f));
        }

        /// <summary>Laid down as a row of small squares along the line, with no turning at all: any turn of GUI.matrix
        /// went astray on 1.7 inside zoomed or scrolled areas (the ritual circle's strokes kept flying off even with the
        /// pivot taken from the live matrix). Squares in the group's own coordinates go wherever the group goes.</summary>
        public static void Draw(Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            float w = Mathf.Max(1f, width);
            float step = Mathf.Max(0.75f, w * 0.5f);
            int n = Mathf.CeilToInt(len / step);
            Color keepColor = GUI.color;
            // the squares overlap about w/step deep; thin the alpha so a faint line stays faint
            float layers = w / step;
            GUI.color = color.a >= 0.999f ? color : new Color(color.r, color.g, color.b, 1f - Mathf.Pow(1f - color.a, 1f / layers));
            for (int i = 0; i <= n; i++)
            {
                Vector2 p = a + d * (i / (float)n);
                GUI.DrawTexture(new Rect(p.x - w / 2f, p.y - w / 2f, w, w), BaseContent.WhiteTex);
            }
            GUI.color = keepColor;
        }
    }
}
