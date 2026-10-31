using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Magic
{
    /// <summary>Writing a circle step by step, for whoever finds the editor's lists too dense: which element, what it does,
    /// how it is delivered, what strengthens it - each a page of large cards, with what the circle will do read anew on the
    /// right at every click. It only ever writes runes into a circle, the same as the editor: whatever it builds can be
    /// changed there freely, and nothing the editor allows is barred because the steps did not foresee it.</summary>
    public class Dialog_SpellWizard : Window
    {
        private readonly System.Action<SpellCircle> keep;
        private RuneDef element;
        private readonly List<List<RuneDef>> sections = new List<List<RuneDef>>();
        private int step;              // 0 element, 1 what it does, 2 how, 3 what strengthens
        private RitualSchema built;
        private SpellEffect fx;
        private Vector2 cardScroll, textScroll;

        private static readonly string[] Steps = { "1. The element", "2. What it does", "3. How it reaches", "4. What strengthens it" };
        private static readonly string[] Asks =
        {
            "The heart of the circle: what the magic is made of.",
            "The verb: what happens where the spell strikes. Each verb opens a section of its own - a circle can do several things in turn.",
            "The form: how the action is carried. Without one it simply strikes the chosen point.",
            "Modifiers work on the rune just inward of them; flow runes act on the whole circle. Click again to add another of the same, right-click to take one away.",
        };

        public override Vector2 InitialSize => new Vector2(Mathf.Min(1200f, Verse.UI.screenWidth - 60f), Mathf.Min(780f, Verse.UI.screenHeight - 60f));
        protected override float Margin => 0f;

        public Dialog_SpellWizard(System.Action<SpellCircle> keep)
        {
            this.keep = keep;
            doCloseX = true; forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false; doWindowBackground = false;
            Rebuild();
        }

        private List<RuneDef> Cur => sections.Count > 0 ? sections[sections.Count - 1] : null;

        private SpellCircle Circle()
        {
            var c = new SpellCircle { element = element };
            foreach (var s in sections.Where(x => x.Count > 0)) c.sections.Add(new RitualRing { runes = s.ToList(), slots = Mathf.Max(2, s.Count) });
            if (c.sections.Count == 0) c.sections.Add(new RitualRing { slots = 2 });
            return c;
        }

        private void Rebuild()
        {
            built = new RitualSchema { name = "New spell", weave = SpellCircle.Bound(Circle()) };
            fx = element != null ? SpellText.Explain(built) : new SpellEffect { main = "Choose an element: what the magic is made of." };
        }

        private static IEnumerable<RuneDef> Of(int page)
        {
            var all = RuneDef.All.Where(Dialog_SpellEditor.ForSpells);
            switch (page)
            {
                case 0: return all.Where(r => r.role == RuneRole.Element && !SpellCircle.GreatElement(r));
                case 1: return all.Where(r => r.role == RuneRole.Verb);
                case 2: return all.Where(r => r.role == RuneRole.Form);
                default: return all.Where(r => r.role == RuneRole.Modifier || r.role == RuneRole.Flow);
            }
        }

        /// <summary>One line on a card: what the rune does with the chosen element, else the first sentence of its text.</summary>
        private string Line(RuneDef r)
        {
            if (element != null && r.role != RuneRole.Element)
            {
                string el = element.defName.Replace("Avir_Rune_", "");
                string l = Dialog_SpellEditor.LineFor(el, r);
                if (!l.NullOrEmpty() && l != "not made yet") return l.CapitalizeFirst();
            }
            string d = r.description ?? "";
            int dot = d.IndexOf(". ");
            return dot > 0 ? d.Substring(0, dot + 1) : d;
        }

        public static Color RoleColor(RuneDef r)
        {
            switch (r.role)
            {
                case RuneRole.Element: return r.color;
                case RuneRole.Verb: return new Color(0.72f, 0.3f, 0.15f);
                case RuneRole.Form: return new Color(0.2f, 0.4f, 0.7f);
                case RuneRole.Modifier: return new Color(0.3f, 0.55f, 0.25f);
                default: return new Color(0.5f, 0.3f, 0.6f);
            }
        }

        public override void DoWindowContents(Rect box)
        {
            FlowStyle.Begin(Find.CurrentMap);
            box = ParchmentFrame.Draw(box).ContractedBy(14f);
            Text.Font = GameFont.Medium; GUI.color = FlowStyle.Ink;
            Widgets.Label(new Rect(box.x, box.y, box.width, 34f), "A circle, step by step");
            Text.Font = GameFont.Small;
            // the steps across the top: any one can be gone back to
            float sw = (box.width * 0.64f - 12f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                bool can = i == 0 || element != null && (i < 2 || sections.Count > 0);
                if (RitualUI.Button(new Rect(box.x + i * (sw + 4f), box.y + 38f, sw, 30f), Steps[i], can, step == i)) { step = i; cardScroll = Vector2.zero; }
            }
            float top = box.y + 76f, h = box.height - 76f;
            Rect left = new Rect(box.x, top, box.width * 0.64f, h), right = new Rect(left.xMax + 16f, top, box.xMax - left.xMax - 16f, h);
            DrawCards(left);
            DrawRight(right);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            FlowStyle.End();
        }

        private void DrawCards(Rect r)
        {
            GUI.color = FlowStyle.InkSoft;
            float ah = Text.CalcHeight(Asks[step], r.width);
            Widgets.Label(new Rect(r.x, r.y, r.width, ah), Asks[step]);
            float y0 = r.y + ah + 8f;
            var runes = Of(step).ToList();
            const int cols = 3; const float ch = 104f, gap = 8f;
            float cw = (r.width - 16f - gap * (cols - 1)) / cols;
            bool none = step == 2;   // the form page: "no form" is a card too
            int n = runes.Count + (none ? 1 : 0);
            Rect area = new Rect(r.x, y0, r.width, r.yMax - y0 - 48f);
            Rect view = new Rect(0f, 0f, r.width - 16f, Mathf.Ceil(n / (float)cols) * (ch + gap));
            Widgets.BeginScrollView(area, ref cardScroll, view);
            for (int i = 0; i < n; i++)
            {
                RuneDef rune = none ? (i == 0 ? null : runes[i - 1]) : runes[i];
                Rect c = new Rect((i % cols) * (cw + gap), (i / cols) * (ch + gap), cw, ch);
                int count = rune == null ? 0 : step == 0 ? (element == rune ? 1 : 0) : Cur?.Count(x => x == rune) ?? 0;
                bool chosen = rune == null ? Cur != null && !Cur.Any(x => x.role == RuneRole.Form) : count > 0;
                DrawCard(c, rune, chosen, count);
                if (Mouse.IsOver(c) && Event.current.type == EventType.MouseDown && Event.current.button == 1 && rune != null && step == 3 && count > 0)
                {
                    Cur.RemoveAt(Cur.LastIndexOf(rune)); Rebuild(); Event.current.Use();
                }
                else if (Widgets.ButtonInvisible(c)) { Pick(rune); SoundDefOf.Click.PlayOneShotOnCamera(); }
            }
            Widgets.EndScrollView();
            // below: on to the next page, another action, or done
            float bw = (r.width - 16f) / 3f, by = r.yMax - 40f;
            if (RitualUI.Button(new Rect(r.x, by, bw, 36f), "Back", step > 0)) { step--; cardScroll = Vector2.zero; }
            if (step < 3)
            {
                bool ready = step == 0 ? element != null : step == 1 ? Cur != null && Cur.Any(x => x.role == RuneRole.Verb) : true;
                if (RitualUI.Button(new Rect(r.x + bw + 8f, by, bw, 36f), "Next", ready)) { step++; cardScroll = Vector2.zero; }
            }
            else if (RitualUI.Button(new Rect(r.x + bw + 8f, by, bw, 36f), "Another action", sections.Count < SpellCircle.MaxSectionsSmall))
            { sections.Add(new List<RuneDef>()); step = 1; cardScroll = Vector2.zero; Rebuild(); }
            if (RitualUI.Button(new Rect(r.x + 2f * (bw + 8f), by, bw, 36f), "Write it into the spell", element != null))
            {
                keep?.Invoke(Circle());
                Close();
            }
        }

        private void DrawCard(Rect c, RuneDef rune, bool chosen, int count)
        {
            Color rc = rune != null ? RoleColor(rune) : FlowStyle.InkSoft;
            Widgets.DrawBoxSolid(c, chosen ? new Color(FlowStyle.Gold.r, FlowStyle.Gold.g, FlowStyle.Gold.b, 0.28f) : Mouse.IsOver(c) ? new Color(1f, 0.9f, 0.6f, 0.18f) : new Color(1f, 0.95f, 0.85f, 0.08f));
            Widgets.DrawBoxSolid(new Rect(c.x, c.y, 5f, c.height), rc);
            GUI.color = chosen ? FlowStyle.Gold : new Color(FlowStyle.InkSoft.r, FlowStyle.InkSoft.g, FlowStyle.InkSoft.b, 0.5f);
            Widgets.DrawBox(c, chosen ? 2 : 1);
            GUI.color = Color.white;
            if (rune != null) SpellGlyph.Draw(new Rect(c.x + 12f, c.y + 10f, 52f, 52f), rune);
            GUI.color = FlowStyle.Ink; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(c.x + 72f, c.y + 8f, c.width - 80f, 32f), rune != null ? rune.LabelCap.ToString() + (count > 1 ? " ×" + count : "") : "No form");
            Text.Font = GameFont.Small; GUI.color = FlowStyle.InkSoft;
            string line = rune != null ? Line(rune) : "It strikes the chosen point itself.";
            Widgets.Label(new Rect(c.x + 72f, c.y + 40f, c.width - 80f, c.height - 44f), line);
            if (rune != null) TooltipHandler.TipRegion(c, rune.description);
        }

        private void Pick(RuneDef rune)
        {
            switch (step)
            {
                case 0: element = rune; break;
                case 1:
                    // a verb opens its section; on a section already acting, it replaces the verb there
                    // (a new section is opened by "Another action")
                    if (Cur == null) sections.Add(new List<RuneDef>());
                    int vi = Cur.FindIndex(x => x.role == RuneRole.Verb);
                    if (vi >= 0) Cur[vi] = rune; else Cur.Insert(0, rune);
                    break;
                case 2:
                    if (Cur == null) break;
                    Cur.RemoveAll(x => x.role == RuneRole.Form);
                    if (rune != null) Cur.Insert(Cur.FindIndex(x => x.role == RuneRole.Verb) + 1, rune);
                    break;
                default:
                    if (Cur == null) sections.Add(new List<RuneDef>());
                    if (Cur.Count < SpellCircle.MaxSlots) Cur.Add(rune);
                    break;
            }
            Rebuild();
        }

        private void DrawRight(Rect r)
        {
            // the circle as it stands: the heart, then each section's runes in order
            float y = r.y;
            GUI.color = FlowStyle.Ink; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(r.x, y, r.width, 32f), "The circle");
            Text.Font = GameFont.Small; y += 34f;
            Rect heart = new Rect(r.x, y, 56f, 56f);
            if (element != null) SpellGlyph.Draw(heart, element);
            else { GUI.color = FlowStyle.InkSoft; Widgets.DrawBox(heart); }
            GUI.color = FlowStyle.Ink;
            Widgets.Label(new Rect(r.x + 64f, y + 16f, r.width - 64f, 24f), element != null ? "Centre: " + element.LabelCap.ToString() : "Centre: -");
            y += 62f;
            for (int i = 0; i < sections.Count; i++)
            {
                GUI.color = i == sections.Count - 1 ? FlowStyle.Ink : FlowStyle.InkSoft;
                Widgets.Label(new Rect(r.x, y + 8f, 30f, 24f), (i + 1) + ".");
                float x = r.x + 30f;
                foreach (RuneDef q in sections[i])
                {
                    Rect g = new Rect(x, y, 38f, 38f);
                    Widgets.DrawBoxSolid(new Rect(g.x, g.yMax - 3f, g.width, 3f), RoleColor(q));
                    SpellGlyph.Draw(g, q);
                    TooltipHandler.TipRegion(g, q.LabelCap);
                    x += 42f;
                    if (x > r.xMax - 40f) break;
                }
                y += 44f;
            }
            if (sections.Count > 0 && RitualUI.Button(new Rect(r.x, y, r.width / 2f - 4f, 28f), "Take the last away", sections.Any(s => s.Count > 0)))
            {
                if (Cur.Count > 0) Cur.RemoveAt(Cur.Count - 1);
                if (Cur.Count == 0 && sections.Count > 0) sections.RemoveAt(sections.Count - 1);
                Rebuild();
            }
            if (RitualUI.Button(new Rect(r.x + r.width / 2f + 4f, y, r.width / 2f - 4f, 28f), "Start over", element != null || sections.Count > 0))
            { element = null; sections.Clear(); step = 0; Rebuild(); }
            y += 38f;
            // what it will do, read the same way as the editor's codex
            if (fx == null) return;
            Color col = element != null ? Spells.ColorOf(built) : FlowStyle.Gold;
            float ph = Mathf.Min(EffectPlaque.Height(fx, r.width), r.yMax - y);
            EffectPlaque.Draw(new Rect(r.x, y, r.width, ph), fx, col);
            y += ph + 8f;
            if (y > r.yMax - 40f || fx.extras.Count == 0) return;
            string text = string.Join("\n\n", fx.extras.Select(e => e.Key.ToUpperInvariant() + "\n" + e.Value));
            Rect area = new Rect(r.x, y, r.width, r.yMax - y);
            Rect view = new Rect(0f, 0f, area.width - 16f, Text.CalcHeight(text, area.width - 16f));
            Widgets.BeginScrollView(area, ref textScroll, view);
            GUI.color = FlowStyle.Ink;
            Widgets.Label(view, text);
            Widgets.EndScrollView();
        }
    }
}
