using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>The encyclopedia's table for trying runes together: write one circle (an element, then the runes section by
    /// section, from the centre outward), set a neighbour beside it if two elements should meet, and read what it would do
    /// - the same reading the codex of the spell editor gives. And a few ready examples of what is easy to miss: a ward on
    /// oneself or on the ground, a step turned round, a ring of walls, there and back, a pure circle, focus.</summary>
    public class Dialog_RuneCombiner : Window
    {
        private RuneDef element;
        private readonly List<List<RuneDef>> sections = new List<List<RuneDef>> { new List<RuneDef>() };
        private RuneDef neighbour;
        private RitualSchema built;
        private SpellEffect fx;
        private Vector2 runeScroll, textScroll;

        public override Vector2 InitialSize => new Vector2(Mathf.Min(1400f, Verse.UI.screenWidth - 60f), Mathf.Min(780f, Verse.UI.screenHeight - 60f));
        protected override float Margin => 0f;

        public Dialog_RuneCombiner()
        {
            doCloseX = true; forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false; doWindowBackground = false;
            Load("Fire", new[] { "Release" }, new[] { "Barrier" }, null);
        }

        private static RuneDef R(string name) => DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + name);

        private void Load(string el, string[] s1, string[] s2, string nb)
        {
            element = R(el);
            sections.Clear();
            sections.Add(s1.Select(R).Where(x => x != null).ToList());
            if (s2 != null) sections.Add(s2.Select(R).Where(x => x != null).ToList());
            neighbour = nb != null ? R(nb) : null;
            Rebuild();
        }

        private void Rebuild()
        {
            var circle = new SpellCircle { element = element };
            foreach (var s in sections) circle.sections.Add(new RitualRing { runes = s.ToList(), slots = Mathf.Max(1, s.Count) });
            var bound = SpellCircle.Bound(circle);
            if (neighbour != null)
            {
                bound = new SpellCircle { binding = true };
                bound.inner.Add(circle);
                bound.inner.Add(new SpellCircle { element = neighbour, sections = new List<RitualRing> { new RitualRing { runes = new List<RuneDef>(), slots = 1 } } });
            }
            built = new RitualSchema { name = "Combination", weave = bound };
            fx = element != null ? SpellText.Explain(built) : new SpellEffect { main = "Choose an element first: fire, water, earth or air." };
        }

        private static readonly string[][] Examples =
        {
            new[] { "Ward on yourself", "Fire", "Barrier", "", "", "A Barrier alone, with nothing that acts: aim it at yourself and the ward is yours; at another - theirs; at the ground - a dome. Nothing strikes, nobody is hurt." },
            new[] { "Strike, then a ward", "Fire", "Release", "|Barrier", "", "The release strikes the target first - aim it away from yourself, it hits whoever is there, you too. Then the Barrier, last after something that acts, lays the ward on the caster." },
            new[] { "Burst, then a new ward", "Earth", "Step", "|Release,Shaping,Barrier,Barrier,Barrier", "", "Where the step comes down the caster stands in the middle of the burst: the last two Barriers spare them (Selective Protection). Then the ring rises, and the first Barrier - last after something that acts - lays a fresh ward on the caster." },
            new[] { "Dome over the target", "Fire", "Barrier,Release", "", "", "The Barrier anywhere but last: a dome over the ground where the spell strikes, guarding whoever stands inside." },
            new[] { "Draw a foe to you", "Earth", "Step,Thread", "", "", "The Thread right outward of the Step turns it round: the one at the target is drawn to the caster." },
            new[] { "Step, then a ring round you", "Earth", "Step", "|Shaping", "", "Outward of the step: it happens where the caster comes out - a shaping there rises as a ring." },
            new[] { "There and back", "Air", "Step", "|Release,Step", "", "A strike where the caster lands, and a second step back home." },
            new[] { "Fire beside water, still fire", "Fire", "Release,Closing", "", "Water", "The Closing rune keeps the circle pure: it does not meet its neighbour." },
            new[] { "Fire beside water: steam", "Fire", "Release", "", "Water", "Neighbours in the chain meet: fire and water make steam." },
            new[] { "Focused fire", "Fire", "Release,Focus", "", "", "The Focus rune draws the stream into the circle: stronger, harder to hold." },
            new[] { "Burst round you, unhurt", "Fire", "Aura,Release", "|Barrier,Barrier,Barrier", "", "An aura puts the caster in the middle of the blow. The last two Barriers of the outer section spare the caster and their people; the one before them, last after something that acts, lays a ward on the caster after the blow." },
            new[] { "Spare your own", "Fire", "Release", "|Barrier,Barrier", "", "Two Barriers in the outermost section shape the blow: foes are struck, your people are not, and it is a quarter weaker. Without the pair the blow strikes everyone in it. Each circle needs its own." },
        };

        public override void DoWindowContents(Rect box)
        {
            FlowStyle.Begin(Find.CurrentMap);
            box = ParchmentFrame.Draw(box).ContractedBy(14f);
            Text.Font = GameFont.Medium; GUI.color = FlowStyle.Ink;
            Widgets.Label(new Rect(box.x, box.y, box.width, 34f), "Runes together");
            Text.Font = GameFont.Small; GUI.color = FlowStyle.InkSoft;
            Widgets.Label(new Rect(box.x, box.y + 32f, box.width, 22f), "Write a circle from the centre outward: an element, then the runes of each section in order. Set a neighbour to see two elements meet.");
            float top = box.y + 60f, h = box.height - 60f;
            // the runes and the circle as written on the left; what it does in the middle; the circle drawn on the right
            float miniW = Mathf.Min(300f, box.width * 0.26f);
            Rect left = new Rect(box.x, top, 220f, h), mid = new Rect(left.xMax + 10f, top, 290f, h);
            Rect mini = new Rect(box.xMax - miniW, top, miniW, h);
            Rect right = new Rect(mid.xMax + 12f, top, mini.x - mid.xMax - 24f, h);
            DrawRunes(left); DrawCircle(mid); DrawEffect(right); DrawMini(mini);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            FlowStyle.End();
        }

        private void DrawRunes(Rect r)
        {
            var runes = RuneDef.All.Where(x => Dialog_SpellEditor.ForSpells(x) && !(x.role == RuneRole.Element && SpellCircle.GreatElement(x))).ToList();
            Rect view = new Rect(0f, 0f, r.width - 16f, runes.Count * 30f);
            Widgets.BeginScrollView(r, ref runeScroll, view);
            float y = 0f;
            foreach (RuneDef x in runes)
            {
                Rect row = new Rect(0f, y, view.width, 28f);
                if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.2f));
                SpellGlyph.Draw(new Rect(row.x, row.y + 2f, 24f, 24f), x);
                GUI.color = FlowStyle.Ink;
                Widgets.Label(new Rect(row.x + 30f, row.y + 3f, row.width - 30f, 24f), x.LabelCap + (x.role == RuneRole.Element ? "  (element)" : ""));
                TooltipHandler.TipRegion(row, x.description);
                if (Widgets.ButtonInvisible(row))
                {
                    if (x.role == RuneRole.Element) element = x;
                    else if (sections.Last().Count < SpellCircle.MaxSlots) sections.Last().Add(x);
                    Rebuild();
                }
                y += 30f;
            }
            Widgets.EndScrollView();
        }

        private void DrawCircle(Rect r)
        {
            float y = r.y;
            GUI.color = FlowStyle.Ink;
            Widgets.Label(new Rect(r.x, y, r.width, 24f), "Centre: " + (element?.LabelCap.ToString() ?? "-"));
            y += 28f;
            for (int i = 0; i < sections.Count; i++)
            {
                string line = "Section " + (i + 1) + ": " + (sections[i].Count == 0 ? "-" : string.Join(", ", sections[i].Select(x => x.label)));
                float lh = Text.CalcHeight(line, r.width);
                GUI.color = i == sections.Count - 1 ? FlowStyle.Ink : FlowStyle.InkSoft;
                Widgets.Label(new Rect(r.x, y, r.width, lh), line);
                y += lh + 4f;
            }
            y += 6f;
            float bw = (r.width - 8f) / 2f;
            if (RitualUI.Button(new Rect(r.x, y, bw, 30f), "New section", sections.Count < SpellCircle.MaxSectionsSmall)) { sections.Add(new List<RuneDef>()); Rebuild(); }
            if (RitualUI.Button(new Rect(r.x + bw + 8f, y, bw, 30f), "Take the last away", sections.Any(s => s.Count > 0) || sections.Count > 1))
            {
                if (sections.Last().Count > 0) sections.Last().RemoveAt(sections.Last().Count - 1); else if (sections.Count > 1) sections.RemoveAt(sections.Count - 1);
                Rebuild();
            }
            y += 36f;
            string nb = neighbour == null ? "none" : neighbour.label;
            if (RitualUI.Button(new Rect(r.x, y, bw, 30f), "Neighbour: " + nb, true))
            {
                string[] order = { null, "Fire", "Water", "Earth", "Air" };
                int at = System.Array.IndexOf(order, neighbour?.defName.Replace("Avir_Rune_", ""));
                string n = order[(at + 1) % order.Length];
                neighbour = n != null ? R(n) : null;
                Rebuild();
            }
            TooltipHandler.TipRegion(new Rect(r.x, y, bw, 30f), "A second circle beside this one in the chain, with only its element: if the two can meet, you read what they make.");
            if (RitualUI.Button(new Rect(r.x + bw + 8f, y, bw, 30f), "Clear", true)) { sections.Clear(); sections.Add(new List<RuneDef>()); neighbour = null; Rebuild(); }
            y += 46f;
            GUI.color = FlowStyle.Ink; Text.Font = GameFont.Small;
            Widgets.Label(new Rect(r.x, y, r.width, 24f), "Examples");
            y += 26f;
            foreach (string[] ex in Examples)
            {
                Rect b = new Rect(r.x, y, r.width, 28f);
                if (RitualUI.Button(b, ex[0], true))
                    Load(ex[1], ex[2].Split(','), ex[3].Length > 1 ? ex[3].TrimStart('|').Split(',') : null, ex[4].Length > 0 ? ex[4] : null);
                TooltipHandler.TipRegion(b, ex[5]);
                y += 32f;
                if (y > r.yMax - 30f) break;
            }
        }

        private void DrawEffect(Rect r)
        {
            if (fx == null) return;
            Color col = built != null && element != null ? Spells.ColorOf(built) : FlowStyle.Gold;
            float ph = Mathf.Min(EffectPlaque.Height(fx, r.width), r.height * 0.55f);
            EffectPlaque.Draw(new Rect(r.x, r.y, r.width, ph), fx, col);
            // the chosen plaque spelled out below (click one); with none chosen, all of them
            var chosen = fx.extras.Where(e => e.Key == EffectPlaque.Selected).ToList();
            string text = string.Join("\n\n", (chosen.Count > 0 ? chosen : fx.extras).Select(e => e.Key.ToUpperInvariant() + "\n" + e.Value));
            Rect area = new Rect(r.x, r.y + ph + 8f, r.width, r.height - ph - 8f);
            float th = Text.CalcHeight(text, area.width - 16f);
            Rect view = new Rect(0f, 0f, area.width - 16f, th);
            Widgets.BeginScrollView(area, ref textScroll, view);
            GUI.color = FlowStyle.Ink;
            Widgets.Label(view, text);
            Widgets.EndScrollView();
        }
    
        /// <summary>The circle as it would be drawn: the element at its heart, each section a ring round it with its runes
        /// set in order, and the neighbour beside it, joined.</summary>
        private void DrawMini(Rect r)
        {
            RitualArt.Panel(r.ExpandedBy(4f));
            Rect area = r.ContractedBy(12f);
            GUI.color = FlowStyle.Ink; Text.Font = GameFont.Small;
            Widgets.Label(new Rect(area.x, area.y, area.width, 24f), "As it is drawn");
            float top = area.y + 28f;
            float size = Mathf.Min(area.width, area.height * 0.6f);
            Vector2 c = new Vector2(area.center.x, top + size / 2f);
            Color col = element != null ? element.color : FlowStyle.InkSoft;
            int n = sections.Count;
            float heart = size * 0.13f, step = (size / 2f - heart - 6f) / Mathf.Max(1, n);
            // the rings, outermost first
            for (int i = n - 1; i >= 0; i--)
            {
                float rr = heart + step * (i + 1);
                GUI.color = new Color(col.r * 0.6f, col.g * 0.5f, col.b * 0.4f, 0.85f);
                GUI.DrawTexture(new Rect(c.x - rr, c.y - rr, rr * 2f, rr * 2f), RitualTex.Ring);
                var runes = sections[i];
                float mid = heart + step * (i + 0.5f), gs = Mathf.Min(step * 0.8f, 30f);
                for (int k = 0; k < runes.Count; k++)
                {
                    float a = -Mathf.PI / 2f + 2f * Mathf.PI * k / Mathf.Max(1, runes.Count) + i * 0.4f;
                    Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * mid;
                    Rect g = new Rect(p.x - gs / 2f, p.y - gs / 2f, gs, gs);
                    GUI.color = Color.white;
                    SpellGlyph.Draw(g, runes[k]);
                    TooltipHandler.TipRegion(g, "Section " + (i + 1) + ": " + runes[k].LabelCap);
                }
            }
            GUI.color = new Color(col.r, col.g, col.b, 0.9f);
            GUI.DrawTexture(new Rect(c.x - heart, c.y - heart, heart * 2f, heart * 2f), RitualTex.Ring);
            if (element != null) { GUI.color = Color.white; SpellGlyph.Draw(new Rect(c.x - heart * 0.75f, c.y - heart * 0.75f, heart * 1.5f, heart * 1.5f), element); }
            float y = top + size + 10f;
            if (neighbour != null)
            {
                // the neighbour, a small circle joined below
                float nr = size * 0.12f;
                Vector2 nc = new Vector2(c.x, y + nr + 6f);
                Widgets.DrawLine(new Vector2(c.x, top + size - 2f), new Vector2(nc.x, nc.y - nr), new Color(0.4f, 0.3f, 0.2f, 0.8f), 2f);
                GUI.color = new Color(neighbour.color.r, neighbour.color.g, neighbour.color.b, 0.9f);
                GUI.DrawTexture(new Rect(nc.x - nr, nc.y - nr, nr * 2f, nr * 2f), RitualTex.Ring);
                GUI.color = Color.white;
                SpellGlyph.Draw(new Rect(nc.x - nr * 0.7f, nc.y - nr * 0.7f, nr * 1.4f, nr * 1.4f), neighbour);
                y = nc.y + nr + 8f;
            }
            GUI.color = FlowStyle.InkSoft;
            string legend = "The heart: the element. Each ring: a section, its runes read from the top round, the inner ring first.";
            Widgets.Label(new Rect(area.x, y, area.width, area.yMax - y), legend);
            GUI.color = Color.white;
        }
}
}
