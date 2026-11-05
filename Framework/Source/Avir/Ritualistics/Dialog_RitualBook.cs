using System.Collections.Generic;
using System.Linq;
using Avir.Artifactorics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Ritualistics
{
    /// <summary>The ritual book (slice 1, read-only): the rituals the colony knows on the left, the
    /// circle as it would be drawn in the middle, what it does and every rune on the right.
    /// Opened from a circle it also assigns the chosen ritual.</summary>
    public class Dialog_RitualBook : Window
    {
        private static Color Ink => FlowStyle.Ink;
        private static Color InkSoft => FlowStyle.InkSoft;
        private static Color InkGrey => FlowStyle.InkGrey;
        private static Color Gold => FlowStyle.Gold;

        private readonly CompRitualCircle circle;
        private RitualPresetDef selected;
        private RitualSchema selectedOwn;          // one of the player's files
        private List<RitualSchema> own = new List<RitualSchema>();
        private RuneDef runeShown;
        private Vector2 leftScroll, rightScroll;
        private bool engrave;
        private readonly RitualView view = new RitualView();
        private bool sheetMax;
        private Rect sheetNormal;
        // how much material the haulers lay in the circle (the chosen ritual's material circles)
        private int matAmount = 1;
        private string matBuf = "1";
        private object matFor;
        private bool conduitTab;
        private Vector2 conduitScroll;

        public Dialog_RitualBook(CompRitualCircle circle)
        {
            this.circle = circle;
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            forcePause = true;
            selected = Presets.FirstOrDefault();
            engrave = circle?.engraveNext ?? false;
            ReloadOwn();
            ShowDrawn();
        }

        /// <summary>Opens on what is drawn in this circle: the player's file of that name, else its preset, else the
        /// drawing itself (a file deleted since, or one from another game) at the top of "Your rituals".</summary>
        private void ShowDrawn()
        {
            RitualSchema d = circle?.drawn;
            if (d == null) return;
            selectedOwn = own.FirstOrDefault(x => x.name == d.name);
            if (selectedOwn != null) return;
            RitualPresetDef p = d.preset != null && (string)d.preset.LabelCap == d.name ? Presets.FirstOrDefault(x => x == d.preset) : null;
            if (p != null) { selected = p; return; }
            own.Insert(0, d);
            selectedOwn = d;
        }

        private void ReloadOwn()
        {
            own = RitualFiles.LoadAll();
            if (selectedOwn != null) selectedOwn = own.FirstOrDefault(x => x.name == selectedOwn.name);
            if (selectedOwn == null && selected == null) selected = Presets.FirstOrDefault();
        }

        private RitualSchema Current => selectedOwn ?? selected?.Schema();

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1280f, Verse.UI.screenWidth - 40f), Mathf.Min(760f, Verse.UI.screenHeight - 40f));

        /// <summary>Rituals whose runes are researched (and hidden ones once found); dev mode shows all.</summary>
        private static List<RitualPresetDef> Presets => RitualPresetDef.All.Where(p => p.Available && !p.sanctuary).ToList();

        public override void PostOpen()
        {
            base.PostOpen();
            SheetWindow.StartMaximized(this, ref sheetMax, ref sheetNormal, true);   // the whole screen by default
            ArliaGuide.Offer(ArliaGuide.Book);
        }

        public override void PostClose()
        {
            base.PostClose();
            ArliaGuide.Abandon(ArliaGuide.Book);
        }

        public override void DoWindowContents(Rect box)
        {
            ArliaGuide.Block(ArliaGuide.Book);
            FlowStyle.Begin(circle?.parent.Map);
            Rect inner = ParchmentFrame.Draw(box).ContractedBy(4f);
            SheetWindow.MaximizeButton(this, new Rect(box.xMax - 100f, box.y + 28f, 24f, 24f), ref sheetMax, ref sheetNormal, true);
            float lw = inner.width * 0.24f, rw = inner.width * 0.3f;
            Rect left = new Rect(inner.x, inner.y, lw, inner.height);
            Rect right = new Rect(inner.xMax - rw, inner.y, rw, inner.height);
            Rect mid = new Rect(left.xMax + 16f, inner.y, inner.width - lw - rw - 32f, inner.height);
            RitualArt.Panel(left.ExpandedBy(6f)); RitualArt.Panel(mid.ExpandedBy(6f)); RitualArt.Panel(right.ExpandedBy(6f));
            if (RitualArt.HasPanel) { left = left.ContractedBy(8f); mid = mid.ContractedBy(8f); right = right.ContractedBy(8f); }
            DrawLeft(left);
            DrawMid(mid);
            DrawRight(right);
            ArliaGuide.Spot("list", new Rect(left.x, left.y, left.width, left.height - 44f));
            ArliaGuide.Spot("picture", mid);
            Arlia.DrawPlaqueIn(this, mid, 3);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            ArliaGuide.Draw(ArliaGuide.Book, box);
            FlowStyle.End();
        }

        private void DrawLeft(Rect r)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 32f), "Ritual book");
            Text.Font = GameFont.Small;
            float y = r.y + 40f;
            List<RitualPresetDef> list = Presets;
            Rect area = new Rect(r.x, y, r.width, r.yMax - y - 44f);
            Rect view = new Rect(0f, 0f, area.width - 16f, list.Count * 52f + 34f + Mathf.Max(1, own.Count) * 52f);
            RitualScroll.Begin(area, ref leftScroll, view);
            float yy = 0f;
            foreach (RitualPresetDef p in list)
            {
                Rect row = new Rect(0f, yy, view.width, 48f);
                bool open = p.Schema().AllUnlocked && p.Researched;
                if (p == selected && selectedOwn == null && !RitualArt.Selected(row)) { Widgets.DrawBoxSolid(row, new Color(Gold.r, Gold.g, Gold.b, 0.18f)); GUI.color = Gold; Widgets.DrawBox(row, 1); }
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                GUI.color = open ? Ink : InkGrey;
                ArtInk.Label(new Rect(row.x + 6f, row.y + 3f, row.width - 12f, 22f), p.LabelCap + (p.hidden ? " (hidden)" : ""));
                Text.Font = GameFont.Tiny; GUI.color = InkSoft;
                ArtInk.Label(new Rect(row.x + 6f, row.y + 25f, row.width - 12f, 20f),
                    RitualCircles.Label(p.size) + " circle · " + p.rings.Sum(x => x.runes.Count) + " runes" + (open ? "" : !p.Researched ? " · needs " + p.researchPrerequisite.label : " · runes not yet researched"));
                Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(row)) { selected = p; selectedOwn = null; runeShown = null; this.view.Reset(); SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                yy += 52f;
            }
            GUI.color = Ink; Text.Font = GameFont.Small;
            ArtInk.Label(new Rect(0f, yy + 6f, view.width, 26f), "Your rituals");
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.6f);
            Widgets.DrawLineHorizontal(0f, yy + 30f, view.width);
            yy += 34f;
            if (own.Count == 0)
            {
                Text.Font = GameFont.Tiny; GUI.color = InkSoft;
                ArtInk.Label(new Rect(6f, yy, view.width - 12f, 48f), "None yet. Write one in the editor - it is saved as a file and shared between games.");
                Text.Font = GameFont.Small;
            }
            foreach (RitualSchema o in own)
            {
                Rect row = new Rect(0f, yy, view.width, 48f);
                bool open = o.AllUnlocked;
                if (o == selectedOwn && !RitualArt.Selected(row)) { Widgets.DrawBoxSolid(row, new Color(Gold.r, Gold.g, Gold.b, 0.18f)); GUI.color = Gold; Widgets.DrawBox(row, 1); }
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                GUI.color = open ? Ink : InkGrey;
                ArtInk.Label(new Rect(row.x + 6f, row.y + 3f, row.width - 12f, 22f), o.name);
                Text.Font = GameFont.Tiny; GUI.color = InkSoft;
                ArtInk.Label(new Rect(row.x + 6f, row.y + 25f, row.width - 12f, 20f),
                    RitualCircles.Label(o.size) + " circle · " + o.AllRunes.Count() + " runes" + (open ? "" : " · runes not yet researched"));
                Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(row)) { selectedOwn = o; runeShown = null; this.view.Reset(); SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                yy += 52f;
            }
            Widgets.EndScrollView();
            Rect newR = new Rect(r.x, r.yMax - 38f, r.width * 0.6f - 4f, 36f);
            Rect codexR = new Rect(newR.xMax + 8f, r.yMax - 38f, r.width * 0.4f - 4f, 36f);
            ArliaGuide.Spot("new", newR);
            ArliaGuide.Spot("codex", codexR);
            if (Button(newR, "New ritual...", true))
                Find.WindowStack.Add(new Dialog_RitualEditor(circle, null, ReloadOwn));
            if (Button(codexR, "Codex...", true))
                Find.WindowStack.Add(new Dialog_RitualCodex(circle?.parent.Map ?? Find.CurrentMap));
            TooltipHandler.TipRegion(codexR, "Every rune, on its own large page - in plain words or in full.");
        }

        private void DrawMid(Rect r)
        {
            RitualSchema s = Current;
            if (s == null)
            {
                GUI.color = InkSoft; Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter;
                ArtInk.Label(r.ContractedBy(40f), "Nothing to show yet.\n\nResearch the runes the book's rituals need - or write a ritual of your own with \"New ritual...\", and the codex will tell you what every rune does.");
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                if (Event.current.type == EventType.Repaint && !ArliaGuide.Active(ArliaGuide.Book)) Arlia.Say("emptybook");
                return;
            }
            // The picture is sized by how far it reaches from its centre (the rim, and the component circles around it), so
            // that all of it fits the square the middle column gives it, centred, and nothing is cut at the edge.
            bool hasComps = s.components.Count > 0;
            float reachK = hasComps ? 1.12f * RitualTex.RimGrow + 0.12f : RitualTex.RimGrow;     // reach, in sections-circle radii
            float avail = Mathf.Min(r.width, r.height - 90f);
            float R0 = Mathf.Max(20f, (avail - 12f) / 2f / reachK);
            float d0 = R0 * 2f / 0.97f;
            float reach = R0 * reachK;
            Vector2 homeC = new Vector2(r.center.x, r.y + 6f + reach);
            float nameY = r.y + 6f + reach * 2f + 6f;     // the name sits below the picture as it is at rest (it does not travel with the zoom)
            Rect viewR = new Rect(r.x, r.y, r.width, nameY - r.y - 2f);   // the picture is seen only here
            view.Handle(viewR, homeC, reach);

            GUI.BeginGroup(viewR);
            float d = d0 * view.zoom;
            Vector2 c = homeC - viewR.position + view.pan;
            int n = Mathf.Max(1, s.rings.Count);
            float R = d / 2f * 0.97f, core = R * 0.28f;
            GUI.color = new Color(InkSoft.r, InkSoft.g, InkSoft.b, 0.8f);
            Ring(c, R);
            if (RitualTex.Rim != RitualTex.Ring) Rim(c, R);
            for (int i = 0; i <= n; i++) if (i < n) Ring(c, core + (R - core) * i / n);
            RitualOrnament.DrawUI(c, R, core, n, i => i < s.rings.Count ? s.rings[i].Slots : 1, i => i < s.rings.Count ? s.rings[i].runes.Count : 0, new Color(InkSoft.r, InkSoft.g, InkSoft.b, 0.8f));
            for (int i = 0; i < s.rings.Count && i < n; i++)
            {
                List<RuneDef> runes = s.rings[i].runes;
                float inner = core + (R - core) * i / n, outer = core + (R - core) * (i + 1) / n;
                int per = s.rings[i].Slots;
                float mid = (inner + outer) / 2f, size = RitualOrnament.GlyphSize(inner, outer, per, 90f * view.zoom);
                for (int k = 0; k < runes.Count && k < per; k++)
                {
                    Rect g = RitualOrnament.GlyphRect(c, mid, RitualOrnament.SlotAngle(i, k, per), size * RitualOrnament.RuneInFrame);
                    bool over = Mouse.IsOver(g);
                    GUI.color = runes[k].Unlocked ? Color.white : new Color(1f, 1f, 1f, 0.75f);
                    { Color keepC = GUI.color; FlowStyle.Backing(g.ExpandedBy(g.width * 0.35f)); GUI.color = keepC; }
                    GUI.DrawTexture(g, FlowStyle.Pick(runes[k].Ink, runes[k].Glow), ScaleMode.ScaleToFit);
                    if (over) { TooltipHandler.TipRegion(g, runes[k].LabelCap + "\n\n" + RitualText.Desc(runes[k])); if (Widgets.ButtonInvisible(g) && !view.dragged) runeShown = runes[k]; }
                }
            }
            // component circles
            for (int k = 0; k < s.components.Count; k++)
            {
                RitualComponent comp = s.components[k];
                float ang = (-90f + 360f * k / s.components.Count) * Mathf.Deg2Rad;
                Vector2 p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R * 1.12f * RitualTex.RimGrow;
                float cr = Mathf.Max(14f, R * 0.11f);
                Rect box = new Rect(p.x - cr, p.y - cr, cr * 2f, cr * 2f);
                GUI.color = comp.kind == ComponentKind.Seal ? Gold : new Color(InkSoft.r, InkSoft.g, InkSoft.b, 0.85f);
                Ring(p, cr);
                bool sealC = comp.kind == ComponentKind.Seal;
                GUI.DrawTexture(box.ExpandedBy(cr * (sealC ? 0.45f : 0.25f)), sealC ? FlowStyle.Pick(RitualOrnament.SealInk, RitualOrnament.SealGlow) : FlowStyle.Pick(RitualOrnament.FrameInk, RitualOrnament.FrameGlow), ScaleMode.ScaleToFit);
                Texture2D icon = comp.Icon;
                GUI.color = Color.white;
                if (icon != null) GUI.DrawTexture(box.ContractedBy(cr * 0.42f), icon, ScaleMode.ScaleToFit);
                TooltipHandler.TipRegion(box, comp.Label);
            }
            GUI.EndGroup();
            view.DrawHint(viewR, InkSoft);
            GUI.color = Ink; Text.Anchor = TextAnchor.UpperCenter;
            Text.Font = GameFont.Medium;
            // the name sits below the component circles, never on them (nameY: at the top of this method)
            ArtInk.Label(new Rect(r.x, nameY, r.width, 32f), selectedOwn != null ? s.name : (string)selected.LabelCap);
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;

            float bx = r.center.x - (circle != null ? 270f : 130f);
            if (Button(new Rect(bx, r.yMax - 44f, 260f, 40f), selectedOwn != null ? "Edit..." : "Edit a copy...", true))
                Find.WindowStack.Add(new Dialog_RitualEditor(circle, s, ReloadOwn));
            if (circle != null)
            {
                Rect b = new Rect(r.center.x + 10f, r.yMax - 44f, 260f, 40f);
                string why = !s.AllRunes.Any() ? "no runes" : !s.Drawable ? "runes not yet researched" : !s.Fits(circle.Size) ? "needs a " + RitualCircles.Label(s.size) + " circle" : null;
                // the material: chalk (quick, washed away by rain) or an engraving (very long, permanent, a little better)
                Rect mat = new Rect(r.center.x + 10f, r.yMax - 88f, 260f, 40f);
                if (Button(mat, engrave ? "Material: engraving" : "Material: chalk", true)) engrave = !engrave;
                TooltipHandler.TipRegion(mat, "Chalk: quick, but rain and snow wash it away under the open sky; an anchored ritual holds a day at most.\n\n"
                    + "Engraving: cut into the floor, whatever it is made of - " + CompRitualCircle.EngraveWork.ToString("0") + " times longer to make (an Avir cuts it with its essentium claws " + CompRitualCircle.ClawEngraving.ToString("0.#") + " times faster), never washed away, cannot be broken or attacked - only levelled away by a constructor, quality +"
                    + CompRitualCircle.EngraveQuality.ToStringPercent() + ". An anchored engraving holds without end, and can sustain itself: it wakes when it is needed, through an artifact, battery or artifact building at its centre.");
                if (Button(b, why == null ? (engrave ? "Engrave into this circle" : "Draw into this circle") : "Cannot: " + why, why == null))
                {
                    circle.Assign(WithAmount(s), engrave);
                    Close();
                }
                if (Prefs.DevMode)
                {
                    Rect dev = new Rect(r.center.x + 10f, r.yMax - 132f, 260f, 40f);
                    if (Button(dev, "Dev: draw instantly", s.AllRunes.Any() && s.Fits(circle.Size)))
                    {
                        circle.Assign(WithAmount(s), engrave);
                        circle.DevDrawNow();
                        Close();
                    }
                }
            }
        }

        private static int FirstMaterial(RitualSchema s) => s.components.FindIndex(c => c.kind == ComponentKind.Material);

        /// <summary>The ritual as it will be drawn: its material circles hold the amount chosen in the book. A copy, so
        /// that a ritual kept in a file is not changed.</summary>
        private RitualSchema WithAmount(RitualSchema s)
        {
            RitualSchema give = s.Copy();
            // a recipe of several materials keeps the amounts set in the editor (Transmutation...); the book's single
            // "Material:" row only sets the one material circle of a simple ritual
            if (give.components.Count(c => c.kind == ComponentKind.Material) != 1) return give;
            foreach (RitualComponent comp in give.components)
                if (comp.kind == ComponentKind.Material)
                    comp.amount = Mathf.Clamp(matAmount, 1, Mathf.Max(1, comp.thing?.stackLimit ?? 1));
            return give;
        }

        /// <summary>When the chosen ritual changes, the amount goes back to what that ritual lays.</summary>
        private void SyncAmount(RitualSchema cur)
        {
            object key = (object)selectedOwn ?? selected;
            if (key == matFor) return;
            matFor = key;
            int i = FirstMaterial(cur);
            matAmount = i >= 0 ? Mathf.Max(1, cur.components[i].amount) : 1;
            matBuf = matAmount.ToString();
        }

        /// <summary>"Material: [-10] [-] [ N ] [+] [+10]" - how much the haulers lay in the circle before the ritual may begin.</summary>
        private void DrawAmount(Rect row, RitualSchema cur)
        {
            int i = FirstMaterial(cur);
            ThingDef thing = i >= 0 ? cur.components[i].thing : null;
            int max = Mathf.Max(1, thing?.stackLimit ?? 75);
            int before = matAmount;
            matAmount = Mathf.Clamp(matAmount, 1, max);
            GUI.color = Ink; Text.Font = GameFont.Small;
            Widgets.Label(new Rect(row.x, row.y + 5f, 84f, 24f), "Material:");
            float x = row.x + 84f;
            if (RitualUI.Button(new Rect(x, row.y, 42f, 32f), "-10")) matAmount = Mathf.Max(1, matAmount - 10);
            if (RitualUI.Button(new Rect(x + 44f, row.y, 32f, 32f), "-")) matAmount = Mathf.Max(1, matAmount - 1);
            Widgets.TextFieldNumeric(new Rect(x + 80f, row.y + 1f, 60f, 30f), ref matAmount, ref matBuf, 1f, max);
            if (RitualUI.Button(new Rect(x + 144f, row.y, 32f, 32f), "+")) matAmount = Mathf.Min(max, matAmount + 1);
            if (RitualUI.Button(new Rect(x + 178f, row.y, 42f, 32f), "+10")) matAmount = Mathf.Min(max, matAmount + 10);
            if (RitualUI.Button(new Rect(x + 222f, row.y, 50f, 32f), "max")) matAmount = max;
            matAmount = Mathf.Clamp(matAmount, 1, max);
            if (matAmount != before) matBuf = matAmount.ToString();
            TooltipHandler.TipRegion(row, "How much of the material the haulers lay in the circle - the ritual does not begin until it all lies there. "
                + "From 1 to " + max + " (one stack). Applies to the circle you draw.");
        }

        private void DrawRight(Rect r)
        {
            float y = r.y;
            var sb = new System.Text.StringBuilder();
            RitualSchema cur = Current;
            if (cur != null)
            {
                RitualReading reading = RitualEvaluator.Read(cur);
                // what it is
                if (selectedOwn == null) sb.AppendLine(RitualText.Desc(selected)).AppendLine();
                else if (reading.pattern != null && (!reading.pattern.hidden || RitualDiscoveries.Known(reading.pattern)))
                    sb.AppendLine(RitualText.Desc(reading.pattern)).AppendLine();
                else sb.AppendLine(reading.Plain()).AppendLine();
                sb.AppendLine("DETAILS");
                sb.AppendLine(reading.Describe());
                for (int i = 0; i < cur.rings.Count; i++)
                    sb.AppendLine("Section " + (i + 1) + ": " + (cur.rings[i].runes.Count == 0 ? "-" : string.Join(", ", cur.rings[i].runes.Select(x => x.label))));
            }
            string text = sb.ToString();
            GUI.color = InkSoft; Text.Font = GameFont.Medium;
            if (cur != null) SyncAmount(cur);
            bool askAmount = circle != null && cur != null && cur.components.Count(c => c.kind == ComponentKind.Material) == 1;
            FlowGauge.Draw(new Rect(r.x, r.y, r.width - 8f, 64f), circle?.parent.Map ?? Find.CurrentMap);
            ArliaGuide.Spot("gauge", new Rect(r.x, r.y, r.width - 8f, 64f));
            // two pages: what the ritual does, and who (or what) holds it
            if (circle != null)
            {
                float tw = (r.width - 12f) / 2f;
                ArliaGuide.Spot("conduit", new Rect(r.x + tw + 4f, r.y + 68f, tw, 32f));
                if (RitualUI.Button(new Rect(r.x, r.y + 68f, tw, 32f), "Codex", true, !conduitTab)) conduitTab = false;
                if (RitualUI.Button(new Rect(r.x + tw + 4f, r.y + 68f, tw, 32f), "Conduit", true, conduitTab)) conduitTab = true;
                if (conduitTab) { DrawConduits(new Rect(r.x, r.y + 106f, r.width, r.height - 106f)); return; }
            }
            GUI.color = InkSoft;
            float top = circle != null ? 106f : 70f;
            if (askAmount) { DrawAmount(new Rect(r.x, r.y + top, r.width, 34f), cur); top += 40f; GUI.color = InkSoft; }
            // the chance of success, on its own plaque at the foot of the page
            float plaqueH = cur != null ? SuccessPlaque.Height : 0f;
            if (cur != null) SuccessPlaque.Draw(new Rect(r.x, r.yMax - plaqueH, r.width - 8f, plaqueH - 4f), cur, circle);
            // her word on it, under the details
            float noteH = 0f;
            if (cur != null && Arlia.On)
            {
                RitualReading rr = RitualEvaluator.Read(cur);
                RitualPresetDef known = selectedOwn == null ? selected : rr.pattern != null && (!rr.pattern.hidden || RitualDiscoveries.Known(rr.pattern)) ? rr.pattern : null;
                string jk = Arlia.Judge(cur, rr, circle);
                string line = Arlia.About(known) ?? (jk == "known" || jk == "hidden" || jk == "works" ? null : Arlia.LineFor(jk, cur.name));
                if (line != null)
                {
                    noteH = Arlia.NoteHeight(line, r.width - 8f) + 6f;
                    Arlia.Note(new Rect(r.x, r.yMax - plaqueH - noteH, r.width - 8f, noteH - 6f), line);
                }
            }
            GUI.color = InkSoft;
            Rect area = new Rect(r.x, r.y + top, r.width, r.height - top - plaqueH - noteH - 4f);
            Text.Font = GameFont.Small;
            float th = RitualArt.TextHeight(text, r.width - 20f);
            Rect view = new Rect(0f, 0f, r.width - 18f, th);
            RitualScroll.Begin(area, ref rightScroll, view);
            RitualArt.TextIn(view, text);
            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
        }

        /// <summary>The conduit page: colonists, and things of energy at the centre; the chosen one begins the ritual.</summary>
        private void DrawConduits(Rect r)
        {
            Map map = circle.parent.Map;
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(map);
            RitualReading rd = circle.Drawn ? circle.Reading : null;
            float risk = rd == null ? 0f : rd.Risk(ext == null ? 0f : ext.Total / MapComponent_ExternalFlow.Calm, circle.drawQuality, 0f);
            var pawns = map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed).ToList();
            var things = circle.ConduitCandidates().ToList();
            const float rowH = 78f;
            float h = 26f + pawns.Count * (rowH + 4f) + 30f + Mathf.Max(1, things.Count) * (rowH + 4f);
            Rect listArea = new Rect(r.x, r.y, r.width, r.height - 52f);
            Rect view = new Rect(0f, 0f, r.width - 16f, h);
            RitualScroll.Begin(listArea, ref conduitScroll, view);
            float y = 0f;
            GUI.color = Ink;
            Widgets.Label(new Rect(0f, y, view.width, 24f), "Colonists");
            y += 26f;
            foreach (Pawn p in pawns)
            {
                Rect row = new Rect(0f, y, view.width, rowH);
                RitualUI.Card(row, circle.chosenPerformer == p && circle.chosenThing == null);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(row.x + 4f, row.y + 8f, 50f, 62f), PortraitsCache.Get(p, new Vector2(50f, 62f), Rot4.South, default, 1.25f));
                float comp = GameComponent_AvirComposure.For(p, circle.drawn ?? Current, circle);
                GUI.color = Ink;
                Widgets.Label(new Rect(row.x + 58f, row.y + 4f, row.width - 62f, 24f), p.LabelShortCap);
                GUI.color = InkSoft;
                string wings = !Avir.AvirWingParts.HasWingGene(p) ? "" : Avir.AvirWingParts.Clipped(p) ? " - clipped wings x0.5" : " - wings x2";
                Widgets.Label(new Rect(row.x + 58f, row.y + 28f, row.width - 62f, 22f), "Sensitivity " + p.GetStatValue(StatDefOf.PsychicSensitivity).ToStringPercent() + wings);
                Steadiness(new Rect(row.x + 58f, row.y + 50f, row.width - 62f, 24f), comp, risk, rd != null);
                Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(row)) { circle.chosenPerformer = p; circle.chosenThing = null; SoundDefOf.Click.PlayOneShotOnCamera(); }
                y += rowH + 4f;
            }
            y += 4f;
            GUI.color = Ink;
            Widgets.Label(new Rect(0f, y, view.width, 24f), "Things of energy at the centre");
            y += 26f;
            if (things.Count == 0)
            {
                GUI.color = InkSoft;
                Widgets.Label(new Rect(0f, y, view.width, rowH), "Lay an artifact or a crystal battery at the centre of the circle (or build a psychic pylon or an artifact building there) and it can hold the ritual instead of a colonist.");
                Text.Font = GameFont.Small;
            }
            foreach (Thing t in things)
            {
                Rect row = new Rect(0f, y, view.width, rowH);
                RitualUI.Card(row, circle.chosenThing == t);
                GUI.color = Color.white;
                Widgets.ThingIcon(new Rect(row.x + 6f, row.y + 16f, 44f, 44f), t);
                GUI.color = Ink;
                Widgets.Label(new Rect(row.x + 58f, row.y + 4f, row.width - 62f, 24f), t.LabelShortCap);
                GUI.color = InkSoft;
                string why = circle.CannotSustain(false);
                Widgets.Label(new Rect(row.x + 58f, row.y + 28f, row.width - 62f, 22f), why == null ? "Gives its own charge" : "Cannot: " + why);
                Steadiness(new Rect(row.x + 58f, row.y + 50f, row.width - 62f, 24f), CompRitualCircle.ConduitComposure, risk, rd != null);
                Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(row)) { circle.chosenThing = t; circle.chosenPerformer = null; SoundDefOf.Click.PlayOneShotOnCamera(); }
                y += rowH + 4f;
            }
            Widgets.EndScrollView();
            // begin
            string label = circle.Running ? "Performing..." : !circle.Drawn ? "Nothing drawn yet" : "Begin: " + circle.drawn.name;
            string lack = circle.Drawn && !circle.Running ? circle.MissingMaterial() : null;
            if (lack != null) label = "Waiting for material";
            bool can = circle.Drawn && !circle.Running && lack == null && (circle.chosenPerformer != null || circle.chosenThing != null);
            Rect beginR = new Rect(r.x, r.yMax - 44f, r.width, 40f);
            if (lack != null) TooltipHandler.TipRegion(beginR, "Cannot begin: " + lack + ".");
            if (RitualUI.Button(beginR, label, can))
            {
                string w = circle.BeginChosen();
                if (w != null) Messages.Message("Cannot begin: " + w + ".", MessageTypeDefOf.RejectInput, false);
                else Close();
            }
            GUI.color = Color.white;
        }

        /// <summary>How well this conduit holds the drawn ritual: composure against what the ritual asks.</summary>
        private static void Steadiness(Rect r, float composure, float risk, bool known)
        {
            string text = "Composure " + composure.ToStringPercent();
            if (known)
            {
                float margin = composure - risk;
                string verdict = margin >= 0.15f ? "steady" : margin >= 0f ? "strained" : margin >= -0.3f ? "may break" : "will likely break";
                Color c = margin >= 0.15f ? new Color(0.15f, 0.5f, 0.15f) : margin >= 0f ? new Color(0.6f, 0.45f, 0.05f) : new Color(0.65f, 0.12f, 0.1f);
                if (FlowStyle.Light) c = Color.Lerp(c, Color.white, 0.5f);
                Widgets.Label(r, text + " vs " + risk.ToStringPercent() + " asked - " + verdict.Colorize(c));
            }
            else Widgets.Label(r, text);
        }

        private static void Rim(Vector2 c, float rad)
        {
            rad *= RitualTex.RimGrow;
            GUI.DrawTexture(new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f), RitualTex.Rim, ScaleMode.StretchToFill);
        }

        private static void Ring(Vector2 c, float rad)
        {
            Rect rr = new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f);
            GUI.DrawTexture(rr, RitualTex.Ring, ScaleMode.StretchToFill);
        }

        private static bool Button(Rect r, string label, bool enabled)
        {
            bool over = enabled && Mouse.IsOver(r);
            Texture2D tex = !over ? FlowStyle.ButtonNormal : Input.GetMouseButton(0) ? FlowStyle.ButtonPressed : FlowStyle.ButtonHover;
            GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            GUI.color = enabled ? FlowStyle.ButtonText : InkGrey;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (enabled && Widgets.ButtonInvisible(r)) { SoundDefOf.Click.PlayOneShotOnCamera(); return true; }
            return false;
        }
    }
}
