using System.Collections.Generic;
using System.Linq;
using Avir.Artifactorics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Ritualistics
{
    /// <summary>The ritual editor: the researched runes on the left; the circle in the middle, where the
    /// player sets how many sections it has and how many slots each section holds (up to the circle's
    /// limit), clicks a section to choose it and a rune to write it in, clicks a glyph to rub it out;
    /// on the right what the ritual will do, in plain words. Runes may repeat. Rituals are saved as files.</summary>
    public class Dialog_RitualEditor : Window
    {
        private static Color Ink => FlowStyle.Ink;
        private static Color InkSoft => FlowStyle.InkSoft;
        private static Color InkGrey => FlowStyle.InkGrey;
        private static Color Gold => FlowStyle.Gold;

        private readonly CompRitualCircle circle;
        private readonly System.Action onSaved;
        private readonly RitualSchema s;
        private int ring;
        private Vector2 leftScroll, rightScroll;

        /// <summary>Writing a spell: the same editor, and a button that keeps the drawing as the mage's spell.</summary>
        private readonly System.Action<RitualSchema> asSpell;

        public Dialog_RitualEditor(RitualSchema start, System.Action<RitualSchema> asSpell) : this(null, start) { this.asSpell = asSpell; }

        public Dialog_RitualEditor(CompRitualCircle circle, RitualSchema start, System.Action onSaved = null)
        {
            this.circle = circle;
            this.onSaved = onSaved;
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            forcePause = true;
            s = start?.Copy() ?? new RitualSchema { size = circle?.Size ?? RitualCircleSize.Small };
            s.preset = null;
            Normalize();
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1320f, Verse.UI.screenWidth - 40f), Mathf.Min(820f, Verse.UI.screenHeight - 40f));

        private int MaxRings => RitualCircles.Rings(s.size);
        private int MaxSlots => RitualCircles.RunesPerRing(s.size);
        private int MaxComponents => RitualCircles.Components(s.size);

        // ---------------------------------------------------------------- zoom and move (like the amulet's spiral)

        private readonly RitualView view = new RitualView();
        private bool sheetMax;
        private Rect sheetNormal;

        // ---------------------------------------------------------------- component circles

        private void DrawComponents(Vector2 c, float R)
        {
            int n = s.components.Count;
            for (int k = 0; k < n; k++)
            {
                RitualComponent comp = s.components[k];
                float ang = (-90f + 360f * k / n) * Mathf.Deg2Rad;
                Vector2 p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R * 1.24f;
                float cr = Mathf.Max(18f, R * 0.15f);
                Rect box = new Rect(p.x - cr, p.y - cr, cr * 2f, cr * 2f);
                bool seal = comp.kind == ComponentKind.Seal;
                // a small circle with its own pattern: two rings and the slot frame around
                GUI.color = seal ? Gold : new Color(InkSoft.r, InkSoft.g, InkSoft.b, 0.85f);
                Ring(p, cr); Ring(p, cr * 0.8f);
                GUI.DrawTexture(box.ExpandedBy(cr * (seal ? 0.45f : 0.25f)), seal ? FlowStyle.Pick(RitualOrnament.SealInk, RitualOrnament.SealGlow) : FlowStyle.Pick(RitualOrnament.FrameInk, RitualOrnament.FrameGlow), ScaleMode.ScaleToFit);
                Texture2D icon = comp.Icon;
                if (icon != null)
                {
                    GUI.color = Color.white;
                    GUI.DrawTexture(box.ContractedBy(cr * 0.42f), icon, ScaleMode.ScaleToFit);
                }
                GUI.color = Color.white;
                TooltipHandler.TipRegion(box, "Component circle " + (k + 1) + ": " + comp.Label + "\nClick to choose what it holds.");
                if (comp.kind == ComponentKind.None) Arlia.Circle(box.ExpandedBy(cr * 0.35f));
                if (Mouse.IsOver(box)) { GUI.color = new Color(1f, 0.9f, 0.6f, 0.25f); GUI.DrawTexture(box, RitualTex.Disc); GUI.color = Color.white; }
                if (Widgets.ButtonInvisible(box)) Find.WindowStack.Add(new Dialog_RitualComponent(s, k, circle?.parent.Map ?? Find.CurrentMap));
            }
        }

        /// <summary>1..max sections, each 1..max slots, never more runes than slots.</summary>
        private void Normalize()
        {
            if (s.rings.Count == 0) s.rings.Add(new RitualRing { slots = 1 });
            if (s.rings.Count > MaxRings) s.rings.RemoveRange(MaxRings, s.rings.Count - MaxRings);
            foreach (RitualRing r in s.rings)
            {
                r.runes.RemoveAll(x => x == null);
                if (r.runes.Count > MaxSlots) r.runes.RemoveRange(MaxSlots, r.runes.Count - MaxSlots);
                r.slots = Mathf.Clamp(r.Slots, 1, MaxSlots);
            }
            ring = Mathf.Clamp(ring, 0, s.rings.Count - 1);
            if (s.components.Count > MaxComponents) s.components.RemoveRange(MaxComponents, s.components.Count - MaxComponents);
        }

        public override void PostOpen()
        {
            base.PostOpen();
            SheetWindow.StartMaximized(this, ref sheetMax, ref sheetNormal, true);   // the whole screen by default
            Arlia.Forget();
            Arlia.Fresh = !s.AllRunes.Any();
            ArliaGuide.Offer(ArliaGuide.Editor);
        }

        public override void PostClose()
        {
            base.PostClose();
            Avir.Magic.SpellText.Caster = null;
            ArliaGuide.Abandon(ArliaGuide.Editor);
        }

        public override void DoWindowContents(Rect box)
        {
            ArliaGuide.Block(ArliaGuide.Editor);
            FlowStyle.Begin(circle?.parent.Map);
            Rect inner = ParchmentFrame.Draw(box).ContractedBy(4f);
            SheetWindow.MaximizeButton(this, new Rect(box.xMax - 100f, box.y + 28f, 24f, 24f), ref sheetMax, ref sheetNormal, true);
            float lw = inner.width * 0.22f, rw = inner.width * 0.3f;
            Rect left = new Rect(inner.x, inner.y, lw, inner.height);
            Rect right = new Rect(inner.xMax - rw, inner.y, rw, inner.height);
            Rect mid = new Rect(left.xMax + 16f, inner.y, inner.width - lw - rw - 32f, inner.height);
            RitualArt.Panel(left.ExpandedBy(6f)); RitualArt.Panel(mid.ExpandedBy(6f)); RitualArt.Panel(right.ExpandedBy(6f));
            if (RitualArt.HasPanel) { left = left.ContractedBy(8f); mid = mid.ContractedBy(8f); right = right.ContractedBy(8f); }
            DrawRunes(left);
            DrawCircle(mid);
            DrawRight(right);
            Arlia.DrawPlaqueIn(this, new Rect(mid.x, mid.y + 78f, mid.width, mid.height - 78f - 134f), 0);   // her corner of the drawing
            ArliaGuide.Spot("runes", left);
            ArliaGuide.Spot("reading", new Rect(right.x, right.y + 34f, right.width, right.height - 34f));
            ArliaGuide.Draw(ArliaGuide.Editor, box);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            FlowStyle.End();
        }

        // ---------------------------------------------------------------- the runes

        /// <summary>Researched runes only; dev mode shows and allows every rune.</summary>
        private static List<RuneDef> Usable => RuneDef.All.Where(r => r.Unlocked || Prefs.DevMode).ToList();

        private void DrawRunes(Rect r)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 32f), "Runes");
            Text.Font = GameFont.Tiny; GUI.color = InkSoft;
            ArtInk.Label(new Rect(r.x, r.y + 32f, r.width, 34f), "Click a rune to write it into section " + (ring + 1) + ". Runes may repeat.");
            Text.Font = GameFont.Small;
            List<RuneDef> runes = Usable;
            Rect area = new Rect(r.x, r.y + 70f, r.width, r.height - 70f);
            if (runes.Count == 0)
            {
                GUI.color = InkSoft;
                ArtInk.Label(area, "No rune is known yet. Research ritualism (Avir research tab) to learn the first runes.");
                return;
            }
            // pinned in the codex: first, with her short words under the name
            var pinnedR = runes.Where(Dialog_RitualCodex.Pinned).ToList();
            runes = pinnedR.Concat(runes.Where(x => !pinnedR.Contains(x))).ToList();
            float pw = area.width - 16f - 50f;
            Text.Font = GameFont.Tiny;
            var pinH = pinnedR.ToDictionary(x => x, x => Mathf.Max(46f, 26f + Text.CalcHeight(Dialog_RitualCodex.Plain(x), pw) + 4f));
            Text.Font = GameFont.Small;
            Rect view = new Rect(0f, 0f, area.width - 16f, runes.Sum(x => pinH.TryGetValue(x, out float ph) ? ph + 4f : 50f) + (pinnedR.Count > 0 ? 8f : 0f));
            bool full = s.rings[ring].runes.Count >= MaxSlots;
            RitualScroll.Begin(area, ref leftScroll, view);
            float y = 0f;
            RuneRole? lastRole = null;
            bool wasPinned = false;
            foreach (RuneDef rune in runes)
            {
                bool isPinned = pinH.TryGetValue(rune, out float rh);
                if (wasPinned && !isPinned) { y += 8f; GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.9f); Widgets.DrawLineHorizontal(0f, y - 5f, view.width); lastRole = null; }
                wasPinned = isPinned;
                Rect row = new Rect(0f, y, view.width, isPinned ? rh : 46f);
                if (isPinned) Widgets.DrawBoxSolid(row, new Color(Gold.r, Gold.g, Gold.b, 0.1f));
                if (!full && Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.16f));
                if (!isPinned && lastRole != null && lastRole != rune.role) { GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.5f); Widgets.DrawLineHorizontal(row.x, row.y - 2f, row.width); }
                if (!isPinned) lastRole = rune.role;
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(row.x + 2f, row.y + 3f, 40f, 40f), FlowStyle.Pick(rune.Ink, rune.Glow), ScaleMode.ScaleToFit);
                GUI.color = Ink;
                ArtInk.Label(new Rect(row.x + 48f, row.y + 3f, row.width - 50f, 22f), rune.LabelCap);
                Text.Font = GameFont.Tiny; GUI.color = InkSoft;
                string sub = rune.role.ToString().ToLower() + (rune.Unlocked ? "" : " · dev") + (rune.implemented ? "" : " · not made yet");
                if (isPinned) ArtInk.Label(new Rect(row.x + 48f, row.y + 24f, row.width - 50f, row.height - 24f), Dialog_RitualCodex.Plain(rune));
                else ArtInk.Label(new Rect(row.x + 48f, row.y + 24f, row.width - 50f, 20f), sub.Truncate(row.width - 50f));
                Text.Font = GameFont.Small;
                TooltipHandler.TipRegion(row, rune.LabelCap + "\n\n" + RitualText.Desc(rune) + (rune.implemented ? "" : "\n\nIts effect is not made yet.")
                    + "\n\nInstability " + rune.instability.ToString("+0.00;-0.00") + (rune.actWeight > 0f ? ", weight of the deed " + rune.actWeight.ToString("0") : ""));
                if (!full && Widgets.ButtonInvisible(row)) Write(rune);
                y += isPinned ? rh + 4f : 50f;
            }
            Widgets.EndScrollView();
        }

        /// <summary>Into the chosen section; a full section grows a slot while the circle allows.</summary>
        private void Write(RuneDef rune)
        {
            RitualRing rr = s.rings[ring];
            if (rr.runes.Count >= rr.Slots)
            {
                if (rr.Slots >= MaxSlots) { Messages.Message("Section " + (ring + 1) + " is full.", MessageTypeDefOf.RejectInput, false); Arlia.Say("full"); return; }
                rr.slots = rr.Slots + 1;
            }
            rr.runes.Add(rune);
            Arlia.Fresh = false;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        // ---------------------------------------------------------------- the circle

        private void DrawCircle(Rect r)
        {
            // size and name
            float bw = (r.width - 16f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var size = (RitualCircleSize)i;
                Rect b = new Rect(r.x + i * (bw + 8f), r.y, bw, 32f);
                if (Button(b, RitualCircles.Label(size).CapitalizeFirst() + " · " + RitualCircles.Rings(size) + "×" + RitualCircles.RunesPerRing(size), s.size != size, s.size == size))
                {
                    s.size = size;
                    Normalize();
                }
                TooltipHandler.TipRegion(b, RitualCircles.Label(size).CapitalizeFirst() + " circle (" + RitualCircles.Cells(size) + "x" + RitualCircles.Cells(size) + "): up to "
                    + RitualCircles.Rings(size) + " sections, up to " + RitualCircles.RunesPerRing(size) + " runes in each."
                    + (circle != null && (int)size > (int)circle.Size ? "\nThis circle is too small to draw it; you can still write and save it." : ""));
            }
            Rect nameR = new Rect(r.x, r.y + 40f, r.width, 30f);
            Widgets.DrawBoxSolid(nameR, new Color(0.93f, 0.86f, 0.70f, 0.9f));
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.9f);
            Widgets.DrawBox(nameR, 1);
            GUI.color = Ink;
            s.name = Widgets.TextField(nameR.ContractedBy(2f), s.name, 48);
            GUI.color = Color.white;

            float top = r.y + 78f, controls = 126f;
            Rect area = new Rect(r.x, top, r.width, r.height - (top - r.y) - controls - 8f);
            ArliaGuide.Spot("circle", area);
            Widgets.DrawBoxSolid(area, new Color(1f, 0.92f, 0.7f, 0.08f));
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.5f); Widgets.DrawBox(area, 1); GUI.color = Color.white;
            float d0 = Mathf.Min(area.width, area.height) * 0.7f;
            view.Handle(area, area.center, d0 / 2f * 0.97f * 1.45f * RitualTex.RimGrow);   // the circle, its rim and the component circles around it
            GUI.BeginGroup(area);
            Rect local = new Rect(0f, 0f, area.width, area.height);
            float d = d0 * view.zoom;
            Vector2 c = local.center + view.pan;
            Rect circleR = new Rect(c.x - d / 2f, c.y - d / 2f, d, d);
            int n = s.rings.Count;
            float R = d / 2f * 0.97f, core = R * 0.28f;

            // the chosen section, shaded
            float sIn = core + (R - core) * ring / n, sOut = core + (R - core) * (ring + 1) / n;
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.07f);
            for (float rad = sIn; rad < sOut; rad += Mathf.Max(2f, rad * 0.025f)) Ring(c, rad);
            GUI.color = new Color(InkSoft.r, InkSoft.g, InkSoft.b, 0.8f);
            Ring(c, R);
            if (RitualTex.Rim != RitualTex.Ring) Rim(c, R);
            for (int i = 0; i < n; i++) Ring(c, core + (R - core) * i / n);
            GUI.color = Gold;
            Ring(c, sOut); Ring(c, sIn);
            RitualOrnament.DrawUI(c, R, core, n, i => s.rings[i].Slots, i => s.rings[i].runes.Count, new Color(InkSoft.r, InkSoft.g, InkSoft.b, 0.85f));

            bool glyphClicked = false;
            for (int i = 0; i < n; i++)
            {
                List<RuneDef> runes = s.rings[i].runes;
                int per = s.rings[i].Slots;
                float inner = core + (R - core) * i / n, outer = core + (R - core) * (i + 1) / n;
                float mid = (inner + outer) / 2f, size = RitualOrnament.GlyphSize(inner, outer, per, 90f);
                for (int k = runes.Count; k < per; k++)   // Arlia's red ink round every empty place
                    Arlia.Circle(RitualOrnament.GlyphRect(c, mid, RitualOrnament.SlotAngle(i, k, per), size * 1.15f));
                for (int k = 0; k < runes.Count; k++)
                {
                    Rect g = RitualOrnament.GlyphRect(c, mid, RitualOrnament.SlotAngle(i, k, per), size * RitualOrnament.RuneInFrame);
                    bool over = Mouse.IsOver(g);
                    GUI.color = over ? new Color(0.6f, 0.15f, 0.1f) : Color.white;
                    { Color keepC = GUI.color; FlowStyle.Backing(g.ExpandedBy(g.width * 0.35f)); GUI.color = keepC; }
                    GUI.DrawTexture(g, FlowStyle.Pick(runes[k].Ink, runes[k].Glow), ScaleMode.ScaleToFit);
                    if (over) TooltipHandler.TipRegion(g, runes[k].LabelCap + " - click to rub it out");
                    if (!glyphClicked && Widgets.ButtonInvisible(g) && !view.dragged)
                    {
                        runes.RemoveAt(k);
                        ring = i;
                        glyphClicked = true;
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                        break;
                    }
                }
            }
            if (!glyphClicked && Event.current.type == EventType.MouseUp && Event.current.button == 0 && !view.dragged && Mouse.IsOver(circleR))
            {
                float dist = (Event.current.mousePosition - c).magnitude;
                if (dist <= R)
                {
                    ring = Mathf.Clamp(dist < core ? 0 : Mathf.FloorToInt((dist - core) / ((R - core) / n)), 0, n - 1);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    Event.current.Use();
                }
            }
            DrawComponents(c, R);
            GUI.EndGroup();
            view.DrawHint(area, InkSoft);

            // sections and slots
            float y = area.yMax + 6f, half = (r.width - 12f) / 2f;
            Stepper(new Rect(r.x, y, half, 30f), "Sections", n, MaxRings,
                () => { s.rings.RemoveAt(s.rings.Count - 1); Normalize(); },
                () => { s.rings.Add(new RitualRing { slots = 1 }); ring = s.rings.Count - 1; });
            RitualRing cur = s.rings[ring];
            Stepper(new Rect(r.x + half + 12f, y, half, 30f), "Slots in section " + (ring + 1), cur.Slots, MaxSlots,
                () => { cur.slots = cur.Slots - 1; if (cur.runes.Count > cur.slots) cur.runes.RemoveAt(cur.runes.Count - 1); },
                () => cur.slots = cur.Slots + 1);
            ArliaGuide.Spot("components", new Rect(r.x, y + 34f, half, 30f));
            Stepper(new Rect(r.x, y + 34f, half, 30f), "Component circles", s.components.Count, MaxComponents,
                () => s.components.RemoveAt(s.components.Count - 1),
                () => s.components.Add(new RitualComponent()), 0);
            // transmutation as a recipe: what it makes and everything it is made from, in one place
            Rect tr = new Rect(r.x + half + 12f, y + 34f, half, 30f);
            if (Button(tr, "Transmutation: makes and materials...", true))
                Find.WindowStack.Add(new Dialog_TransmuteRecipe(s, MaxComponents, circle?.parent.Map ?? Find.CurrentMap));
            TooltipHandler.TipRegion(tr, "What the ritual makes (its seal) and what it is made from: several materials, or the same one more than once, "
                + "all pooled by value into the one result.\n\nSections are read from the centre outward. A component circle can also be clicked on the drawing.");

            // buttons
            float by = r.yMax - 36f, w = (r.width - 16f) / 3f;
            if (Button(new Rect(r.x, by, w, 34f), "Rub out all", s.AllRunes.Any() || s.components.Any(cm => cm.kind != ComponentKind.None)))
            {
                foreach (RitualRing rr in s.rings) rr.runes.Clear();
                s.components.Clear();
            }
            if (Button(new Rect(r.x + w + 8f, by, w, 34f), "Save", s.AllRunes.Any() && !s.name.NullOrEmpty()))
                Save();
            if (Button(new Rect(r.x + 2f * (w + 8f), by, w, 34f), "Load...", true))
                LoadMenu();
        }

        /// <summary>"Label: [-] n of max [+]".</summary>
        private static void Stepper(Rect r, string label, int value, int max, System.Action less, System.Action more, int min = 1)
        {
            Rect minus = new Rect(r.x, r.y, 30f, r.height), plus = new Rect(r.xMax - 30f, r.y, 30f, r.height);
            if (Button(minus, "-", value > min)) less();
            if (Button(plus, "+", value < max)) more();
            GUI.color = Ink; Text.Anchor = TextAnchor.MiddleCenter;
            ArtInk.Label(new Rect(minus.xMax, r.y, r.width - 60f, r.height), label + ": " + value + " of " + max);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void Save()
        {
            System.Action save = () =>
            {
                if (RitualFiles.Save(s))
                {
                    Messages.Message("Ritual \"" + s.name + "\" saved to " + RitualFiles.Folder, MessageTypeDefOf.SilentInput, false);
                    Arlia.Say("saved");
                    onSaved?.Invoke();
                }
            };
            if (RitualFiles.Exists(s.name))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("A ritual named \"" + s.name + "\" is already saved. Write over it?", save, true));
            else save();
        }

        private void LoadMenu()
        {
            Find.WindowStack.Add(new Dialog_RitualLoad(circle?.parent.Map ?? Find.CurrentMap, (f, fromBook) =>
            {
                RitualSchema cp = f.Copy();
                s.name = fromBook ? f.name + " (copy)" : f.name;
                s.size = cp.size; s.rings = cp.rings; s.components = cp.components;
                Normalize();
            }));
        }

        // ---------------------------------------------------------------- the reading

        private void DrawRight(Rect r)
        {
            RitualReading reading = RitualEvaluator.Read(s);
            Arlia.Watch(s, reading, circle);
            bool chance = s.AllRunes.Any();
            float bottom = (circle != null || asSpell != null ? 48f : 0f) + (chance ? SuccessPlaque.Height : 0f);
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 32f), "Codex");
            Text.Font = GameFont.Small;
            // Arlia: sent away, or called back
            Rect ar = new Rect(r.xMax - 128f, r.y + 2f, 120f, 26f);
            ArliaGuide.Spot("arlia", ar);
            if (Button(ar, Arlia.On ? Arlia.Name + ": on" : Arlia.Name + ": off", true, Arlia.On)) Arlia.Toggle();
            Rect cx = new Rect(ar.x - 108f, r.y + 2f, 100f, 26f);
            if (Button(cx, "Codex...", true)) Find.WindowStack.Add(new Dialog_RitualCodex(circle?.parent.Map ?? Find.CurrentMap));
            TooltipHandler.TipRegion(ar, Arlia.Name + "'s remarks in the margins - and her red ink round whatever you left empty. Click to send her away, or to call her back.");
            var sb = new System.Text.StringBuilder();
            if (reading.pattern != null && (!reading.pattern.hidden || RitualDiscoveries.Known(reading.pattern)))
                sb.AppendLine("Pattern: " + reading.pattern.LabelCap).AppendLine();
            else if (reading.pattern != null)
                sb.AppendLine("Something in this pattern stirs - an old drawing, maybe. Draw it to learn.").AppendLine();
            sb.AppendLine(reading.Plain());
            sb.AppendLine();
            sb.AppendLine(Risk(reading));
            sb.AppendLine();
            sb.AppendLine("DETAILS");
            sb.AppendLine(reading.Describe());
            for (int i = 0; i < s.rings.Count; i++)
                sb.AppendLine("Section " + (i + 1) + " (" + s.rings[i].Slots + " slots): " + (s.rings[i].runes.Count == 0 ? "-" : string.Join(", ", s.rings[i].runes.Select(x => x.label))));
            sb.AppendLine();
            sb.Append("Files are kept in " + RitualFiles.Folder);
            string text = sb.ToString();
            FlowGauge.Draw(new Rect(r.x, r.y + 34f, r.width - 8f, 64f), circle?.parent.Map ?? Find.CurrentMap);
            float top = 104f;
            if (asSpell != null)
            {
                Avir.Magic.SpellEffect fx = Avir.Magic.SpellText.Explain(s);
                float ph = Avir.Magic.EffectPlaque.Height(fx, r.width - 8f);
                Avir.Magic.EffectPlaque.Draw(new Rect(r.x, r.y + top, r.width - 8f, ph), fx, Avir.Magic.Spells.ColorOf(s));
                top += ph + 6f;
            }
            Rect area = new Rect(r.x, r.y + top, r.width, r.height - bottom - top);
            GUI.color = InkSoft;
            float th = RitualArt.TextHeight(text, r.width - 20f);
            Rect view = new Rect(0f, 0f, r.width - 18f, th);
            RitualScroll.Begin(area, ref rightScroll, view);
            RitualArt.TextIn(view, text);
            Widgets.EndScrollView();
            if (chance) SuccessPlaque.Draw(new Rect(r.x, r.yMax - bottom + 2f, r.width - 8f, SuccessPlaque.Height - 4f), s, circle);

            if (asSpell != null)
            {
                Rect b = new Rect(r.x, r.yMax - 40f, r.width, 38f);
                string why = !s.AllRunes.Any() ? "no runes" : reading.verbs.Count == 0 && reading.barrier == 0 ? "no verb or form - it would do nothing" : null;
                if (Button(b, why == null ? "Keep as a spell" : "Cannot: " + why, why == null)) { asSpell(s.Copy()); Close(); }
            }
            else if (circle != null)
            {
                Rect b = new Rect(r.x, r.yMax - 40f, r.width, 38f);
                string why = !s.AllRunes.Any() ? "no runes" : !s.Drawable ? "runes not yet researched" : !s.Fits(circle.Size) ? "needs a " + RitualCircles.Label(s.size) + " circle" : null;
                if (Button(b, why == null ? "Draw into this circle" : "Cannot: " + why, why == null))
                {
                    circle.Assign(s.Copy());
                    Close();
                }
            }
        }

        /// <summary>What composure it asks for at the calm flow, in plain words.</summary>
        private static string Risk(RitualReading r)
        {
            float calm = r.Risk(1f, 1f, 0f);
            string word = calm < 0.4f ? "gentle - anyone can hold it" : calm < 0.6f ? "steady - an ordinary ritualist holds it"
                : calm < 0.85f ? "heavy - it wants a steadfast mind" : calm < 1.2f ? "dangerous - only an iron will holds it"
                : "beyond any ritualist - it will break";
            string s = "Composure it asks (calm stream): " + calm.ToStringPercent() + " - " + word + ".";
            if (r.focus > 0) s += "\nAt twice the calm stream: " + r.Risk(2f, 1f, 0f).ToStringPercent() + ".";
            s += "\nComposure: base 25% (Avir 5%, Linarian 15%), iron will +20%, good mood +10%, psylink +4% a level, Intellectual +1% a level above 8; grown by practice, steadier on a familiar ritual, lower after each ritual of the day.";
            if (r.RawFlow) s += "\nThe held energy adds to it every moment the ritual runs.";
            return s;
        }

        // ---------------------------------------------------------------- drawing helpers

        private static void Rim(Vector2 c, float rad)
        {
            rad *= RitualTex.RimGrow;   // the ornamental band lies outside the sections
            GUI.DrawTexture(new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f), RitualTex.Rim, ScaleMode.StretchToFill);
        }

        private static void Ring(Vector2 c, float rad) =>
            GUI.DrawTexture(new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f), RitualTex.Ring, ScaleMode.StretchToFill);

        private static bool Button(Rect r, string label, bool enabled, bool chosen = false)
        {
            bool over = enabled && Mouse.IsOver(r);
            Texture2D tex = chosen ? FlowStyle.ButtonPressed : !over ? FlowStyle.ButtonNormal : Input.GetMouseButton(0) ? FlowStyle.ButtonPressed : FlowStyle.ButtonHover;
            GUI.color = enabled || chosen ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            GUI.color = enabled || chosen ? FlowStyle.ButtonText : InkGrey;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (enabled && Widgets.ButtonInvisible(r)) { SoundDefOf.Click.PlayOneShotOnCamera(); return true; }
            return false;
        }
    }
}
