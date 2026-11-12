using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Avir.Artifactorics;
using Avir.Ritualistics;

namespace Avir.Magic
{
    // =====================================================================================
    // THE MAGIC PANEL - the third of the mod's panels (Artifacts, the Aria, Magic). On a mage's inspect pane, a tab
    // that opens a page of its own: the mage (gift, core, Arcana and its band, what steadies or swells them), the spells
    // at hand (to come: the editor of spells, up to five ready), and the runes the mage knows (to come: learning them).
    // =====================================================================================
    [StaticConstructorOnStartup]
    public static class MagicTabSetup
    {
        static MagicTabSetup()
        {
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (d.race == null || !d.race.Humanlike) continue;
                if (d.inspectorTabs == null) d.inspectorTabs = new List<System.Type>();
                if (d.inspectorTabs.Contains(typeof(ITab_Magic))) continue;
                d.inspectorTabs.Add(typeof(ITab_Magic));
                if (d.inspectorTabsResolved == null) d.inspectorTabsResolved = new List<InspectTabBase>();
                d.inspectorTabsResolved.Add(InspectTabManager.GetSharedInstance(typeof(ITab_Magic)));
            }
        }
    }

    public class ITab_Magic : ITab
    {
        public ITab_Magic() { size = new Vector2(10f, 10f); labelKey = "Avir_TabMagic"; }

        public override bool IsVisible => AvirMagic.On && SelPawn != null && Mages.IsMage(SelPawn);

        public override void OnOpen()
        {
            base.OnOpen();
            Pawn p = SelPawn;
            if (p != null && !Find.WindowStack.IsOpen<Dialog_Magic>()) Find.WindowStack.Add(new Dialog_Magic(p));
            CloseTab();
        }

        protected override void FillTab() { }
    }

    public class Dialog_Magic : Window
    {
        private readonly Pawn pawn;
        private Vector2 scroll;

        private bool sheetMax;
        private Rect sheetNormal;

        public Dialog_Magic(Pawn p)
        {
            pawn = p;
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            forcePause = true;
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1150f, Verse.UI.screenWidth - 60f), Mathf.Min(760f, Verse.UI.screenHeight - 60f));

        public override void PostOpen()
        {
            base.PostOpen();
            SheetWindow.StartMaximized(this, ref sheetMax, ref sheetNormal, true);
        }

        private static Color Ink => FlowStyle.Ink;
        private static Color InkSoft => FlowStyle.InkSoft;

        public override void DoWindowContents(Rect box)
        {
            Hediff_ArcaneCore core = Mages.Core(pawn);
            FlowStyle.Begin(pawn.MapHeld);
            Rect inner = ParchmentFrame.Draw(box).ContractedBy(8f);
            SheetWindow.MaximizeButton(this, new Rect(box.xMax - 100f, box.y + 28f, 24f, 24f), ref sheetMax, ref sheetNormal, true);
            // the pages: the mage and their spells, or the path of the mage's growth
            float tx = inner.x;
            Text.Font = GameFont.Small;
            foreach (int pg in new[] { 0, 1, 2 })
            {
                string label = pg == 0 ? "Spells" : pg == 2 ? "Arcane works" : core != null && core.Sorcerer ? "Path of the sorcerer" : "Path of the wizard";
                float w = Text.CalcSize(label).x + 48f;
                Rect tr = new Rect(tx, inner.y, w, 32f);
                if (RitualUI.Button(tr, label, true, page == pg)) page = pg;
                tx += w + 8f;
            }
            inner.yMin += 38f;
            if (page == 2)
            {
                Rect full = inner;
                RitualArt.Panel(full.ExpandedBy(6f));
                if (RitualArt.HasPanel) full = full.ContractedBy(10f);
                DrawArcaneWorks(full, core);
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                FlowStyle.End();
                return;
            }
            if (page == 1)
            {
                Rect full = inner;
                RitualArt.Panel(full.ExpandedBy(6f));
                if (RitualArt.HasPanel) full = full.ContractedBy(10f);
                DrawPath(full, core);
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                FlowStyle.End();
                return;
            }
            float lw = inner.width * 0.30f, rw = inner.width * 0.24f;
            Rect left = new Rect(inner.x, inner.y, lw, inner.height);
            Rect right = new Rect(inner.xMax - rw, inner.y, rw, inner.height);
            Rect mid = new Rect(left.xMax + 16f, inner.y, inner.width - lw - rw - 32f, inner.height);
            RitualArt.Panel(left.ExpandedBy(6f)); RitualArt.Panel(mid.ExpandedBy(6f)); RitualArt.Panel(right.ExpandedBy(6f));
            if (RitualArt.HasPanel) { left = left.ContractedBy(10f); mid = mid.ContractedBy(10f); right = right.ContractedBy(10f); }
            if (runeShown != null) DrawRuneCard(left, runeShown); else DrawMage(left, core);
            DrawSpells(mid);
            DrawRunes(right);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            FlowStyle.End();
        }

        private int page;

        /// <summary>The path of the mage: how they grow - levels and what each brings. Not laid out yet: the page waits.</summary>
        private Vector2 worksScroll, schoolsScroll;
        private float worksH, schoolsH;
        private string school;

        /// <summary>The caster's arcane works (the author, Fix155: large, dark, readable letters; no copy of the Work tab's
        /// boxes): on the left the schools - how he stands to each (green well, orange a meeting with a poor part, red
        /// poorly), and from what stage he takes up its works; on the right what the chosen school opens to him.</summary>
        private void DrawArcaneWorks(Rect r, Hediff_ArcaneCore core)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, r.y, r.width, 34f), "Arcane works of " + pawn.LabelShort);
            Text.Font = GameFont.Small;
            if (core == null) { Widgets.Label(new Rect(r.x, r.y + 40f, r.width, 30f), "No arcane core."); return; }
            string intro = "Work done by magic. The caster puts its charm together himself the first time, pays Arcana and grows in the element; above the lower mark of his Arcana he works by magic, below it by hand (if his hands are good). "
                + "The priorities are in the Work tab - the \"Arcane works\" button. Choose a school on the left to see what it opens to him.";
            float ih = Text.CalcHeight(intro, r.width);
            Widgets.Label(new Rect(r.x, r.y + 36f, r.width, ih), intro);
            float top = r.y + 44f + ih;
            Rect left = new Rect(r.x, top, r.width * 0.5f - 8f, r.yMax - top);
            Rect right = new Rect(left.xMax + 16f, top, r.xMax - left.xMax - 16f, r.yMax - top);
            if (school == null) { RuneDef m = MageClass.Main(core); school = m != null ? m.kind.ToString() : "Fire"; }

            // --- left: the schools
            Text.Font = GameFont.Medium; GUI.color = Ink;
            Widgets.Label(new Rect(left.x, left.y, left.width, 32f), "Schools - from what stage");
            Text.Font = GameFont.Small;
            Rect la = new Rect(left.x, left.y + 34f, left.width, left.height - 34f);
            Rect lv = new Rect(0f, 0f, la.width - 30f, schoolsH > 0f ? schoolsH : 900f);
            RitualScroll.Begin(la, ref schoolsScroll, lv);
            float y = 0f, w = lv.width;
            foreach (string sc in ArcaneWork.Schools)
            {
                RuneKind[] ks = SchoolKinds(sc);
                bool main = ks.Length == 1 && MageClass.Main(core)?.kind == ks[0];
                int standing = ks.Length > 0 ? ArcaneWork.Standing(core, ks) : -1;
                Color sc0 = standing < 0 ? Ink : ArcaneWork.StandingColor(standing);
                int his = ks.Length > 0 ? ArcaneWork.Stage(core, ks) : -1;
                int fromSt = ArcaneWork.From(core, sc);
                Rect row = new Rect(0f, y, w, 64f);
                bool chosen = school == sc;
                Widgets.DrawBoxSolid(row, chosen ? new Color(0.25f, 0.18f, 0.08f, 0.22f) : new Color(0.12f, 0.1f, 0.08f, Mouse.IsOver(row) ? 0.16f : 0.08f));
                if (chosen) Widgets.DrawBox(row, 2);
                GUI.color = sc0; Text.Font = GameFont.Small;
                Widgets.Label(new Rect(8f, y + 4f, w * 0.55f, 26f), "<b>" + ArcaneWork.SchoolLabel(sc).CapitalizeFirst() + "</b>" + (main ? "  (his own)" : ""));
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(w * 0.4f, y + 4f, w * 0.6f - 8f, 26f), his >= 0 ? MageClass.StageNames[Mathf.Clamp(his, 0, 5)] : "by its elements");
                Text.Anchor = TextAnchor.UpperLeft;
                float bw = (w - 16f) / 7f;
                for (int i = 0; i < 7; i++)
                {
                    Rect b = new Rect(8f + i * bw, y + 32f, bw - 4f, 26f);
                    bool on = fromSt == i;
                    Widgets.DrawBoxSolid(b, on ? (i == 6 ? ArcaneWork.Poor : new Color(0.16f, 0.3f, 0.6f)) : new Color(0.1f, 0.08f, 0.06f, Mouse.IsOver(b) ? 0.32f : 0.16f));
                    Text.Anchor = TextAnchor.MiddleCenter; GUI.color = on ? Color.white : Ink;
                    Widgets.Label(b, i == 5 ? "Grandm." : ArcaneWork.StageShort[i]);
                    Text.Anchor = TextAnchor.UpperLeft;
                    if (Widgets.ButtonInvisible(b)) { core.workFrom[sc] = i; ArcaneWork.Dirty(pawn); school = sc; }
                }
                if (Widgets.ButtonInvisible(new Rect(row.x, row.y, row.width, 30f))) school = sc;
                y += 68f;
            }
            schoolsH = y + 4f;
            Widgets.EndScrollView();

            // --- right: what the chosen school opens
            RuneKind[] sk = SchoolKinds(school);
            Text.Font = GameFont.Medium; GUI.color = Ink;
            Widgets.Label(new Rect(right.x, right.y, right.width, 32f), "What " + ArcaneWork.SchoolLabel(school) + " opens");
            Text.Font = GameFont.Small;
            Rect ra = new Rect(right.x, right.y + 34f, right.width, right.height - 34f);
            Rect rv = new Rect(0f, 0f, ra.width - 30f, worksH > 0f ? worksH : 900f);
            RitualScroll.Begin(ra, ref worksScroll, rv);
            y = 0f; w = rv.width;
            string rule = school == "Second"
                ? "The second order: three elements together - a meeting of two and a third beside it (lava is magma and fire, a geyser is steam and earth). The caster must know all its elements; the works are taken up from the stage chosen for \"second order\", and anything of magma (lava, basalt, obsidian) also not below the stage chosen for magma. Speed and slips go by his stage in its elements, on average; a slip is paid at the price of its meeting."
                : sk.Length == 1 ? (MageClass.Main(core)?.kind == sk[0] ? "His own element: open from Novice." : "Not his own element: open from Apprentice at least.") + " A slip is light."
                : "A meeting of two: open from Novice, at a risk - it slips more often, and at the meeting's own price" + (school == "Magma" ? " (magma pours lava round the caster)" : "") + ".";
            if (sk.Length > 0) rule += "\nHim now: speed x" + ArcaneWork.Speed(core, sk).ToString("0.##") + ", a slip " + ArcaneWork.SlipChance(core, sk).ToStringPercent() + " a work.";
            float rh = Text.CalcHeight(rule, w);
            GUI.color = Ink;
            Widgets.Label(new Rect(0f, 0f, w, rh), rule);
            y = rh + 8f;
            var givers = ArcaneWork.Groups.SelectMany(g => g.givers.Select(n => new { g, n })).Where(x => ArcaneWork.SchoolsOf(x.n).Contains(school)).ToList();
            if (givers.Count == 0) { Widgets.Label(new Rect(0f, y, w, 24f), "No works of this school yet."); y += 26f; }
            foreach (var x in givers)
            {
                string why = ArcaneWork.Why(core, x.n);
                GUI.color = why == null ? ArcaneWork.Good : ArcaneWork.Poor;
                string line = "<b>" + (why == null ? "+ " : "- ") + ArcaneWork.Label(x.n).CapitalizeFirst() + "</b>   [" + x.g.label + "]"
                    + (ArcaneWork.Gives.TryGetValue(x.n, out string gives) ? "\n     " + gives.CapitalizeFirst() + "." : "") + (why == null ? "" : "\n     Not yet: " + why + ".");
                float h = Text.CalcHeight(line, w);
                Widgets.Label(new Rect(0f, y, w, h), line);
                y += h + 4f;
            }
            worksH = y + 4f;
            Widgets.EndScrollView();
            GUI.color = Color.white;
        }

        private static RuneKind[] SchoolKinds(string sc)
        {
            if (Enum.TryParse(sc, out RuneKind k) && (k == RuneKind.Fire || k == RuneKind.Water || k == RuneKind.Earth || k == RuneKind.Air)) return new[] { k };
            if (Enum.TryParse(sc, out ComboKind ck) && ck != ComboKind.None) return Combos.Parts(ck);
            return new RuneKind[0];   // the second order: by its three elements
        }

        private Vector2 pathScroll;
        private float pathH;

        /// <summary>The path of the mage: the core's level, then a scale for every element they work - its stage,
        /// what the stage gives, how far to the next; Life with its store.</summary>
        private void DrawPath(Rect r, Hediff_ArcaneCore core)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, r.y, r.width, 34f), "The path of " + pawn.LabelShort);
            Text.Font = GameFont.Small;
            if (core == null) { GUI.color = InkSoft; Widgets.Label(new Rect(r.x, r.y + 40f, r.width, 30f), "No arcane core."); return; }
            RuneDef main = MageClass.Main(core);
            GUI.color = SpellGlyph.Ink(core.Sorcerer ? Hediff_ArcaneCore.SorcererColor : Hediff_ArcaneCore.MageColor);
            Widgets.Label(new Rect(r.x, r.y + 32f, r.width, 24f), (core.Sorcerer ? "Sorcerer" : "Wizard") + " - main element: " + (main != null ? main.LabelCap.ToString() : "none yet - a path ritual decides"));

            // the core's level: one long bar to the next level
            float all = MageClass.All(core);
            int lv = MageClass.LevelOf(all);
            float from = MageClass.ForLevel(lv), to = MageClass.ForLevel(lv + 1);
            Rect lb = new Rect(r.x, r.y + 60f, r.width, 26f);
            Widgets.FillableBar(lb, Mathf.Clamp01((all - from) / Mathf.Max(1f, to - from)), barLevel ?? (barLevel = SolidColorMaterials.NewSolidColorTexture(new Color(0.78f, 0.6f, 0.22f))),
                barBack ?? (barBack = SolidColorMaterials.NewSolidColorTexture(new Color(0.12f, 0.1f, 0.08f, 0.6f))), false);
            GUI.color = Color.white; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(lb, "Core level " + core.level + "   " + all.ToString("0") + " / " + to.ToString("0"));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = InkSoft;
            Widgets.Label(new Rect(r.x, lb.yMax + 2f, r.width, 22f), "Free points of the path: " + core.points + "   ·   reserve " + Mathf.FloorToInt(core.Capacity) + " Arcana" + (core.reservePoints > 0 ? " (" + core.reservePoints + " point(s) in it)" : ""));

            // the scales
            Rect card = new Rect(r.x, lb.yMax + 28f, r.width, r.yMax - lb.yMax - 28f - 46f);
            if (RitualArt.HasCard) RitualArt.CardAt(card); else Widgets.DrawBoxSolid(card, new Color(0.93f, 0.85f, 0.7f, 0.85f));
            Rect area = card.ContractedBy(12f);
            Rect view = new Rect(0f, 0f, area.width - 16f, pathH > 0f ? pathH : 600f);
            RitualScroll.Begin(area, ref pathScroll, view);
            float y = 0f, w = view.width;
            var els = MagePath.Made.ToList();
            RuneDef life = DefDatabase<RuneDef>.AllDefsListForReading.FirstOrDefault(x => x.role == RuneRole.Element && x.kind == RuneKind.Life);
            // the great elements have their scales too (Fix232): Light always - it can be taken by the path ritual, and every
            // cast of it teaches; Life always - shown closed while it cannot be taken, filled if the mage has touched it
            RuneDef light = DefDatabase<RuneDef>.AllDefsListForReading.FirstOrDefault(x => x.role == RuneRole.Element && x.kind == RuneKind.Light);
            if (light != null && !els.Contains(light)) els.Add(light);
            if (life != null && !els.Contains(life)) els.Add(life);
            foreach (RuneDef e in els)
            {
                y += Scale(new Rect(0f, y, w, 0f), core, e, e == main);
                // Life (the author, Fix233): its experience is open to anyone - only the path ritual cannot make it the main element
                string closed = LifeMagic.Is(e) && MageClass.Main(core)?.kind != RuneKind.Life ? "Life grows by casting it, but no path ritual makes it the main element - only druids walk it" : null;
                if (closed != null)
                {
                    GUI.color = InkSoft; Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(58f, y - 6f, w - 58f, 18f), closed + ".");
                    y += 14f; Text.Font = GameFont.Small;
                }
            }
            y += 4f;
            GUI.color = InkSoft; Text.Font = GameFont.Tiny;
            y += 4f;
            float th = Text.CalcHeight("A meeting of two elements is not a scale of its own: its experience goes to both, shared.", w);
            Widgets.Label(new Rect(0f, y, w, th), "A meeting of two elements is not a scale of its own: its experience goes to both, shared.");
            y += th;
            Text.Font = GameFont.Small;
            pathH = y + 8f;
            Widgets.EndScrollView();

            GUI.color = Color.white;
            Rect b = new Rect(r.x, r.yMax - 40f, 320f, 34f);
            if (core.points > 0)
            {
                if (Widgets.ButtonText(b, "Put a point into the reserve (+" + (100f * ArcanaIntake.PerPoint).ToString("0") + "%)")) ArcanaIntake.Invest(core);
            }
            else { GUI.color = InkSoft; Widgets.Label(new Rect(b.x, b.y + 7f, b.width, 24f), "No free point of the path."); }
            GUI.color = Color.white;
        }

        /// <summary>One element's scale: the rune, its stage, a bar in its colour with the stage marks, and what the stage gives.</summary>
        private float Scale(Rect r, Hediff_ArcaneCore core, RuneDef e, bool main)
        {
            float x = MageClass.XpIn(core, e.kind);
            int st = MageClass.Stage(x);
            bool top = st >= MageClass.StageAt.Length - 1;
            float h = 70f;
            Rect row = new Rect(r.x, r.y, r.width, h - 6f);
            Color col = e.color;
            Widgets.DrawBoxSolid(row, new Color(col.r, col.g, col.b, main ? 0.16f : 0.08f));
            SpellGlyph.Draw(new Rect(row.x + 4f, row.y + 8f, 46f, 46f), e);
            float tx = row.x + 58f, tw = row.width - 62f;
            GUI.color = SpellGlyph.Ink(col); Text.Font = GameFont.Small;
            Widgets.Label(new Rect(tx, row.y + 2f, tw * 0.5f, 24f), e.LabelCap + (main ? "  (main)" : ""));
            Text.Anchor = TextAnchor.UpperRight; GUI.color = Ink;
            Widgets.Label(new Rect(tx + tw * 0.5f, row.y + 2f, tw * 0.5f, 24f), MageClass.StageNames[st]);
            Text.Anchor = TextAnchor.UpperLeft;
            // the bar runs over the whole scale, Novice to Grandmaster; the marks are the stages
            float max = MageClass.StageAt[MageClass.StageAt.Length - 1];
            Rect bar = new Rect(tx, row.y + 24f, tw, 14f);
            Widgets.DrawBoxSolid(bar, new Color(0.12f, 0.1f, 0.08f, 0.55f));
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(Pos(x) / Pos(max)), bar.height), new Color(col.r, col.g, col.b, 0.9f));
            for (int i = 1; i < MageClass.StageAt.Length; i++)
            {
                float fx = bar.x + bar.width * Pos(MageClass.StageAt[i]) / Pos(max);
                Widgets.DrawBoxSolid(new Rect(fx - 1f, bar.y - 2f, 2f, bar.height + 4f), i <= st ? new Color(1f, 0.86f, 0.4f) : new Color(0.25f, 0.2f, 0.15f));
            }
            TooltipHandler.TipRegion(bar, string.Join("\n", MageClass.StageNames.Select((n, i) => n + " - " + MageClass.StageAt[i].ToString("0"))));
            GUI.color = InkSoft; Text.Font = GameFont.Tiny;
            string next = top ? "the top of the scale" : x.ToString("0") + " / " + MageClass.StageAt[st + 1].ToString("0") + " to " + MageClass.StageNames[st + 1];
            Widgets.Label(new Rect(tx, bar.yMax + 2f, tw, 18f), next + "   ·   power x" + MageClass.PowerAt[st].ToString("0.##") + ", slip x" + MageClass.SlipAt[st].ToString("0.##")
                + ", cast x" + MageClass.CastAt[st].ToString("0.##") + ", learns x" + MageClass.Learns(core, e.kind).ToString("0.#"));
            Text.Font = GameFont.Small;
            if (LifeMagic.Is(e))
            {
                // the stores of Life and its meetings under its scale, those shown
                foreach (RuneKind sk in LifeMagic.AllKinds.Where(core.StoreShown))
                {
                    Rect vb = new Rect(tx, r.y + h - 4f, tw, 16f);
                    Color sc = sk == LifeMagic.None ? new Color(0.35f, 0.7f, 0.3f, 0.9f) : LifeMagic.PairColor(sk);
                    Widgets.DrawBoxSolid(vb, new Color(0.12f, 0.1f, 0.08f, 0.55f));
                    Widgets.DrawBoxSolid(new Rect(vb.x, vb.y, vb.width * Mathf.Clamp01(core.Store(sk) / Mathf.Max(1f, core.VitaMax)), vb.height), new Color(sc.r, sc.g, sc.b, 0.9f));
                    GUI.color = Color.white; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(vb, "Store of " + LifeMagic.StoreName(sk) + " " + Mathf.FloorToInt(core.Store(sk)) + " / " + Mathf.FloorToInt(core.VitaMax));
                    Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                    h += 20f;
                }
            }
            GUI.color = Color.white;
            return h;
        }

        /// <summary>The scale is drawn by square root, so the first stages are not squeezed into a sliver.</summary>
        private static float Pos(float xp) => Mathf.Sqrt(Mathf.Max(0f, xp));

        private static Texture2D barSorc, barMage, barBack, barLevel;
        private void DrawMage(Rect r, Hediff_ArcaneCore core)
        {
            bool sorc = core != null && core.Sorcerer;
            GUI.color = Ink; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, r.y, r.width, 34f), pawn.LabelShortCap);
            Text.Font = GameFont.Small;
            GUI.color = SpellGlyph.Ink(sorc ? Hediff_ArcaneCore.SorcererColor : Hediff_ArcaneCore.MageColor);
            // the element the mage belongs to stands right under the name, with its rune
            RuneDef el = core != null ? MageClass.Main(core) : null;
            string of = el != null ? " of " + el.LabelCap : "";
            float gx = r.x;
            if (el != null) { SpellGlyph.Draw(new Rect(r.x, r.y + 34f, 24f, 24f), el); gx += 28f; }
            Widgets.Label(new Rect(gx, r.y + 34f, r.width - (gx - r.x), 24f), sorc ? "Sorcerer" + of + " - born with magic" : "Wizard" + of + " - magic taken in");
            if (core == null) { GUI.color = InkSoft; Widgets.Label(new Rect(r.x, r.y + 64f, r.width, 60f), "No arcane core - the gift will grow it back."); return; }
            // the Arcana, large
            Rect bar = new Rect(r.x, r.y + 70f, r.width, 34f);
            // (Fix250) the bar's colours are made once, not every frame
            barSorc = barSorc ?? SolidColorMaterials.NewSolidColorTexture(new Color(0.93f, 0.68f, 0.12f));
            barMage = barMage ?? SolidColorMaterials.NewSolidColorTexture(new Color(0.16f, 0.32f, 0.92f));
            barBack = barBack ?? SolidColorMaterials.NewSolidColorTexture(new Color(0.12f, 0.1f, 0.08f, 0.6f));
            Widgets.FillableBar(bar, Mathf.Clamp01(core.Fill), sorc ? barSorc : barMage, barBack, false);
            core.Band(out float lo, out float hi);
            Color mk = sorc ? new Color(0.05f, 0.04f, 0.03f) : new Color(1f, 0.84f, 0.35f);
            Widgets.DrawBoxSolid(new Rect(bar.x + bar.width * lo - 1f, bar.y, 2f, bar.height), mk);
            if (hi < 0.999f) Widgets.DrawBoxSolid(new Rect(bar.x + bar.width * hi - 1f, bar.y, 2f, bar.height), mk);
            if (core.vent) Widgets.DrawBoxSolid(new Rect(bar.x + bar.width * core.target - 1f, bar.y - 4f, 2f, bar.height + 4f), Color.white);
            GUI.color = Color.white; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, "Arcana " + Mathf.FloorToInt(core.arcana) + " / " + Mathf.FloorToInt(core.Capacity));
            Text.Anchor = TextAnchor.UpperLeft;
            if (Mouse.IsOver(bar) && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                core.target = Mathf.Clamp01((Event.current.mousePosition.x - bar.x) / bar.width); core.vent = true; Event.current.Use();
            }
            Rect vt = new Rect(r.x, bar.yMax + 6f, r.width, 24f);
            GUI.color = Ink;
            bool v = core.vent;
            Widgets.CheckboxLabeled(vt, "Let the excess out (above the white mark)", ref v);
            core.vent = v;
            // behaviour: the artifacts' charge - by itself when low, or now by hand (no gizmo: the author wants fewer of them)
            float held = ArcanaIntake.Held(pawn);
            vt = new Rect(r.x, vt.yMax + 2f, r.width - 116f, 24f);
            bool da = core.drawArtifacts;
            Widgets.CheckboxLabeled(vt, "Draw from worn artifacts when low", ref da);
            core.drawArtifacts = da;
            TooltipHandler.TipRegion(vt, "When the Arcana falls below the lower mark, the mage takes the charge of the artifacts worn back into the core - half of it comes as Arcana. "
                + (ArcanaIntake.Avir(pawn) ? "The core fills to its edge; the rest goes into the essence." : "Only up to the edge of the reserve: what is past it stays in the artifacts.")
                + "\nHeld in the artifacts now: " + held.ToString("0") + " (" + (held * ArcanaIntake.FromArtifact).ToString("0") + " Arcana).");
            Rect now = new Rect(vt.xMax + 6f, vt.y, 110f, 24f);
            bool can = held >= 1f && (ArcanaIntake.Avir(pawn) || core.arcana < core.Capacity);
            GUI.color = can ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            if (Widgets.ButtonText(now, "Draw now", true, true, can))
            {
                float drawn = ArcanaIntake.Absorb(pawn);
                Messages.Message(pawn.LabelShortCap + " drew " + drawn.ToString("0") + " Arcana from the artifacts.", pawn, MessageTypeDefOf.NeutralEvent, false);
            }
            TooltipHandler.TipRegion(now, held < 1f ? "No charge in the artifacts worn." : !can ? "The reserve is full." : "Take the artifacts' charge back now.");
            GUI.color = Ink;
            vt.width = r.width;
            // battle charms (Fix156): put together and used by himself; the dangerous ones only if allowed
            vt = new Rect(r.x, vt.yMax + 2f, r.width, 24f);
            bool bc = core.battleCharms;
            Widgets.CheckboxLabeled(vt, "Battle charms - puts one together, uses it himself", ref bc);
            core.battleCharms = bc;
            TooltipHandler.TipRegion(vt, "He puts a battle charm together by his element and stage (a burst, a line, a hold or a throw - his own manner), with two Barriers so his own are spared, and casts it by himself: hunting, drafted with fire at will, and - if allowed below - at a foe that comes near while undrafted.");
            vt = new Rect(r.x, vt.yMax + 2f, r.width * 0.5f - 4f, 24f);
            bool dc = core.dangerousCharms, fu = core.fightUndrafted;
            Widgets.CheckboxLabeled(vt, "Dangerous allowed", ref dc);
            TooltipHandler.TipRegion(vt, "Lets him build dangerous charms: amplified bursts (explosions). Off - he keeps them tame.");
            Rect vt2 = new Rect(r.x + r.width * 0.5f + 4f, vt.y, r.width * 0.5f - 4f, 24f);
            Widgets.CheckboxLabeled(vt2, "Fights undrafted", ref fu);
            TooltipHandler.TipRegion(vt2, "Undrafted, he casts his charm at a foe that comes within 14 cells.");
            if (dc != core.dangerousCharms) { core.dangerousCharms = dc; if (core.battleCharms) CombatCharms.Ensure(core, dc); }
            core.fightUndrafted = fu;
            vt.width = r.width;
            // below: six blocks - the path, the Arcana coming in, the core, the casting, what steadies, what happened
            Rect card = new Rect(r.x, vt.yMax + 8f, r.width, r.yMax - vt.yMax - 8f);
            if (RitualArt.HasCard) RitualArt.CardAt(card); else Widgets.DrawBoxSolid(card, new Color(0.93f, 0.85f, 0.7f, 0.85f));
            Rect area = card.ContractedBy(12f);
            Rect view = new Rect(0f, 0f, area.width - 16f, mageH > 0f ? mageH : 900f);
            RitualScroll.Begin(area, ref mageScroll, view);
            float y = 0f, w = view.width;
            Map map = pawn.MapHeld;
            int tier = Spells.FlowTier(map);
            MapComponent_ExternalFlow ext = map != null ? MapComponent_ExternalFlow.Of(map) : null;
            string tierName = ext != null ? MapComponent_ExternalFlow.Tier(ext.Total) : "calm";
            CompSanctuary sanct = CompSanctuary.Near(pawn);

            // 1. the path
            Heading(ref y, w, "Path");
            if (core.path != null)
            {
                SpellGlyph.Draw(new Rect(0f, y, 40f, 40f), core.path);
                int own = core.Book.spells.Count(sp => Spells.Element(sp) == core.path);
                Line(new Rect(48f, y, w - 48f, 44f), core.path.LabelCap + ". " + (own == 0 ? "No spells of it in the book yet." : own == 1 ? "One spell of it in the book." : own + " spells of it in the book."), Ink);
                y += 48f;
            }
            else y += Line(new Rect(0f, y, w, 60f), "No path yet. A path ritual decides it.", InkSoft);

            // 2. the Arcana coming in, an hour
            Heading(ref y, w, "Arcana, an hour");
            float med = core.Capacity * 0.25f * (1f + 0.5f * MapComponent_ExternalFlow.TierIndex(ext?.Total ?? 0f));
            y += Row(y, w, "Meditating", "+" + (sanct != null ? (med * 3f).ToString("0") + " by the sanctuary" : med.ToString("0")), true);
            int seepTier = MapComponent_ExternalFlow.TierIndex(ext?.Total ?? 0f);
            y += Row(y, w, "The stream", seepTier > 0 ? "+" + (core.Capacity * 0.02f * seepTier).ToString("0.#") + ", " + tierName : "quiet", seepTier > 0);
            y += Row(y, w, "Sanctuary", sanct != null ? sanct.element.LabelCap + ", " + sanct.Fill.ToStringPercent() + " full" : "none near", sanct != null);

            // 3. the core
            Heading(ref y, w, "Core");
            y += Row(y, w, "Level " + core.level, Mathf.FloorToInt(core.Capacity) + " Arcana", true);
            y += Bar(y, w, "Composure", core.Composure, new Color(0.35f, 0.55f, 0.85f), "Steadier hands, a wider safe band. Now " + lo.ToStringPercent() + " to " + hi.ToStringPercent() + "."
                + "\n\nHow a spell holds (Fix246): a spell's instability is the sum of its runes' (each element 0.05, a form 0.15, a Reach " + (AvirMod.Settings?.reachRisk ?? 0.1f).ToString("0.00") + "...), with the pull of the stream. Up to the caster's composure it always holds; past it, every 0.1 over takes off about 4% (more for an element the caster knows little)."
                + " A sorcerer (not Avir) holds as if with 30% more composure. Composure here: the stat, plus a pattern the hand knows (practice), plus a steady hand standing near."
                + "\n\n" + ComposureExplained(pawn));
            Hediff g = pawn.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneGreatness);
            y += Bar(y, w, "Greatness", g?.Severity ?? 0f, new Color(0.9f, 0.62f, 0.15f), "Grows when the Arcana runs high, and with spells cast in a strong stream. Too much of it, and the Arcana bursts out.");
            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneHollow);
            y += Bar(y, w, "Hollow", h?.Severity ?? 0f, new Color(0.45f, 0.42f, 0.5f), "Grows when the Arcana runs low. It never quite leaves.");

            // 4. the casting
            Heading(ref y, w, "Casting");
            y += Row(y, w, "Speed", sorc ? "quick - a sorcerer" : "slow - a wizard", true);
            RitualSchema probe = SpellExamples.Fire().FirstOrDefault();
            if (probe != null) y += Row(y, w, "Holds a simple spell", Spells.HoldChance(pawn, probe).ToStringPercent(), true);
            y += Row(y, w, "The stream", tier > 0 ? "+" + (15 * tier) + "% power" : "no help", tier > 0);

            // 5. what steadies
            Heading(ref y, w, "What steadies");
            bool horned = MagicConductors.Horned(pawn);
            y += Row(y, w, "Horns", horned ? "yes" : "none", horned);
            int arts = MagicConductors.Artifacts(pawn);
            y += Row(y, w, "Artifacts worn", arts > 0 ? arts.ToString() : "none", arts > 0);
            y += Row(y, w, "Core size", MagicConductors.CapacityFactor(pawn).ToStringPercent(), true);

            // 6. what happened lately
            Heading(ref y, w, "Lately");
            if (core.log.NullOrEmpty()) y += Line(new Rect(0f, y, w, 26f), "Nothing yet.", InkSoft);
            else
                foreach (string e in Enumerable.Reverse(core.log))
                {
                    int cut = e.IndexOf('|');
                    int tick = cut > 0 && int.TryParse(e.Substring(0, cut), out int tk) ? tk : 0;
                    string ago = (Find.TickManager.TicksGame - tick).ToStringTicksToPeriod(true, true) + " ago";
                    y += Line(new Rect(0f, y, w, 44f), ago + ": " + (cut > 0 ? e.Substring(cut + 1) : e), Ink);
                }
            mageH = y + 10f;
            Widgets.EndScrollView();
        }

        private Vector2 mageScroll;
        private float mageH;

        private static void Heading(ref float y, float w, string label)
        {
            y += 6f;
            GUI.color = SpellGlyph.Ink(new Color(0.9f, 0.6f, 0.2f));
            Widgets.Label(new Rect(0f, y, w, 24f), label.ToUpper());
            GUI.color = new Color(0.55f, 0.4f, 0.2f, 0.5f);
            Widgets.DrawLineHorizontal(0f, y + 23f, w);
            y += 28f;
        }

        private static float Line(Rect r, string text, Color c)
        {
            GUI.color = c;
            float h = Text.CalcHeight(text, r.width);
            Widgets.Label(new Rect(r.x, r.y, r.width, h), text);
            return h + 2f;
        }

        private static float Row(float y, float w, string label, string value, bool on)
        {
            GUI.color = Ink;
            Widgets.Label(new Rect(0f, y, w * 0.5f, 24f), label);
            GUI.color = on ? Ink : InkSoft;
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(w * 0.42f, y, w * 0.58f, 24f), value);
            Text.Anchor = TextAnchor.UpperLeft;
            return 24f;
        }

        /// <summary>The composure stat taken apart, as the game's stat card would show it.</summary>
        private static string ComposureExplained(Pawn p)
        {
            StatDef st = RitualDefOf.Avir_Composure;
            if (st == null || p == null) return "";
            try { return st.Worker.GetExplanationFull(StatRequest.For(p), ToStringNumberSense.Absolute, p.GetStatValue(st)); }
            catch { return ""; }
        }

        private static float Bar(float y, float w, string label, float v, Color c, string tip)
        {
            GUI.color = Ink;
            Widgets.Label(new Rect(0f, y, 110f, 24f), label);
            Rect b = new Rect(112f, y + 4f, w - 112f, 16f);
            Widgets.DrawBoxSolid(b, new Color(0.93f, 0.86f, 0.7f, 0.8f));
            Widgets.DrawBoxSolid(new Rect(b.x, b.y, b.width * Mathf.Clamp01(v), b.height), c);
            GUI.color = new Color(0.4f, 0.3f, 0.18f, 0.8f); Widgets.DrawBox(b, 1);
            GUI.color = Ink; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(b.x, b.y - 4f, b.width, 24f), v.ToStringPercent());
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(new Rect(0f, y, w, 24f), tip);
            return 26f;
        }

        private Vector2 spellScroll;
        private int groupBy;
        private string search = "";
        private readonly HashSet<string> folded = new HashSet<string>();

        private void DrawSpells(Rect r)
        {
            Hediff_ArcaneCore core = Mages.Core(pawn);
            GUI.color = Ink; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, r.y, r.width, 34f), "Spells");
            Text.Font = GameFont.Small;
            if (core == null) return;
            MageBook b = core.Book;
            // the places at hand: three at first, up to five; click one to clear it
            float w = Mathf.Min(86f, (r.width - 40f) / 5f);
            for (int i = 0; i < 5; i++)
            {
                Rect sl = new Rect(r.x + i * (w + 10f), r.y + 40f, w, w);
                bool open = i < b.open;
                GUI.color = new Color(1f, 1f, 1f, open ? 0.9f : 0.3f);
                GUI.DrawTexture(sl, RitualOrnament.FrameGlow ?? BaseContent.WhiteTex, ScaleMode.ScaleToFit);
                RitualSchema s = b.ready[i];
                if (open && s != null)
                {
                    RuneDef icon = Spells.Element(s) ?? RitualEvaluator.Read(s).verbs.FirstOrDefault();
                    SpellIconArt.Draw(sl.ContractedBy(w * 0.14f), s);
                    GUI.color = Ink; Text.Anchor = TextAnchor.UpperCenter;
                    Widgets.Label(new Rect(sl.x - 8f, sl.yMax + 2f, sl.width + 16f, 22f), s.name.Truncate(sl.width + 16f));
                    Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                    TooltipHandler.TipRegion(sl, s.name + "\n" + Spells.Describe(s) + "\n\nClick to take it from the hand.");
                    if (Widgets.ButtonInvisible(sl)) b.ready[i] = null;
                }
                else TooltipHandler.TipRegion(sl, open ? "An empty place: put a spell here from the book below." : "Not open yet - the caster's growth opens it.");
            }
            // the book of the mage
            float top = r.y + 52f + w + 32f;
            GUI.color = Ink;
            Widgets.Label(new Rect(r.x, top, r.width - 540f, 26f), "The book (" + b.spells.Count + ")");
            // for testing until the mage's path gives spells: every ready spell of fire at once (dev mode only)
            if (RitualUI.Button(new Rect(r.xMax - 340f, top - 4f, 182f, 32f), "Kept spells..."))
                Find.WindowStack.Add(new Dialog_SpellLibrary(pawn));
            // dev mode (Fix233, the author: "all at once" filled the book with hundreds and they were hard to sort): a window
            // to pick the ready spells by group, one by one, with a search - and to take them out again
            if (Prefs.DevMode && RitualUI.Button(new Rect(r.xMax - 524f, top - 4f, 176f, 32f), "Dev: ready spells..."))
                Find.WindowStack.Add(new Dialog_DevSpells(b));
            if (RitualUI.Button(new Rect(r.xMax - 150f, top - 4f, 150f, 32f), "New spell..."))
                Find.WindowStack.Add(new Dialog_SpellEditor(new RitualSchema { name = "New spell", author = pawn.LabelShort }, pawn, sp => { if (sp.author.NullOrEmpty()) sp.author = pawn.LabelShort; b.spells.Add(sp); }));
            // the book in sections: by what the spells do, by their element, or by who wrote them; and a search
            float gy = top + 32f, gx = r.x;
            foreach (int g in new[] { 0, 1, 2 })
            {
                string gl = g == 0 ? "By effect" : g == 1 ? "By element" : "By author";
                float gw = Text.CalcSize(gl).x + 30f;
                if (RitualUI.Button(new Rect(gx, gy, gw, 28f), gl, true, groupBy == g)) groupBy = g;
                gx += gw + 6f;
            }
            Rect sr = new Rect(Mathf.Max(gx + 10f, r.xMax - 220f), gy, Mathf.Min(220f, r.xMax - gx - 10f), 28f);
            search = Widgets.TextField(sr, search ?? "");
            if (search.NullOrEmpty()) { GUI.color = InkSoft; Widgets.Label(new Rect(sr.x + 6f, sr.y + 4f, sr.width, 24f), "search..."); GUI.color = Ink; }
            Rect area = new Rect(r.x, gy + 36f, r.width, r.yMax - gy - 36f);
            const float rowH = 112f, headH = 30f;
            var shown = b.spells.Where(x => search.NullOrEmpty() || x.name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var groups = shown.GroupBy(x => SpellSections.Of(x, groupBy, pawn)).OrderBy(g => SpellSections.Order(g.Key, groupBy)).ToList();
            float total = 0f;
            foreach (var g in groups) total += headH + (folded.Contains(groupBy + g.Key) ? 0f : g.Count() * (rowH + 6f));
            Rect view = new Rect(0f, 0f, area.width - 16f, Mathf.Max(60f, total));
            RitualScroll.Begin(area, ref spellScroll, view);
            float y = 0f;
            if (b.spells.Count == 0) { GUI.color = InkSoft; Widgets.Label(new Rect(0f, 0f, view.width, 60f), "No spells yet. Write one: an element, a form or verb, and the modifiers you like - in a small circle at first."); }
            foreach (var grp in groups)
            {
            // the section's head: click to fold it
            Rect hr = new Rect(0f, y, view.width, headH - 4f);
            bool shut = folded.Contains(groupBy + grp.Key);
            Widgets.DrawBoxSolid(hr, new Color(0.55f, 0.4f, 0.25f, 0.18f));
            GUI.color = Ink; Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(hr.x + 8f, hr.y, hr.width - 8f, hr.height), (shut ? "▸ " : "▾ ") + grp.Key + "  (" + grp.Count() + ")");
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonInvisible(hr)) { if (shut) folded.Remove(groupBy + grp.Key); else folded.Add(groupBy + grp.Key); }
            y += headH;
            if (shut) continue;
            foreach (RitualSchema s in grp.ToList())
            {
                // one card a spell: its sign, its name, what it does, what it costs - and the buttons under it
                Rect row = new Rect(0f, y, view.width, rowH);
                bool inHand = b.ready.Contains(s);
                if (RitualArt.HasCard) RitualArt.CardAt(row); else Widgets.DrawBoxSolid(row, new Color(0.95f, 0.88f, 0.7f, 0.55f));
                if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row.ContractedBy(4f), new Color(1f, 0.9f, 0.6f, 0.12f));
                Rect body = row.ContractedBy(10f);
                RuneDef icon = Spells.Element(s) ?? RitualEvaluator.Read(s).verbs.FirstOrDefault();
                SpellIconArt.Draw(new Rect(body.x, body.y, 56f, 56f), s);
                float tx = body.x + 66f, tw = body.width - 66f;
                GUI.color = Ink;
                Widgets.Label(new Rect(tx, body.y - 2f, tw - 90f, 24f), s.name.Truncate(tw - 90f));
                if (inHand) { GUI.color = SpellGlyph.Ink(Spells.ColorOf(s)); Text.Anchor = TextAnchor.UpperRight; Widgets.Label(new Rect(tx, body.y - 2f, tw, 24f), "in hand"); Text.Anchor = TextAnchor.UpperLeft; }
                GUI.color = InkSoft;
                string main = SpellText.Explain(s).main;
                Widgets.Label(new Rect(tx, body.y + 22f, tw, 40f), main.Truncate(tw * 2f - 30f));
                Hediff_ArcaneCore cc = Mages.Core(pawn);
                string stats = "Arcana " + Spells.Cost(s).ToString("0.#") + " · range " + Spells.Range(s).ToString("0") + (cc != null ? " · cast " + (Spells.CastTicks(cc, s) / 60f).ToString("0.#") + " s" : "");
                GUI.color = Ink;
                Widgets.Label(new Rect(body.x, body.yMax - 26f, body.width - 345f, 26f), stats);
                TooltipHandler.TipRegion(new Rect(row.x, row.y, row.width - 350f, row.height), s.name + "\n\n" + Spells.Describe(s) + "\n\nDouble-click: " + (inHand ? "take it from the hand." : "put it in the hand."));
                bool dbl = Event.current.type == EventType.MouseDown && Event.current.button == 0 && Event.current.clickCount == 2 && Mouse.IsOver(new Rect(row.x, row.y, row.width - 270f, row.height));
                float by = body.yMax - 32f;
                if (dbl && inHand) { for (int i = 0; i < 5; i++) if (b.ready[i] == s) b.ready[i] = null; Event.current.Use(); }
                else if (RitualUI.Button(new Rect(body.xMax - 258f, by, 92f, 32f), "To hand", !inHand) || dbl)
                {
                    if (dbl) Event.current.Use();
                    int free = -1;
                    for (int i = 0; i < b.open; i++) if (b.ready[i] == null) { free = i; break; }
                    if (free < 0) Messages.Message("Every place at hand is taken - clear one first.", MessageTypeDefOf.RejectInput, false);
                    else b.ready[free] = s;
                }
                if (RitualUI.Button(new Rect(body.xMax - 336f, by, 72f, 32f), "Keep")) SpellKeep.Keep(s);
                TooltipHandler.TipRegion(new Rect(body.xMax - 336f, by, 72f, 32f), "Keep this spell - in the colony's book or in files beside the mod (mod settings, Magic).");
                if (RitualUI.Button(new Rect(body.xMax - 162f, by, 76f, 32f), "Edit"))
                {
                    RitualSchema old = s;
                    Find.WindowStack.Add(new Dialog_SpellEditor(s, pawn, sp =>
                    {
                        int at = b.spells.IndexOf(old);
                        if (at >= 0) b.spells[at] = sp; else b.spells.Add(sp);
                        for (int i = 0; i < 5; i++) if (b.ready[i] == old) b.ready[i] = sp;
                    }));
                }
                if (RitualUI.Button(new Rect(body.xMax - 82f, by, 82f, 32f), "Erase"))
                {
                    b.spells.Remove(s);
                    for (int i = 0; i < 5; i++) if (b.ready[i] == s) b.ready[i] = null;
                }
                y += rowH + 6f;
            }
            }
            Widgets.EndScrollView();
        }

        private RuneDef runeShown;

        /// <summary>The left column given to a rune pressed on the right: what it is, and what it does with each element.</summary>
        private void DrawRuneCard(Rect r, RuneDef rn)
        {
            Rect back = new Rect(r.x, r.y, r.width, r.height - 48f);
            if (RitualArt.HasCard) RitualArt.CardAt(back); else Widgets.DrawBoxSolid(back, new Color(0.93f, 0.85f, 0.7f, 0.85f));
            Rect full = r;
            r = new Rect(r.x + 14f, r.y + 14f, r.width - 28f, r.height - 28f);
            SpellGlyph.Draw(new Rect(r.x, r.y, 64f, 64f), rn);
            GUI.color = Ink; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x + 76f, r.y + 4f, r.width - 76f, 34f), rn.LabelCap);
            Text.Font = GameFont.Small; GUI.color = InkSoft;
            Widgets.Label(new Rect(r.x + 76f, r.y + 38f, r.width - 76f, 24f), rn.role == RuneRole.Element ? (SpellCircle.GreatElement(rn) ? "great element - wraps other circles" : "small element") : rn.role.ToString().ToLower());
            float y = r.y + 76f;
            GUI.color = Ink;
            string desc = RitualText.Desc(rn);
            float h = Text.CalcHeight(desc, r.width);
            Widgets.Label(new Rect(r.x, y, r.width, h), desc);
            y += h + 14f;
            GUI.color = SpellGlyph.Ink(new Color(0.9f, 0.6f, 0.2f));
            Widgets.Label(new Rect(r.x, y, r.width, 24f), "WITH EACH ELEMENT");
            y += 28f;
            foreach (string el in new[] { "Fire", "Water", "Earth", "Air" })
            {
                RuneDef e = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + el);
                if (e != null) SpellGlyph.Draw(new Rect(r.x, y, 24f, 24f), e);
                bool made = true;
                GUI.color = made ? Ink : InkSoft;
                string line = el + " - " + Dialog_SpellEditor.LineFor(el, rn);
                float lh = Text.CalcHeight(line, r.width - 32f);
                Widgets.Label(new Rect(r.x + 32f, y, r.width - 32f, lh), line);
                y += Mathf.Max(28f, lh + 4f);
            }
            if (RitualUI.Button(new Rect(full.x, full.yMax - 40f, full.width, 38f), "Back to " + pawn.LabelShort)) runeShown = null;
        }

        private void DrawRunes(Rect r)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, r.y, r.width, 34f), "Runes");
            Text.Font = GameFont.Small;
            List<RuneDef> runes = RuneDef.All.Where(x => x.magic).ToList();
            Rect area = new Rect(r.x, r.y + 40f, r.width, r.height - 40f);
            Rect view = new Rect(0f, 0f, area.width - 16f, runes.Count * 46f);
            RitualScroll.Begin(area, ref scroll, view);
            float y = 0f;
            foreach (RuneDef rn in runes)
            {
                Rect row = new Rect(0f, y, view.width, 44f);
                if (rn == runeShown) { if (!RitualArt.Selected(row)) Widgets.DrawBoxSolid(row, new Color(FlowStyle.Gold.r, FlowStyle.Gold.g, FlowStyle.Gold.b, 0.25f)); }
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                SpellGlyph.Draw(new Rect(0f, y + 2f, 40f, 40f), rn);
                GUI.color = Ink;
                Widgets.Label(new Rect(48f, y + 2f, view.width - 50f, 22f), rn.LabelCap);
                GUI.color = InkSoft;
                Widgets.Label(new Rect(48f, y + 22f, view.width - 50f, 22f), rn.role == RuneRole.Element ? (SpellCircle.GreatElement(rn) ? "great element" : "small element") : rn.role == RuneRole.Form ? "form" : rn.role.ToString().ToLower());
                Text.Font = GameFont.Small;
                // pressed: its description takes the left column; pressed again, the mage comes back
                if (Widgets.ButtonInvisible(row)) { runeShown = runeShown == rn ? null : rn; SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); }
                y += 46f;
            }
            Widgets.EndScrollView();
        }
    }
}
