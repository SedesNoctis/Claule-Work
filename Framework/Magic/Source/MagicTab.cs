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

        public Dialog_Magic(Pawn p)
        {
            pawn = p;
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1150f, Verse.UI.screenWidth - 60f), Mathf.Min(760f, Verse.UI.screenHeight - 60f));

        private static Color Ink => FlowStyle.Ink;
        private static Color InkSoft => FlowStyle.InkSoft;

        public override void DoWindowContents(Rect box)
        {
            Hediff_ArcaneCore core = Mages.Core(pawn);
            FlowStyle.Begin(pawn.MapHeld);
            Rect inner = ParchmentFrame.Draw(box).ContractedBy(8f);
            float lw = inner.width * 0.36f, rw = inner.width * 0.28f;
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
            GUI.color = sorc ? Hediff_ArcaneCore.SorcererColor * 0.75f : Hediff_ArcaneCore.MageColor * 0.75f;
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
            // the facts
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Core: level " + core.level + " - it holds " + Mathf.FloorToInt(core.Capacity) + " (100 a level" + (MagicConductors.CapacityFactor(pawn) < 0.999f ? ", less for what steadies the mage" : "") + ").");
            sb.AppendLine("Safe band: " + lo.ToStringPercent() + " - " + hi.ToStringPercent() + ", from composure " + core.Composure.ToStringPercent() + ".");
            if (MagicConductors.Horned(pawn)) sb.AppendLine("Horns: a natural conductor - composure +" + MagicConductors.HornComposure.ToStringPercent() + ", a wider band, a smaller core.");
            int a = MagicConductors.Artifacts(pawn);
            if (a > 0) sb.AppendLine("Artifacts worn: " + a + " - a wider band, a smaller core.");
            Hediff g = pawn.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneGreatness);
            if (g != null) sb.AppendLine("Greatness: " + g.Severity.ToStringPercent() + " (" + g.LabelCap + ") - it holds work beneath the mage.");
            Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(MagicDefOf.Avir_ArcaneHollow);
            if (h != null) sb.AppendLine("Hollow: " + h.Severity.ToStringPercent() + ".");
            CompSanctuary s = CompSanctuary.Near(pawn);
            if (s != null) sb.AppendLine("Near a sanctuary of " + s.element.label + " (" + s.Fill.ToStringPercent() + "): meditation here fills three times as fast.");
            sb.AppendLine();
            sb.AppendLine("Filled by meditation, by a filling ritual in whose reach the mage stands, slowly by the stream, and beside a lit sanctuary.");
            sb.Append("Above the band greatness swells; held there long, the Arcana breaks out. Below it the mage grows hollow, never ordinary.");
            GUI.color = InkSoft;
            Widgets.Label(new Rect(r.x, vt.yMax + 10f, r.width, r.yMax - vt.yMax - 10f), sb.ToString());
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
                    GUI.color = Spells.ColorOf(s);
                    if (icon != null) GUI.DrawTexture(sl.ContractedBy(w * 0.22f), icon.Glow, ScaleMode.ScaleToFit);
                    GUI.color = Ink; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.LowerCenter;
                    Widgets.Label(new Rect(sl.x - 6f, sl.yMax - 2f, sl.width + 12f, 18f), s.name.Truncate(sl.width + 12f));
                    Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                    TooltipHandler.TipRegion(sl, s.name + "\n" + Spells.Describe(s) + "\n\nClick to take it from the hand.");
                    if (Widgets.ButtonInvisible(sl)) b.ready[i] = null;
                }
                else TooltipHandler.TipRegion(sl, open ? "An empty place: put a spell here from the book below." : "Not open yet - the mage's growth opens it.");
            }
            // the book of the mage
            float top = r.y + 52f + w + 20f;
            GUI.color = Ink;
            Widgets.Label(new Rect(r.x, top, r.width - 260f, 26f), "The book (" + b.spells.Count + ")");
            if (Widgets.ButtonText(new Rect(r.xMax - 254f, top - 2f, 124f, 28f), "Examples..."))
                Find.WindowStack.Add(new FloatMenu(SpellExamples.Fire().Select(ex => new FloatMenuOption(ex.name + " - " + SpellText.Explain(ex).main.Truncate(320f), () =>
                {
                    b.spells.Add(ex);
                    Messages.Message(ex.name + " is written into the book. Open it with Edit to see how it is made.", MessageTypeDefOf.NeutralEvent, false);
                })).ToList()));
            if (Widgets.ButtonText(new Rect(r.xMax - 124f, top - 2f, 124f, 28f), "New spell..."))
                SpellText.Caster = pawn; Find.WindowStack.Add(new Dialog_RitualEditor(new RitualSchema { name = "New spell", size = RitualCircleSize.Small }, sp => b.spells.Add(sp)));
            Rect area = new Rect(r.x, top + 32f, r.width, r.yMax - top - 32f);
            Rect view = new Rect(0f, 0f, area.width - 16f, Mathf.Max(1, b.spells.Count) * 64f);
            RitualScroll.Begin(area, ref spellScroll, view);
            float y = 0f;
            if (b.spells.Count == 0) { GUI.color = InkSoft; Widgets.Label(new Rect(0f, 0f, view.width, 60f), "No spells yet. Write one: an element, a form or verb, and the modifiers you like - in a small circle at first."); }
            foreach (RitualSchema s in b.spells.ToList())
            {
                Rect row = new Rect(0f, y, view.width, 60f);
                if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                RuneDef icon = Spells.Element(s) ?? RitualEvaluator.Read(s).verbs.FirstOrDefault();
                GUI.color = Spells.ColorOf(s);
                if (icon != null) GUI.DrawTexture(new Rect(row.x + 2f, row.y + 6f, 48f, 48f), icon.Glow, ScaleMode.ScaleToFit);
                GUI.color = Ink;
                Widgets.Label(new Rect(row.x + 56f, row.y + 4f, row.width - 250f, 24f), s.name);
                Text.Font = GameFont.Tiny; GUI.color = InkSoft;
                Widgets.Label(new Rect(row.x + 56f, row.y + 26f, row.width - 250f, 34f), Spells.Describe(s));
                Text.Font = GameFont.Small;
                if (Widgets.ButtonText(new Rect(row.xMax - 186f, row.y + 14f, 70f, 30f), "To hand"))
                {
                    int free = -1;
                    for (int i = 0; i < b.open; i++) if (b.ready[i] == null) { free = i; break; }
                    if (free < 0) Messages.Message("Every place at hand is taken - clear one first.", MessageTypeDefOf.RejectInput, false);
                    else b.ready[free] = s;
                }
                if (Widgets.ButtonText(new Rect(row.xMax - 112f, row.y + 14f, 54f, 30f), "Edit"))
                {
                    RitualSchema old = s;
                    SpellText.Caster = pawn; Find.WindowStack.Add(new Dialog_RitualEditor(s, sp =>
                    {
                        int at = b.spells.IndexOf(old);
                        if (at >= 0) b.spells[at] = sp; else b.spells.Add(sp);
                        for (int i = 0; i < 5; i++) if (b.ready[i] == old) b.ready[i] = sp;
                    }));
                }
                if (Widgets.ButtonText(new Rect(row.xMax - 54f, row.y + 14f, 54f, 30f), "Erase"))
                {
                    b.spells.Remove(s);
                    for (int i = 0; i < 5; i++) if (b.ready[i] == s) b.ready[i] = null;
                }
                y += 64f;
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
                GUI.color = rn.role == RuneRole.Element ? rn.color : Color.white;
                GUI.DrawTexture(new Rect(0f, y + 2f, 40f, 40f), FlowStyle.Pick(rn.Ink, rn.Glow), ScaleMode.ScaleToFit);
                GUI.color = Ink;
                Widgets.Label(new Rect(48f, y + 2f, view.width - 50f, 22f), rn.LabelCap);
                Text.Font = GameFont.Tiny; GUI.color = InkSoft;
                Widgets.Label(new Rect(48f, y + 22f, view.width - 50f, 20f), (rn.role == RuneRole.Element ? "element" : "form") + " - not learned yet");
                Text.Font = GameFont.Small;
                TooltipHandler.TipRegion(new Rect(0f, y, view.width, 44f), rn.LabelCap + "\n\n" + rn.description);
                y += 46f;
            }
            Widgets.EndScrollView();
        }
    }
}
