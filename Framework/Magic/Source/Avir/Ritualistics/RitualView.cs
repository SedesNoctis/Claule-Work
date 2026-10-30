using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>
    /// Zoom and move for a ritual circle's picture - the way the artifact spiral and the amulet's pattern do it: the wheel
    /// zooms toward the cursor, the left button (once it has moved a few pixels) or the right/middle one drags, and the
    /// picture cannot be lost off the edge. Used by the ritual book and the editor.
    /// </summary>
    public class RitualView
    {
        public const float Min = 0.5f, Max = 4f;
        /// <summary>How far this view may zoom in (the spell editor goes much deeper).</summary>
        public float max = Max;
        public float zoom = 1f;
        public Vector2 pan;
        /// <summary>The left button moved the picture: the click that ends the drag selects and rubs nothing out.</summary>
        public bool dragged;
        private float dragDist;

        public bool Changed => Mathf.Abs(zoom - 1f) > 0.001f || pan.sqrMagnitude > 0.25f;
        public void Reset() { zoom = 1f; pan = Vector2.zero; }

        /// <summary>Call once per frame BEFORE the picture is drawn, in the coordinates the mouse is in.
        /// area - where the picture may be seen; home - the circle's centre at zoom 1 without any move;
        /// reach - the picture's radius at zoom 1 (components included), in pixels.</summary>
        public void Handle(Rect area, Vector2 home, float reach)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown) { dragged = false; dragDist = 0f; }
            if (!Mouse.IsOver(area)) return;
            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                dragDist += e.delta.magnitude;
                if (dragDist > 5f) dragged = true;
                if (dragged) { pan += e.delta; Clamp(area, reach); e.Use(); }
                return;
            }
            if (e.type == EventType.MouseDrag && (e.button == 1 || e.button == 2))
            {
                pan += e.delta; Clamp(area, reach); e.Use();
                return;
            }
            if (e.type == EventType.ScrollWheel)
            {
                float old = zoom;
                zoom = Mathf.Clamp(zoom * (e.delta.y > 0f ? 0.9f : 1.1f), Min, max);
                Vector2 rel = e.mousePosition - (home + pan);
                pan -= rel * (zoom / old - 1f);     // the point under the cursor stays under it
                Clamp(area, reach);
                e.Use();
            }
        }

        /// <summary>At rest or zoomed out the picture sits exactly in the centre; zoomed in, it may be moved only as far
        /// as it overflows the area (so it never drifts off centre and stays there after zooming back out).</summary>
        private void Clamp(Rect area, float reach)
        {
            if (zoom <= 1.001f) { pan = Vector2.zero; return; }
            float r = reach * zoom;
            float mx = Mathf.Max(0f, r - area.width * 0.5f), my = Mathf.Max(0f, r - area.height * 0.5f);
            pan.x = Mathf.Clamp(pan.x, -mx, mx);
            pan.y = Mathf.Clamp(pan.y, -my, my);
        }

        /// <summary>The hint in the corner and, when the picture has been moved, a button that puts it back.</summary>
        public void DrawHint(Rect area, Color ink)
        {
            Text.Font = GameFont.Tiny; GUI.color = ink; Text.Anchor = TextAnchor.LowerRight;
            Widgets.Label(new Rect(area.x, area.yMax - 18f, area.width - 6f, 18f), "Wheel to zoom - drag to move");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;   // the button draws itself; the ink colour would only darken it
            if (Changed && Widgets.ButtonText(new Rect(area.x + 6f, area.yMax - 24f, 92f, 20f), "Reset view"))
            {
                Reset();
                Verse.Sound.SoundStarter.PlayOneShotOnCamera(RimWorld.SoundDefOf.Tick_High);
            }
            Text.Font = GameFont.Small; GUI.color = Color.white;
        }
    }
}
