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
            foreach (int pg in new[] { 0, 1 })
            {
                string label = pg == 0 ? "Spells" : core != null && core.Sorcerer ? "Path of the sorcerer" : "Path of the wizard";
                float w = Text.CalcSize(label).x + 40f;
                Rect tr = new Rect(tx, inner.y, w, 30f);
                if (RitualUI.Button(tr, label, true, page == pg)) page = pg;
                tx += w + 8f;
            }
            inner.yMin += 38f;
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
        private void DrawPath(Rect r, Hediff_ArcaneCore core)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, r.y, r.width, 34f), "The path of " + pawn.LabelShort);
            Text.Font = GameFont.Small; GUI.color = InkSoft;
            Widgets.Label(new Rect(r.x, r.y + 40f, r.width, 60f), core == null ? "No arcane core." :
                "Core level " + core.level + ". How a " + (core.Sorcerer ? "sorcerer" : "wizard") + " grows - and where their path leads - is still to be written here.");
        }

        private void DrawMage(Rect r, Hediff_ArcaneCore core)
        {
            bool sorc = core != null && core.Sorcerer;
            GUI.color = Ink; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, r.y, r.width, 34f), pawn.LabelShortCap);
            Text.Font = GameFont.Small;
            GUI.color = SpellGlyph.Ink(sorc ? Hediff_ArcaneCore.SorcererColor : Hediff_ArcaneCore.MageColor);
            Widgets.Label(new Rect(r.x, r.y + 34f, r.width, 24f), sorc ? "Sorcerer - born with magic" : "Wizard - magic taken in");
            if (core == null) { GUI.color = InkSoft; Widgets.Label(new Rect(r.x, r.y + 64f, r.width, 60f), "No arcane core - the gift will grow it back."); return; }
            // the Arcana, large
            Rect bar = new Rect(r.x, r.y + 70f, r.width, 34f);
            Widgets.FillableBar(bar, Mathf.Clamp01(core.Fill), SolidColorMaterials.NewSolidColorTexture(sorc ? new Color(0.93f, 0.68f, 0.12f) : new Color(0.16f, 0.32f, 0.92f)),
                SolidColorMaterials.NewSolidColorTexture(new Color(0.12f, 0.1f, 0.08f, 0.6f)), false);
            core.Band(out float lo, out float hi);
            Color mk = sorc ? new Color(0.05f, 0.04f, 0.03f) : new Color(1f, 0.84f, 0.35f);
            Widgets.DrawBoxSolid(new Rect(bar.x + bar.width * lo - 1f, bar.y, 2f, bar.height), mk);
            Widgets.DrawBoxSolid(new Rect(bar.x + bar.width * hi - 1f, bar.y, 2f, bar.height), mk);
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
            y += Bar(y, w, "Composure", core.Composure, new Color(0.35f, 0.55f, 0.85f), "Steadier hands, a wider safe band. Now " + lo.ToStringPercent() + " to " + hi.ToStringPercent() + ".");
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
            if (Prefs.DevMode && RitualUI.Button(new Rect(r.xMax - 524f, top - 4f, 176f, 32f), "Dev: all spells"))
                foreach (RitualSchema ex in SpellExamples.Fire().Concat(SpellExamples.Water()).Concat(SpellExamples.Earth()).Concat(SpellExamples.Air()).Concat(SpellExamples.Meetings())) if (!b.spells.Any(x => x.name == ex.name)) b.spells.Add(ex);
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
