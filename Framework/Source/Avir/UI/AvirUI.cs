using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.UI
{
    /// <summary>
    /// Loads the crafter skin and draws nine-sliced widgets with it.
    ///
    /// WHY NINE-SLICE BY HAND RATHER THAN Widgets.DrawAtlas. Vanilla's DrawAtlas assumes a fixed
    /// quarter-of-the-texture inset. Our frames have large corner bosses and thin edges, so the
    /// inset is nothing like a quarter - it has to be stated per texture. Getting it wrong shows up
    /// as smeared corners rather than as an error, which is why every inset below is a named
    /// constant measured off the actual file rather than a guess.
    ///
    /// TWO SIZES OF BUTTON, DELIBERATELY. The source art's border is about 34px thick. Nine-sliced
    /// onto a 26px-tall control the inset clamps, no interior survives, and the widget reads as a
    /// blob. Anything shorter than SmallButtonThreshold gets the 30% texture instead, whose border
    /// is proportional to the control. This is the same reason vanilla's own ButtonBGAtlas is
    /// visually plain.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirUI
    {
        private const string Root = "UI/Avir/Crafter/";

        public static readonly Texture2D PanelFrame = ContentFinder<Texture2D>.Get(Root + "PanelFrame");
        public static readonly Texture2D CardFrame = ContentFinder<Texture2D>.Get(Root + "CardFrame");

        public static readonly Texture2D ButtonIdle = ContentFinder<Texture2D>.Get(Root + "ButtonIdle");
        public static readonly Texture2D ButtonMouseover = ContentFinder<Texture2D>.Get(Root + "ButtonMouseover");
        public static readonly Texture2D ButtonClick = ContentFinder<Texture2D>.Get(Root + "ButtonClick");

        public static readonly Texture2D ButtonSmallIdle = ContentFinder<Texture2D>.Get(Root + "ButtonSmallIdle");
        public static readonly Texture2D ButtonSmallMouseover = ContentFinder<Texture2D>.Get(Root + "ButtonSmallMouseover");
        public static readonly Texture2D ButtonSmallClick = ContentFinder<Texture2D>.Get(Root + "ButtonSmallClick");

        public static readonly Texture2D SliderRail = ContentFinder<Texture2D>.Get(Root + "SliderRail");
        public static readonly Texture2D SliderHandle = ContentFinder<Texture2D>.Get(Root + "SliderHandle");
        public static readonly Texture2D CheckOn = ContentFinder<Texture2D>.Get(Root + "CheckOn");
        public static readonly Texture2D CheckOff = ContentFinder<Texture2D>.Get(Root + "CheckOff");
        public static readonly Texture2D RadioOn = ContentFinder<Texture2D>.Get(Root + "RadioOn");
        public static readonly Texture2D RadioOff = ContentFinder<Texture2D>.Get(Root + "RadioOff");
        public static readonly Texture2D CloseX = ContentFinder<Texture2D>.Get(Root + "CloseX");
        public static readonly Texture2D Divider = ContentFinder<Texture2D>.Get(Root + "Divider");
        public static readonly Texture2D RowHighlight = ContentFinder<Texture2D>.Get(Root + "RowHighlight");
        public static readonly Texture2D WellFill = ContentFinder<Texture2D>.Get(Root + "WellFill");

        // Insets measured off the exported files, not estimated.
        public const int InsetPanel = 96;
        public const int InsetCard = 31;
        public const int InsetButton = 34;
        public const int InsetButtonSmall = 11;
        public const int InsetRail = 22;
        public const int InsetDivider = 14;

        /// <summary>Below this height a control uses the shrunk button texture.</summary>
        public const float SmallButtonThreshold = 40f;

        public static readonly Color Gold = new Color(0.807f, 0.658f, 0.360f);
        public static readonly Color Amber = new Color(0.909f, 0.576f, 0.117f);
        public static readonly Color Parchment = new Color(0.870f, 0.839f, 0.768f);
        public static readonly Color Dim = new Color(0.572f, 0.541f, 0.478f);

        /// <summary>
        /// Which control the mouse went down on, tracked through IMGUI events.
        ///
        /// UnityEngine.Input IS NOT AVAILABLE TO US. In modern Unity it lives in
        /// UnityEngine.InputLegacyModule, which RimWorld mod projects do not reference by default -
        /// hence "The name 'Input' does not exist in the current context". Adding the reference
        /// would work, but a held-button visual is not worth making everyone's build depend on an
        /// extra assembly, and Event.current already carries everything needed.
        ///
        /// rawType rather than type on the mouse-up test: a MouseUp consumed by something else
        /// still reports through rawType, and without that a button could stay stuck looking
        /// pressed after the click was handled elsewhere.
        /// </summary>
        private static Rect pressedRect;
        private static bool pressing;

        private static bool IsHeld(Rect rect)
        {
            Event e = Event.current;
            if (e == null) return false;

            if (e.rawType == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                pressedRect = rect;
                pressing = true;
            }
            else if (e.rawType == EventType.MouseUp)
            {
                pressing = false;
            }

            return pressing && pressedRect == rect;
        }

        /// <summary>
        /// Draws a texture as a nine-slice: corners at native size, edges stretched along one axis,
        /// centre stretched both ways.
        ///
        /// The inset is clamped to half the target so a control smaller than twice its own border
        /// degrades to "all corners" instead of drawing overlapping slices on top of each other.
        /// </summary>
        public static void DrawNine(Rect rect, Texture2D tex, int inset)
        {
            if (tex == null) return;

            float i = Mathf.Min(inset, rect.width / 2f - 1f, rect.height / 2f - 1f);
            if (i <= 0f)
            {
                GUI.DrawTexture(rect, tex);
                return;
            }

            float tw = tex.width, th = tex.height;
            float u = inset / tw, v = inset / th;

            // rows: bottom, middle, top  (UV origin is bottom-left)
            Draw(new Rect(rect.x, rect.y, i, i), new Rect(0f, 1f - v, u, v));
            Draw(new Rect(rect.xMax - i, rect.y, i, i), new Rect(1f - u, 1f - v, u, v));
            Draw(new Rect(rect.x, rect.yMax - i, i, i), new Rect(0f, 0f, u, v));
            Draw(new Rect(rect.xMax - i, rect.yMax - i, i, i), new Rect(1f - u, 0f, u, v));

            float mw = rect.width - i * 2f, mh = rect.height - i * 2f;
            Draw(new Rect(rect.x + i, rect.y, mw, i), new Rect(u, 1f - v, 1f - u * 2f, v));
            Draw(new Rect(rect.x + i, rect.yMax - i, mw, i), new Rect(u, 0f, 1f - u * 2f, v));
            Draw(new Rect(rect.x, rect.y + i, i, mh), new Rect(0f, v, u, 1f - v * 2f));
            Draw(new Rect(rect.xMax - i, rect.y + i, i, mh), new Rect(1f - u, v, u, 1f - v * 2f));
            Draw(new Rect(rect.x + i, rect.y + i, mw, mh), new Rect(u, v, 1f - u * 2f, 1f - v * 2f));

            void Draw(Rect screen, Rect uv) => GUI.DrawTextureWithTexCoords(screen, tex, uv);
        }

        public static void Panel(Rect r) => DrawNine(r, PanelFrame, InsetPanel);

        public static void Card(Rect r) => DrawNine(r, CardFrame, InsetCard);

        public static void Well(Rect r)
        {
            if (WellFill != null) GUI.DrawTexture(r, WellFill);
        }

        public static void DividerAt(Rect r) => DrawNine(r, Divider, InsetDivider);

        /// <summary>Quiet wash for a selected list row. Not a button - a row that looks pressable
        /// invites the player to hunt for what pressing it does.</summary>
        public static void Highlight(Rect r) => DrawNine(r, RowHighlight, 2);

        /// <summary>
        /// A skinned button. Returns true on click.
        ///
        /// Mouseover and click states are read from Unity directly rather than through
        /// Widgets.ButtonInvisible so the visual state and the click test can never disagree - the
        /// two-texture flicker that happens when they are computed separately is very visible on an
        /// ornate skin.
        /// </summary>
        public static bool Button(Rect rect, string label, bool selected = false,
            Color? textColor = null, GameFont font = GameFont.Small)
        {
            bool over = Mouse.IsOver(rect);
            bool held = over && IsHeld(rect);

            bool small = rect.height < SmallButtonThreshold;
            Texture2D tex = held
                ? (small ? ButtonSmallClick : ButtonClick)
                : (over || selected ? (small ? ButtonSmallMouseover : ButtonMouseover)
                                    : (small ? ButtonSmallIdle : ButtonIdle));

            DrawNine(rect, tex, small ? InsetButtonSmall : InsetButton);

            if (!label.NullOrEmpty())
            {
                GameFont prevFont = Text.Font;
                TextAnchor prevAnchor = Text.Anchor;
                Color prevColor = GUI.color;

                Text.Font = font;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = textColor ?? (selected || over ? Gold : Parchment);
                Widgets.Label(rect, label);

                GUI.color = prevColor;
                Text.Anchor = prevAnchor;
                Text.Font = prevFont;
            }

            return Widgets.ButtonInvisible(rect);
        }

        public static bool Checkbox(Rect rect, ref bool value, string label)
        {
            float box = Mathf.Min(rect.height, 22f);
            Rect boxRect = new Rect(rect.x, rect.y + (rect.height - box) / 2f, box, box);
            GUI.DrawTexture(boxRect, value ? CheckOn : CheckOff);

            Rect labelRect = new Rect(rect.x + box + 8f, rect.y, rect.width - box - 8f, rect.height);
            TextAnchor prev = Text.Anchor;
            Color prevColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = value ? Parchment : Dim;
            Widgets.Label(labelRect, label);
            GUI.color = prevColor;
            Text.Anchor = prev;

            if (Widgets.ButtonInvisible(rect))
            {
                value = !value;
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                return true;
            }

            return false;
        }

        public static bool Radio(Rect rect, bool selected, string label)
        {
            float box = Mathf.Min(rect.height, 20f);
            Rect boxRect = new Rect(rect.x, rect.y + (rect.height - box) / 2f, box, box);
            GUI.DrawTexture(boxRect, selected ? RadioOn : RadioOff);

            Rect labelRect = new Rect(rect.x + box + 8f, rect.y, rect.width - box - 8f, rect.height);
            TextAnchor prev = Text.Anchor;
            Color prevColor = GUI.color;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = selected ? Parchment : Dim;
            Widgets.Label(labelRect, label);
            GUI.color = prevColor;
            Text.Anchor = prev;

            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>Rail and handle are separate textures - the rail stretches, the handle is placed
        /// at the computed position. A fused source cannot do this without stretching the handle.</summary>
        public static float Slider(Rect rect, float value, float min, float max)
        {
            Rect railRect = new Rect(rect.x, rect.y + rect.height / 2f - 8f, rect.width, 16f);
            DrawNine(railRect, SliderRail, InsetRail);

            float pct = Mathf.InverseLerp(min, max, value);
            float handle = Mathf.Min(rect.height, 26f);
            Rect handleRect = new Rect(
                rect.x + (rect.width - handle) * pct,
                rect.y + rect.height / 2f - handle / 2f,
                handle, handle);
            GUI.DrawTexture(handleRect, SliderHandle);

            // Invisible vanilla slider underneath carries the drag behaviour, so keyboard and
            // click-to-position work exactly as players expect them to.
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0f);
            float result = Widgets.HorizontalSlider(rect, value, min, max);
            GUI.color = prev;
            return result;
        }

        public static void Label(Rect rect, string text, Color color, GameFont font = GameFont.Small,
            TextAnchor anchor = TextAnchor.UpperLeft)
        {
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;
            Color prevColor = GUI.color;

            Text.Font = font;
            Text.Anchor = anchor;
            GUI.color = color;
            Widgets.Label(rect, text);

            GUI.color = prevColor;
            Text.Anchor = prevAnchor;
            Text.Font = prevFont;
        }
    }
}
