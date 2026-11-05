using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Ritualistics
{
    /// <summary>Shared pieces of the ritual windows: the parchment button and a small drawing of a ritual.</summary>
    public static class RitualUI
    {
        public static bool Button(Rect r, string label, bool enabled = true, bool chosen = false)
        {
            bool over = enabled && Mouse.IsOver(r);
            Texture2D tex = chosen ? FlowStyle.ButtonPressed : !over ? FlowStyle.ButtonNormal : Input.GetMouseButton(0) ? FlowStyle.ButtonPressed : FlowStyle.ButtonHover;
            GUI.color = enabled || chosen ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            GUI.color = enabled || chosen ? FlowStyle.ButtonText : FlowStyle.InkGrey;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (enabled && Widgets.ButtonInvisible(r)) { SoundDefOf.Click.PlayOneShotOnCamera(); return true; }
            return false;
        }

        public static void Ring(Vector2 c, float rad) =>
            GUI.DrawTexture(new Rect(c.x - rad, c.y - rad, rad * 2f, rad * 2f), RitualTex.Ring, ScaleMode.StretchToFill);

        /// <summary>A card: a light plate with an inked edge; brighter under the mouse, gold when chosen.</summary>
        public static void Card(Rect r, bool chosen)
        {
            bool over = Mouse.IsOver(r);
            Widgets.DrawBoxSolid(r, FlowStyle.Light ? new Color(0.10f, 0.02f, 0.16f, over ? 0.75f : 0.6f) : new Color(1f, 0.95f, 0.82f, over ? 0.42f : 0.26f));
            GUI.color = chosen ? FlowStyle.Gold : new Color(FlowStyle.InkSoft.r, FlowStyle.InkSoft.g, FlowStyle.InkSoft.b, over ? 0.8f : 0.45f);
            Widgets.DrawBox(r, chosen ? 2 : 1);
            GUI.color = Color.white;
        }

        /// <summary>A ritual drawn small: its rings, slot frames and runes - for lists and cards.</summary>
        public static void Preview(Rect r, RitualSchema s)
        {
            Vector2 c = r.center;
            float R = Mathf.Min(r.width, r.height) / 2f * 0.94f, core = R * 0.28f;
            int n = Mathf.Max(1, s.rings.Count);
            Color ink = FlowStyle.InkSoft;
            GUI.color = new Color(ink.r, ink.g, ink.b, 0.8f);
            Ring(c, R);
            for (int i = 0; i < n; i++) Ring(c, core + (R - core) * i / n);
            for (int i = 0; i < s.rings.Count; i++)
            {
                List<RuneDef> runes = s.rings[i].runes;
                int per = s.rings[i].Slots;
                float inner = core + (R - core) * i / n, outer = core + (R - core) * (i + 1) / n, mid = (inner + outer) / 2f;
                float size = RitualOrnament.GlyphSize(inner, outer, per, 90f) * 0.8f;
                for (int k = 0; k < runes.Count && k < per; k++)
                {
                    Rect g = RitualOrnament.GlyphRect(c, mid, RitualOrnament.SlotAngle(i, k, per), size);
                    FlowStyle.Backing(g);
                    GUI.color = Color.white;
                    GUI.DrawTexture(g, FlowStyle.Pick(runes[k].Ink, runes[k].Glow), ScaleMode.ScaleToFit);
                }
            }
            // component circles as small dots with their things
            int nc = s.components.Count;
            for (int k = 0; k < nc; k++)
            {
                float ang = (-90f + 360f * k / nc) * Mathf.Deg2Rad;
                Vector2 p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R * 1.0f;
                float cr = Mathf.Max(6f, R * 0.12f);
                GUI.color = s.components[k].kind == ComponentKind.Seal ? FlowStyle.Gold : new Color(ink.r, ink.g, ink.b, 0.8f);
                Ring(p, cr);
                Texture2D icon = s.components[k].Icon;
                if (icon != null) { GUI.color = Color.white; GUI.DrawTexture(new Rect(p.x - cr * 0.7f, p.y - cr * 0.7f, cr * 1.4f, cr * 1.4f), icon, ScaleMode.ScaleToFit); }
            }
            GUI.color = Color.white;
        }
    }

    // =====================================================================================
    // A COMPONENT CIRCLE, chosen in its own window: what kind of circle it is (each explained), and for a
    // seal or a material, which one - from a grid of the allowed metals and jade with their icons.
    // =====================================================================================
    public class Dialog_RitualComponent : Window
    {
        private readonly RitualComponent comp;
        private readonly RitualSchema schema;
        private readonly int index;
        private readonly Map map;
        private ComponentKind kind;
        private ThingDef thing;
        private int amount;
        private string amountBuf;
        private Vector2 scroll;

        private static readonly string[] KindNames = { "Empty", "Material", "Seal", "Psychic crystals", "Avir feathers", "Crystal battery", "Artifact" };
        private static string KindText(ComponentKind k)
        {
            switch (k)
            {
                case ComponentKind.Seal: return "What the ritual makes. A transmutation turns material into this, by value. Only the first seal is read. Nothing is laid here - the seal is drawn.";
                case ComponentKind.Material: return "What the ritual works on. A transmutation takes only this - from the main circle and from this circle's cell. Without any material circle it takes any metal or jade in the main circle.";
                case ComponentKind.Crystal: return "Fuel. Lay crystals of psychic energy on this circle's cell: one breaks open whenever the ritual runs low, 150 energy each.";
                case ComponentKind.Feathers: return "Fuel. Lay Avir feathers on this circle's cell: ten at a time burn into the ritual, 6 energy each - twice as much for a filling ritual, for the wings' energy goes most readily back into artifacts.";
                case ComponentKind.Battery: return "Fuel. Lay a crystal battery on this circle's cell: once crystals and feathers are spent, the ritual drinks its charge.";
                case ComponentKind.Artifact: return "Strength. Lay an artifact on this circle's cell: while it holds charge the whole ritual is stronger - +15%, and +5% more for every step of its heart's quality. The ritual slowly drinks its charge.";
                default: return "Nothing drawn in this circle. It stays part of the pattern, but does nothing.";
            }
        }

        public Dialog_RitualComponent(RitualSchema schema, int index, Map map)
        {
            this.schema = schema;
            this.index = index;
            this.map = map;
            comp = schema.components[index];
            kind = comp.kind;
            thing = comp.thing;
            amount = Mathf.Max(1, comp.amount);
            amountBuf = amount.ToString();
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            forcePause = true;
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(900f, Verse.UI.screenWidth - 60f), Mathf.Min(640f, Verse.UI.screenHeight - 60f));

        public override void DoWindowContents(Rect box)
        {
            FlowStyle.Begin(map);
            Rect r = ParchmentFrame.Draw(box).ContractedBy(8f);
            Text.Font = GameFont.Medium; GUI.color = FlowStyle.Ink;
            Widgets.Label(new Rect(r.x, r.y, r.width, 34f), "Component circle " + (index + 1));
            Text.Font = GameFont.Small;

            // left: the four kinds, each a card with its glyph and meaning
            float lw = 270f, y = r.y + 42f;
            ComponentKind[] kinds = { ComponentKind.Seal, ComponentKind.Material, ComponentKind.Crystal, ComponentKind.Feathers, ComponentKind.Battery, ComponentKind.Artifact, ComponentKind.None };
            float kh = Mathf.Min(96f, (r.height - 42f - 56f) / kinds.Length - 6f);
            foreach (ComponentKind k in kinds)
            {
                Rect card = new Rect(r.x, y, lw, kh);
                RitualUI.Card(card, kind == k);
                Texture2D thingIcon = new RitualComponent { kind = k }.Icon;
                Texture2D glyph = k == ComponentKind.Seal ? FlowStyle.Pick(RitualOrnament.SealInk, RitualOrnament.SealGlow)
                    : thingIcon ?? FlowStyle.Pick(RitualOrnament.FrameInk, RitualOrnament.FrameGlow);
                GUI.color = k == ComponentKind.Seal ? FlowStyle.Gold : thingIcon != null ? Color.white : FlowStyle.InkSoft;
                if (k == ComponentKind.None) GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, 0.4f);
                float gs = kh - 16f;
                GUI.DrawTexture(new Rect(card.x + 8f, card.y + 8f, gs, gs), glyph, ScaleMode.ScaleToFit);
                GUI.color = FlowStyle.Ink;
                Widgets.Label(new Rect(card.x + gs + 16f, card.y + (kh - 24f) / 2f, card.width - gs - 20f, 24f), KindNames[(int)k]);
                TooltipHandler.TipRegion(card, KindText(k));
                if (Widgets.ButtonInvisible(card))
                {
                    kind = k;
                    if (k != ComponentKind.Seal && k != ComponentKind.Material) thing = new RitualComponent { kind = k }.Expected;
                    else if (thing == null || !Transmutation.Materials.Contains(thing)) thing = Transmutation.Materials.FirstOrDefault();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
                y += kh + 6f;
            }

            // right: the explanation of the chosen kind, then the grid of materials for a seal or a material
            Rect right = new Rect(r.x + lw + 20f, r.y + 42f, r.width - lw - 20f, r.height - 42f - 56f);
            GUI.color = FlowStyle.InkSoft;
            float th = Text.CalcHeight(KindText(kind), right.width);
            Widgets.Label(new Rect(right.x, right.y, right.width, th), KindText(kind));
            string warn = Warning();
            float gy = right.y + th + 6f;
            if (warn != null)
            {
                GUI.color = new Color(0.65f, 0.15f, 0.12f);
                if (FlowStyle.Light) GUI.color = new Color(1f, 0.6f, 0.55f);
                float wh = Text.CalcHeight(warn, right.width);
                Widgets.Label(new Rect(right.x, gy, right.width, wh), warn);
                gy += wh + 6f;
            }
            if (kind == ComponentKind.Seal || kind == ComponentKind.Material)
            {
                List<ThingDef> mats = Transmutation.Materials;
                const float cell = 104f;
                int cols = Mathf.Max(1, Mathf.FloorToInt((right.width - 16f) / cell));
                float reserve = kind == ComponentKind.Material ? 44f : 0f;   // the row for the amount
                Rect area = new Rect(right.x, gy, right.width, right.yMax - gy - reserve);
                Rect view = new Rect(0f, 0f, area.width - 16f, Mathf.CeilToInt(mats.Count / (float)cols) * (cell + 6f));
                RitualScroll.Begin(area, ref scroll, view);
                for (int i = 0; i < mats.Count; i++)
                {
                    ThingDef d = mats[i];
                    Rect c = new Rect(i % cols * cell, i / cols * (cell + 6f), cell - 6f, cell);
                    RitualUI.Card(c, thing == d);
                    GUI.color = Color.white;
                    Widgets.ThingIcon(new Rect(c.x + (c.width - 48f) / 2f, c.y + 8f, 48f, 48f), d);
                    GUI.color = FlowStyle.Ink;
                    Text.Anchor = TextAnchor.UpperCenter;
                    Widgets.Label(new Rect(c.x + 2f, c.y + 58f, c.width - 4f, 22f), d.LabelCap);
                    Text.Font = GameFont.Tiny; GUI.color = FlowStyle.InkSoft;
                    Widgets.Label(new Rect(c.x + 2f, c.y + 78f, c.width - 4f, 20f), d.BaseMarketValue.ToStringMoney() + " each");
                    Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                    TooltipHandler.TipRegion(c, d.LabelCap + "\n\n" + RitualText.Desc(d));
                    if (Widgets.ButtonInvisible(c)) { thing = d; SoundDefOf.Click.PlayOneShotOnCamera(); }
                }
                Widgets.EndScrollView();
            }
            if (kind == ComponentKind.Material) DrawAmount(new Rect(right.x, right.yMax - 38f, right.width, 34f));

            // bottom: what it is now, and done
            GUI.color = FlowStyle.Ink;
            var now = new RitualComponent { kind = kind, thing = thing, amount = kind == ComponentKind.Material ? amount : 1 };
            Widgets.Label(new Rect(r.x, r.yMax - 40f, r.width - 260f, 30f), "This circle: " + now.Label);
            if (RitualUI.Button(new Rect(r.xMax - 240f, r.yMax - 46f, 240f, 42f), "Done"))
            {
                comp.kind = kind;
                comp.thing = kind == ComponentKind.None ? null : thing;
                comp.amount = kind == ComponentKind.Material ? Mathf.Clamp(amount, 1, Mathf.Max(1, thing?.stackLimit ?? 1)) : 1;
                Close();
            }
            GUI.color = Color.white;
            FlowStyle.End();
        }

        /// <summary>How much material lies in the circle: steps of 1 and 10, or typed; at most one stack of it.</summary>
        private void DrawAmount(Rect row)
        {
            int max = Mathf.Max(1, thing?.stackLimit ?? 1);
            int before = amount;
            amount = Mathf.Clamp(amount, 1, max);
            GUI.color = FlowStyle.Ink;
            Widgets.Label(new Rect(row.x, row.y + 5f, 150f, 24f), "Laid in the circle:");
            float x = row.x + 150f;
            if (RitualUI.Button(new Rect(x, row.y, 46f, 32f), "-10")) amount = Mathf.Max(1, amount - 10);
            if (RitualUI.Button(new Rect(x + 50f, row.y, 36f, 32f), "-")) amount = Mathf.Max(1, amount - 1);
            Widgets.TextFieldNumeric(new Rect(x + 90f, row.y + 1f, 70f, 30f), ref amount, ref amountBuf, 1f, max);
            if (RitualUI.Button(new Rect(x + 164f, row.y, 36f, 32f), "+")) amount = Mathf.Min(max, amount + 1);
            if (RitualUI.Button(new Rect(x + 204f, row.y, 46f, 32f), "+10")) amount = Mathf.Min(max, amount + 10);
            if (RitualUI.Button(new Rect(x + 254f, row.y, 56f, 32f), "max")) amount = max;
            amount = Mathf.Clamp(amount, 1, max);
            if (amount != before) amountBuf = amount.ToString();
            Text.Font = GameFont.Tiny; GUI.color = FlowStyle.InkSoft;
            Widgets.Label(new Rect(x + 316f, row.y + 2f, Mathf.Max(60f, row.xMax - x - 316f), 30f), "1 to " + max + " (one stack). Haulers top it up between runs.");
            Text.Font = GameFont.Small;
        }

        /// <summary>What the rest of the ritual makes of this choice, when it makes nothing of it.</summary>
        private string Warning()
        {
            RitualReading rd = RitualEvaluator.Read(schema);
            bool transmutes = rd.Has(RuneKind.Transmute);
            if ((kind == ComponentKind.Seal || kind == ComponentKind.Material) && !transmutes)
                return "This ritual has no transmutation rune, so seals and materials do nothing in it yet.";
            if (kind == ComponentKind.Seal && schema.components.Where((c, i) => i != index).Any(c => c.kind == ComponentKind.Seal))
                return "Another circle already holds a seal: only the first seal is read.";
            if (kind == ComponentKind.Seal && thing != null && schema.components.Where((c, i) => i != index).Any(c => c.kind == ComponentKind.Material && c.thing == thing))
                return "This seal makes what a material circle names as its input - nothing would change.";
            return null;
        }
    }

    // =====================================================================================
    // LOADING A RITUAL: the book's rituals and your own files as cards with a small drawing of each,
    // the chosen one described on the right.
    // =====================================================================================
    public class Dialog_RitualLoad : Window
    {
        private readonly System.Action<RitualSchema, bool> onLoad;   // schema, fromBook
        private readonly Map map;
        private bool own;
        private List<RitualSchema> files;
        private object chosen;
        private Vector2 scroll, descScroll;

        public Dialog_RitualLoad(Map map, System.Action<RitualSchema, bool> onLoad)
        {
            this.map = map;
            this.onLoad = onLoad;
            files = RitualFiles.LoadAll();
            own = files.Count > 0;
            doWindowBackground = false;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            forcePause = true;
        }

        protected override float Margin => 0f;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(1180f, Verse.UI.screenWidth - 60f), Mathf.Min(720f, Verse.UI.screenHeight - 60f));

        private RitualSchema Schema(object o) => o is RitualPresetDef p ? p.Schema() : o as RitualSchema;

        public override void DoWindowContents(Rect box)
        {
            FlowStyle.Begin(map);
            Rect r = ParchmentFrame.Draw(box).ContractedBy(8f);
            Text.Font = GameFont.Medium; GUI.color = FlowStyle.Ink;
            Widgets.Label(new Rect(r.x, r.y, 300f, 34f), "Load a ritual");
            Text.Font = GameFont.Small;
            if (RitualUI.Button(new Rect(r.x + 320f, r.y, 200f, 34f), "The ritual book", true, !own)) { own = false; chosen = null; }
            if (RitualUI.Button(new Rect(r.x + 530f, r.y, 200f, 34f), "Your rituals (" + files.Count + ")", true, own)) { own = true; chosen = null; }

            List<object> items = own ? files.Cast<object>().ToList() : RitualPresetDef.All.Where(p => p.Available).Cast<object>().ToList();
            float dw = Mathf.Min(360f, r.width * 0.32f);
            Rect grid = new Rect(r.x, r.y + 46f, r.width - dw - 20f, r.height - 46f);
            Rect desc = new Rect(grid.xMax + 20f, r.y + 46f, dw, r.height - 46f);

            const float cw = 168f, ch = 200f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((grid.width - 16f) / (cw + 8f)));
            Rect view = new Rect(0f, 0f, grid.width - 16f, Mathf.CeilToInt(items.Count / (float)cols) * (ch + 8f));
            RitualScroll.Begin(grid, ref scroll, view);
            for (int i = 0; i < items.Count; i++)
            {
                RitualSchema s = Schema(items[i]);
                Rect card = new Rect(i % cols * (cw + 8f), i / cols * (ch + 8f), cw, ch);
                RitualUI.Card(card, chosen == items[i] || (chosen is RitualSchema cs && items[i] is RitualSchema fs && cs.name == fs.name));
                RitualUI.Preview(new Rect(card.x + 14f, card.y + 8f, cw - 28f, cw - 28f), s);
                GUI.color = FlowStyle.Ink;
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(card.x + 4f, card.y + cw - 16f, cw - 8f, 22f), s.name.Truncate(cw - 8f));
                Text.Font = GameFont.Tiny; GUI.color = FlowStyle.InkSoft;
                Widgets.Label(new Rect(card.x + 4f, card.y + cw + 4f, cw - 8f, 20f), RitualCircles.Label(s.size) + " circle - " + s.AllRunes.Count() + " runes");
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(card))
                {
                    if (chosen == items[i] && Event.current.clickCount >= 2) { Load(items[i]); return; }
                    chosen = items[i];
                    SoundDefOf.Click.PlayOneShotOnCamera();
                }
            }
            if (items.Count == 0)
            {
                GUI.color = FlowStyle.InkSoft;
                Widgets.Label(new Rect(0f, 0f, view.width, 60f), own ? "No ritual of your own is saved yet. Write one in the editor and press Save." : "No ritual in the book is open yet - research stream ritualism.");
            }
            Widgets.EndScrollView();

            // the chosen one: its description, then load (and delete for a file)
            if (chosen != null)
            {
                RitualSchema s = Schema(chosen);
                RitualUI.Preview(new Rect(desc.x + (desc.width - 180f) / 2f, desc.y, 180f, 180f), s);
                GUI.color = FlowStyle.Ink; Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(desc.x, desc.y + 186f, desc.width, 32f), s.name);
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                string text = (chosen is RitualPresetDef p ? RitualText.Desc(p) + "\n\n" : "") + RitualEvaluator.Read(s).Plain();
                Rect ta = new Rect(desc.x, desc.y + 222f, desc.width, desc.height - 222f - 96f);
                GUI.color = FlowStyle.InkSoft;
                float th = Text.CalcHeight(text, ta.width - 16f);
                Rect tv = new Rect(0f, 0f, ta.width - 16f, th);
                RitualScroll.Begin(ta, ref descScroll, tv);
                Widgets.Label(tv, text);
                Widgets.EndScrollView();
                if (RitualUI.Button(new Rect(desc.x, desc.yMax - 90f, desc.width, 40f), own ? "Load" : "Load a copy")) { Load(chosen); return; }
                if (own && RitualUI.Button(new Rect(desc.x, desc.yMax - 44f, desc.width, 40f), "Delete the file"))
                {
                    var f = (RitualSchema)chosen;
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Delete the ritual file \"" + f.name + "\"?", () =>
                    {
                        RitualFiles.Delete(f.name);
                        files = RitualFiles.LoadAll();
                        chosen = null;
                    }, true));
                }
            }
            else
            {
                GUI.color = FlowStyle.InkSoft;
                Widgets.Label(new Rect(desc.x, desc.y, desc.width, 80f), "Choose a ritual on the left to read what it does. A double click loads it.");
            }
            GUI.color = Color.white;
            FlowStyle.End();
        }

        private void Load(object o)
        {
            onLoad?.Invoke(Schema(o), o is RitualPresetDef);
            Close();
        }
    }

    /// <summary>A rune chosen on a circle: a gold halo that breathes behind it, and a small card that names it with a
    /// button to rub it out. Shared by the ritual editor and the spell editor.</summary>
    [StaticConstructorOnStartup]
    public static class RitualPick
    {
        private static Texture2D halo;
        private static Texture2D Halo
        {
            get
            {
                if (halo != null) return halo;
                const int n = 128;
                halo = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        // a bright ring near the edge, a soft glow inside
                        float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.86f) / 0.07f);
                        float glow = Mathf.Clamp01(1f - d) * 0.45f;
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(Mathf.Max(ring, glow)) * 255));
                    }
                halo.SetPixels32(px); halo.Apply();
                return halo;
            }
        }

        public static void Mark(Rect glyph)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.realtimeSinceStartup * 4f);
            Color keep = GUI.color;
            GUI.color = new Color(1f, 0.78f, 0.25f, pulse);
            GUI.DrawTexture(glyph.ExpandedBy(glyph.width * 0.42f), Halo, ScaleMode.StretchToFill);
            GUI.color = keep;
        }

        /// <summary>The card of the chosen rune; true when "Rub it out" was pressed.</summary>
        /// <summary>Where the last card stood (screen) - clicks there are the card's, not the circle's beneath.</summary>
        public static Rect LastCard;

        public static bool Card(Rect at, RuneDef rune, string where)
        {
            Text.Font = GameFont.Small;
            string desc = RitualText.Desc(rune);
            float h = 100f + Mathf.Min(80f, Text.CalcHeight(desc, at.width - 20f));
            Rect r = new Rect(at.x, at.y, at.width, h);
            LastCard = r;
            if (RitualArt.HasCard) RitualArt.CardAt(r); else Widgets.DrawBoxSolid(r, new Color(0.95f, 0.88f, 0.7f, 0.95f));
            Rect c = r.ContractedBy(10f);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(c.x, c.y, 32f, 32f), FlowStyle.Pick(rune.Ink, rune.Glow), ScaleMode.ScaleToFit);
            GUI.color = FlowStyle.Ink;
            Widgets.Label(new Rect(c.x + 40f, c.y, c.width - 40f, 24f), rune.LabelCap + " - chosen");
            GUI.color = FlowStyle.InkSoft;
            Widgets.Label(new Rect(c.x + 40f, c.y + 20f, c.width - 40f, 24f), where);
            Widgets.Label(new Rect(c.x, c.y + 40f, c.width, Mathf.Min(80f, Text.CalcHeight(desc, c.width))), desc);
            GUI.color = Color.white;
            bool rub = RitualUI.Button(new Rect(c.xMax - 130f, r.yMax - 36f, 130f, 28f), "Rub it out");
            return rub;
        }
    }
}
