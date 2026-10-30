using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
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
            float lw = inner.width * 0.30f, rw = inner.width * 0.24f;
            Rect left = new Rect(inner.x, inner.y, lw, inner.height);
            Rect right = new Rect(inner.xMax - rw, inner.y, rw, inner.height);
            Rect mid = new Rect(left.xMax + 16f, inner.y, inner.width - lw - rw - 32f, inner.height);
            RitualArt.Panel(left.ExpandedBy(6f)); RitualArt.Panel(mid.ExpandedBy(6f)); RitualArt.Panel(right.ExpandedBy(6f));
            if (RitualArt.HasPanel) { left = left.ContractedBy(10f); mid = mid.ContractedBy(10f); right = right.ContractedBy(10f); }
            DrawMage(left, core);
            DrawSpells(mid);
            DrawRunes(right);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            FlowStyle.End();
        }

        private void DrawMage(Rect r, Hediff_ArcaneCore core)
        {
            bool sorc = core != null && core.Sorcerer;
            GUI.color = Ink; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, r.y, r.width, 34f), pawn.LabelShortCap);
            Text.Font = GameFont.Small;
            GUI.color = SpellGlyph.Ink(sorc ? Hediff_ArcaneCore.SorcererColor : Hediff_ArcaneCore.MageColor);
            Widgets.Label(new Rect(r.x, r.y + 34f, r.width, 24f), sorc ? "Sorcerer - born with magic" : "Mage - magic taken in");
            if (core == null) { GUI.color = InkSoft; Widgets.Label(new Rect(r.x, r.y + 64f, r.width, 60f), "No arcane core - the gift will grow it back."); return; }
            // the Arcana, large
            Rect bar = new Rect(r.x, r.y + 70f, r.width, 34f);
            Widgets.FillableBar(bar, core.Fill, SolidColorMaterials.NewSolidColorTexture(sorc ? new Color(0.93f, 0.68f, 0.12f) : new Color(0.16f, 0.32f, 0.92f)),
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
            Rect area = new Rect(r.x, vt.yMax + 8f, r.width, r.yMax - vt.yMax - 8f);
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
                SpellGlyph.Draw(new Rect(0f, y, 40f, 40f), core.path.Glow, core.path.color);
                int own = core.Book.spells.Count(sp => Spells.Element(sp) == core.path);
                Line(new Rect(48f, y, w - 48f, 44f), core.path.LabelCap + " - the mage's element. Spells of it in the book: " + own + ".", Ink);
                y += 48f;
            }
            else y += Line(new Rect(0f, y, w, 60f), "No path yet. The path ritual will give the mage an element - and the first spells of it.", InkSoft);

            // 2. the Arcana coming in, an hour
            Heading(ref y, w, "Arcana coming in, an hour");
            float med = core.Capacity * 0.25f * (1f + 0.5f * MapComponent_ExternalFlow.TierIndex(ext?.Total ?? 0f));
            y += Row(y, w, "Meditation", "+" + med.ToString("0") + (sanct != null ? " (x3 here: " + (med * 3f).ToString("0") + ")" : ""), true);
            int seepTier = MapComponent_ExternalFlow.TierIndex(ext?.Total ?? 0f);
            y += Row(y, w, "The stream (" + tierName + ")", seepTier > 0 ? "+" + (core.Capacity * 0.02f * seepTier).ToString("0.#") : "none while calm", seepTier > 0);
            y += Row(y, w, "Sanctuary", sanct != null ? sanct.element.LabelCap + ", " + sanct.Fill.ToStringPercent() + " stored" : "none near", sanct != null);
            y += Row(y, w, "Filling ritual", "fills the core first, in its reach", true);

            // 3. the core
            Heading(ref y, w, "The core");
            y += Row(y, w, "Level " + core.level, "holds " + Mathf.FloorToInt(core.Capacity), true);
            y += Bar(y, w, "Composure", core.Composure, new Color(0.35f, 0.55f, 0.85f), "Composure " + core.Composure.ToStringPercent() + ": the safe band is " + lo.ToStringPercent() + " - " + hi.ToStringPercent() + ". Wider with composure and with what steadies the mage.");
            Hediff g = pawn.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneGreatness);
            y += Bar(y, w, "Greatness", g?.Severity ?? 0f, new Color(0.9f, 0.62f, 0.15f), "Greatness swells above the band and with the stream's casts" + (g != null ? " (" + g.LabelCap + ")" : "") + ". It holds work beneath the mage; held long, the Arcana breaks out.");
            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneHollow);
            y += Bar(y, w, "Hollow", h?.Severity ?? 0f, new Color(0.45f, 0.42f, 0.5f), "Below the band the mage grows hollow - never ordinary again.");

            // 4. the casting
            Heading(ref y, w, "Casting");
            y += Row(y, w, sorc ? "Sorcerer: quick" : "Mage: slow", "a small circle in " + ((sorc ? 60f : 150f) / 60f).ToString("0.#") + " s", true);
            RitualSchema probe = SpellExamples.Fire().FirstOrDefault();
            if (probe != null) y += Row(y, w, "Hold, a simple spell", Spells.HoldChance(pawn, probe).ToStringPercent(), true);
            y += Row(y, w, "The stream's strength", tier > 0 ? "+" + (15 * tier) + "% power, greatness +" + (0.02f * tier).ToStringPercent() + " a cast" : "none while calm", tier > 0);

            // 5. what steadies
            Heading(ref y, w, "What steadies the mage");
            bool horned = MagicConductors.Horned(pawn);
            y += Row(y, w, "Horns", horned ? "composure +" + MagicConductors.HornComposure.ToStringPercent() + ", core x0.7" : "none", horned);
            int arts = MagicConductors.Artifacts(pawn);
            y += Row(y, w, "Artifacts worn", arts > 0 ? arts + ": band +" + Mathf.Min(0.1f, 0.05f * arts).ToStringPercent() + ", core x" + Mathf.Pow(0.85f, arts).ToString("0.00") : "none", arts > 0);
            y += Row(y, w, "Core in all", "x" + MagicConductors.CapacityFactor(pawn).ToString("0.00") + " of its size", true);

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
                else TooltipHandler.TipRegion(sl, open ? "An empty place: put a spell here from the book below." : "Not open yet - the mage's growth opens it.");
            }
            // the book of the mage
            float top = r.y + 52f + w + 32f;
            GUI.color = Ink;
            Widgets.Label(new Rect(r.x, top, r.width - 160f, 26f), "The book (" + b.spells.Count + ")");
            // for testing until the mage's path gives spells: every ready spell of fire at once (dev mode only)
            if (Prefs.DevMode && RitualUI.Button(new Rect(r.xMax - 330f, top - 4f, 172f, 32f), "Dev: fire spells"))
                foreach (RitualSchema ex in SpellExamples.Fire()) if (!b.spells.Any(x => x.name == ex.name)) b.spells.Add(ex);
            if (RitualUI.Button(new Rect(r.xMax - 150f, top - 4f, 150f, 32f), "New spell..."))
                Find.WindowStack.Add(new Dialog_SpellEditor(new RitualSchema { name = "New spell" }, pawn, sp => b.spells.Add(sp)));
            Rect area = new Rect(r.x, top + 32f, r.width, r.yMax - top - 32f);
            const float rowH = 112f;
            Rect view = new Rect(0f, 0f, area.width - 16f, Mathf.Max(1, b.spells.Count) * (rowH + 6f));
            RitualScroll.Begin(area, ref spellScroll, view);
            float y = 0f;
            if (b.spells.Count == 0) { GUI.color = InkSoft; Widgets.Label(new Rect(0f, 0f, view.width, 60f), "No spells yet. Write one: an element, a form or verb, and the modifiers you like - in a small circle at first."); }
            foreach (RitualSchema s in b.spells.ToList())
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
                Widgets.Label(new Rect(body.x, body.yMax - 26f, body.width - 270f, 26f), stats);
                TooltipHandler.TipRegion(new Rect(row.x, row.y, row.width - 270f, row.height), s.name + "\n\n" + Spells.Describe(s) + "\n\nDouble-click: " + (inHand ? "take it from the hand." : "put it in the hand."));
                bool dbl = Event.current.type == EventType.MouseDown && Event.current.button == 0 && Event.current.clickCount == 2 && Mouse.IsOver(new Rect(row.x, row.y, row.width - 270f, row.height));
                float by = body.yMax - 32f;
                if (dbl && inHand) { for (int i = 0; i < 5; i++) if (b.ready[i] == s) b.ready[i] = null; Event.current.Use(); }
                else if (RitualUI.Button(new Rect(body.xMax - 262f, by, 96f, 32f), "To hand", !inHand) || dbl)
                {
                    if (dbl) Event.current.Use();
                    int free = -1;
                    for (int i = 0; i < b.open; i++) if (b.ready[i] == null) { free = i; break; }
                    if (free < 0) Messages.Message("Every place at hand is taken - clear one first.", MessageTypeDefOf.RejectInput, false);
                    else b.ready[free] = s;
                }
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
            Widgets.EndScrollView();
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
                if (rn.role == RuneRole.Element)
                {
                    GUI.color = new Color(0.10f, 0.07f, 0.12f, 0.88f);
                    GUI.DrawTexture(new Rect(0f, y + 2f, 40f, 40f), RitualTex.Disc, ScaleMode.StretchToFill);
                    GUI.color = Color.Lerp(rn.color, Color.white, 0.15f);
                    GUI.DrawTexture(new Rect(5f, y + 7f, 30f, 30f), rn.Glow, ScaleMode.ScaleToFit);
                }
                else { GUI.color = Color.white; GUI.DrawTexture(new Rect(0f, y + 2f, 40f, 40f), FlowStyle.Pick(rn.Ink, rn.Glow), ScaleMode.ScaleToFit); }
                GUI.color = Ink;
                Widgets.Label(new Rect(48f, y + 2f, view.width - 50f, 22f), rn.LabelCap);
                GUI.color = InkSoft;
                Widgets.Label(new Rect(48f, y + 22f, view.width - 50f, 20f), (rn.role == RuneRole.Element ? "element" : "form") + " - not learned yet");
                Text.Font = GameFont.Small;
                TooltipHandler.TipRegion(new Rect(0f, y, view.width, 44f), rn.LabelCap + "\n\n" + rn.description);
                y += 46f;
            }
            Widgets.EndScrollView();
        }
    }
}
