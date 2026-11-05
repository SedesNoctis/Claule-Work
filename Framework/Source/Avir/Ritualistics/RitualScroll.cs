using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>The ritual windows' own scrollbar: a groove with stars at its ends and a gold thumb with crystals,
    /// drawn and dragged here (the game's skin is not asked). Without the art, the game's own scrollbar.</summary>
    public static class RitualScroll
    {
        private static Texture2D track, thumb;
        private static bool looked;
        private static int dragging;     // the scroll view being dragged (its place, hashed); 0 - none
        private static float grab;
        private const float Width = 16f;

        private static bool Art
        {
            get
            {
                if (!looked)
                {
                    looked = true;
                    track = ContentFinder<Texture2D>.Get("UI/Ritualistics/ScrollTrack", false);
                    thumb = ContentFinder<Texture2D>.Get("UI/Ritualistics/ScrollThumb", false);
                }
                return track != null && thumb != null;
            }
        }

        public static void Begin(Rect outRect, ref Vector2 pos, Rect view)
        {
            if (!Art || view.height <= outRect.height) { Widgets.BeginScrollView(outRect, ref pos, view); return; }
            Bar(outRect, ref pos, view);
            Widgets.BeginScrollView(outRect, ref pos, view, false);
        }

        private static void Bar(Rect o, ref Vector2 pos, Rect v)
        {
            Rect bar = new Rect(o.xMax - Width, o.y, Width, o.height);
            float range = v.height - o.height;
            float th = Mathf.Max(48f, bar.height * o.height / v.height);
            float free = bar.height - th;
            pos.y = Mathf.Clamp(pos.y, 0f, range);
            Rect knob = new Rect(bar.x, bar.y + free * (pos.y / range), Width, th);
            int id = Gen.HashCombineInt(Mathf.RoundToInt(o.x), Mathf.RoundToInt(o.y) * 7919 + Mathf.RoundToInt(o.width));
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && bar.Contains(e.mousePosition))
            {
                if (knob.Contains(e.mousePosition)) grab = e.mousePosition.y - knob.y;
                else grab = th / 2f;   // a click on the groove: the thumb jumps there
                dragging = id;
                e.Use();
            }
            if (dragging == id)
            {
                if (e.type == EventType.MouseDrag || e.type == EventType.MouseDown)
                {
                    pos.y = Mathf.Clamp((e.mousePosition.y - grab - bar.y) / Mathf.Max(1f, free), 0f, 1f) * range;
                    if (e.type == EventType.MouseDrag) e.Use();
                }
                if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp || !Input.GetMouseButton(0)) dragging = 0;
            }
            knob.y = bar.y + free * (pos.y / range);
            Color keep = GUI.color;
            GUI.color = Color.white;
            Ends(bar, track, 18f);
            Ends(knob, thumb, 26f);
            GUI.color = keep;
        }

        /// <summary>Drawn in three, top to bottom: the ends keep their shape, the middle stretches.</summary>
        private static void Ends(Rect r, Texture2D t, float endPx)
        {
            float e = Mathf.Min(endPx * r.width / t.width, r.height / 2f);
            float v = endPx / t.height;
            GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y + e - 1f, r.width, r.height - 2f * e + 2f), t, new Rect(0f, v, 1f, 1f - 2f * v));
            GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y, r.width, e), t, new Rect(0f, 1f - v, 1f, v));
            GUI.DrawTextureWithTexCoords(new Rect(r.x, r.yMax - e, r.width, e), t, new Rect(0f, 0f, 1f, v));
        }
    }
}
