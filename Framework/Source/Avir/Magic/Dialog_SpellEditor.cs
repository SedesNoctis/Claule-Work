using System.Collections.Generic;
using System.Linq;
using Avir.Artifactorics;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Magic
{
    /// <summary>The spell editor. Left: the runes (one click shows a rune, a double click writes it) and the card of
    /// the rune shown. Middle: the circles in the Song's sky - each circle its element in the centre and its sections
    /// outward; great circles wrap whatever is inside them; zoom with the wheel, move by dragging. Under it: how many
    /// great circles wrap the spell, how many circles a great one holds, the sections and slots of the chosen circle.
    /// Right: the main effect, the details for this mage, and Arlie's remark on what is in the circle.</summary>
    public class Dialog_SpellEditor : Window
    {
        private static Color Ink => FlowStyle.Ink;
        private static Color InkSoft => FlowStyle.InkSoft;
        private static Color Gold => FlowStyle.Gold;

        private readonly RitualSchema s;
        private readonly System.Action<RitualSchema> keep;
        private readonly Pawn mage;
        private SpellCircle sel;
        /// <summary>Which in the chain each circle is (shown on it while the spell has more than one).</summary>
        private readonly Dictionary<SpellCircle, int> orderOf = new Dictionary<SpellCircle, int>();
        private int section;                    // -1: the centre
        private RuneDef shown, hovered;
        // the rune chosen on the circles: its circle, its section (-1: the centre element) and place
        private SpellCircle pickC;
        private int pickSec = -1, pickK = -1;
        private float lastWrite, lastFilled = -9f;
        private SpellCircle focus;
        private Vector2 outlineScroll;
        private int tab;                        // 0 all, 1 elements, 2 verbs, 3 forms, 4 modifiers and flow
        private Vector2 leftScroll;
        private string search = "";
        private readonly RitualView view = new RitualView();
        private bool sheetMax;
        private Rect sheetNormal;

        public Dialog_SpellEditor(RitualSchema start, Pawn mage, System.Action<RitualSchema> keep)
        {
            this.keep = keep;
            this.mage = mage;
            s = start?.Copy() ?? new RitualSchema { name = "New spell" };
            if (s.weave == null)
            {
                s.weave = s.rings.Any(r => r.runes.Any()) ? SpellCircle.Bound(SpellCircle.FromRings(s)) : NewSpell();
                s.rings.Clear();
            }
            s.weave = SpellCircle.Bound(s.weave);
            sel = s.weave.inner.FirstOrDefault() ?? s.weave;
            section = -1;
            view.max = 80f;
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            forcePause = true;
        }

        private static SpellCircle NewCircle() => new SpellCircle { sections = { new RitualRing { slots = 2 } } };

        /// <summary>A fresh spell: the binding rim round the heart, one empty circle in it.</summary>
        private static SpellCircle NewSpell() { var b = new SpellCircle { binding = true }; b.inner.Add(NewCircle()); return b; }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1320f, Verse.UI.screenWidth - 40f), Mathf.Min(820f, Verse.UI.screenHeight - 40f));

        public override void PostOpen()
        {
            base.PostOpen();
            SheetWindow.StartMaximized(this, ref sheetMax, ref sheetNormal, true);
            SpellText.Caster = mage;
        }

        public override void PostClose()
        {
            base.PostClose();
            SpellText.Caster = null;
        }

        public override void DoWindowContents(Rect box)
        {
            FlowStyle.Begin(null);
            Rect inner = ParchmentFrame.Draw(box).ContractedBy(4f);
            SheetWindow.MaximizeButton(this, new Rect(box.xMax - 100f, box.y + 28f, 24f, 24f), ref sheetMax, ref sheetNormal, true);
            float lw = inner.width * 0.22f, rw = inner.width * 0.27f;
            Rect left = new Rect(inner.x, inner.y, lw, inner.height);
            Rect right = new Rect(inner.xMax - rw, inner.y, rw, inner.height);
            Rect mid = new Rect(left.xMax + 16f, inner.y, inner.width - lw - rw - 32f, inner.height);
            RitualArt.Panel(left.ExpandedBy(6f)); RitualArt.Panel(right.ExpandedBy(6f));
            if (RitualArt.HasPanel) { left = left.ContractedBy(8f); right = right.ContractedBy(8f); }
            if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Delete || Event.current.keyCode == KeyCode.Backspace)
                && GUIUtility.keyboardControl == 0 && Picked() != null) { RubPicked(); Event.current.Use(); }
            DrawLeft(left);
            DrawMiddle(mid);
            DrawRight(right);
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            FlowStyle.End();
        }

        // ================================================================== left: the runes and the card

        private static readonly string[] Tabs = { "All", "Elements", "Verbs", "Forms", "Modifiers", "Pinned", "Spells" };

        internal static bool ForSpells(RuneDef r)
        {
            if (!(r.UnlockedForSpells || Prefs.DevMode)) return false;
            switch (r.role)
            {
                case RuneRole.Element: return r.magic;
                case RuneRole.Form: return r.magic && r.kind != RuneKind.Summon;   // spirits: not made yet
                case RuneRole.Verb: return Spells.Made(r.kind);
                case RuneRole.Modifier: return true;
                case RuneRole.Flow: return r.kind == RuneKind.Barrier || r.kind == RuneKind.Thread || r.kind == RuneKind.Focus || r.kind == RuneKind.Closing;   // the ward; the thread that turns a step round; focus strengthens; closing keeps a circle pure
            }
            return false;
        }

        /// <summary>Does the search find this text (any word of it, any case)?</summary>
        private bool Found(params string[] texts)
        {
            if (search.NullOrEmpty()) return true;
            foreach (string w in search.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                if (!texts.Any(t => t != null && t.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0)) return false;
            return true;
        }

        private List<RuneDef> Shown()
        {
            var all = RuneDef.All.Where(ForSpells).Where(r => Found(r.label, r.description, r.kind.ToString(), Role(r)));
            switch (tab)
            {
                case 1: return all.Where(r => r.role == RuneRole.Element).ToList();
                case 2: return all.Where(r => r.role == RuneRole.Verb).ToList();
                case 3: return all.Where(r => r.role == RuneRole.Form).ToList();
                case 4: return all.Where(r => r.role == RuneRole.Modifier || r.role == RuneRole.Flow).ToList();
                case 5: return all.Where(Dialog_RitualCodex.Pinned).ToList();
            }
            // pinned in the codex: first
            var list = all.ToList();
            return list.Where(Dialog_RitualCodex.Pinned).Concat(list.Where(r => !Dialog_RitualCodex.Pinned(r))).ToList();
        }

        private void DrawLeft(Rect r)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 32f), "Runes");
            Text.Font = GameFont.Small; GUI.color = InkSoft;
            ArtInk.Label(new Rect(r.x, r.y + 32f, r.width, 44f), tab == 6 ? "The spells of this caster's book: click one to open it here." : "Choose a circle or a section, then click a rune to write it there. Point at a rune to read it.");
            // the tabs, in two rows so they read
            float tx = r.x, ty = r.y + 80f;
            for (int i = 0; i < Tabs.Length; i++)
            {
                float w = (r.width - 8f) / 3f;
                if (Button(new Rect(r.x + (i % 3) * (w + 4f), ty + (i / 3) * 32f, w, 28f), Tabs[i], true, tab == i)) { tab = i; leftScroll = Vector2.zero; }
            }
            // the search: runes by name, kind and description; in the book - spells by name and what they do
            Rect sr = new Rect(r.x, ty + 98f, r.width, 26f);
            Rect sf = new Rect(sr.x + 24f, sr.y, sr.width - (search.NullOrEmpty() ? 24f : 52f), sr.height);
            GUI.color = InkSoft;
            Widgets.DrawTextureFitted(new Rect(sr.x + 2f, sr.y + 3f, 20f, 20f), TexButton.Search, 1f);
            GUI.color = Color.white;
            GUI.SetNextControlName("AvirSpellSearch");
            search = Widgets.TextField(sf, search, 40);
            if (search.NullOrEmpty() && GUI.GetNameOfFocusedControl() != "AvirSpellSearch")
            {
                GUI.color = FlowStyle.InkGrey;
                ArtInk.Label(new Rect(sf.x + 6f, sf.y + 3f, sf.width - 8f, sf.height), tab == 6 ? "Search the spells..." : "Search the runes...");
            }
            if (!search.NullOrEmpty() && Button(new Rect(sr.xMax - 26f, sr.y, 26f, 26f), "×", true)) { search = ""; GUI.FocusControl(null); }
            GUI.color = Color.white;
            const float cardH = 290f, encH = 34f, top = 212f;
            Rect list = new Rect(r.x, r.y + top, r.width, r.height - top - cardH - encH - 12f);
            if (tab == 6)
            {
                DrawSpellList(new Rect(r.x, list.y, r.width, r.height - top - encH - 6f));
                if (Button(new Rect(r.x, r.yMax - encH, r.width, encH), "Encyclopedia of runes...", true))
                    Find.WindowStack.Add(new Dialog_RitualCodex(Find.CurrentMap));
                return;
            }
            List<RuneDef> runes = Shown();
            Rect view = new Rect(0f, 0f, list.width - 16f, runes.Count * 48f);
            RitualScroll.Begin(list, ref leftScroll, view);
            float y = 0f;
            hovered = null;
            foreach (RuneDef rune in runes)
            {
                Rect row = new Rect(0f, y, view.width, 46f);
                if (Mouse.IsOver(row)) hovered = rune;
                if (shown == rune && !RitualArt.Selected(row)) Widgets.DrawBoxSolid(row, new Color(Gold.r, Gold.g, Gold.b, 0.18f));
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                string refused = rune.role == RuneRole.Element ? ElementRefused(rune) : sel.binding ? "The binding rim holds no runes - choose a circle round the heart." : null;
                if (refused != null) TooltipHandler.TipRegion(row, refused);
                // the kind of rune at a glance: a strip of its colour - the element's own, verbs rust, forms blue, modifiers green, flow violet
                Widgets.DrawBoxSolid(new Rect(row.x, row.y + 3f, 3f, 40f), Dialog_SpellWizard.RoleColor(rune));
                Glyph(new Rect(row.x + 4f, row.y + 3f, 40f, 40f), rune);
                if (refused != null) Widgets.DrawBoxSolid(new Rect(row.x + 2f, row.y + 3f, 40f, 40f), new Color(0.88f, 0.8f, 0.64f, 0.6f));   // faded: it cannot go into the chosen circle
                GUI.color = refused != null ? FlowStyle.InkGrey : Ink;
                ArtInk.Label(new Rect(row.x + 48f, row.y + 3f, row.width - 50f, 22f), rune.LabelCap);
                GUI.color = InkSoft;
                ArtInk.Label(new Rect(row.x + 48f, row.y + 23f, row.width - 50f, 22f), Role(rune) + (Dialog_RitualCodex.Pinned(rune) ? " · pinned" : ""));
                if (Dialog_RitualCodex.Pinned(rune)) { GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.9f); Widgets.DrawBox(row, 1); }
                Text.Font = GameFont.Small;
                // one click fills the chosen or the next empty place; a double click adds a place when there is none
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && Event.current.clickCount == 2 && Mouse.IsOver(row))
                {
                    // the first click of the double one may have filled a place already: then nothing more
                    if (Time.realtimeSinceStartup - lastFilled > 0.5f) Write(rune, true);
                    lastWrite = Time.realtimeSinceStartup; Event.current.Use();
                }
                else if (Widgets.ButtonInvisible(row) && Time.realtimeSinceStartup - lastWrite > 0.35f) { if (Write(rune, false)) lastFilled = Time.realtimeSinceStartup; lastWrite = Time.realtimeSinceStartup; }
                y += 48f;
            }
            Widgets.EndScrollView();
            Rect cardR = new Rect(r.x, list.yMax + 6f, r.width, cardH);
            RuneDef picked = Picked();
            if (hovered == null && picked != null)
            {
                if (RitualPick.Card(new Rect(cardR.x, cardR.y, cardR.width, 0f), picked, PickedWhere())) RubPicked();
            }
            else DrawCard(cardR);
            if (Button(new Rect(r.x, r.yMax - encH, r.width, encH), "Encyclopedia of runes...", true))
                Find.WindowStack.Add(new Dialog_RitualCodex(Find.CurrentMap));
        }

        private Vector2 spellScroll;

        /// <summary>The mage's book as a list of cards: click one to open it in the editor (what is written now is replaced).</summary>
        private void DrawSpellList(Rect r)
        {
            // this mage's book first, then the colony's book
            var book = new List<RitualSchema>();
            if (mage != null && Mages.Core(mage) != null) book.AddRange(Mages.Core(mage).Book.spells);
            int own = book.Count;
            if (GameComponent_SpellLibrary.Get != null) book.AddRange(GameComponent_SpellLibrary.Get.spells.Where(x => !book.Any(y => y.name == x.name)));
            if (!search.NullOrEmpty())
            {
                var mine = book.Take(own).Where(x => Found(x.name, SpellText.Explain(x).main)).ToList();
                var theirs = book.Skip(own).Where(x => Found(x.name, SpellText.Explain(x).main)).ToList();
                own = mine.Count; book = mine.Concat(theirs).ToList();
                if (book.Count == 0) { GUI.color = InkSoft; ArtInk.Label(r, "Nothing found for \"" + search + "\"."); return; }
            }
            if (book.Count == 0)
            {
                GUI.color = InkSoft;
                ArtInk.Label(r, "No spells yet - neither in this caster's book nor in the colony's.");
                return;
            }
            const float h = 70f;
            Rect view = new Rect(0f, 0f, r.width - 16f, book.Count * (h + 4f));
            RitualScroll.Begin(r, ref spellScroll, view);
            float y = 0f;
            for (int bi = 0; bi < book.Count; bi++)
            {
                RitualSchema sp = book[bi];
                Rect row = new Rect(0f, y, view.width, h);
                if (bi == own) { GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.8f); Widgets.DrawLineHorizontal(0f, y - 2f, view.width); }
                if (bi >= own) { GUI.color = new Color(0.66f, 0.27f, 0.1f); Text.Anchor = TextAnchor.UpperRight; ArtInk.Label(new Rect(row.x, row.y + 2f, row.width - 4f, 20f), "colony's book"); Text.Anchor = TextAnchor.UpperLeft; }
                if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.18f));
                RuneDef icon = Spells.Element(sp);
                SpellIconArt.Draw(new Rect(row.x + 2f, row.y + 6f, 44f, 44f), sp);
                GUI.color = Ink;
                ArtInk.Label(new Rect(row.x + 52f, row.y + 2f, row.width - 54f, 24f), sp.name);
                GUI.color = InkSoft;
                ArtInk.Label(new Rect(row.x + 52f, row.y + 24f, row.width - 54f, 44f), SpellText.Explain(sp).main.Truncate((row.width - 54f) * 2f - 20f));
                TooltipHandler.TipRegion(row, sp.name + "\n\n" + SpellText.Explain(sp).main + "\n\nClick to open it here.");
                if (Widgets.ButtonInvisible(row))
                {
                    s.name = sp.name;
                    s.weave = SpellCircle.Bound(sp.weave != null ? sp.weave.Copy() : SpellCircle.FromRings(sp));
                    sel = s.weave.inner.FirstOrDefault() ?? s.weave; section = -1; focus = s.weave; ClearPick();
                    tab = 0;
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
                y += h + 4f;
            }
            Widgets.EndScrollView();
        }

        private static string Role(RuneDef r)
        {
            switch (r.role)
            {
                case RuneRole.Element: return SpellCircle.GreatElement(r) ? "great element - wraps circles" : "small element";
                case RuneRole.Verb: return "verb - what it does";
                case RuneRole.Form: return "form - how it is delivered";
                case RuneRole.Modifier: return "modifier";
                default: return "flow";
            }
        }

        private void DrawCard(Rect r)
        {
            if (RitualArt.HasCard) RitualArt.CardAt(r); else Widgets.DrawBoxSolid(r, new Color(0.95f, 0.88f, 0.7f, 0.9f));
            Rect c = r.ContractedBy(14f);
            RuneDef rune = hovered ?? shown;
            if (rune == null)
            {
                GUI.color = InkSoft; Text.Font = GameFont.Small;
                ArtInk.Label(c, "Point at a rune on the list to read what it does - and what it does with each element.");
                return;
            }
            Glyph(new Rect(c.x, c.y, 40f, 40f), rune);
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(c.x + 48f, c.y - 2f, c.width - 48f, 30f), rune.LabelCap);
            Text.Font = GameFont.Small; GUI.color = InkSoft;
            ArtInk.Label(new Rect(c.x + 48f, c.y + 26f, c.width - 48f, 22f), Role(rune));
            string text = RitualText.Desc(rune);
            float y = c.y + 50f;
            float th = Mathf.Min(96f, Text.CalcHeight(text, c.width));
            GUI.color = Ink;
            ArtInk.Label(new Rect(c.x, y, c.width, th), text);
            y += th + 6f;
            GUI.color = new Color(0.66f, 0.27f, 0.1f);
            ArtInk.Label(new Rect(c.x, y, c.width, 22f), "WITH EACH ELEMENT");
            y += 22f;
            foreach (string el in new[] { "Fire", "Water", "Earth", "Air" })
            {
                if (y > c.yMax - 18f) break;
                RuneDef e = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + el);
                GUI.color = e?.color ?? Color.white;
                if (e != null) Glyph(new Rect(c.x, y, 20f, 20f), e);
                bool made = true;
                GUI.color = made ? Ink : InkSoft;
                ArtInk.Label(new Rect(c.x + 26f, y, c.width - 26f, 22f), el + " - " + LineFor(el, rune));
                y += 22f;
            }
            Text.Font = GameFont.Small;
        }

        /// <summary>What a rune does with earth, in a few words.</summary>
        internal static string EarthLine(RuneDef r)
        {
            switch (r.kind)
            {
                case RuneKind.Earth: return "the circle is stone";
                case RuneKind.Release: return "a tremor: blunt blows, foes fall";
                case RuneKind.Hold: return "rooted: no step, hands still free";
                case RuneKind.Drain: return "weight into foes, kept by the caster";
                case RuneKind.Lift: return "stone spikes from below";
                case RuneKind.Growth: return "rich soil, plants grow";
                case RuneKind.Purge: return "smothers fire, swallows filth";
                case RuneKind.Aura: return "the caster's ground: stone skin, mud";
                case RuneKind.Beam: return "a fissure that throws foes down";
                case RuneKind.Shape: return "a wall of stone between two points";
                case RuneKind.Step: return "under the ground - anywhere on the map";
                case RuneKind.Restore: return "stone splints and stone skin";
                case RuneKind.Enchant: return "heavy blows that may stun";
                case RuneKind.Summon: return "spirits are not made yet";
                case RuneKind.Sense: return "every step through the ground";
                case RuneKind.Barrier: return "a ward: no stun, no push";
                case RuneKind.Amplify: return "harder blows";
                case RuneKind.Breadth: return "wider";
                case RuneKind.Reach: return "further (+5% cast)";
                case RuneKind.Anchor: return "stone stands longer";
                case RuneKind.Thread: return "with a step - drags one to the caster, stunned";
                default: return SpellCircle.GreatElement(r) ? "wraps it (see the effect)" : "their meeting is not made yet";
            }
        }

        /// <summary>What a rune does with air, in a few words.</summary>
        internal static string AirLine(RuneDef r)
        {
            switch (r.kind)
            {
                case RuneKind.Air: return "the circle blows";
                case RuneKind.Release: return "a gust that throws foes back";
                case RuneKind.Hold: return "still air: no breath, slow going";
                case RuneKind.Drain: return "breath pulled out, kept by the caster";
                case RuneKind.Lift: return "an updraft: up, and a fall that breaks";
                case RuneKind.Growth: return "the wind spreads wider";
                case RuneKind.Purge: return "clean air: gas gone, breathed out";
                case RuneKind.Aura: return "a whirlwind round the caster";
                case RuneKind.Beam: return "a blow that leaps foe to foe (lightning with water)";
                case RuneKind.Shape: return "a wall of wind that throws back";
                case RuneKind.Step: return "flight over walls and foes";
                case RuneKind.Restore: return "a second wind";
                case RuneKind.Enchant: return "blows that throw foes back";
                case RuneKind.Summon: return "spirits are not made yet";
                case RuneKind.Sense: return "every sound, the unseen too";
                case RuneKind.Barrier: return "a ward that throws attackers back";
                case RuneKind.Amplify: return "stronger; the blow leaps further";
                case RuneKind.Breadth: return "wider; the blow leaps further";
                case RuneKind.Reach: return "further (+5% cast)";
                case RuneKind.Anchor: return "the wind blows longer";
                case RuneKind.Thread: return "with a step - snatches one to the caster, reeling";
                default: return SpellCircle.GreatElement(r) ? "wraps it (see the effect)" : "their meeting is not made yet";
            }
        }

        /// <summary>A rune with a named element, in a few words.</summary>
        internal static string LineFor(string el, RuneDef r) =>
            el == "Fire" ? FireLine(r) : el == "Water" ? WaterLine(r) : el == "Earth" ? EarthLine(r) : el == "Air" ? AirLine(r) : "not made yet";

        /// <summary>What a rune does with water, in a few words.</summary>
        internal static string WaterLine(RuneDef r)
        {
            switch (r.kind)
            {
                case RuneKind.Water: return "the circle flows";
                case RuneKind.Release: return "a burst that knocks foes back";
                case RuneKind.Hold: return "standing water: slows, keeps fire off";
                case RuneKind.Drain: return "draws water out of foes, keeps it";
                case RuneKind.Lift: return "a geyser that throws foes up";
                case RuneKind.Growth: return "the water spreads, plants drink";
                case RuneKind.Purge: return "washes fire, filth, poison away";
                case RuneKind.Aura: return "a tide of mist round the caster";
                case RuneKind.Beam: return "a jet to the target";
                case RuneKind.Shape: return "the ground turns to shallow water";
                case RuneKind.Step: return "a wave that strikes and carries foes";
                case RuneKind.Restore: return "wounds washed and bound";
                case RuneKind.Enchant: return "blows that soak and weaken";
                case RuneKind.Summon: return "spirits are not made yet";
                case RuneKind.Sense: return "feels the living, the unseen too";
                case RuneKind.Barrier: return "a ward that puts out fires round it";
                case RuneKind.Amplify: return "stronger; bursts throw further";
                case RuneKind.Breadth: return "wider";
                case RuneKind.Reach: return "further (+5% cast)";
                case RuneKind.Anchor: return "the water stands longer";
                case RuneKind.Thread: return "with a step - sweeps foes to the caster";
                default: return SpellCircle.GreatElement(r) ? "wraps it (see the effect)" : "their meeting is not made yet";
            }
        }

        /// <summary>What a rune does with fire, in a few words.</summary>
        internal static string FireLine(RuneDef r)
        {
            switch (r.kind)
            {
                case RuneKind.Fire: return "the circle burns";
                case RuneKind.Release: return "sets fire; amplified - explodes";
                case RuneKind.Hold: return "held fire: burns and slows foes";
                case RuneKind.Drain: return "takes the heat, keeps it";
                case RuneKind.Lift: return "tall flames, a pillar or a wall";
                case RuneKind.Growth: return "fire spreads faster and further";
                case RuneKind.Purge: return "puts fire out, burns infection";
                case RuneKind.Aura: return "all round the caster";
                case RuneKind.Beam: return "a line to the target";
                case RuneKind.Shape: return "a wall across the target";
                case RuneKind.Step: return "a leap with a trail of fire";
                case RuneKind.Restore: return "cautery of friends";
                case RuneKind.Enchant: return "a burning weapon";
                case RuneKind.Summon: return "spirits are not made yet";
                case RuneKind.Sense: return "warm bodies through walls";
                case RuneKind.Barrier: return "a ward fire cannot pass";
                case RuneKind.Water: return "steam, where the two meet";
                case RuneKind.Amplify: return "stronger; with release - explosion";
                case RuneKind.Breadth: return "wider";
                case RuneKind.Reach: return "further (+5% cast)";
                case RuneKind.Anchor: return "purged ground lasts longer";
                case RuneKind.Thread: return "with a step - draws one through flame";
                default: return SpellCircle.GreatElement(r) ? "wraps it (see the effect)" : "their meeting is not made yet";
            }
        }

        /// <summary>Whether this element may stand in the chosen circle: a great one only in a circle round the heart
        /// (its seal), a small one only in a circle that holds no others.</summary>
        private string ElementRefused(RuneDef rune)
        {
            if (sel.binding) return "The binding rim holds no runes - choose a circle round the heart.";
            SpellCircle parent = s.weave.ParentOf(sel);
            bool great = SpellCircle.GreatElement(rune);
            if (great && parent != null && !parent.binding) return "A circle inside a great one takes a small element - fire, water, earth or air.";
            if (!great && sel.inner.Count > 0) return "This is a great circle: its seal takes a great element - Life, Light, Time, Space or Darkness.";
            if (great && !sel.Great && s.weave.GreatCount >= SpellCircle.MaxGreat) return "A spell holds at most " + SpellCircle.MaxGreat + " great circles.";
            return null;
        }

        /// <summary>An element into the chosen circle's centre (a great circle's seal); anything else into the chosen
        /// place, or else at the end of the chosen section (a full one grows a place).</summary>
        private bool Write(RuneDef rune, bool grow)
        {
            shown = rune;
            if (rune.role == RuneRole.Element)
            {
                string no = ElementRefused(rune);
                if (no != null) { Messages.Message(no, MessageTypeDefOf.RejectInput, false); return false; }
                sel.element = rune;
                section = -1;
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                return true;
            }
            if (sel.binding) { Messages.Message("The binding rim holds no runes - choose a circle round the heart.", MessageTypeDefOf.RejectInput, false); return false; }
            if (sel.sections.Count == 0) sel.sections.Add(new RitualRing { slots = 1 });
            bool place = pickC == sel && pickSec >= 0 && pickSec < sel.sections.Count && pickK >= 0;
            int i = place ? pickSec : section < 0 ? 0 : Mathf.Min(section, sel.sections.Count - 1);
            RitualRing ring = sel.sections[i];
            if (place && pickK < ring.runes.Count) ring.runes[pickK] = rune;   // the chosen place held a rune: it is replaced
            else
            {
                if (ring.runes.Count >= ring.Slots)
                {
                    if (!grow) { Messages.Message("No empty place in section " + (i + 1) + " - double-click the rune to add a place.", MessageTypeDefOf.RejectInput, false); return false; }
                    if (ring.Slots >= SpellCircle.MaxSlots) { Messages.Message("Section " + (i + 1) + " is full.", MessageTypeDefOf.RejectInput, false); return false; }
                    ring.slots = ring.Slots + 1;
                }
                ring.runes.Add(rune);
                // an empty place chosen: the next empty one is chosen after it, so a section fills place by place
                if (place) { if (ring.runes.Count < ring.Slots) pickK = ring.runes.Count; else ClearPick(); }
            }
            section = i;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            return true;
        }

        // ================================================================== middle: the circles

        private class Node
        {
            public SpellCircle c;
            public float R, core;             // outer radius; for a small circle its centre plate, for a great one the space it holds
            public List<Node> kids = new List<Node>();
            public List<Vector2> at = new List<Vector2>();
        }

        private const float Core = 34f, Band = 40f, GreatBand = 46f, Inside = 0.90f, Heart = 46f;
        /// <summary>A great circle wears the Linarian ring outside its rim, in its own colour: so much further out it reaches.</summary>
        private const float Halo = 1.70f, HaloOuter = 1.57f;
        private static float Outer(Node k) => k.c.Great ? k.R * HaloOuter : k.R;
        private static Texture2D linarian;
        private static Texture2D Linarian => Tex(ref linarian, "UI/Avir/Herald/PathRing");

        private static Node Measure(SpellCircle c)
        {
            var n = new Node { c = c };
            foreach (SpellCircle k in c.inner) n.kids.Add(Measure(k));
            int m = n.kids.Count;
            if (c.binding && m == 1)
            {
                // a single circle is the spell itself - the mage stands in its centre; no rim, no heart round it
                n.at.Add(Vector2.zero);
                n.core = n.kids[0].R;
                n.R = n.kids[0].R;
                return n;
            }
            if (c.binding)
            {
                // the circles stand round the heart, as far out as the largest of them needs - none touching another
                float rmax = m > 0 ? n.kids.Max(k => Outer(k)) * 1.06f : Core * 2f;
                float d = Heart + 34f + rmax;
                if (m > 1) d = Mathf.Max(d, rmax / Mathf.Sin(Mathf.PI / m) + 8f);
                for (int i = 0; i < m; i++)
                {
                    float a = (-90f + 360f * i / m) * Mathf.Deg2Rad;
                    n.at.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d);
                }
                n.core = (m > 0 ? d + rmax : Heart + 34f) + 16f;
                n.R = n.core / 0.94f;
                return n;
            }
            int secs = Mathf.Max(1, c.sections.Count);
            if (m == 0)
            {
                n.core = Core;
                n.R = (Core + secs * Band) / Inside;
                return n;
            }
            float rk = n.kids.Max(k => k.R) * 1.07f;
            float dk = m == 1 ? 0f : rk / Mathf.Sin(Mathf.PI / m) + 4f;
            for (int i = 0; i < m; i++)
            {
                float a = (-90f + 360f * i / m) * Mathf.Deg2Rad;
                n.at.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * dk);
            }
            n.core = dk + rk + 8f;
            n.R = (n.core + c.sections.Count * GreatBand) / Inside;
            return n;
        }

        private static Texture2D bg, frame, disc, outline, thin, dashed, thick, heart;
        private static Texture2D Tex(ref Texture2D t, string path) => t ?? (t = ContentFinder<Texture2D>.Get(path, false));
        private static Texture2D Ring(ref Texture2D t, float hole, bool dash = false)
        {
            if (t != null) return t;
            const int size = 256;
            t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float c = (size - 1) / 2f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01((1f - d) * size * 0.5f) * (hole <= 0f ? 1f : Mathf.Clamp01((d - hole) * size * 0.5f));
                    if (dash && ((int)((Mathf.Atan2(y - c, x - c) + Mathf.PI) / (2f * Mathf.PI) * 18f)) % 2 == 1) a = 0f;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        private static void DrawAt(Vector2 c, float r, Texture2D t, Color col)
        {
            if (t == null || r < 0.5f) return;
            GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), t);
        }

        private static readonly Color BindingColor = new Color(0.92f, 0.84f, 0.66f);
        /// <summary>Circles whose elements met: both wear the colour of what they made.</summary>
        private static readonly Dictionary<SpellCircle, ComboKind> met = new Dictionary<SpellCircle, ComboKind>();
        private static Color ColorOf(SpellCircle c) => c.binding ? BindingColor : met.TryGetValue(c, out ComboKind k) ? Combos.ColorOf(k) : c.element?.color ?? new Color(0.85f, 0.78f, 0.62f);
        private static Color Light(Color c, float k) => new Color(c.r + (1f - c.r) * k, c.g + (1f - c.g) * k, c.b + (1f - c.b) * k, 1f);
        private static Color Dark(Color c) => new Color(c.r * 0.28f, c.g * 0.28f, c.b * 0.28f, 0.92f);

        private struct Hit { public SpellCircle c; public int section; public RitualRing ring; public int rune; public int slot; }
        private readonly List<KeyValuePair<Rect, Hit>> plates = new List<KeyValuePair<Rect, Hit>>();
        private readonly List<KeyValuePair<Node, Vector2>> placed = new List<KeyValuePair<Node, Vector2>>();
        private float scale;

        private void DrawMiddle(Rect r)
        {
            // the spell's sign, its name, and the button that opens the sign editor
            Rect signR = new Rect(r.x, r.y - 2f, 34f, 34f);
            SpellIconArt.Draw(signR, s);
            if (Button(new Rect(r.xMax - 110f, r.y, 110f, 30f), "Sign...", true))
                Find.WindowStack.Add(new Dialog_SpellIcon(s, ic => s.icon = ic));
            Rect nameR = new Rect(r.x + 40f, r.y, r.width - 156f, 30f);
            Widgets.DrawBoxSolid(nameR, new Color(0.93f, 0.86f, 0.70f, 0.9f));
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.9f); Widgets.DrawBox(nameR, 1); GUI.color = Ink;
            s.name = Widgets.TextField(nameR.ContractedBy(2f), s.name, 48);
            GUI.color = Color.white;

            const float controls = 156f;
            Rect area = new Rect(r.x, r.y + 38f, r.width, r.height - 38f - controls - 8f);
            Node root = Measure(s.weave);
            var ord = MapComponent_SpellChain.Order(s.weave);
            orderOf.Clear();
            met.Clear();
            foreach (var kv in Combos.Pairs(s.weave)) met[kv.Key] = kv.Value;
            if (ord.Count > 1) for (int k = 0; k < ord.Count; k++) orderOf[ord[k]] = k + 1;
            float fit = Mathf.Min(area.width, area.height) * 0.47f / (Outer(root) * 1.06f);
            // a circle asked to be brought close: zoomed until it fills the view, moved to its centre
            if (focus != null)
            {
                if (Locate(root, focus, Vector2.zero, out Vector2 off, out Node hit))
                {
                    view.zoom = Mathf.Clamp(Mathf.Min(area.width, area.height) * 0.46f / (hit.R * 1.12f * fit), RitualView.Min, view.max);
                    view.pan = view.zoom <= 1.001f && hit == root ? Vector2.zero : -off * fit * view.zoom;
                }
                focus = null;
            }
            view.Handle(area, area.center, Outer(root) * 1.06f * fit);
            scale = fit * view.zoom;

            GUI.BeginGroup(area);
            Rect local = new Rect(0f, 0f, area.width, area.height);
            GUI.color = Color.white;
            Texture2D sky = Tex(ref bg, "UI/Avir/Herald/SongTab_Bg");
            if (sky != null) GUI.DrawTexture(local, sky, ScaleMode.ScaleAndCrop); else Widgets.DrawBoxSolid(local, new Color(0.08f, 0.07f, 0.14f));
            Vector2 c = local.center + view.pan;
            plates.Clear(); placed.Clear();
            DrawNode(root, c);
            HandleClicks(local);
            GUI.color = new Color(0.94f, 0.9f, 0.82f, 0.9f); Text.Font = GameFont.Small;
            Widgets.Label(new Rect(10f, local.height - 26f, local.width - 20f, 24f), "wheel - zoom · drag - move · click a circle, a section or a place · double-click a circle to bring it close");
            Widgets.Label(new Rect(10f, 4f, local.width - 20f, 24f), Chosen());
            GUI.EndGroup();
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.7f); Widgets.DrawBox(area, 2); GUI.color = Color.white;
            DrawControls(new Rect(r.x, area.yMax + 8f, r.width, controls));
        }

        /// <summary>Where a circle stands from the spell's centre (unscaled), and its measure.</summary>
        private static bool Locate(Node n, SpellCircle target, Vector2 at, out Vector2 off, out Node hit)
        {
            if (n.c == target) { off = at; hit = n; return true; }
            for (int i = 0; i < n.kids.Count; i++)
                if (Locate(n.kids[i], target, at + n.at[i], out off, out hit)) return true;
            off = Vector2.zero; hit = null;
            return false;
        }

        private static string NameOf(SpellCircle c)
        {
            if (c.binding) return "The binding - " + c.inner.Count + " round the heart";
            if (c.Great) return "Great: " + (c.element != null ? c.element.LabelCap.ToString() : "no seal yet") + " (" + c.inner.Count + " inside)";
            return c.element != null ? c.element.LabelCap.ToString() : "empty circle";
        }

        private string Chosen()
        {
            if (sel.binding) return "Chosen: the binding rim - it holds the circles round the heart";
            string name = sel.element != null ? sel.element.LabelCap.ToString() : sel.Great ? "a great circle" : "an empty circle";
            string where = section < 0 ? (sel.Great ? "its seal" : "its centre") : "section " + (section + 1);
            if (pickC == sel && pickSec >= 0 && pickSec == section) where += ", place " + (pickK + 1);
            return "Chosen: " + name + " · " + where;
        }

        /// <summary>A thread of the Song between two circles, from edge to edge.</summary>
        private static void Thread(Vector2 a, float ra, Vector2 b, float rb, Color col)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len <= ra + rb + 1f) return;
            d /= len;
            Widgets.DrawLine(a + d * ra, b - d * rb, col, 2f);
        }

        private void DrawNode(Node n, Vector2 c)
        {
            if (!(n.c.binding && n.kids.Count == 1)) placed.Add(new KeyValuePair<Node, Vector2>(n, c));
            Color col = ColorOf(n.c);
            float R = n.R * scale;
            if (n.c.binding && n.kids.Count == 1) { DrawNode(n.kids[0], c); return; }
            if (n.c.binding)
            {
                // the binding: a plain rim round everything, the heart in the middle where the mage stands,
                // threads from every circle to the heart and from each to the next
                DrawAt(c, R * 1.14f, Linarian, new Color(col.r, col.g, col.b, 0.55f));
                DrawAt(c, R / 0.94f, RitualTex.Rim, new Color(col.r, col.g, col.b, 0.95f));
                if (n.c == sel) Chosen(c, R);
                int m = n.kids.Count;
                float hr = Heart * scale;
                // the orbits round the heart: the Linarian ring where the circles stand, and a smaller one within, beaded
                float orbit = m > 0 ? n.at[0].magnitude * scale : hr * 3f;
                DrawAt(c, orbit, Linarian, new Color(col.r, col.g, col.b, 0.47f));
                DrawAt(c, orbit * 0.55f, Linarian, new Color(col.r, col.g, col.b, 0.35f));
                GUI.color = new Color(0.86f, 0.82f, 1f, 0.6f);
                for (int b = 0; b < 24; b++)
                {
                    float a = b * 15f * Mathf.Deg2Rad, rr = orbit * 0.55f * 0.83f;
                    GUI.DrawTexture(new Rect(c.x + Mathf.Cos(a) * rr - 2f, c.y + Mathf.Sin(a) * rr - 2f, 4f, 4f), BaseContent.WhiteTex);
                }
                Color th = new Color(0.92f, 0.86f, 1f, 0.55f);
                for (int i = 0; i < m; i++)
                {
                    Vector2 p = c + n.at[i] * scale;
                    Thread(c, hr * 0.8f, p, n.kids[i].R * scale, th);
                    if (m > 2) { int j = (i + 1) % m; Thread(p, n.kids[i].R * scale, c + n.at[j] * scale, n.kids[j].R * scale, new Color(th.r, th.g, th.b, 0.35f)); }
                    else if (m == 2 && i == 0) Thread(p, n.kids[0].R * scale, c + n.at[1] * scale, n.kids[1].R * scale, new Color(th.r, th.g, th.b, 0.35f));
                }
                Texture2D star = Tex(ref heart, "UI/Avir/Herald/Star_Lit");
                Rect hRect = new Rect(c.x - hr, c.y - hr, hr * 2f, hr * 2f);
                // the heart as a medallion: a golden Linarian ring, a rim, the star within
                DrawAt(c, hr * 1.35f, Linarian, new Color(1f, 0.84f, 0.55f, 0.95f));
                DrawAt(c, hr * 0.95f, RitualTex.Rim, new Color(1f, 0.84f, 0.55f, 0.9f));
                if (star != null) { GUI.color = Color.white; GUI.DrawTexture(new Rect(c.x - hr * 0.7f, c.y - hr * 0.7f, hr * 1.4f, hr * 1.4f), star, ScaleMode.ScaleToFit); }
                else DrawAt(c, hr * 0.6f, Ring(ref disc, 0f), new Color(1f, 0.85f, 0.5f, 0.9f));
                if (Mouse.IsOver(hRect)) TooltipHandler.TipRegion(hRect, "The heart of the spell: here the caster stands. Every circle is bound to it by a thread.");
                for (int i = 0; i < m; i++) DrawNode(n.kids[i], c + n.at[i] * scale);
                return;
            }
            if (n.c.Great) DrawAt(c, R * Halo, Linarian, new Color(col.r, col.g, col.b, 0.9f));
            DrawAt(c, R * 1.02f, disc ?? Ring(ref disc, 0f), new Color(col.r, col.g, col.b, 0.06f));
            DrawAt(c, R / 0.94f, RitualTex.Rim, col);
            if (n.c == sel) Chosen(c, R);
            // inside: the circles it holds, or the centre plate
            if (n.kids.Count > 0 || n.c.Great)
            {
                for (int i = 0; i < n.kids.Count; i++) DrawNode(n.kids[i], c + n.at[i] * scale);
                // its great element sits as a seal on the top of its rim
                float sr = Mathf.Clamp(GreatBand * 0.42f * scale, 6f, 40f);
                Plate(c + new Vector2(0f, -R), sr, col, n.c.element, new Hit { c = n.c, section = -1, rune = -1, slot = -1 },
                      "The seal of this great circle: write a great element here - Life, Light, Time, Space or Darkness.");
            }
            else Plate(c, n.core * 0.78f * scale, col, n.c.element, new Hit { c = n.c, section = -1, rune = -1, slot = -1 },
                       "The centre: this circle's element - fire, water, earth or air (or a great one, if it stands round the heart alone).");
            if (orderOf.TryGetValue(n.c, out int nth))
            {
                // its place in the chain, just off the top-left of its rim
                float d = R * 0.78f;
                Rect lr = new Rect(c.x - d - 14f, c.y - d - 14f, 28f, 28f);
                Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Ink;
                Widgets.Label(lr, nth.ToString());
                Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
                TooltipHandler.TipRegion(lr, "Its place in the chain: the circles act one after another, each where the one before it ended.");
            }
            // the sections
            float inner = n.core, band = n.kids.Count > 0 ? GreatBand : Band;
            int secs = n.c.sections.Count;
            for (int i = 0; i < secs; i++)
            {
                float rin = (inner + i * band) * scale, rout = (inner + (i + 1) * band) * scale;
                DrawAt(c, rin, Ring(ref thin, 0.985f), new Color(Light(col, 0.2f).r, Light(col, 0.2f).g, Light(col, 0.2f).b, n.c == sel && i == section ? 1f : 0.75f));
                if (n.c == sel && i == section)
                {
                    // the chosen section: its band washed in gold between two firm rims
                    DrawAt(c, rout, BandTex(rout <= 0f ? 0f : rin / rout), new Color(Gold.r, Gold.g, Gold.b, 0.22f));
                    DrawAt(c, rin, Ring(ref thick, 0.96f), new Color(Gold.r, Gold.g, Gold.b, 1f));
                    DrawAt(c, rout, Ring(ref thick, 0.96f), new Color(Gold.r, Gold.g, Gold.b, 1f));
                }
                RitualRing ring = n.c.sections[i];
                int per = ring.Slots;
                float mid = (rin + rout) / 2f;
                float pr = Mathf.Min((rout - rin) * 0.36f, Mathf.PI * mid / per * 0.38f);
                float off = n.kids.Count > 0 ? ClearOffset(per) : i * 25f;
                for (int k = 0; k < per; k++)
                {
                    float a = (-90f + off + k * 360f / per) * Mathf.Deg2Rad;
                    Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * mid;
                    Plate(p, pr, col, k < ring.runes.Count ? ring.runes[k] : null, new Hit { c = n.c, section = i, ring = ring, rune = k < ring.runes.Count ? k : -1, slot = k }, null);
                }
            }
        }

        /// <summary>The chosen circle: a soft gold wash, a firm gold rim, and a halo that breathes.</summary>
        private void Chosen(Vector2 c, float R)
        {
            float pulse = 0.55f + 0.35f * Mathf.Sin(Time.realtimeSinceStartup * 4f);
            DrawAt(c, R * 1.02f, Ring(ref disc, 0f), new Color(Gold.r, Gold.g, Gold.b, 0.08f));
            DrawAt(c, R * 1.09f, Ring(ref thick, 0.96f), new Color(Gold.r, Gold.g, Gold.b, 1f));
            DrawAt(c, R * 1.14f, Ring(ref thick, 0.96f), new Color(1f, 0.8f, 0.3f, pulse));
        }

        /// <summary>Turns a great circle's runes so none sits on the four stars of the rim, nor on the seal at the top.</summary>
        private static float ClearOffset(int per)
        {
            float best = -1f, bo = 0f;
            for (int o = 0; o <= 360 / per; o += 3)
            {
                float dmin = 999f;
                for (int k = 0; k < per; k++)
                {
                    float a = Mathf.Repeat(o + k * 360f / per, 90f);
                    dmin = Mathf.Min(dmin, Mathf.Min(a, 90f - a));
                }
                if (dmin > best) { best = dmin; bo = o; }
            }
            return bo;
        }

        private bool IsPicked(Hit h) => pickC == h.c && pickSec == h.section && (h.section < 0 || pickK == h.slot);

        private void Plate(Vector2 p, float r, Color col, RuneDef rune, Hit hit, string emptyTip)
        {
            Rect box = new Rect(p.x - r, p.y - r, r * 2f, r * 2f);
            bool picked = IsPicked(hit);
            if (picked) RitualPick.Mark(box);
            if (rune != null)
            {
                DrawAt(p, r, Ring(ref disc, 0f), Dark(col));
                DrawAt(p, r, Ring(ref outline, 0.86f), Light(col, 0.35f));
                GUI.color = rune.role == RuneRole.Element ? Light(rune.color, 0.25f) : Light(col, 0.3f);
                GUI.DrawTexture(box.ContractedBy(r * 0.22f), rune.Glow, ScaleMode.ScaleToFit);
                if (Mouse.IsOver(box))
                {
                    DrawAt(p, r, Ring(ref disc, 0f), picked ? new Color(0.8f, 0.2f, 0.15f, 0.35f) : new Color(1f, 0.85f, 0.4f, 0.25f));
                    TooltipHandler.TipRegion(box, rune.LabelCap + "\n\n" + (picked ? "Click again (or press Delete) to rub it out; click a rune on the left to put another here." : "Click to choose this place."));
                }
            }
            else
            {
                DrawAt(p, r * 0.92f, Ring(ref dashed, 0.88f, true), picked ? new Color(1f, 0.85f, 0.4f, 1f) : Light(col, 0.4f));
                if (Mouse.IsOver(box))
                {
                    DrawAt(p, r * 0.9f, Ring(ref disc, 0f), new Color(1f, 0.85f, 0.4f, 0.22f));
                    TooltipHandler.TipRegion(box, (emptyTip ?? "An empty place.") + "\n\nClick to choose it, then click a rune on the left to write it here." + (hit.section >= 0 ? " Double-click to take the place away." : ""));
                }
            }
            plates.Add(new KeyValuePair<Rect, Hit>(box, hit));
            GUI.color = Color.white;
        }

        private void HandleClicks(Rect local)
        {
            Event e = Event.current;
            // a double click on an empty place takes the place away
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && Mouse.IsOver(local))
                for (int i = plates.Count - 1; i >= 0; i--)
                {
                    if (!plates[i].Key.Contains(e.mousePosition)) continue;
                    Hit h = plates[i].Value;
                    if (h.section >= 0 && h.rune < 0 && h.ring != null && h.ring.Slots > 1)
                    {
                        h.ring.slots = h.ring.Slots - 1;
                        ClearPick(); lastWrite = Time.realtimeSinceStartup;
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                        e.Use();
                        return;
                    }
                    break;
                }
            // a double click brings the circle under the mouse close
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && Mouse.IsOver(local))
            {
                for (int i = placed.Count - 1; i >= 0; i--)
                    if ((e.mousePosition - placed[i].Value).magnitude / scale <= placed[i].Key.R) { focus = placed[i].Key.c; sel = focus; e.Use(); return; }
            }
            if (e.type != EventType.MouseUp || e.button != 0 || view.dragged || !Mouse.IsOver(local)) return;
            Vector2 m = e.mousePosition;
            // a place first (the innermost drawn last): one click chooses it - filled or empty; a second click on a filled one rubs it out
            for (int i = plates.Count - 1; i >= 0; i--)
            {
                if (!plates[i].Key.Contains(m)) continue;
                Hit h = plates[i].Value;
                bool filled = h.section < 0 ? h.c.element != null : h.rune >= 0;
                bool same = IsPicked(h);
                sel = h.c; section = h.section;
                if (same && filled) RubPicked();
                else { pickC = h.c; pickSec = h.section; pickK = h.section < 0 ? -1 : h.slot; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                e.Use();
                return;
            }
            ClearPick();
            // else the innermost circle under the mouse, and which of its sections
            for (int i = placed.Count - 1; i >= 0; i--)
            {
                Node n = placed[i].Key; Vector2 c = placed[i].Value;
                float d = (m - c).magnitude / scale;
                if (d > n.R) continue;
                sel = n.c;
                float band = n.kids.Count > 0 ? GreatBand : Band;
                section = n.c.binding || d < n.core ? -1 : Mathf.Clamp(Mathf.FloorToInt((d - n.core) / band), 0, Mathf.Max(0, n.c.sections.Count - 1));
                if (n.c.sections.Count == 0) section = -1;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                e.Use();
                return;
            }
            // the empty sky: the whole spell again
            if (s.weave.inner.Count > 1) { sel = s.weave; section = -1; e.Use(); }
        }

        // ================================================================== under the circles

        private void DrawControls(Rect r)
        {
            SpellCircle root = s.weave;
            if (!root.All().Contains(sel)) { sel = root; section = -1; }
            SpellCircle parent = root.ParentOf(sel);
            // the circle round the heart the chosen one belongs to
            SpellCircle group = sel.binding ? null : parent == root ? sel : parent;
            int gi = group != null ? root.inner.IndexOf(group) : -1;
            float half = (r.width - 12f) / 2f, y = r.y;
            bool roomSmall = root.SmallCount < SpellCircle.MaxSmall, roomGreat = root.GreatCount < SpellCircle.MaxGreat;
            Stepper(new Rect(r.x, y, half, 30f), "Circles round the heart", root.inner.Count, SpellCircle.MaxInner, 0,
                () => { root.inner.Remove(gi >= 0 ? group : root.inner.Last()); sel = root; section = -1; ClearPick(); },
                () => { var k = NewCircle(); root.inner.Add(k); sel = k; section = -1; ClearPick(); }, roomSmall);
            Rect right = new Rect(r.x + half + 12f, y, half, 30f);
            if (group == null) { GUI.color = InkSoft; Text.Anchor = TextAnchor.MiddleCenter; ArtInk.Label(right, "choose a circle round the heart"); Text.Anchor = TextAnchor.UpperLeft; }
            else if (!group.Great)
            {
                if (Button(right, "Make it a great circle (up to 5 inside)", roomGreat))
                {
                    var g = new SpellCircle { sections = { new RitualRing { slots = 2 } } };
                    g.inner.Add(group);
                    root.inner[gi] = g;
                    sel = g; section = -1; ClearPick();
                }
            }
            else
            {
                SpellCircle g = group;
                Stepper(right, "Small circles in it", g.inner.Count, SpellCircle.MaxInner, 0,
                    () =>
                    {
                        // the last one out; a great circle left with one plain circle and nothing of its own opens back into it
                        if (g.inner.Count == 1 && g.element == null && !g.OwnRunes.Any()) { root.inner[gi] = g.inner[0]; sel = g.inner[0]; }
                        else { SpellCircle last = g.inner.Last(); g.inner.Remove(last); if (sel == last) sel = g; }
                        section = -1; ClearPick();
                    },
                    () => { var k = NewCircle(); g.inner.Add(k); sel = k; section = -1; ClearPick(); }, roomSmall);
            }
            y += 36f;
            if (sel.binding)
            {
                GUI.color = InkSoft; Text.Anchor = TextAnchor.MiddleCenter;
                ArtInk.Label(new Rect(r.x, y, r.width, 30f), "The binding rim holds no runes - choose a circle round the heart.");
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                string name = sel.element != null ? sel.element.label : "circle";
                Stepper(new Rect(r.x, y, half, 30f), "Sections in " + name, sel.sections.Count, sel.MaxSections, sel.Great ? 0 : 1,
                    () => { sel.sections.RemoveAt(sel.sections.Count - 1); section = Mathf.Min(section, sel.sections.Count - 1); ClearPick(); },
                    () => { sel.sections.Add(new RitualRing { slots = 2 }); section = sel.sections.Count - 1; });
                if (section >= 0 && section < sel.sections.Count)
                {
                    RitualRing cur = sel.sections[section];
                    Stepper(new Rect(r.x + half + 12f, y, half, 30f), "Places in section " + (section + 1), cur.Slots, SpellCircle.MaxSlots, 1,
                        () => { cur.slots = cur.Slots - 1; if (cur.runes.Count > cur.slots) cur.runes.RemoveAt(cur.runes.Count - 1); ClearPick(); },
                        () => cur.slots = cur.Slots + 1);
                }
                else { GUI.color = InkSoft; Text.Anchor = TextAnchor.MiddleCenter; ArtInk.Label(new Rect(r.x + half + 12f, y, half, 30f), "choose a section for its places"); Text.Anchor = TextAnchor.UpperLeft; }
            }
            y += 36f;
            // where a clicked rune goes: the centre (or a great circle's seal) or one of the sections
            if (!sel.binding)
            {
                GUI.color = Ink; Text.Anchor = TextAnchor.MiddleLeft;
                ArtInk.Label(new Rect(r.x, y, 90f, 30f), "Write into:");
                Text.Anchor = TextAnchor.UpperLeft;
                float cx = r.x + 92f;
                if (Button(new Rect(cx, y, 70f, 30f), sel.Great ? "Seal" : "Centre", true, section < 0)) { section = -1; ClearPick(); }
                cx += 74f;
                for (int i = 0; i < sel.sections.Count; i++)
                {
                    if (Button(new Rect(cx, y, 84f, 30f), "Section " + (i + 1), true, section == i)) { section = i; ClearPick(); }
                    cx += 88f;
                }
            }
            if (Button(new Rect(r.xMax - 170f, y, 170f, 30f), "Step by step...", true))
                Find.WindowStack.Add(new Dialog_SpellWizard(TakeCircle));
            TooltipHandler.TipRegion(new Rect(r.xMax - 170f, y, 170f, 30f), "Write a circle page by page: the element, what it does, how it reaches, what strengthens it - with large cards and what it will do read at every click. It goes into the chosen empty circle, or beside the others.");
            y += 38f;
            float w = (r.width - 24f) / 4f;
            if (Button(new Rect(r.x, y, w, 34f), "Rub out all", true)) { s.weave = NewSpell(); sel = s.weave.inner[0]; section = -1; ClearPick(); }
            if (Button(new Rect(r.x + (w + 8f), y, w, 34f), "Remove this circle", !sel.binding))
            {
                SpellCircle p = root.ParentOf(sel);
                if (p != null) { p.inner.Remove(sel); if (p != root && p.inner.Count == 0 && p.element == null && !p.OwnRunes.Any()) root.inner.Remove(p); }
                sel = root; section = -1; ClearPick();
            }
            if (Button(new Rect(r.x + 2f * (w + 8f), y, w, 34f), "Bring it close", true)) focus = sel;
            if (Button(new Rect(r.x + 3f * (w + 8f), y, w, 34f), "Reset the view", view.Changed)) view.Reset();
        }

        private static void Stepper(Rect r, string label, int value, int max, int min, System.Action less, System.Action more, bool room = true)
        {
            Rect minus = new Rect(r.x, r.y, 30f, r.height), plus = new Rect(r.xMax - 30f, r.y, 30f, r.height);
            if (Button(minus, "-", value > min)) less();
            if (Button(plus, "+", value < max && room)) more();
            if (value < max && !room) TooltipHandler.TipRegion(plus, "The spell is full: at most " + SpellCircle.MaxGreat + " great and " + SpellCircle.MaxSmall + " small circles in all.");
            GUI.color = Ink; Text.Anchor = TextAnchor.MiddleCenter;
            ArtInk.Label(new Rect(minus.xMax, r.y, r.width - 60f, r.height), label + ": " + value + " of " + max);
            Text.Anchor = TextAnchor.UpperLeft;
        }

        // ================================================================== right: the effect, Arlie, keep

        private SpellEffect shownFx;
        private SpellCircle shownSel;
        private RitualSchema shownSpell;
        private bool shownWhole;
        private float shownAt;

        private void DrawRight(Rect r)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 32f), "Codex");
            Text.Font = GameFont.Small;
            // a circle of several chosen: what that circle does alone; the binding (or a click on the sky) - the whole spell
            bool whole = sel.binding || s.weave.Working.Count() <= 1 || !s.weave.All().Contains(sel);
            // what the codex reads is read anew a few times a second, not every frame: the circle alone is a new spell each time
            if (shownFx == null || shownSel != sel || shownWhole != whole || Time.realtimeSinceStartup - shownAt > 0.3f)
            {
                shownSpell = whole ? s : new RitualSchema { name = s.name, weave = SpellCircle.Bound(sel.Copy()) };
                SpellEffect fx0 = SpellText.Explain(shownSpell);
                // a circle whose element met its neighbour's: it works as what they made
                if (!whole && met.TryGetValue(sel, out ComboKind selMet))
                {
                    SpellEffect cfx = ComboText.Explain(selMet, sel.AsSchema());
                    cfx.details = fx0.details; fx0 = cfx;
                }
                if (!whole) fx0.details = (fx0.details ?? "") + "\nThe whole spell: choose the outer rim (or click the sky).";
                shownFx = fx0; shownSel = sel; shownWhole = whole; shownAt = Time.realtimeSinceStartup;
            }
            SpellEffect fx = shownFx;
            float ph = EffectPlaque.Height(fx, r.width);
            EffectPlaque.Draw(new Rect(r.x, r.y + 40f, r.width, ph), fx, !whole && met.TryGetValue(sel, out ComboKind tint) ? Combos.ColorOf(tint) : Spells.ColorOf(shownSpell), whole ? "THE MAIN EFFECT" : "THIS CIRCLE ALONE");
            float y = r.y + 40f + ph + 10f;
            float oh = Mathf.Max(64f, Mathf.Min(r.yMax - 60f - y - 120f, 40f + 26f * OutlineRows().Count));
            DrawOutline(new Rect(r.x, y, r.width, oh)); y += oh + 10f;
            // the chosen plaque, read as the almanac has it (with none chosen: what the Thread does here)
            var entry = EffectPlaque.Entry(fx);
            if (entry.HasValue)
            {
                float room = r.yMax - 50f - y;
                Text.Font = GameFont.Small;
                float eh = Mathf.Min(room, 34f + Text.CalcHeight(entry.Value.Value, r.width - 20f));
                if (eh > 60f)
                {
                    Rect er = new Rect(r.x, y, r.width, eh);
                    Widgets.DrawBoxSolid(er, new Color(0.95f, 0.88f, 0.7f, 0.55f));
                    GUI.color = new Color(0.55f, 0.25f, 0.1f); Text.Font = GameFont.Small;
                    ArtInk.Label(new Rect(er.x + 10f, er.y + 6f, er.width - 20f, 24f), entry.Value.Key.ToUpperInvariant());
                    GUI.color = Ink;
                    Rect body = new Rect(er.x + 10f, er.y + 30f, er.width - 20f, er.height - 34f);
                    float bh = Text.CalcHeight(entry.Value.Value, body.width - 16f);
                    Widgets.BeginScrollView(body, ref entryScroll, new Rect(0f, 0f, body.width - 16f, bh));
                    Widgets.Label(new Rect(0f, 0f, body.width - 16f, bh), entry.Value.Value);
                    Widgets.EndScrollView();
                    y += eh + 10f;
                }
            }
            string line = ArlieLine();
            if (Arlia.On && !line.NullOrEmpty())
            {
                float nh = Arlia.NoteHeight(line, r.width);
                if (y + nh < r.yMax - 50f) Arlia.Note(new Rect(r.x, y, r.width, nh), line);
            }
            string why = WhyNot();
            if (Button(new Rect(r.x, r.yMax - 40f, r.width, 38f), why == null ? "Keep as a spell" : "Cannot: " + why, why == null))
            {
                keep?.Invoke(s.Copy());
                Close();
            }
        }

        private Vector2 entryScroll;
        private readonly HashSet<SpellCircle> folded = new HashSet<SpellCircle>();

        private List<KeyValuePair<SpellCircle, int>> OutlineRows()
        {
            var rows = new List<KeyValuePair<SpellCircle, int>>();
            void Walk(SpellCircle c, int depth)
            {
                rows.Add(new KeyValuePair<SpellCircle, int>(c, depth));
                if (c.binding || !folded.Contains(c)) foreach (SpellCircle k in c.inner) Walk(k, depth + 1);
            }
            // one circle alone is the whole spell: the binding and the heart are not shown
            if (s.weave.binding && s.weave.inner.Count == 1) Walk(s.weave.inner[0], 0); else Walk(s.weave, 0);
            return rows;
        }

        /// <summary>Every circle of the spell as a list: click to choose it, double-click to bring it close, the arrow
        /// folds a circle's branch away (nothing is removed).</summary>
        private void DrawOutline(Rect r)
        {
            var rows = OutlineRows();
            if (RitualArt.HasCard) RitualArt.CardAt(r); else Widgets.DrawBoxSolid(r, new Color(0.93f, 0.86f, 0.72f, 0.9f));
            Rect c = r.ContractedBy(8f);
            GUI.color = new Color(0.66f, 0.27f, 0.1f);
            ArtInk.Label(new Rect(c.x, c.y, c.width, 22f), "CIRCLES  ·  " + s.weave.GreatCount + "/" + SpellCircle.MaxGreat + " great  ·  " + s.weave.SmallCount + "/" + SpellCircle.MaxSmall + " small");
            Rect list = new Rect(c.x, c.y + 24f, c.width, c.height - 24f);
            Rect inner = new Rect(0f, 0f, list.width - 16f, rows.Count * 26f);
            RitualScroll.Begin(list, ref outlineScroll, inner);
            float y = 0f;
            foreach (var kv in rows)
            {
                SpellCircle sc = kv.Key;
                Rect row = new Rect(0f, y, inner.width, 26f);
                if (sc == sel) Widgets.DrawBoxSolid(row, new Color(Gold.r, Gold.g, Gold.b, 0.35f));
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.18f));
                float x = 4f + 18f * kv.Value;
                if (sc.inner.Count > 0 && !sc.binding)
                {
                    Rect fold = new Rect(x, row.y + 4f, 18f, 18f);
                    GUI.color = Ink; Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(fold, folded.Contains(sc) ? "▸" : "▾");
                    Text.Anchor = TextAnchor.UpperLeft;
                    TooltipHandler.TipRegion(fold, folded.Contains(sc) ? "Unfold this branch" : "Fold this branch away (nothing is removed)");
                    if (Widgets.ButtonInvisible(fold)) { if (!folded.Remove(sc)) folded.Add(sc); SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); y += 26f; continue; }
                }
                x += 20f;
                Rect g = new Rect(x, row.y + 3f, 20f, 20f);
                if (sc.element != null) Glyph(g, sc.element);
                else if (sc.binding) { GUI.color = Color.white; Texture2D st = Tex(ref heart, "UI/Avir/Herald/Star_Lit"); if (st != null) GUI.DrawTexture(g, st, ScaleMode.ScaleToFit); }
                else { GUI.color = InkSoft; GUI.DrawTexture(g, Ring(ref dashed, 0.88f, true)); }
                GUI.color = sc == sel ? Ink : InkSoft;
                ArtInk.Label(new Rect(g.xMax + 4f, row.y + 2f, row.width - g.xMax - 4f, 24f), NameOf(sc));
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && Mouse.IsOver(row))
                {
                    if (Event.current.clickCount == 2) focus = sc;
                    sel = sc; section = -1; ClearPick();
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    Event.current.Use();
                }
                y += 26f;
            }
            Widgets.EndScrollView();
            GUI.color = Color.white;
        }

        private string WhyNot()
        {
            var all = s.weave.Working.ToList();
            if (all.Count == 0) return "no circle - add one round the heart";
            if (all.All(c => c.element == null)) return "no element";
            if (all.Any(c => c.Great && c.element == null)) return "a great circle has no seal";
            if (all.Any(c => c.element == null)) return "a circle has no element";
            if (all.Where(c => !c.Great).All(c => !c.OwnRunes.Any(x => x.role == RuneRole.Verb || x.role == RuneRole.Form || x.kind == RuneKind.Barrier))
                && !all.Any(c => c.Great && c.OwnRunes.Any()))
                return "nothing to do - add a verb or a form";
            return null;
        }

        /// <summary>Arlie's remark on what is in the circle now.</summary>
        private string ArlieLine()
        {
            SpellCircle root = s.weave;
            var all = root.Working.ToList();
            var smalls = all.Where(c => !c.Great).ToList();
            // a verb said four times in one section: the fourth does nothing at all (Tautology)
            foreach (SpellCircle c in smalls)
                foreach (RitualRing sec in c.sections)
                    if (sec.runes.Where(x => x != null && x.role == RuneRole.Verb).GroupBy(x => x.kind).Any(g => g.Count() >= 4))
                        return "You know, there is a word made to describe you. \"The s-s-s-st-stutterer.\"";
            if (sel.binding) return "The rim holds everything together. You, so far, hold nothing. Pick a circle.";
            if (all.All(c => c.element == null && !c.OwnRunes.Any())) return "An empty heart. Give it an element - any will do for a start. I'll wait. Not long.";
            if (sel.Great && sel.element == null) return "A great circle without its seal is a crown without a head. Life, Light, Time, Space or Darkness - on top.";
            if (sel.element == null) return "A circle with no heart is a nest with no egg. Put an element in the middle.";
            if (all.Count >= 10) return "So many circles. Whoever holds this - I'd like to meet them. Briefly.";
            if (met.TryGetValue(sel, out ComboKind mk) && sel.OwnRunes.Any())
                switch (mk)
                {
                    case ComboKind.Bog: return "You're rather fond of filth, aren't you?";
                    case ComboKind.Magma: return "The dirt is burning? Good.";
                    case ComboKind.Steam: return "Ha! I've seen oceans turn to steam! Though you won't manage.";
                    case ComboKind.Storm: return "Mind dressing up in metal?";
                    case ComboKind.Haze: return "Can't see straight? Neither can they. That's the point.";
                    case ComboKind.Dust: return "Eat dust. No, really - that's the spell.";
                }
            if (!sel.OwnRunes.Any())
                switch (sel.element.kind)
                {
                    case RuneKind.Water: return "It's wet and it's deep. Yes, yes, this is water. Now tell it what to do.";
                    case RuneKind.Fire: return "Fire is always good. Mostly. Well, unless you're not fireproof. Are you?";
                    case RuneKind.Earth: return "Boring and heavy, stubborn and dirty. You have something in common.";
                    case RuneKind.Air: return "Air. Light, quick, never where you left it. I approve. Mostly.";
                    default: return "A heart of " + sel.element.label + " and nothing round it. A verb, perhaps?";
                }
            var same = smalls.Where(c => c.element != null).GroupBy(c => c.element).FirstOrDefault(g => g.Count() >= 3);
            if (WeaveSpell.Steam(root)) return "Fire and water in one weave? You will boil them before they burn.";
            if (same != null) return same.Count() + " circles of " + same.Key.label + ". They'll feed each other - and burn out together.";
            if (WeaveSpell.Wraps(root, RuneKind.Life)) return "Life round the fire: it burns whom it must, and spares whom it should.";
            SpellCircle hollow = all.FirstOrDefault(c => c.Great && c.inner.Count == 0 && !c.OwnRunes.Any());
            if (hollow != null) return "A great circle with nothing in it and nothing of its own. A very expensive hoop.";
            for (int i = 0; i < sel.sections.Count; i++)
                if (sel.sections[i].runes.Count < sel.sections[i].Slots) return "Section " + (i + 1) + " has a gap in it. Fill it or cut it - a gap in a circle is where the magic leaks out.";
            return "Good. Now read what it does - before it does it to you.";
        }

        // ================================================================== helpers

        private static void Glyph(Rect g, RuneDef rune) => SpellGlyph.Draw(g, rune);

        private void ClearPick() { pickC = null; pickSec = -1; pickK = -1; }

        /// <summary>A circle written step by step: into the chosen circle if it is empty, else beside the others round the heart.</summary>
        private void TakeCircle(SpellCircle c)
        {
            SpellCircle root = s.weave, parent = root.ParentOf(sel);
            if (!sel.binding && !sel.Great && sel.element == null && !sel.OwnRunes.Any() && parent != null) parent.inner[parent.inner.IndexOf(sel)] = c;
            else if (root.inner.Count < SpellCircle.MaxInner && root.SmallCount < SpellCircle.MaxSmall) root.inner.Add(c);
            else { Messages.Message("No room round the heart for another circle - free a place first.", MessageTypeDefOf.RejectInput, false); return; }
            sel = c; section = -1; ClearPick();
        }

        /// <summary>The rune chosen on the circles, if it is still there.</summary>
        private RuneDef Picked()
        {
            if (pickC == null || !s.weave.All().Contains(pickC)) return null;
            if (pickSec < 0) return pickC.element;
            if (pickSec >= pickC.sections.Count || pickK < 0 || pickK >= pickC.sections[pickSec].runes.Count) return null;
            return pickC.sections[pickSec].runes[pickK];
        }

        private string PickedWhere() => (pickC?.element != null ? (pickC.Great ? "Great circle of " : "Circle of ") + pickC.element.label : "An empty circle") + " · " + (pickSec < 0 ? (pickC != null && pickC.Great ? "its seal" : "its centre") : "section " + (pickSec + 1) + ", place " + (pickK + 1));

        private void RubPicked()
        {
            if (Picked() == null) { ClearPick(); return; }
            if (pickSec < 0) pickC.element = null;
            else pickC.sections[pickSec].runes.RemoveAt(pickK);
            ClearPick();
            SoundDefOf.Tick_Low.PlayOneShotOnCamera();
        }

        private static readonly Dictionary<int, Texture2D> bands = new Dictionary<int, Texture2D>();

        /// <summary>A flat ring whose hole is this share of its radius (cached by twentieths).</summary>
        private static Texture2D BandTex(float hole)
        {
            int key = Mathf.Clamp(Mathf.RoundToInt(hole * 40f), 0, 39);
            if (bands.TryGetValue(key, out Texture2D t)) return t;
            t = null;
            t = Ring(ref t, key / 40f);
            bands[key] = t;
            return t;
        }

        private static bool DoubleClicked(Rect r) =>
            Event.current.type == EventType.MouseDown && Event.current.button == 0 && Event.current.clickCount == 2 && Mouse.IsOver(r);

        private static bool Button(Rect r, string label, bool enabled, bool chosen = false)
        {
            bool over = enabled && Mouse.IsOver(r);
            Texture2D t = chosen ? FlowStyle.ButtonPressed : !over ? FlowStyle.ButtonNormal : Input.GetMouseButton(0) ? FlowStyle.ButtonPressed : FlowStyle.ButtonHover;
            GUI.color = enabled || chosen ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(r, t, ScaleMode.StretchToFill);
            GUI.color = enabled || chosen ? FlowStyle.ButtonText : FlowStyle.InkGrey;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (enabled && Widgets.ButtonInvisible(r)) { SoundDefOf.Click.PlayOneShotOnCamera(); return true; }
            return false;
        }
    }
}
