using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    /// <summary>Text on the parchment sheets. The game's tiny font has hairline strokes, so dark ink
    /// in it reads grey next to the small font; it is printed twice, half a pixel apart, which
    /// thickens the stroke without moving anything.</summary>
    public static class ArtInk
    {
        public static void Label(Rect r, string text)
        {
            Widgets.Label(r, text);
            if (Text.Font != GameFont.Tiny) return;
            Color c = GUI.color;
            if (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f > 0.4f) return;   // only dark ink
            Widgets.Label(new Rect(r.x + 0.5f, r.y, r.width, r.height), text);
        }

        public static void Label(Rect r, TaggedString text) => Label(r, text.Resolve());
    }
}
