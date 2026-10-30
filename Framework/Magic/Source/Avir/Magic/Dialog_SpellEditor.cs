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
        private int section;                    // -1: the centre
        private RuneDef shown, hovered;
        // the rune chosen on the circles: its circle, its section (-1: the centre element) and place
        private SpellCircle pickC;
        private int pickSec = -1, pickK = -1;
        private float lastWrite;
        private SpellCircle focus;
        private Vector2 outlineScroll;
        private int tab;                        // 0 all, 1 elements, 2 verbs, 3 forms, 4 modifiers and flow
        private Vector2 leftScroll;
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
                s.weave = s.rings.Any(r => r.runes.Any()) ? SpellCircle.FromRings(s) : NewCircle();
                s.rings.Clear();
            }
            sel = s.weave;
            section = -1;
            view.max = 80f;
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            forcePause = true;
        }

        private static SpellCircle NewCircle() => new SpellCircle { sections = { new RitualRing { slots = 2 } } };

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

        private static readonly string[] Tabs = { "All", "Elements", "Verbs", "Forms", "Modifiers", "Spells" };

        private static bool ForSpells(RuneDef r)
        {
            if (!(r.Unlocked || Prefs.DevMode)) return false;
            switch (r.role)
            {
                case RuneRole.Element: case RuneRole.Form: return r.magic;
                case RuneRole.Verb: return Spells.Made(r.kind);
                case RuneRole.Modifier: return true;
                case RuneRole.Flow: return r.kind == RuneKind.Barrier;
            }
            return false;
        }

        private List<RuneDef> Shown()
        {
            var all = RuneDef.All.Where(ForSpells);
            switch (tab)
            {
                case 1: return all.Where(r => r.role == RuneRole.Element).ToList();
                case 2: return all.Where(r => r.role == RuneRole.Verb).ToList();
                case 3: return all.Where(r => r.role == RuneRole.Form).ToList();
                case 4: return all.Where(r => r.role == RuneRole.Modifier || r.role == RuneRole.Flow).ToList();
            }
            return all.ToList();
        }

        private void DrawLeft(Rect r)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 32f), "Runes");
            Text.Font = GameFont.Small; GUI.color = InkSoft;
            ArtInk.Label(new Rect(r.x, r.y + 32f, r.width, 44f), tab == 5 ? "The spells of this mage's book: click one to open it here." : "Choose a circle or a section, then click a rune to write it there. Point at a rune to read it.");
            // the tabs, in two rows so they read
            float tx = r.x, ty = r.y + 80f;
            for (int i = 0; i < Tabs.Length; i++)
            {
                float w = (r.width - 8f) / 3f;
                if (Button(new Rect(r.x + (i % 3) * (w + 4f), ty + (i / 3) * 32f, w, 28f), Tabs[i], true, tab == i)) { tab = i; leftScroll = Vector2.zero; }
            }
            const float cardH = 290f, encH = 34f;
            Rect list = new Rect(r.x, r.y + 148f, r.width, r.height - 148f - cardH - encH - 12f);
            if (tab == 5)
            {
                DrawSpellList(new Rect(r.x, list.y, r.width, r.height - 148f - encH - 6f));
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
                Glyph(new Rect(row.x + 2f, row.y + 3f, 40f, 40f), rune);
                GUI.color = Ink;
                ArtInk.Label(new Rect(row.x + 48f, row.y + 3f, row.width - 50f, 22f), rune.LabelCap);
                GUI.color = InkSoft;
                ArtInk.Label(new Rect(row.x + 48f, row.y + 23f, row.width - 50f, 22f), Role(rune));
                Text.Font = GameFont.Small;
                // one click writes into the chosen place (the second click of a double one writes nothing more)
                if (Widgets.ButtonInvisible(row) && Time.realtimeSinceStartup - lastWrite > 0.35f) { Write(rune); lastWrite = Time.realtimeSinceStartup; }
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
            List<RitualSchema> book = mage != null ? Mages.Core(mage)?.Book.spells : null;
            if (book == null || book.Count == 0)
            {
                GUI.color = InkSoft;
                ArtInk.Label(r, mage == null ? "No mage: the book belongs to a mage." : "The book is empty yet.");
                return;
            }
            const float h = 70f;
            Rect view = new Rect(0f, 0f, r.width - 16f, book.Count * (h + 4f));
            RitualScroll.Begin(r, ref spellScroll, view);
            float y = 0f;
            foreach (RitualSchema sp in book.ToList())
            {
                Rect row = new Rect(0f, y, view.width, h);
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
                    s.weave = sp.weave != null ? sp.weave.Copy() : SpellCircle.FromRings(sp);
                    sel = s.weave; section = -1; focus = s.weave;
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
                bool made = el == "Fire";
                GUI.color = made ? Ink : InkSoft;
                ArtInk.Label(new Rect(c.x + 26f, y, c.width - 26f, 22f), el + " - " + (made ? FireLine(rune) : "not made yet"));
                y += 22f;
            }
            Text.Font = GameFont.Small;
        }

        /// <summary>What a rune does with fire, in a few words.</summary>
        internal static string FireLine(RuneDef r)
        {
            switch (r.kind)
            {
                case RuneKind.Fire: return "the circle burns";
                case RuneKind.Release: return "sets fire; amplified - explodes";
                case RuneKind.Hold: return "held fire: never spreads, warms friends";
                case RuneKind.Drain: return "takes the heat, keeps it";
                case RuneKind.Lift: return "tall flames, a pillar or a wall";
                case RuneKind.Growth: return "fire spreads faster and further";
                case RuneKind.Purge: return "puts fire out, burns infection";
                case RuneKind.Aura: return "all round the mage";
                case RuneKind.Beam: return "a line to the target";
                case RuneKind.Shape: return "a wall across the target";
                case RuneKind.Step: return "a leap with a trail of fire";
                case RuneKind.Restore: return "cautery of friends";
                case RuneKind.Enchant: return "a burning weapon";
                case RuneKind.Summon: return "a fire wisp";
                case RuneKind.Sense: return "warm bodies through walls";
                case RuneKind.Barrier: return "a ward fire cannot pass";
                case RuneKind.Amplify: return "stronger; with release - explosion";
                case RuneKind.Breadth: return "wider";
                case RuneKind.Reach: return "further (+5% cast)";
                case RuneKind.Anchor: return "purged ground lasts longer";
                default: return SpellCircle.GreatElement(r) ? "wraps it (see the effect)" : "-";
            }
        }

        /// <summary>An element into the chosen circle's centre; anything else into its chosen section (a full one grows a slot).</summary>
        private void Write(RuneDef rune)
        {
            shown = rune;
            if (rune.role == RuneRole.Element)
            {
                if (sel.inner.Count > 0 && !SpellCircle.GreatElement(rune))
                {
                    Messages.Message("A small element cannot wrap circles - only Life, Light, Time, Space or Darkness can.", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                if (SpellCircle.GreatElement(rune) && !sel.Great && s.weave.GreatCount >= SpellCircle.MaxGreat)
                {
                    Messages.Message("A spell holds at most " + SpellCircle.MaxGreat + " great circles.", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                sel.element = rune;
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                return;
            }
            if (sel.sections.Count == 0) sel.sections.Add(new RitualRing { slots = 1 });
            int i = section < 0 ? 0 : Mathf.Min(section, sel.sections.Count - 1);
            RitualRing ring = sel.sections[i];
            if (ring.runes.Count >= ring.Slots)
            {
                if (ring.Slots >= SpellCircle.MaxSlots) { Messages.Message("Section " + (i + 1) + " is full.", MessageTypeDefOf.RejectInput, false); return; }
                ring.slots = ring.Slots + 1;
            }
            ring.runes.Add(rune);
            section = i;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        // ================================================================== middle: the circles

        private class Node
        {
            public SpellCircle c;
            public float R, core;             // outer radius; for a small circle its centre plate, for a great one the space it wraps
            public List<Node> kids = new List<Node>();
            public List<Vector2> at = new List<Vector2>();
        }

        private const float Core = 34f, Band = 40f, GreatBand = 46f, Inside = 0.90f;

        private static Node Measure(SpellCircle c)
        {
            var n = new Node { c = c };
            int secs = Mathf.Max(1, c.sections.Count);
            if (c.inner.Count == 0)
            {
                n.core = Core;
                n.R = (Core + secs * Band) / Inside;
                return n;
            }
            foreach (SpellCircle k in c.inner) n.kids.Add(Measure(k));
            float rmax = n.kids.Max(k => k.R) * 1.07f;
            int m = n.kids.Count;
            float d = m == 1 ? 0f : rmax / Mathf.Sin(Mathf.PI / m) + 4f;
            for (int i = 0; i < m; i++)
            {
                float a = (-90f + 360f * i / m) * Mathf.Deg2Rad;
                n.at.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d);
            }
            n.core = d + rmax + 8f;
            n.R = (n.core + c.sections.Count * GreatBand) / Inside;
            return n;
        }

        private static Texture2D bg, frame, disc, outline, thin, dashed, thick;
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

        private static Color ColorOf(SpellCircle c) => c.element?.color ?? new Color(0.85f, 0.78f, 0.62f);
        private static Color Light(Color c, float k) => new Color(c.r + (1f - c.r) * k, c.g + (1f - c.g) * k, c.b + (1f - c.b) * k, 1f);
        private static Color Dark(Color c) => new Color(c.r * 0.28f, c.g * 0.28f, c.b * 0.28f, 0.92f);

        private struct Hit { public SpellCircle c; public int section; public RitualRing ring; public int rune; }
        private readonly List<KeyValuePair<Rect, Hit>> plates = new List<KeyValuePair<Rect, Hit>>();
        private readonly List<KeyValuePair<Node, Vector2>> placed = new List<KeyValuePair<Node, Vector2>>();
        private float scale;

        private void DrawMiddle(Rect r)
        {
            // name
            // the spell's sign, and the button that opens its editor
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
            float fit = Mathf.Min(area.width, area.height) * 0.47f / (root.R * 1.16f);
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
            Rect outlineR = new Rect(area.x + 8f, area.y + 30f, Mathf.Min(280f, area.width * 0.32f), Mathf.Min(area.height * 0.6f, 28f + 24f * s.weave.All().Count()));
            if (!Mouse.IsOver(outlineR)) view.Handle(area, area.center, root.R * 1.16f * fit);
            scale = fit * view.zoom;

            GUI.BeginGroup(area);
            Rect local = new Rect(0f, 0f, area.width, area.height);
            GUI.color = Color.white;
            Texture2D sky = Tex(ref bg, "UI/Avir/Herald/SongTab_Bg");
            if (sky != null) GUI.DrawTexture(local, sky, ScaleMode.ScaleAndCrop); else Widgets.DrawBoxSolid(local, new Color(0.08f, 0.07f, 0.14f));
            Vector2 c = local.center + view.pan;
            plates.Clear(); placed.Clear();
            DrawAt(c, root.R * 1.14f * scale, Tex(ref frame, "UI/Avir/Herald/PathRing"), new Color(ColorOf(root.c).r, ColorOf(root.c).g, ColorOf(root.c).b, 0.4f));
            DrawNode(root, c);
            Rect outlineLocal = new Rect(outlineR.x - area.x, outlineR.y - area.y, outlineR.width, outlineR.height);
            DrawOutline(outlineLocal);
            if (!Mouse.IsOver(outlineLocal)) HandleClicks(local);
            GUI.color = new Color(0.94f, 0.9f, 0.82f, 0.9f); Text.Font = GameFont.Small;
            Widgets.Label(new Rect(10f, local.height - 26f, local.width - 20f, 24f), "wheel - zoom · drag - move · click a circle or a section to choose it · double-click a circle to bring it close · click a rune of the chosen section to rub it out");
            Widgets.Label(new Rect(10f, 4f, local.width - 20f, 24f), Chosen());
            Text.Font = GameFont.Small;
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

        private static string NameOf(SpellCircle c) =>
            (c.Great ? "Great: " : "") + (c.element != null ? c.element.LabelCap.ToString() : "empty circle") + (c.inner.Count > 0 ? " (" + c.inner.Count + " inside)" : "");

        /// <summary>Every circle of the spell as a list: click to choose it, double-click to bring it close.</summary>
        private void DrawOutline(Rect r)
        {
            var rows = new List<KeyValuePair<SpellCircle, int>>();
            void Walk(SpellCircle c, int depth) { rows.Add(new KeyValuePair<SpellCircle, int>(c, depth)); foreach (SpellCircle k in c.inner) Walk(k, depth + 1); }
            Walk(s.weave, 0);
            Widgets.DrawBoxSolid(r, new Color(0.05f, 0.04f, 0.10f, 0.72f));
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.6f); Widgets.DrawBox(r, 1);
            Text.Font = GameFont.Small; GUI.color = new Color(0.95f, 0.88f, 0.7f);
            Widgets.Label(new Rect(r.x + 6f, r.y + 3f, r.width - 12f, 20f), "Circles: " + s.weave.GreatCount + "/" + SpellCircle.MaxGreat + " great · " + s.weave.SmallCount + "/" + SpellCircle.MaxSmall + " small");
            Rect list = new Rect(r.x, r.y + 26f, r.width, r.height - 28f);
            Rect inner = new Rect(0f, 0f, list.width - 16f, rows.Count * 24f);
            Widgets.BeginScrollView(list, ref outlineScroll, inner);
            float y = 0f;
            foreach (var kv in rows)
            {
                SpellCircle c = kv.Key;
                Rect row = new Rect(0f, y, inner.width, 24f);
                if (c == sel) Widgets.DrawBoxSolid(row, new Color(Gold.r, Gold.g, Gold.b, 0.35f));
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.12f));
                Rect g = new Rect(row.x + 4f + 14f * kv.Value, row.y + 2f, 18f, 18f);
                if (c.element != null) { GUI.color = Light(c.element.color, 0.2f); GUI.DrawTexture(g, c.element.Glow, ScaleMode.ScaleToFit); }
                else { GUI.color = new Color(1f, 1f, 1f, 0.5f); GUI.DrawTexture(g, Ring(ref dashed, 0.88f, true)); }
                GUI.color = c == sel ? Color.white : new Color(0.92f, 0.86f, 0.74f);
                Widgets.Label(new Rect(g.xMax + 4f, row.y + 1f, row.width - g.xMax - 4f, 24f), NameOf(c));
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && Mouse.IsOver(row))
                {
                    if (Event.current.clickCount == 2) focus = c;
                    sel = c; section = -1;
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    Event.current.Use();
                }
                y += 24f;
            }
            Widgets.EndScrollView();
            Text.Font = GameFont.Small; GUI.color = Color.white;
        }

        private string Chosen()
        {
            string name = sel.element != null ? sel.element.LabelCap.ToString() : "an empty circle";
            return "Chosen: " + name + " · " + (section < 0 ? "its centre" : "section " + (section + 1));
        }

        private void DrawNode(Node n, Vector2 c)
        {
            placed.Add(new KeyValuePair<Node, Vector2>(n, c));
            Color col = ColorOf(n.c);
            float R = n.R * scale;
            DrawAt(c, R * 1.02f, disc ?? Ring(ref disc, 0f), new Color(col.r, col.g, col.b, 0.06f));
            Texture2D rim = RitualTex.Rim;
            DrawAt(c, R / 0.94f, rim, col);
            if (n.c == sel)
            {
                // the chosen circle: a soft gold wash, a firm gold rim, and a halo that breathes
                float pulse = 0.55f + 0.35f * Mathf.Sin(Time.realtimeSinceStartup * 4f);
                DrawAt(c, R * 1.02f, Ring(ref disc, 0f), new Color(Gold.r, Gold.g, Gold.b, 0.10f));
                DrawAt(c, R * 1.09f, Ring(ref thick, 0.96f), new Color(Gold.r, Gold.g, Gold.b, 1f));
                DrawAt(c, R * 1.14f, Ring(ref thick, 0.96f), new Color(1f, 0.8f, 0.3f, pulse));
            }
            // inside: the wrapped circles, or the centre plate
            if (n.kids.Count > 0)
            {
                for (int i = 0; i < n.kids.Count; i++) DrawNode(n.kids[i], c + n.at[i] * scale);
                // its element sits as a seal on the top of its rim
                float sr = Mathf.Clamp(GreatBand * 0.42f * scale, 6f, 40f);
                Plate(c + new Vector2(0f, -R), sr, col, n.c.element, new Hit { c = n.c, section = -1, rune = -1 });
            }
            else Plate(c, n.core * 0.78f * scale, col, n.c.element, new Hit { c = n.c, section = -1, rune = -1 });
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
                    Plate(p, pr, col, k < ring.runes.Count ? ring.runes[k] : null, new Hit { c = n.c, section = i, ring = ring, rune = k < ring.runes.Count ? k : -1 });
                }
            }
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

        private void Plate(Vector2 p, float r, Color col, RuneDef rune, Hit hit)
        {
            Rect box = new Rect(p.x - r, p.y - r, r * 2f, r * 2f);
            if (rune != null && hit.c == pickC && hit.section == pickSec && (hit.section < 0 || hit.rune == pickK)) RitualPick.Mark(box);
            if (rune != null)
            {
                DrawAt(p, r, Ring(ref disc, 0f), Dark(col));
                DrawAt(p, r, Ring(ref outline, 0.86f), Light(col, 0.35f));
                GUI.color = rune.role == RuneRole.Element ? Light(rune.color, 0.25f) : Light(col, 0.3f);
                GUI.DrawTexture(box.ContractedBy(r * 0.22f), rune.Glow, ScaleMode.ScaleToFit);
                if (Mouse.IsOver(box)) { DrawAt(p, r, Ring(ref disc, 0f), new Color(0.8f, 0.2f, 0.15f, 0.35f)); TooltipHandler.TipRegion(box, rune.LabelCap + "\n\nClick to choose it; click it again (or press Delete) to rub it out."); }
            }
            else DrawAt(p, r * 0.92f, Ring(ref dashed, 0.88f, true), Light(col, 0.4f));
            plates.Add(new KeyValuePair<Rect, Hit>(box, hit));
            GUI.color = Color.white;
        }

        private void HandleClicks(Rect local)
        {
            Event e = Event.current;
            // a double click brings the circle under the mouse close
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && Mouse.IsOver(local))
            {
                for (int i = placed.Count - 1; i >= 0; i--)
                    if ((e.mousePosition - placed[i].Value).magnitude / scale <= placed[i].Key.R) { focus = placed[i].Key.c; sel = focus; e.Use(); return; }
            }
            if (e.type != EventType.MouseUp || e.button != 0 || view.dragged || !Mouse.IsOver(local)) return;
            Vector2 m = e.mousePosition;
            // a rune plate first (the innermost drawn last): one click chooses it, a second rubs it out
            for (int i = plates.Count - 1; i >= 0; i--)
            {
                if (!plates[i].Key.Contains(m)) continue;
                Hit h = plates[i].Value;
                sel = h.c; section = h.section;
                bool hasRune = h.section < 0 ? h.c.element != null : h.ring != null && h.rune >= 0;
                bool already = hasRune && pickC == h.c && pickSec == h.section && (h.section < 0 || pickK == h.rune);
                if (already) RubPicked();
                else if (hasRune) { pickC = h.c; pickSec = h.section; pickK = h.rune; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                else { ClearPick(); SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); }
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
                section = d < n.core ? -1 : Mathf.Clamp(Mathf.FloorToInt((d - n.core) / band), 0, Mathf.Max(0, n.c.sections.Count - 1));
                if (n.c.sections.Count == 0) section = -1;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                e.Use();
                return;
            }
        }

        // ================================================================== under the circles

        private int Wraps()
        {
            int n = 0;
            SpellCircle c = s.weave;
            while (c.Great && c.inner.Count == 1 && n < 99) { n++; c = c.inner[0]; }
            return n;
        }

        private void DrawControls(Rect r)
        {
            float half = (r.width - 12f) / 2f, y = r.y;
            int wraps = Wraps();
            int greats = s.weave.GreatCount, smalls = s.weave.SmallCount;
            bool roomGreat = greats < SpellCircle.MaxGreat;
            Stepper(new Rect(r.x, y, half, 30f), "Great circles round it", wraps, SpellCircle.MaxWraps, 0,
                () => { if (s.weave.inner.Count == 1) { s.weave = s.weave.inner[0]; sel = s.weave; section = -1; } },
                () => { var g = new SpellCircle { sections = { new RitualRing { slots = 3 } } }; g.inner.Add(s.weave); s.weave = g; sel = g; section = -1; }, roomGreat);
            // a circle may take one more inside while the whole spell has room: a small one more, and a great one more if this one becomes great
            bool canHold = (sel.element == null || SpellCircle.GreatElement(sel.element)) && smalls < SpellCircle.MaxSmall + (sel.Great ? 0 : 1)
                           && (sel.Great || roomGreat) && sel.inner.Count < SpellCircle.MaxInner;
            Stepper(new Rect(r.x + half + 12f, y, half, 30f), "Circles inside", sel.inner.Count, SpellCircle.MaxInner, 0,
                () => { sel.inner.RemoveAt(sel.inner.Count - 1); },
                () => { var k = NewCircle(); sel.inner.Add(k); }, canHold);
            if (!s.weave.All().Contains(sel)) { sel = s.weave; section = -1; }
            y += 36f;
            string name = sel.element != null ? sel.element.label : "circle";
            Stepper(new Rect(r.x, y, half, 30f), "Sections in " + name, sel.sections.Count, sel.MaxSections, sel.inner.Count > 0 ? 0 : 1,
                () => { sel.sections.RemoveAt(sel.sections.Count - 1); section = Mathf.Min(section, sel.sections.Count - 1); },
                () => { sel.sections.Add(new RitualRing { slots = 2 }); section = sel.sections.Count - 1; });
            if (section >= 0 && section < sel.sections.Count)
            {
                RitualRing cur = sel.sections[section];
                Stepper(new Rect(r.x + half + 12f, y, half, 30f), "Slots in section " + (section + 1), cur.Slots, SpellCircle.MaxSlots, 1,
                    () => { cur.slots = cur.Slots - 1; if (cur.runes.Count > cur.slots) cur.runes.RemoveAt(cur.runes.Count - 1); },
                    () => cur.slots = cur.Slots + 1);
            }
            else { GUI.color = InkSoft; Text.Anchor = TextAnchor.MiddleCenter; ArtInk.Label(new Rect(r.x + half + 12f, y, half, 30f), "choose a section for its slots"); Text.Anchor = TextAnchor.UpperLeft; }
            y += 36f;
            // where a clicked rune goes: the centre (its element) or one of the sections
            GUI.color = Ink; Text.Anchor = TextAnchor.MiddleLeft;
            ArtInk.Label(new Rect(r.x, y, 90f, 30f), "Write into:");
            Text.Anchor = TextAnchor.UpperLeft;
            float cx = r.x + 92f;
            if (Button(new Rect(cx, y, 70f, 30f), "Centre", true, section < 0)) section = -1;
            cx += 74f;
            for (int i = 0; i < sel.sections.Count; i++)
            {
                if (Button(new Rect(cx, y, 84f, 30f), "Section " + (i + 1), true, section == i)) section = i;
                cx += 88f;
            }
            Text.Font = GameFont.Small;
            y += 38f;
            float w = (r.width - 16f) / 3f;
            if (Button(new Rect(r.x, y, w, 34f), "Rub out all", true)) { s.weave = NewCircle(); sel = s.weave; section = -1; }
            if (Button(new Rect(r.x + w + 8f, y, w, 34f), "Bring the chosen circle close", true)) focus = sel;
            if (Button(new Rect(r.x + 2f * (w + 8f), y, w, 34f), "Reset the view", view.Changed)) view.Reset();
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

        private void DrawRight(Rect r)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(r.x, r.y, r.width, 32f), "Codex");
            Text.Font = GameFont.Small;
            SpellEffect fx = SpellText.Explain(s);
            float ph = EffectPlaque.Height(fx, r.width);
            EffectPlaque.Draw(new Rect(r.x, r.y + 40f, r.width, ph), fx, Spells.ColorOf(s));
            float y = r.y + 40f + ph + 10f;
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

        private string WhyNot()
        {
            var all = s.weave.All().ToList();
            if (all.All(c => c.element == null)) return "no element";
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
            var all = root.All().ToList();
            var smalls = all.Where(c => !c.Great).ToList();
            if (all.All(c => c.element == null && !c.OwnRunes.Any())) return "Start in the centre. Which element? Fire, for now - the others are still being worked out.";
            if (sel.element == null) return "This circle has no heart yet. Give it an element - double-click one on the left.";
            if (all.Count >= 10) return "So many circles. Whoever holds this - I would like to meet them.";
            if (smalls.Any(c => c.element != null && !c.OwnRunes.Any())) return "A circle of " + smalls.First(c => c.element != null && !c.OwnRunes.Any()).element.label + " with nothing to do. Give it a verb - release, hold, drain...";
            var same = smalls.Where(c => c.element != null).GroupBy(c => c.element).FirstOrDefault(g => g.Count() >= 3);
            if (WeaveSpell.Steam(root)) return "Fire and water in one weave? You will boil them before they burn.";
            if (same != null) return same.Count() + " circles of " + same.Key.label + ". They will sing together - and burn together.";
            if (WeaveSpell.Wraps(root, RuneKind.Life)) return "Life round the fire: it burns whom it must, and spares whom it should.";
            SpellCircle hollow = all.FirstOrDefault(c => c.Great && c.inner.Count == 0 && !c.OwnRunes.Any());
            if (hollow != null) return "A great circle with nothing in it and nothing of its own. What is it for?";
            for (int i = 0; i < sel.sections.Count; i++)
                if (sel.sections[i].runes.Count < sel.sections[i].Slots) return "An empty place in section " + (i + 1) + ". Fill it, or take the place away.";
            return "Good. Read the main effect before you keep it.";
        }

        // ================================================================== helpers

        private static void Glyph(Rect g, RuneDef rune) => SpellGlyph.Draw(g, rune);

        private void ClearPick() { pickC = null; pickSec = -1; pickK = -1; }

        /// <summary>The rune chosen on the circles, if it is still there.</summary>
        private RuneDef Picked()
        {
            if (pickC == null || !s.weave.All().Contains(pickC)) return null;
            if (pickSec < 0) return pickC.element;
            if (pickSec >= pickC.sections.Count || pickK < 0 || pickK >= pickC.sections[pickSec].runes.Count) return null;
            return pickC.sections[pickSec].runes[pickK];
        }

        private string PickedWhere() => (pickC?.element != null ? "Circle of " + pickC.element.label : "An empty circle") + " · " + (pickSec < 0 ? "its centre" : "section " + (pickSec + 1) + ", place " + (pickK + 1));

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
