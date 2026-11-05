using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// "Change feather colours" window. This is Anthrosonae's Window_ColorPicker ported over
    /// (Willow, Erin, Taranchuk), with their FurGene/FurColorDef swapped for ours and the
    /// translation keys replaced by literals: live portrait, Hue/Sat/Lum/Hex fields, colour
    /// wheel, swatch palette built from the gene's colour list, and a First/Second channel
    /// switch (back, breast, wings) with a current/old readback.
    ///
    /// Any colour can be dialled in by hand - the palette is a shortcut, not a restriction.
    /// What the palette does restrict is pawn GENERATION, which only rolls presets with a
    /// weight above zero.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Window_AvirFeatherColors : Window
    {
        // Three channels: 0 - first (back), 1 - second (breast), 2 - the wings (their own colour,
        // or - until it is touched - the first colour, as before).
        private const int First = 0, Second = 1, Wings = 2, Tips = 3;
        private readonly Color[] col = new Color[4], oldCol = new Color[4];
        private readonly string[] hex = new string[4], lumBuf = new string[4];
        private readonly float[] lum = new float[4];
        /// <summary>The wing tips carry a colour of their own (else they follow the breast).</summary>
        private bool tipsOwn;
        /// <summary>The wing tip pattern: -1 - as the pawn was born with.</summary>
        private int pattern;
        private int ch;
        /// <summary>The wings carry a colour of their own (else they follow the first channel).</summary>
        private bool wingsOwn;

        private bool colorTwoChosen => ch == Second;
        private bool hsvColorWheelDragging;

        private readonly Gene_AvirFeathers gene;
        private readonly List<AvirFeatherColorDef> paletteDefs;

        private readonly string[][] tfBuf = { new string[6], new string[6], new string[6], new string[6] };
        private readonly Color[] tfColBuf = new Color[4];
        private string previousFocusedControlName;

        private Rot4 pawnRot = Rot4.South;

        public static Widgets.ColorComponents visibleColorTextfields =
            Widgets.ColorComponents.Hue | Widgets.ColorComponents.Sat;

        public static Widgets.ColorComponents editableColorTextfields =
            Widgets.ColorComponents.Hue | Widgets.ColorComponents.Sat;

        // RimWorld 1.6 keeps ButSize and focusableControlNames on Dialog_ColorPickerBase as
        // protected members, so we cannot borrow them; our own copies do the same job.
        private static readonly Vector2 ButSize = new Vector2(150f, 38f);

        private static readonly List<string> FocusableControlNames = new List<string>
        {
            "AvirFeatherColorPickerHeader",
            "colorTextfields_hue",
            "colorTextfields_sat",
            "colorTextfields_val"
        };

        private static readonly Vector3 PortraitOffset = new Vector3(0f, 0f, 0.1f);
        private static readonly Texture2D RotateButton = ContentFinder<Texture2D>.Get("UI/Widgets/RotRight");

        public override Vector2 InitialSize => new Vector2(800f, 440f);

        public Window_AvirFeatherColors(Gene_AvirFeathers gene)
        {
            this.gene = gene;

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            closeOnAccept = false;

            col[First] = gene.colorOne;
            col[Second] = gene.colorTwo ?? Color.white;
            wingsOwn = gene.wingColor.HasValue;
            col[Wings] = gene.wingColor ?? gene.colorOne;
            tipsOwn = gene.wingTipColor.HasValue;
            col[Tips] = gene.wingTipColor ?? gene.colorTwo ?? Color.white;
            pattern = gene.wingPattern;
            for (int i = 0; i < 4; i++)
            {
                oldCol[i] = col[i];
                hex[i] = "#" + ColorUtility.ToHtmlStringRGB(col[i]);
                lum[i] = AvirFeatherColorUtils.BrightnessLevel(col[i]);
                lumBuf[i] = lum[i].ToString();
            }

            // The gene's own roll list plus every picker-only colour (weight 0). Same split
            // Anthrosonae keeps between fur presets and fur colours.
            paletteDefs = new List<AvirFeatherColorDef>();
            List<AvirFeatherColorDef> allowed = gene.def.GetModExtension<FeatherColorsExtension>()?.allowedColors;
            if (allowed != null)
            {
                paletteDefs.AddRange(allowed);
            }
            foreach (AvirFeatherColorDef pickerOnly in DefDatabase<AvirFeatherColorDef>.AllDefsListForReading)
            {
                if (pickerOnly.selectionWeight <= 0f && !paletteDefs.Contains(pickerOnly))
                {
                    paletteDefs.Add(pickerOnly);
                }
            }
            paletteDefs = paletteDefs.OrderBy(d => d.displayOrder).ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (TextBlock.Default())
            {
                Rect portrait = new Rect(inRect.x, inRect.y, 190f, 240f);
                DrawPortrait(portrait);

                Rect layoutRect = new Rect(inRect.x + 200f, inRect.y, inRect.width - 200f, 240f);
                RectDivider layout = new RectDivider(layoutRect, 195906069);

                HeaderRow(ref layout);
                layout.NewRow(0f);

                Color color = col[ch];
                Color oldColor = color;

                ColorPalette(ref layout, ref color, out float paletteHeight);
                if (oldColor != color)
                {
                    ResetColorValues(color);
                }

                ColorFields(ref layout, ref color, ref hex[ch], ref lum[ch], ref lumBuf[ch],
                    ref tfBuf[ch], ref tfColBuf[ch], out Vector2 size);

                float height = Mathf.Max(paletteHeight, 128f, size.y);
                RectDivider row = layout.NewRow(height);
                row.NewCol(size.x);
                row.NewCol(250f, HorizontalJustification.Right);

                oldColor = color;
                Widgets.HSVColorWheel(row.Rect.ContractedBy((row.Rect.width - 128f) / 2f,
                    (row.Rect.height - 128f) / 2f), ref color, ref hsvColorWheelDragging, 1f);
                if (oldColor != color)
                {
                    ResetColorValues(color);
                }

                // Write the edited channel back BEFORE the readback and the buttons. Doing it
                // afterwards (as the original did) left both one frame behind, so Accept could
                // commit the previous colour and the readback showed the wrong swatch.
                if (col[ch] != color && ch == Wings) wingsOwn = true;
                if (col[ch] != color && ch == Tips) tipsOwn = true;
                col[ch] = color;
                if (!wingsOwn) { col[Wings] = col[First]; lum[Wings] = lum[First]; }
                if (!tipsOwn) { col[Tips] = col[Second]; lum[Tips] = lum[Second]; }

                layout = new RectDivider(new Rect(inRect.x, portrait.yMax + 69f, inRect.width,
                    inRect.height - portrait.height - 69f), 65436135);
                BottomButtons(ref layout);
                layout.NewRow(0f, VerticalJustification.Bottom);

                ColorReadback(layout, AvirFeatherColorUtils.SetBrightness(col[ch], (int)lum[ch]), oldCol[ch]);

                // the channel: back, breast, wings
                Rect channelRect = new Rect(inRect.x, portrait.yMax + 10f, 117f, 24f);
                Widgets.Label(channelRect, "Colour channel");
                float cx = channelRect.xMax;
                string[] names = { "Back", "Breast", "Wings", "Wing tips" };
                float[] widths = { 46f, 52f, 52f, 72f };
                for (int i = 0; i < 4; i++)
                {
                    // the button before its label, so it reads as that label's
                    if (Widgets.RadioButton(new Vector2(cx, channelRect.y), ch == i)) ch = i;
                    Rect lr = new Rect(cx + 28f, channelRect.y, widths[i], 24f);
                    Widgets.Label(lr, names[i]);
                    if (Widgets.ButtonInvisible(lr)) ch = i;
                    cx = lr.xMax + 14f;
                }
                if (ch == Wings)
                {
                    Rect follow = new Rect(cx + 6f, channelRect.y, 170f, 24f);
                    if (wingsOwn) { if (Widgets.ButtonText(follow, "Wings follow the back")) { wingsOwn = false; col[Wings] = col[First]; lum[Wings] = lum[First]; ResetColorValues(col[Wings]); } }
                    else Widgets.Label(follow, "(follow the back)");
                }
                if (ch == Tips)
                {
                    Rect follow = new Rect(cx + 6f, channelRect.y, 170f, 24f);
                    if (tipsOwn) { if (Widgets.ButtonText(follow, "Tips follow the breast")) { tipsOwn = false; col[Tips] = col[Second]; lum[Tips] = lum[Second]; ResetColorValues(col[Tips]); } }
                    else Widgets.Label(follow, "(follow the breast)");
                }

                // the wing tip pattern: how far up the wing the tip colour reaches
                Rect patRect = new Rect(inRect.x, channelRect.yMax + 6f, 117f, 24f);
                Widgets.Label(patRect, "Wing pattern");
                float px = patRect.xMax;
                bool membranous = Patch_AvirAShacsaignSpawn.IsSubrace(gene.pawn);   // the Shaksai's wings have one pattern
                if (membranous) Widgets.Label(new Rect(px, patRect.y, 400f, 24f), "membranous wings: bones and membrane (tips colour the membrane)");
                else for (int i = -1; i < WingPatterns.Patterns.Length; i++)
                {
                    string lbl = i < 0 ? "as born" : WingPatterns.PatternLabels[i];
                    if (Widgets.RadioButton(new Vector2(px, patRect.y), pattern == i)) pattern = i;
                    Rect lr = new Rect(px + 28f, patRect.y, Text.CalcSize(lbl).x + 4f, 24f);
                    Widgets.Label(lr, lbl);
                    if (Widgets.ButtonInvisible(lr)) pattern = i;
                    px = lr.xMax + 14f;
                }

                TabControl();
                if (Event.current.type == EventType.Layout)
                {
                    previousFocusedControlName = GUI.GetNameOfFocusedControl();
                }
            }
        }

        private void DrawPortrait(Rect rect)
        {
            Widgets.DrawMenuSection(rect);

            Color savedOne = gene.colorOne;
            Color? savedTwo = gene.colorTwo;

            Color? savedWings = gene.wingColor;
            Color? savedTips = gene.wingTipColor;
            int savedPattern = gene.wingPattern;
            gene.wingTipColor = tipsOwn ? AvirFeatherColorUtils.SetBrightness(col[Tips], (int)lum[Tips]) : (Color?)null;
            gene.wingPattern = pattern;
            gene.colorOne = AvirFeatherColorUtils.SetBrightness(col[First], (int)lum[First]);
            gene.colorTwo = AvirFeatherColorUtils.SetBrightness(col[Second], (int)lum[Second]);
            gene.wingColor = wingsOwn ? AvirFeatherColorUtils.SetBrightness(col[Wings], (int)lum[Wings]) : (Color?)null;
            gene.ApplyColors();
            PortraitsCache.SetDirty(gene.pawn);

            RenderTexture image = PortraitsCache.Get(gene.pawn, new Vector2(200f, 200f), pawnRot,
                PortraitOffset, healthStateOverride: PawnHealthState.Mobile, cameraZoom: 1.1f,
                renderClothes: false, renderHeadgear: false);

            gene.colorOne = savedOne;
            gene.colorTwo = savedTwo;
            gene.wingColor = savedWings;
            gene.wingTipColor = savedTips;
            gene.wingPattern = savedPattern;
            gene.ApplyColors();

            GUI.DrawTexture(rect, image, ScaleMode.ScaleAndCrop);

            if (Widgets.ButtonImage(new Rect(rect.xMax - 24f, rect.y, 24f, 24f), RotateButton))
            {
                pawnRot = pawnRot.Rotated(RotationDirection.Clockwise);
            }
        }

        private static void HeaderRow(ref RectDivider layout)
        {
            using (new TextBlock(GameFont.Medium))
            {
                string label = "Change feather colours";
                RectDivider row = layout.NewRow(Text.CalcHeight(label, layout.Rect.width));
                GUI.SetNextControlName(FocusableControlNames[0]);
                Widgets.Label(row, label);
            }
        }

        private void BottomButtons(ref RectDivider layout)
        {
            RectDivider row = layout.NewRow(ButSize.y, VerticalJustification.Bottom);

            if (Widgets.ButtonText(row.NewCol(ButSize.x), "Cancel".Translate()))
            {
                Close();
            }
            if (Widgets.ButtonText(row.NewCol(ButSize.x, HorizontalJustification.Right),
                "Accept".Translate()))
            {
                // Commit both channels every time. Comparing against the opening colours
                // skipped the write whenever a channel had already been nudged back to its
                // starting value, which is another way the wrong colour got kept.
                gene.colorOne = AvirFeatherColorUtils.SetBrightness(col[First], (int)lum[First]);
                gene.colorTwo = AvirFeatherColorUtils.SetBrightness(col[Second], (int)lum[Second]);
                gene.wingColor = wingsOwn ? AvirFeatherColorUtils.SetBrightness(col[Wings], (int)lum[Wings]) : (Color?)null;
                gene.wingTipColor = tipsOwn ? AvirFeatherColorUtils.SetBrightness(col[Tips], (int)lum[Tips]) : (Color?)null;
                gene.wingPattern = pattern;
                gene.featherColor = null;        // hand-picked; no longer one of the presets
                gene.colorsInitialized = true; // ...but still initialised, so nothing re-rolls
                // and redrawn now: the preview restored the old colours and nothing else dirtied the graphics
                gene.ApplyColors();
                PortraitsCache.SetDirty(gene.pawn);
                Close();
            }
        }

        private void ColorFields(ref RectDivider layout, ref Color color, ref string hexValue, ref float lumValue,
            ref string lumBuf, ref string[] textfieldBuffers, ref Color textfieldColorBuffer, out Vector2 size)
        {
            RectAggregator aggregator = new RectAggregator(new Rect(layout.Rect.position, new Vector2(125f, 0f)), 195906069);
            bool changed = Widgets.ColorTextfields(ref aggregator, ref color, ref textfieldBuffers,
                ref textfieldColorBuffer, previousFocusedControlName, "colorTextfields",
                editableColorTextfields, visibleColorTextfields);
            size = aggregator.Rect.size;

            if (changed)
            {
                Color.RGBToHSV(color, out float h, out float s, out float _);
                color = Color.HSVToRGB(h, s, 1f);
                hexValue = "#" + ColorUtility.ToHtmlStringRGB(AvirFeatherColorUtils.SetBrightness(color, (int)lumValue));
            }

            Rect lumLabel = new Rect(layout.Rect.x, aggregator.Rect.yMax + 4f, 50f, 32f);
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(lumLabel, "Lum");
            }
            Rect lumRect = new Rect(lumLabel.xMax, lumLabel.y, 75f, 32f);
            float oldLum = lumValue;
            try
            {
                Widgets.TextFieldNumeric(lumRect, ref lumValue, ref lumBuf, 1f, 99f);
            }
            catch (Exception)
            {
                lumValue = 1f;
                lumBuf = lumValue.ToString();
            }
            if (!lumBuf.NullOrEmpty() && !Mathf.Approximately(oldLum, lumValue))
            {
                hexValue = "#" + ColorUtility.ToHtmlStringRGB(AvirFeatherColorUtils.SetBrightness(color, (int)lumValue));
            }

            if (Event.current.type == EventType.Layout)
            {
                previousFocusedControlName = GUI.GetNameOfFocusedControl();
            }

            Rect hexLabel = new Rect(lumLabel.x, lumLabel.yMax + 4f, 50f, 32f);
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(hexLabel, "Hex");
            }
            Rect hexRect = new Rect(hexLabel.xMax, hexLabel.y, 75f, 32f);
            string oldValue = hexValue;
            hexValue = Widgets.TextField(hexRect, hexValue).Trim();
            if (AvirFeatherColorUtils.TryGetColorFromHex(hexValue, out Color fromHex) && hexValue != oldValue)
            {
                color = fromHex;
                ResetColorValues(color);
            }

            if (Event.current.type == EventType.Layout)
            {
                previousFocusedControlName = GUI.GetNameOfFocusedControl();
            }
        }

        private void ColorPalette(ref RectDivider layout, ref Color color, out float paletteHeight)
        {
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                RectDivider divider = layout;
                RectDivider col = divider.NewCol(250f, HorizontalJustification.Right);

                List<Color> colors = (colorTwoChosen
                        ? paletteDefs.Where(d => !d.blacklistSecondary && d.secondaryColor.HasValue)
                            .Select(d => d.secondaryColor.Value)
                        : paletteDefs.Where(d => !d.blacklistPrimary).Select(d => d.primaryColor))
                    .Distinct().ToList();
                colors.SortByColor(c => c);

                Widgets.ColorSelector(col, ref color, colors, out paletteHeight, extraOnGUI: PaletteTooltip);
            }
        }

        private void PaletteTooltip(Color color, Rect boxRect)
        {
            AvirFeatherColorDef def = colorTwoChosen
                ? paletteDefs.FirstOrDefault(d => !d.blacklistSecondary && d.secondaryColor.HasValue && d.secondaryColor.Value == color)
                : paletteDefs.FirstOrDefault(d => !d.blacklistPrimary && d.primaryColor == color);

            if (def != null)
            {
                TooltipHandler.TipRegion(boxRect, def.LabelCap);
            }
        }

        private static void ColorReadback(Rect rect, Color color, Color oldColor)
        {
            rect.SplitVertically((rect.width - 26f) / 2f, out Rect left, out Rect right);
            RectDivider divider = new RectDivider(left, 195906069);

            string label = "Current colour";
            string label2 = "Old colour";
            float width = Mathf.Max(100f, label.GetWidthCached(), label2.GetWidthCached());

            RectDivider row = divider.NewRow(Text.LineHeight);
            Widgets.Label(row.NewCol(width), label);
            Widgets.DrawBoxSolid(row, color);

            RectDivider row2 = divider.NewRow(Text.LineHeight);
            Widgets.Label(row2.NewCol(width), label2);
            Widgets.DrawBoxSolid(row2, oldColor);

            RectDivider rightDivider = new RectDivider(right, 195906069);
            rightDivider.NewCol(26f);
        }

        private static void TabControl()
        {
            if (Event.current.type != EventType.KeyDown || Event.current.keyCode != KeyCode.Tab)
            {
                return;
            }

            bool forward = !Event.current.shift;
            Event.current.Use();

            string focused = GUI.GetNameOfFocusedControl();
            if (focused.NullOrEmpty())
            {
                focused = FocusableControlNames[0];
            }

            int index = FocusableControlNames.IndexOf(focused);
            if (index < 0)
            {
                index = FocusableControlNames.Count;
            }
            index = forward ? index + 1 : index - 1;
            if (index >= FocusableControlNames.Count)
            {
                index = 0;
            }
            else if (index < 0)
            {
                index = FocusableControlNames.Count - 1;
            }

            GUI.FocusControl(FocusableControlNames[index]);
        }

        private void ResetColorValues(Color color)
        {
            lum[ch] = AvirFeatherColorUtils.BrightnessLevel(color);
            lumBuf[ch] = lum[ch].ToString();
            hex[ch] = "#" + ColorUtility.ToHtmlStringRGB(AvirFeatherColorUtils.SetBrightness(color, (int)lum[ch]));
        }

        public override void Close(bool doCloseSound = true)
        {
            base.Close(doCloseSound);
            gene.ApplyColors();
        }
    }
}
