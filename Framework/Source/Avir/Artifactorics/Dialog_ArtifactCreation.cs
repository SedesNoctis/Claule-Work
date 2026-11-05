using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Artifactorics
{
    /// <summary>
    /// The artificer's sheet - the creation panel opened from the artificer spot (or from a setting
    /// that still has no heart). Same parchment frame as the future artifact window:
    ///   left   - rim metals (jeweller's work: crafting + artistic)
    ///   centre - the assembled heart, who can do each half, warnings, order buttons, queue
    ///   right  - heart metals (the idea: intellectual)
    /// Metals that are short in stock, or that no colonist is skilled enough for, are drawn grey
    /// but can still be ordered (the bill waits, like any vanilla bill). Metals that may not be
    /// used in that place at all (essentium / tisserium as a rim, the rim's own metal as a heart)
    /// are grey and locked.
    /// </summary>
    public partial class Dialog_ArtifactCreation : Window
    {
        private readonly Building_ArtificerSpot spot;
        private readonly CompArtifact targetSetting;   // heart-only mode when set

        private ArtifactMetalDef rim;
        private ArtifactMetalDef heart;
        private Vector2 rimScroll, heartScroll, queueScroll;

        // Dark iron-gall ink: readable on the parchment at small sizes.
        private static readonly Color Ink = new Color(0.03f, 0.018f, 0.008f);
        private static readonly Color InkSoft = new Color(0.10f, 0.06f, 0.025f);
        private static readonly Color InkGrey = new Color(0.22f, 0.17f, 0.12f);
        private static readonly Color Warn = new Color(0.50f, 0.05f, 0.02f);
        private static readonly Color Gold = new Color(0.70f, 0.50f, 0.12f);

        // Section bounds measured on Panel.png (fractions of the full panel). Side columns start
        // below the corner flourishes and stop above the bottom ones.
        private const float LeftX0 = 0.056f, LeftX1 = 0.232f;
        private const float MidX0 = 0.262f, MidX1 = 0.740f;
        private const float RightX0 = 0.770f, RightX1 = 0.944f;
        private const float SideTopY = 0.165f, SideBotY = 0.845f;
        private const float MidTopY = 0.120f, MidBotY = 0.890f;
        private const float MidFlourishX = 0.070f;   // corner flourishes inside the centre section

        private const float RowH = 48f;

        private bool HeartOnly => targetSetting != null;

        public Dialog_ArtifactCreation(Building_ArtificerSpot spot, CompArtifact targetSetting)
        {
            this.spot = spot;
            this.targetSetting = targetSetting;
            forcePause = true;            // decided Oct 2, 2026: creating artifacts pauses the game
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            doCloseX = false;
            doWindowBackground = false;
            drawShadow = false;          // the frame has rounded, transparent corners
            draggable = true;
            preventCameraMotion = false;

            if (HeartOnly) rim = targetSetting.rimMetal;
            else rim = ArtifactoricsDatabase.Metals.FirstOrDefault(m => m.allowedAsRim && ArtifactCraftUtility.SettingRecipe(m) != null && ArtifactCraftUtility.SettingRecipe(m).AvailableNow);
        }

        private bool sheetMax;
        private Rect sheetNormal;

        public override Vector2 InitialSize
        {
            get
            {
                float w = Mathf.Min(1280f, Verse.UI.screenWidth - 40f);
                float h = w * (941f / 1672f);
                if (h > Verse.UI.screenHeight - 40f) { h = Verse.UI.screenHeight - 40f; w = h * (1672f / 941f); }
                return new Vector2(w, h);
            }
        }

        protected override float Margin => 0f;

        private Map Map => spot?.Map;

        private static Rect Sub(Rect r, float x0, float y0, float x1, float y1)
        {
            return new Rect(r.x + r.width * x0, r.y + r.height * y0, r.width * (x1 - x0), r.height * (y1 - y0));
        }

        public override void PostOpen()
        {
            base.PostOpen();
            SheetWindow.StartMaximized(this, ref sheetMax, ref sheetNormal);   // whole screen by default
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (spot == null || !spot.Spawned) { Close(); return; }
            if (HeartOnly && (targetSetting.parent.Destroyed || targetSetting.HasHeart || targetSetting.rimMetal == null)) { Close(); return; }

            GUI.DrawTexture(inRect, ArtifactoricsTex.Panel, ScaleMode.StretchToFill);

            Rect close = new Rect(inRect.xMax - inRect.width * 0.052f - 26f, inRect.y + inRect.height * 0.085f, 24f, 24f);
            SheetWindow.MaximizeButton(this, close, ref sheetMax, ref sheetNormal);
            Rect left = Sub(inRect, LeftX0, SideTopY, LeftX1, SideBotY);
            Rect mid = Sub(inRect, MidX0, MidTopY, MidX1, MidBotY);
            Rect right = Sub(inRect, RightX0, SideTopY, RightX1, SideBotY);

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;
            try
            {
                if (itemsMode && !HeartOnly)
                {
                    DrawItemColumn(left);
                    DrawItemRight(right);
                    DrawItemCentre(mid, inRect.width * MidFlourishX);
                }
                else if (buildingsMode && !HeartOnly)
                {
                    DrawBuildingsLeft(left);
                    DrawBuildingsRight(right);
                    DrawBuildingsCentre(mid, inRect.width * MidFlourishX);
                }
                else if (amuletMode && !HeartOnly)
                {
                    DrawAmuletLeft(left);
                    DrawAmuletRight(right);
                    DrawAmuletMiddle(mid, inRect.width * MidFlourishX);
                }
                else if (stoneMode && !HeartOnly)
                {
                    DrawAbilityColumn(left);
                    DrawStoneMetalColumn(right);
                    DrawStoneCentre(mid, inRect.width * MidFlourishX);
                }
                else if (inlayMode && !HeartOnly)
                {
                    DrawArtifactColumn(left);
                    DrawGarmentColumn(right);
                    DrawInlayCentre(mid, inRect.width * MidFlourishX);
                }
                else
                {
                    DrawRimColumn(left);
                    DrawHeartColumn(right);
                    DrawCentre(mid, inRect.width * MidFlourishX);
                }
            }
            finally
            {
                GUI.color = oldColor;
                Text.Anchor = oldAnchor;
                Text.Font = oldFont;
            }

            if (SheetWindow.CloseButton(close)) Close();
        }

        // ------------------------------------------------------------------ columns

        private void Header(ref Rect r, string title, string sub)
        {
            GUI.color = Ink;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            ArtInk.Label(new Rect(r.x + 4f, r.y, r.width, 34f), title);
            Text.Font = GameFont.Small;
            GUI.color = InkSoft;
            ArtInk.Label(new Rect(r.x + 4f, r.y + 32f, r.width, 22f), sub);
            GUI.color = Color.white;
            r.yMin += 60f;
        }

        private static Rect ViewFor(Rect outer, int rows)
        {
            float h = rows * RowH;
            return new Rect(0f, 0f, h > outer.height ? outer.width - 16f : outer.width, h);
        }

        private void DrawRimColumn(Rect r)
        {
            Header(ref r, "Rim", "jeweller: crafting + artistic");
            List<ArtifactMetalDef> metals = ArtifactoricsDatabase.Metals;
            Rect view = ViewFor(r, metals.Count);
            Widgets.BeginScrollView(r, ref rimScroll, view);
            float y = 0f;
            foreach (ArtifactMetalDef m in metals)
            {
                RecipeDef rec = ArtifactCraftUtility.SettingRecipe(m);
                if (rec != null && !rec.AvailableNow) continue;   // not researched yet: not shown
                bool forbidden = !m.allowedAsRim || rec == null;
                bool locked = forbidden || (HeartOnly && m != rim);
                string note = forbidden ? "cannot be a rim"
                            : HeartOnly && m != rim ? "this setting has another rim"
                            : null;
                if (MetalRow(new Rect(0f, y, view.width, RowH - 4f), m, rec, ArtifactCraftStep.Setting, m == rim, locked, note))
                {
                    rim = m;
                    if (heart == rim) heart = null;   // rim and heart may not share a metal
                }
                y += RowH;
            }
            Widgets.EndScrollView();
        }

        private void DrawHeartColumn(Rect r)
        {
            Header(ref r, "Heart", "the idea: intellectual");
            List<ArtifactMetalDef> metals = ArtifactoricsDatabase.Metals;
            int extra = HeartOnly ? 0 : 1;
            Rect view = ViewFor(r, metals.Count + extra);
            Widgets.BeginScrollView(r, ref heartScroll, view);
            float y = 0f;
            if (!HeartOnly)
            {
                Rect none = new Rect(0f, y, view.width, RowH - 4f);
                if (heart == null) DrawSelection(none);
                else if (Mouse.IsOver(none)) Widgets.DrawBoxSolid(none, new Color(1f, 0.9f, 0.6f, 0.14f));
                GUI.color = Ink;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                ArtInk.Label(new Rect(none.x + 40f, none.y, none.width - 40f, none.height), "None - set it later");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(none)) heart = null;
                y += RowH;
            }
            foreach (ArtifactMetalDef m in metals)
            {
                RecipeDef rec = ArtifactCraftUtility.HeartRecipe(m);
                if (rec != null && !rec.AvailableNow) continue;   // not researched yet: not shown
                bool forbidden = !m.allowedAsHeart || rec == null;
                bool sameAsRim = !forbidden && rim != null && m == rim;
                string note = forbidden ? "cannot be a heart" : sameAsRim ? "same metal as the rim" : null;
                if (MetalRow(new Rect(0f, y, view.width, RowH - 4f), m, rec, ArtifactCraftStep.Heart, m == heart,
                        forbidden || sameAsRim, note))
                    heart = m;
                y += RowH;
            }
            Widgets.EndScrollView();
        }

        /// <summary>One metal line. Returns true when clicked (and not locked).</summary>
        private bool MetalRow(Rect row, ArtifactMetalDef m, RecipeDef rec, ArtifactCraftStep step, bool selected, bool locked, string lockNote)
        {
            int need = ArtifactCraftUtility.MetalCount(rec, m);
            int have = ArtifactCraftUtility.InStock(Map, m);
            int skill = ArtifactCraftUtility.MinSkill(rec);
            bool shortStock = !locked && have < need;
            bool noWorker = !locked && ArtifactCraftUtility.BestFor(Map, rec, step) == null;
            bool grey = locked || shortStock || noWorker;

            if (selected) DrawSelection(row);
            else if (!locked && Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));

            float iconA = locked ? 0.35f : grey ? 0.6f : 1f;
            Rect icon = new Rect(row.x + 4f, row.y + (row.height - 30f) / 2f, 30f, 30f);
            if (step == ArtifactCraftStep.Setting || step == ArtifactCraftStep.Necklace) DrawRimIcon(icon, m, iconA);
            else if (step == ArtifactCraftStep.Stone) AbilityStoneUI.Draw(icon, m, iconA);
            else DrawGem(icon, m, iconA);

            Rect text = new Rect(row.x + 40f, row.y + 1f, row.width - 40f, row.height);
            Text.Font = GameFont.Small;
            GUI.color = locked ? InkGrey : Ink;
            ArtInk.Label(new Rect(text.x, text.y, text.width, 22f), m.LabelCap);

            string line;
            if (locked && lockNote != null) line = lockNote;
            else
            {
                line = "need " + need + ", have " + have;
                if (skill > 0) line += ", skill " + skill + "+";
            }
            GUI.color = locked ? InkGrey : shortStock || noWorker ? Warn : InkSoft;
            ArtInk.Label(new Rect(text.x, text.y + 21f, text.width, 22f), line);
            GUI.color = Color.white;

            string role = step == ArtifactCraftStep.Setting ? ArtifactMetalPreview.RimText(m)
                        : step == ArtifactCraftStep.Heart ? ArtifactMetalPreview.HeartText(m)
                        : step == ArtifactCraftStep.Necklace || step == ArtifactCraftStep.AmuletCore ? AmuletMetalTip(m, step) : null;
            if (!locked)
            {
                var tip = new List<string>();
                if (!m.description.NullOrEmpty()) tip.Add(m.description);
                if (!role.NullOrEmpty()) tip.Add(role + (step == ArtifactCraftStep.Setting || step == ArtifactCraftStep.Heart ? "\n\n" + ArtifactMetalPreview.FocusMeaning : ""));
                if (shortStock) tip.Add("Not enough " + m.material.label + " in stockpiles (" + have + " / " + need + "). The order will wait for it.");
                if (noWorker) tip.Add("No colonist has the skill for this yet (" + skill + "+ in " +
                                      (step == ArtifactCraftStep.Setting || step == ArtifactCraftStep.Necklace ? "crafting and artistic" : "intellectual") + ").");
                if (tip.Count > 0) TooltipHandler.TipRegion(row, string.Join("\n\n", tip));
            }
            else if (lockNote != null)
                TooltipHandler.TipRegion(row, lockNote.CapitalizeFirst() + "." + (role.NullOrEmpty() ? "" : "\n\n" + role));

            if (locked) return false;
            if (Widgets.ButtonInvisible(row))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                return true;
            }
            return false;
        }

        private static void DrawSelection(Rect r)
        {
            Widgets.DrawBoxSolid(r, new Color(Gold.r, Gold.g, Gold.b, 0.20f));
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;
        }

        private static void DrawGem(Rect r, ArtifactMetalDef m, float alpha)
        {
            Color c = m.GemColorAt(Time.realtimeSinceStartup);
            GUI.color = new Color(c.r, c.g, c.b, alpha);
            GUI.DrawTexture(r, ArtifactoricsTex.GemBase, ScaleMode.ScaleToFit);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(r, ArtifactoricsTex.GemHighlight, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }

        private static void DrawRimIcon(Rect r, ArtifactMetalDef m, float alpha)
        {
            Color c = m.RimColor;
            GUI.color = new Color(c.r, c.g, c.b, alpha);
            GUI.DrawTexture(r.ExpandedBy(2f), ArtifactoricsTex.HeartRimMask, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ centre

        private void DrawCentre(Rect r, float flourish)
        {
            GUI.color = Ink;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            if (HeartOnly) ArtInk.Label(new Rect(r.x, r.y, r.width, 34f), "Setting the heart");
            else ModeTabs(new Rect(r.x, r.y, r.width, 34f));
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            // The heart: all layers share one canvas, so they go into the same square.
            float size = Mathf.Min(r.width * 0.50f, r.height * 0.26f);
            Rect heartRect = new Rect(r.center.x - size / 2f, r.y + 40f, size, size);
            Rect guides = heartRect.ExpandedBy(size * 0.18f);
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            GUI.DrawTexture(guides, ArtifactoricsTex.HeartGuides, ScaleMode.ScaleToFit);
            if (rim != null)
            {
                GUI.color = rim.RimColor;
                GUI.DrawTexture(heartRect, ArtifactoricsTex.HeartRimMask, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.25f);
                GUI.DrawTexture(heartRect, ArtifactoricsTex.HeartRimMask, ScaleMode.ScaleToFit);
            }
            if (heart != null)
            {
                GUI.color = heart.GemColorAt(Time.realtimeSinceStartup);
                GUI.DrawTexture(heartRect, ArtifactoricsTex.GemBase, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                GUI.DrawTexture(heartRect, ArtifactoricsTex.GemHighlight, ScaleMode.ScaleToFit);
            }
            // The setting is the jeweller's metal too: tinted like the rim.
            if (rim != null)
            {
                GUI.color = rim.RimColor;
                GUI.DrawTexture(heartRect, ArtifactoricsTex.HeartSettingMask, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.color = Color.white;
                GUI.DrawTexture(heartRect, ArtifactoricsTex.HeartSetting, ScaleMode.ScaleToFit);
            }
            GUI.color = Color.white;

            // Info block: two halves, side by side.
            float y = heartRect.yMax + size * 0.08f;
            Rect info = new Rect(r.x + 14f, y, r.width - 28f, r.yMax - y);
            float half = info.width / 2f - 10f;
            const float InfoH = 104f;
            Rect setInfo = new Rect(info.x, info.y, half, InfoH);
            Rect heartInfo = new Rect(info.x + half + 20f, info.y, half, InfoH);

            RecipeDef setRec = ArtifactCraftUtility.SettingRecipe(rim);
            RecipeDef heartRec = ArtifactCraftUtility.HeartRecipe(heart);
            if (HeartOnly)
                StepInfo(setInfo, "Setting" + (rim != null ? " - " + rim.label + " rim" : ""), targetSetting.settingQuality.GetLabel().CapitalizeFirst() + " work by " +
                                             (targetSetting.settingMaker ?? "unknown") + ".", null, ArtifactCraftStep.Setting, false);
            else
                StepInfo(setInfo, "Setting" + (rim != null ? " - " + rim.label + " rim" : ""), null, setRec, ArtifactCraftStep.Setting, rim == null);
            StepInfo(heartInfo, "Heart" + (heart != null ? " - " + heart.label : ""), heart == null ? "No heart chosen. The setting can wait for one." : null,
                heartRec, ArtifactCraftStep.Heart, false);

            // What the chosen metals will favour - shown before anything is spent.
            Rect focusR = new Rect(info.x, info.y + InfoH + 2f, info.width, Mathf.Clamp(r.height * 0.20f, 72f, 170f));
            DrawFocusPreview(focusR);

            // Buttons - centred, kept clear of the centre section's bottom corner flourishes.
            float by = focusR.yMax + 6f;
            const float Gap = 24f;
            float bw = Mathf.Min(230f, (r.width - 2f * flourish - Gap) / 2f);
            float bh = bw * (117f / 480f);
            bool sameMetal = rim != null && heart == rim;
            if (HeartOnly)
            {
                if (ParchmentButton(new Rect(r.center.x - bw / 2f, by, bw, bh), "Order heart", heartRec != null && !sameMetal))
                    OrderHeart(heartRec, rim);
            }
            else
            {
                if (ParchmentButton(new Rect(r.center.x - Gap / 2f - bw, by, bw, bh), "Order setting", setRec != null))
                    OrderSetting(setRec);
                if (ParchmentButton(new Rect(r.center.x + Gap / 2f, by, bw, bh), "Order setting + heart",
                        setRec != null && heartRec != null && !sameMetal))
                {
                    OrderSetting(setRec);
                    OrderHeart(heartRec, rim);
                }
            }

            // Queue - kept clear of the centre section's bottom corner flourishes.
            float qy = by + bh + 8f;
            Rect queue = new Rect(r.x + flourish, qy, r.width - 2f * flourish, r.yMax - qy);
            DrawQueue(queue);
        }

        private Vector2 focusScroll;

        private void DrawFocusPreview(Rect r)
        {
            GUI.color = Ink;
            Text.Font = GameFont.Small;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 22f), "Paths it will favour");
            Rect outer = new Rect(r.x, r.y + 22f, r.width, r.height - 22f);
            Text.Font = GameFont.Tiny;
            string text = ArtifactMetalPreview.CombinedText(rim, heart);
            float th = Text.CalcHeight(text, outer.width - 16f);
            Rect view = new Rect(0f, 0f, th > outer.height ? outer.width - 16f : outer.width, th);
            GUI.color = InkSoft;
            Widgets.BeginScrollView(outer, ref focusScroll, view);
            ArtInk.Label(view, text);
            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(r, ArtifactMetalPreview.FocusMeaning
                + "\n\nRim and heart together, before heart quality. Hover a metal in the side lists for its own table.");
        }

        private void StepInfo(Rect r, string title, string fixedText, RecipeDef rec, ArtifactCraftStep step, bool missing)
        {
            GUI.color = Ink;
            Text.Font = GameFont.Small;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 24f), title.CapitalizeFirst());
            string body;
            bool warn = false;
            if (fixedText != null) body = fixedText;
            else if (missing || rec == null) { body = "Choose a rim - without a rim the artifact does not work."; warn = true; }
            else
            {
                Pawn best = ArtifactCraftUtility.BestFor(Map, rec, step);
                int skill = ArtifactCraftUtility.MinSkill(rec);
                string skills = step == ArtifactCraftStep.Setting ? "crafting and artistic" : "intellectual";
                if (best == null)
                {
                    body = "Nobody can do this yet: needs " + skills + " " + skill + "+.";
                    warn = true;
                }
                else
                {
                    string lv = step == ArtifactCraftStep.Setting
                        ? "crafting " + best.skills.GetSkill(SkillDefOf.Crafting).Level + ", artistic " + best.skills.GetSkill(SkillDefOf.Artistic).Level
                        : "intellectual " + best.skills.GetSkill(SkillDefOf.Intellectual).Level;
                    body = "Best hands: " + best.LabelShort + " (" + lv + ").";
                }
                ArtifactMetalDef m = rec.GetModExtension<ArtifactRecipeExtension>()?.metal;
                int need = ArtifactCraftUtility.MetalCount(rec, m);
                int have = ArtifactCraftUtility.InStock(Map, m);
                body += "\n" + need + " " + m.material.label + (have < need ? " - only " + have + " in stock, the order will wait." : ".");
                if (have < need) warn = true;
            }
            GUI.color = warn ? Warn : InkSoft;
            ArtInk.Label(new Rect(r.x, r.y + 24f, r.width, r.height - 24f), body);
            GUI.color = Color.white;
        }

        private bool ParchmentButton(Rect r, string label, bool enabled)
        {
            bool over = enabled && Mouse.IsOver(r);
            Texture2D tex = !over ? ArtifactoricsTex.ButtonNormal
                          : Input.GetMouseButton(0) ? ArtifactoricsTex.ButtonPressed : ArtifactoricsTex.ButtonHover;
            GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            GUI.color = enabled ? Ink : InkGrey;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Small;
            ArtInk.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (!enabled) return false;
            if (Widgets.ButtonInvisible(r))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                return true;
            }
            return false;
        }

        private void DrawQueue(Rect r)
        {
            List<Bill> bills = spot.BillStack.Bills
                .Where(b => b.recipe?.GetModExtension<ArtifactRecipeExtension>() != null || IsAmuletRecipe(b.recipe) || IsItemRecipe(b.recipe)).ToList();
            GUI.color = InkSoft;
            Text.Font = GameFont.Small;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 22f), bills.Count == 0 ? "Nothing ordered here yet." : "Ordered here:");
            if (bills.Count == 0) { GUI.color = Color.white; return; }
            Rect outer = new Rect(r.x, r.y + 22f, r.width, r.height - 22f);
            float h = bills.Count * 22f;
            Rect view = new Rect(0f, 0f, h > outer.height ? outer.width - 16f : outer.width, h);
            Widgets.BeginScrollView(outer, ref queueScroll, view);
            float y = 0f;
            Bill remove = null;
            foreach (Bill b in bills)
            {
                Rect line = new Rect(0f, y, view.width, 22f);
                GUI.color = b.suspended ? InkGrey : Ink;
                string extra = b is Bill_ArtifactHeart bh && bh.requiredRim != null ? " (for a " + bh.requiredRim.label + " rim)"
                             : b is Bill_AmuletCore bc && bc.requiredNecklace != null ? " (for a " + bc.requiredNecklace.label + " necklace)"
                             : b is Bill_Production bp && bp.PawnRestriction != null ? " (" + bp.PawnRestriction.LabelShort + ")" : "";
                ArtInk.Label(new Rect(line.x, line.y, line.width - 24f, 22f), b.LabelCap + extra + (b.suspended ? " - suspended" : ""));
                GUI.color = Color.white;
                if (Widgets.ButtonImage(new Rect(line.xMax - 20f, line.y + 3f, 16f, 16f), TexButton.Delete)) remove = b;
                y += 22f;
            }
            Widgets.EndScrollView();
            if (remove != null) spot.BillStack.Delete(remove);
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ orders

        private void OrderSetting(RecipeDef rec)
        {
            if (rec == null) return;
            Bill bill = rec.MakeNewBill(null);   // vanilla picks the unfinished-thing bill type
            spot.BillStack.AddBill(bill);
            Messages.Message("Ordered: " + rec.label + ".", spot, MessageTypeDefOf.SilentInput, false);
        }

        private void OrderHeart(RecipeDef rec, ArtifactMetalDef forRim)
        {
            if (rec == null) return;
            var bill = new Bill_ArtifactHeart(rec, forRim);
            spot.BillStack.AddBill(bill);
            Messages.Message("Ordered: " + rec.label + (forRim != null ? " for a " + forRim.label + " rim" : "") + ".",
                spot, MessageTypeDefOf.SilentInput, false);
        }
    }
}
