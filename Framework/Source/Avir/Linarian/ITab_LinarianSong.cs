using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Linarian
{
    // =====================================================================================
    // THE SONG - the Singer's own tab on the pawn's panel, where others have their artifacts (she can carry none).
    //   Top: her Song - points, strength, the way to the next point.
    //   Middle: the network as a night sky - every stone a star, lit as she opens it; the wheel zooms, a drag moves it.
    //   Right: the chosen star's page (its tactic, its gift, opening it, putting it in the chant), or her voices.
    //   Bottom: the chant - five places for the tactics at hand (one button each below), and its presets.
    // The art is soft: while it is not drawn, the sky, the stars and the frame are painted here.
    //   UI/Avir/Herald/SongTab_Bg                         - the sky behind everything
    //   UI/Avir/Herald/Star_Locked, Star_Ready, Star_Lit   - the three stars
    //   UI/Avir/Herald/ChantSlot                          - the frame of a place of the chant
    // =====================================================================================
    [StaticConstructorOnStartup]
    public static class LinarianSongTab
    {
        static LinarianSongTab()
        {
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (d.race == null || !d.race.Humanlike) continue;
                if (d.inspectorTabs == null) d.inspectorTabs = new List<System.Type>();
                if (d.inspectorTabs.Contains(typeof(ITab_LinarianSong))) continue;
                d.inspectorTabs.Add(typeof(ITab_LinarianSong));
                if (d.inspectorTabsResolved == null) d.inspectorTabsResolved = new List<InspectTabBase>();
                d.inspectorTabsResolved.Add(InspectTabManager.GetSharedInstance(typeof(ITab_LinarianSong)));
            }
        }
    }

    /// <summary>The Aria's tab on the pawn's panel: it opens the Aria over the whole screen.</summary>
    public class ITab_LinarianSong : ITab
    {
        public ITab_LinarianSong()
        {
            size = new Vector2(10f, 10f);
            labelKey = "Avir_TabSong";
        }

        public override bool IsVisible => SelPawn != null && Linarian.Is(SelPawn) && Linarian.Song(SelPawn) != null;

        public override void OnOpen()
        {
            base.OnOpen();
            Pawn p = SelPawn;
            if (p != null && Linarian.Song(p) != null && !Find.WindowStack.IsOpen<Dialog_LinarianAria>())
                Find.WindowStack.Add(new Dialog_LinarianAria(p));
            CloseTab();
        }

        protected override void FillTab() { }
    }

    /// <summary>The Aria, over the whole screen.
    ///   Left: lists - her paths and how far each is lit, every tactic (search and filters), the presets of the chant.
    ///   Middle: the network as a night sky (wheel - closer, drag - move). Below it: the chant, five places.
    ///   Right: her Aria (points, power, the way to the next point) and the chosen star's page, or her voices.</summary>
    public class Dialog_LinarianAria : Window
    {
        private readonly Pawn p;
        private string sel;
        private string selArea;
        private int leftTab = 1;   // 0 paths, 1 tactics, 2 presets
        private bool voicesPage;
        private float zoom = 1f;
        private Vector2 pan;
        private bool dragging;
        private float dragged;
        private int selSlot = -1;
        private string search = "";
        private int filter;        // 0 all, 1 lit, 2 can light, 3 dark, 4 in the chant
        private Vector2 leftScroll, rightScroll;

        private static readonly Color Sky = new Color(0.045f, 0.04f, 0.085f);
        private static readonly Color Pane = new Color(0.05f, 0.045f, 0.11f, 0.78f);
        private static readonly Color Line = new Color(0.62f, 0.55f, 0.88f, 0.6f);
        private static readonly Color Bright = new Color(0.98f, 0.97f, 1f);
        private static readonly Color Soft = new Color(0.88f, 0.86f, 0.97f);
        private static readonly Color Faint = new Color(0.74f, 0.72f, 0.88f);
        private static readonly Color Silver = new Color(0.84f, 0.82f, 0.98f);
        private static readonly Color Lit = new Color(1f, 0.88f, 0.55f);
        private static readonly Color Warm = new Color(0.76f, 0.64f, 1f);

        public static readonly Dictionary<string, Color> AreaColor = new Dictionary<string, Color>
        {
            { "L", new Color(0.62f, 0.76f, 1.00f) }, { "D", new Color(0.78f, 0.66f, 1.00f) }, { "C", new Color(0.50f, 0.95f, 0.88f) },
            { "W", new Color(1.00f, 0.58f, 0.58f) }, { "F", new Color(1.00f, 0.76f, 0.45f) }, { "S", new Color(0.92f, 0.70f, 0.52f) },
            { "G", new Color(0.86f, 0.86f, 0.90f) }, { "P", new Color(0.70f, 0.94f, 0.52f) }, { "T", new Color(0.78f, 0.52f, 1.00f) },
        };

        public Dialog_LinarianAria(Pawn p)
        {
            this.p = p;
            forcePause = true;
            doCloseX = false;
            doWindowBackground = false;
            drawShadow = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            preventCameraMotion = true;
        }

        public override Vector2 InitialSize => new Vector2(Verse.UI.screenWidth, Verse.UI.screenHeight);
        protected override float Margin => 0f;

        protected override void SetInitialSizeAndPosition()
        {
            windowRect = new Rect(0f, 0f, Verse.UI.screenWidth, Verse.UI.screenHeight);
        }

        private Gene_LinarianSong G => Linarian.Song(p);
        private bool Mine => p.Faction == Faction.OfPlayer && p.IsColonistPlayerControlled;

        public override void DoWindowContents(Rect inRect)
        {
            Gene_LinarianSong g = G;
            if (g == null || p.Destroyed) { Close(false); return; }
            windowRect = new Rect(0f, 0f, Verse.UI.screenWidth, Verse.UI.screenHeight);
            Color oc = GUI.color; GameFont of = Text.Font; TextAnchor oa = Text.Anchor;
            try
            {
                Widgets.DrawBoxSolid(inRect, Sky);
                if (SongArt.Bg != null) GUI.DrawTexture(inRect, SongArt.Bg, ScaleMode.StretchToFill);
                else SongArt.Dust(inRect, 0);
                float m = Mathf.Max(28f, inRect.width * 0.022f);
                Rect inner = new Rect(m, m * 0.8f, inRect.width - m * 2f, inRect.height - m * 1.6f);
                Rect head = new Rect(inner.x + 60f, inner.y, inner.width - 120f, 40f);
                float lw = Mathf.Clamp(inner.width * 0.24f, 300f, 440f), rw = Mathf.Clamp(inner.width * 0.24f, 320f, 460f);
                Rect left = new Rect(inner.x, head.yMax + 8f, lw, inner.yMax - head.yMax - 8f);
                Rect right = new Rect(inner.xMax - rw, left.y, rw, left.height);
                Rect mid = new Rect(left.xMax + 12f, left.y, right.x - left.xMax - 24f, left.height);
                DrawHead(head, g);
                DrawLeft(left, g);
                DrawSky(mid, g);
                DrawRight(right, g);
            }
            finally { GUI.color = oc; Text.Font = of; Text.Anchor = oa; }
        }

        // ---------------------------------------------------------------- top

        private void DrawHead(Rect r, Gene_LinarianSong g)
        {
            Text.Font = GameFont.Medium; GUI.color = Bright;
            Widgets.Label(new Rect(r.x, r.y + 4f, r.width - 60f, 34f), p.LabelShortCap + "  -  the Aria" + (g.shadow ? " in the shadow of the Stream" : ""));
            Text.Font = GameFont.Small;
            Rect close = new Rect(r.xMax - 34f, r.y + 4f, 30f, 30f);
            if (DarkButton(close, "X")) Close();
            if (Prefs.DevMode) DrawDev(new Rect(r.x + 520f, r.y + 6f, close.x - r.x - 530f, 26f), G);
            TooltipHandler.TipRegion(close, "Close (Esc)");
        }

        /// <summary>Dev mode only: quick ways to test the Aria.</summary>
        private void DrawDev(Rect r, Gene_LinarianSong g)
        {
            var acts = new List<(string, System.Action)>
            {
                ("Dev: +1 point", () => { if (g.songLevel < Gene_LinarianSong.MaxLevel) { g.songLevel++; g.songXp = 0f; } }),
                ("Dev: +300 xp", () => g.Learn(300f)),
                ("Dev: light all", () => { foreach (LinarianNode n in LinarianNetwork.Nodes) if (!n.Shadow || g.shadow) g.opened.Add(n.key); g.songLevel = Mathf.Max(g.songLevel, g.opened.Count - Gene_LinarianSong.StartPoints); }),
                ("Dev: reset", () => { g.opened.Clear(); g.offered = 0; for (int i = 0; i < Gene_LinarianSong.Slots; i++) g.SetSlot(i, null); }),
                ("Dev: shadow", () => g.shadow = !g.shadow),
                ("Dev: ready", () => { g.ready.Clear(); g.chantSince = Enumerable.Repeat(-999999, Gene_LinarianSong.Slots).ToList(); }),
                ("Dev: +voice", () => g.voices.Add(new SongVoice { name = "Voice " + (g.voices.Count + 1), skill = DefDatabase<SkillDef>.AllDefsListForReading.RandomElement(), level = Rand.RangeInclusive(8, 18), worth = Rand.Chance(0.3f) ? 3f : 1f })),
            };
            float bw = Mathf.Min(120f, (r.width - (acts.Count - 1) * 4f) / acts.Count);
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < acts.Count; i++)
                if (DarkButton(new Rect(r.x + i * (bw + 4f), r.y, bw, r.height), acts[i].Item1)) acts[i].Item2();
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(new Rect(r.x + 5 * (bw + 4f), r.y, bw, r.height), "Clears every cooldown and ends the warming up of the chant.");
            TooltipHandler.TipRegion(new Rect(r.x + 4 * (bw + 4f), r.y, bw, r.height), "Opens or closes the shadow of the Stream.");
        }

        // ---------------------------------------------------------------- left: lists

        private void DrawLeft(Rect r, Gene_LinarianSong g)
        {
            Widgets.DrawBoxSolid(r, Pane);
            GUI.color = Line; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            string[] tabs = { "Paths", "Tactics", "Presets", "Offering" };
            float tw = (r.width - 16f - 12f) / 4f;
            for (int i = 0; i < 4; i++)
                if (DarkButton(new Rect(r.x + 8f + i * (tw + 4f), r.y + 8f, tw, 28f), tabs[i], leftTab == i)) { leftTab = i; leftScroll = Vector2.zero; }
            Rect body = new Rect(r.x + 8f, r.y + 44f, r.width - 16f, r.height - 52f);
            if (leftTab == 0) DrawPaths(body, g);
            else if (leftTab == 1) DrawTactics(body, g);
            else if (leftTab == 2) DrawPresets(body, g);
            else DrawOffering(body, g);
        }

        /// <summary>Points given to the Aria itself: no star, but a wider zone and a stronger song.</summary>
        private void DrawOffering(Rect r, Gene_LinarianSong g)
        {
            float y = r.y;
            Text.Font = GameFont.Small;
            string zone = g.ZoneRadius >= float.MaxValue ? "the whole map" : g.ZoneRadius.ToString("0") + " cells round her";
            string text = "\u201CA star is a gift to me. A point given to the Aria itself is a gift to everything I am.\u201D\n\n"
                + "A point she does not need for a star can be offered to the Aria. It lights nothing - instead her influence reaches further and every tactic grows stronger. "
                + "She need not walk every path: after one, she may deepen what she already has.\n\n"
                + "Offered: " + g.offered + "\n"
                + "Her influence: " + zone + "\n"
                + "Power of the Aria: x" + g.Power.ToString("0.00") + "\n\n"
                + "Every offered point: influence +4 cells, power +6%. An offered point cannot be taken back.";
            float h = Text.CalcHeight(text, r.width);
            GUI.color = Soft;
            Widgets.Label(new Rect(r.x, y, r.width, h), text);
            y += h + 12f;
            if (Mine && DarkButton(new Rect(r.x, y, r.width, 34f), g.PointsFree > 0 ? "Offer a point to the Aria" : "No free point to offer", false, g.PointsFree > 0))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Offer a point to the Aria itself? It will light no star, and it cannot be taken back.", () =>
                {
                    g.Offer();
                    SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                }));
        }

        private IEnumerable<string> Areas(Gene_LinarianSong g) => g.shadow ? LinarianNetwork.Areas.Concat(new[] { "T" }) : LinarianNetwork.Areas;

        private void DrawPaths(Rect r, Gene_LinarianSong g)
        {
            List<string> areas = Areas(g).ToList();
            float w = r.width - 16f;
            Text.Font = GameFont.Small;
            var heights = areas.Select(a => Mathf.Max(64f, 26f + Text.CalcHeight(LinarianNetwork.AreaSummary(a), w - 64f) + 8f)).ToList();
            Widgets.BeginScrollView(r, ref leftScroll, new Rect(0f, 0f, w, heights.Sum() + areas.Count * 6f));
            try
            {
                float y = 0f;
                for (int i = 0; i < areas.Count; i++)
                {
                    string a = areas[i];
                    Rect row = new Rect(0f, y, w, heights[i]);
                    if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.06f));
                    Texture2D em = LinarianTex.Path(a);
                    if (em != null) { GUI.color = Color.white; GUI.DrawTexture(new Rect(2f, y + 6f, 52f, 52f), em, ScaleMode.ScaleToFit); }
                    int lit = LinarianNetwork.Nodes.Count(n => n.area == a && g.Has(n.key));
                    GUI.color = AreaColor[a];
                    Widgets.Label(new Rect(64f, y + 3f, w - 64f - 60f, 24f), LinarianNetwork.AreaLabel(a));
                    Text.Anchor = TextAnchor.UpperRight;
                    GUI.color = lit > 0 ? Lit : Faint;
                    Widgets.Label(new Rect(w - 64f, y + 3f, 60f, 24f), lit + " / 5");
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Soft;
                    Widgets.Label(new Rect(64f, y + 26f, w - 64f, heights[i] - 26f), LinarianNetwork.AreaSummary(a));
                    if (Widgets.ButtonInvisible(row)) { FocusArea(a); SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); }
                    TooltipHandler.TipRegion(row, "Show the path in the sky.");
                    y += heights[i] + 6f;
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        private Rect lastSky;

        /// <summary>Brings a star to the middle of the sky.</summary>
        private void FocusStar(LinarianNode n)
        {
            zoom = Mathf.Max(zoom, 1.8f);
            float s = Mathf.Min(lastSky.width, lastSky.height) * 0.94f * zoom;
            pan = -(n.pos - new Vector2(0.5f, 0.5f)) * s;
            sel = n.key; selArea = null; voicesPage = false; rightScroll = Vector2.zero;
        }

        /// <summary>Into the chant (the chosen place, or the first free one), or out of it if it is there.</summary>
        private void ToggleChant(Gene_LinarianSong g, string t, int at)
        {
            if (at >= 0) { g.SetSlot(at, null); SoundDefOf.Tick_Low.PlayOneShotOnCamera(); return; }
            int target = selSlot >= 0 ? selSlot : Enumerable.Range(0, Gene_LinarianSong.Slots).FirstOrDefault(i => g.Slot(i) == null);
            bool full = selSlot < 0 && Enumerable.Range(0, Gene_LinarianSong.Slots).All(i => g.Slot(i) != null);
            if (full) { Messages.Message("The chant is full - choose a place, or take one out.", MessageTypeDefOf.RejectInput, false); return; }
            g.SetSlot(target, t);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        private bool DoubleClicked(Rect r) =>
            Event.current.type == EventType.MouseDown && Event.current.button == 0 && Event.current.clickCount == 2 && Mouse.IsOver(r);

        private void FocusArea(string a)
        {
            zoom = 1.8f;
            int i = System.Array.IndexOf(LinarianNetwork.Areas, a);
            Vector2 c = i >= 0 ? LinarianNetwork.RingCentreOf(i) : new Vector2(0.5f, 0.5f);
            float s = Mathf.Min(lastSky.width, lastSky.height) * 0.94f * zoom;
            pan = -(c - new Vector2(0.5f, 0.5f)) * s;
            LinarianNode crown = LinarianNetwork.Get(a + "5");
            selArea = a; sel = null; voicesPage = false; rightScroll = Vector2.zero;
        }

        private void DrawTactics(Rect r, Gene_LinarianSong g)
        {
            float y = r.y;
            GUI.color = Soft;
            Widgets.Label(new Rect(r.x, y + 4f, 60f, 24f), "Search");
            search = Widgets.TextField(new Rect(r.x + 62f, y, r.width - 62f, 28f), search ?? "");
            y += 34f;
            string[] fl = { "All", "Lit", "To light", "Dark", "Chant" };
            float fw = (r.width - 4f * 4f) / 5f;
            for (int i = 0; i < fl.Length; i++)
                if (DarkButton(new Rect(r.x + i * (fw + 4f), y, fw, 26f), fl[i], filter == i)) { filter = i; leftScroll = Vector2.zero; }
            y += 32f;
            if (Mine)
            {
                GUI.color = Faint;
                string place = selSlot >= 0 ? "Chosen place of the chant: " + (selSlot + 1) + "." : "Click a place of the chant below to choose where a tactic goes (else the first empty one).";
                float ph = Text.CalcHeight(place, r.width);
                Widgets.Label(new Rect(r.x, y, r.width, ph), place);
                y += ph + 4f;
            }
            Rect list = new Rect(r.x, y, r.width, r.yMax - y);

            var rows = new List<(LinarianNode n, string t)>();
            foreach (string a in Areas(g))
                foreach (LinarianNode n in LinarianNetwork.Nodes.Where(n => n.area == a))
                    foreach (string t in LinarianTactics.ChantKeys(n))
                    {
                        bool lit = g.Has(n.key) || LinarianTactics.Innate(t), can = Mine && LinarianNetwork.CannotOpen(g, n) == null, inChant = g.SlotOf(t) >= 0;
                        if (filter == 1 && !lit || filter == 2 && !can || filter == 3 && (lit || can) || filter == 4 && !inChant) continue;
                        string label = LinarianTactics.ChantLabel(t);
                        if (!search.NullOrEmpty() && label.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0
                            && LinarianNetwork.AreaLabel(a).IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0
                            && n.Desc.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                        rows.Add((n, t));
                    }
            float w = list.width - 16f, rh = 54f;
            Widgets.BeginScrollView(list, ref leftScroll, new Rect(0f, 0f, w, rows.Count * (rh + 4f) + 4f));
            try
            {
                float ry = 0f;
                foreach (var (n, t) in rows)
                {
                    Rect row = new Rect(0f, ry, w, rh);
                    bool lit = g.Has(n.key) || LinarianTactics.Innate(t), can = Mine && LinarianNetwork.CannotOpen(g, n) == null;
                    int at = g.SlotOf(t);
                    if (sel == n.key) Widgets.DrawBoxSolid(row, new Color(Warm.r, Warm.g, Warm.b, 0.22f));
                    else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.06f));
                    GUI.color = new Color(1f, 1f, 1f, lit ? 1f : can ? 0.75f : 0.45f);
                    GUI.DrawTexture(new Rect(2f, ry + 3f, 48f, 48f), LinarianTex.Tactic(t), ScaleMode.ScaleToFit);
                    float bw = lit && Mine ? 92f : 0f;
                    GUI.color = lit ? Bright : can ? Soft : Faint;
                    Widgets.Label(new Rect(58f, ry + 4f, w - 62f - bw, 24f), LinarianTactics.ChantLabel(t).CapitalizeFirst());
                    GUI.color = AreaColor[n.area];
                    string state = lit ? (at >= 0 ? "in the chant, place " + (at + 1) : "lit") : can ? "can be lit" : "dark";
                    Widgets.Label(new Rect(58f, ry + 27f, w - 62f - bw, 24f), LinarianNetwork.AreaLabel(n.area) + "  -  " + state);
                    TooltipHandler.TipRegion(row, LinarianTactics.Label(t).CapitalizeFirst() + "\n\n" + LinarianTactics.Brief(t, g) + "\n\nGift: " + n.passive + "\n\nDouble click: into the chant, or out of it (a dark one is shown in the sky).");
                    if (lit && Mine)
                    {
                        Rect b = new Rect(w - bw, ry + 12f, bw - 4f, 30f);
                        if (at >= 0) { if (DarkButton(b, "Remove")) g.SetSlot(at, null); }
                        else
                        {
                            int target = selSlot >= 0 ? selSlot : Enumerable.Range(0, Gene_LinarianSong.Slots).FirstOrDefault(i => g.Slot(i) == null);
                            bool full = selSlot < 0 && Enumerable.Range(0, Gene_LinarianSong.Slots).All(i => g.Slot(i) != null);
                            if (DarkButton(b, full ? "Chant full" : "To place " + (target + 1), false, !full)) g.SetSlot(target, t);
                        }
                    }
                    if (DoubleClicked(new Rect(row.x, row.y, row.width - bw, row.height)))
                    {
                        // a double click puts a lit tactic into the chant, or takes it out; a dark one is shown in the sky
                        if (lit && Mine) ToggleChant(g, t, at);
                        else FocusStar(n);
                        Event.current.Use();
                    }
                    if (Widgets.ButtonInvisible(new Rect(row.x, row.y, row.width - bw, row.height))) { sel = n.key; selArea = null; voicesPage = false; rightScroll = Vector2.zero; }
                    ry += rh + 4f;
                }
                if (rows.Count == 0) { GUI.color = Faint; Widgets.Label(new Rect(0f, 0f, w, 60f), "Nothing here."); }
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawPresets(Rect r, Gene_LinarianSong g)
        {
            float w = r.width - 16f, bh = 28f, ic = 34f;
            float block = 26f + ic + 8f + (Mine ? bh + 8f : 0f);
            Rect list = new Rect(r.x, r.y, r.width, r.height - (Mine ? 40f : 0f));
            Widgets.BeginScrollView(list, ref leftScroll, new Rect(0f, 0f, w, g.presets.Count * (block + 8f) + 4f));
            try
            {
                float y = 0f;
                for (int i = 0; i < g.presets.Count; i++)
                {
                    SongPreset pr = g.presets[i];
                    Rect box = new Rect(0f, y, w, block);
                    Widgets.DrawBoxSolid(box, new Color(1f, 1f, 1f, 0.04f));
                    GUI.color = Line; Widgets.DrawBox(box, 1);
                    GUI.color = Bright;
                    Widgets.Label(new Rect(8f, y + 4f, w - 16f, 24f), pr.name);
                    for (int k = 0; k < Gene_LinarianSong.Slots; k++)
                    {
                        Rect ir = new Rect(8f + k * (ic + 6f), y + 28f, ic, ic);
                        Widgets.DrawBoxSolid(ir, new Color(0f, 0f, 0f, 0.3f));
                        string t = k < pr.tactics.Count && !pr.tactics[k].NullOrEmpty() ? pr.tactics[k] : null;
                        if (t != null)
                        {
                            bool have = LinarianTactics.Unlocked(g, t);
                            GUI.color = new Color(1f, 1f, 1f, have ? 1f : 0.35f);
                            GUI.DrawTexture(ir.ContractedBy(2f), LinarianTex.Tactic(t), ScaleMode.ScaleToFit);
                            TooltipHandler.TipRegion(ir, LinarianTactics.Label(t).CapitalizeFirst() + (have ? "" : " (its star is not lit yet)"));
                        }
                    }
                    if (Mine)
                    {
                        float bw = (w - 16f - 12f) / 4f, by = y + 28f + ic + 8f;
                        int ii = i;
                        if (DarkButton(new Rect(8f, by, bw, bh), "Sing it")) g.Apply(pr);
                        TooltipHandler.TipRegion(new Rect(8f, by, bw, bh), "Lay it into the chant at once. Tactics already in the chant keep their strength; new ones sing at half for two hours.");
                        if (DarkButton(new Rect(8f + (bw + 4f), by, bw, bh), "Write")) g.presets[ii] = g.Snapshot(pr.name);
                        TooltipHandler.TipRegion(new Rect(8f + (bw + 4f), by, bw, bh), "Write the present chant into this preset.");
                        if (DarkButton(new Rect(8f + (bw + 4f) * 2f, by, bw, bh), "Rename")) Find.WindowStack.Add(new Dialog_SongPresetName(pr));
                        if (DarkButton(new Rect(8f + (bw + 4f) * 3f, by, bw, bh), "Delete")) { g.presets.RemoveAt(ii); break; }
                    }
                    y += block + 8f;
                }
                if (g.presets.Count == 0) { GUI.color = Faint; Widgets.Label(new Rect(0f, 0f, w, 60f), "No presets yet."); }
            }
            finally { Widgets.EndScrollView(); }
            if (Mine && DarkButton(new Rect(r.x, r.yMax - 32f, r.width, 32f), "New preset from the chant"))
                g.presets.Add(g.Snapshot("Chant " + (g.presets.Count + 1)));
        }

        // ---------------------------------------------------------------- the sky

        private void DrawSky(Rect r, Gene_LinarianSong g)
        {
            lastSky = r;
            Event e = Event.current;
            float baseS = Mathf.Min(r.width, r.height) * 0.94f;
            if (Mouse.IsOver(r))
            {
                if (e.type == EventType.ScrollWheel)
                {
                    float old = zoom;
                    zoom = Mathf.Clamp(zoom * (e.delta.y < 0f ? 1.12f : 1f / 1.12f), 0.8f, 4f);
                    Vector2 mp = e.mousePosition - r.center;
                    pan = mp - (mp - pan) * (zoom / old);
                    e.Use();
                }
                if (e.type == EventType.MouseDown) { dragging = true; dragged = 0f; }
            }
            if (dragging && e.type == EventType.MouseDrag) { pan += e.delta; dragged += e.delta.magnitude; e.Use(); }
            if (e.rawType == EventType.MouseUp) dragging = false;
            // the network never leaves the sky: its centre stays within reach
            float s = baseS * zoom;
            float lx = Mathf.Max(0f, (s - r.width) / 2f) + r.width * 0.15f, ly = Mathf.Max(0f, (s - r.height) / 2f) + r.height * 0.15f;
            pan = new Vector2(Mathf.Clamp(pan.x, -lx, lx), Mathf.Clamp(pan.y, -ly, ly));

            Widgets.BeginGroup(r);
            try
            {
                Rect local = new Rect(0f, 0f, r.width, r.height);
                Rect sq = new Rect(local.center.x - s / 2f + pan.x, local.center.y - s / 2f + pan.y, s, s);
                Vector2 Pos(LinarianNode n) => new Vector2(sq.x + n.pos.x * s, sq.y + n.pos.y * s);
                Vector2 U(Vector2 u) => new Vector2(sq.x + u.x * s, sq.y + u.y * s);

                float size0 = Mathf.Clamp(s * 0.036f, 20f, 110f);
                bool starHit = LinarianNetwork.Nodes.Any(n => (!n.Shadow || g.shadow) && Vector2.Distance(e.mousePosition, Pos(n)) < (n.Crown ? size0 * 1.25f : size0) * 0.5f);
                if (g.shadow)
                {
                    // the shadow's own ring, round all the others: its four stars sit on it, an eighth and a half turned
                    float sr = LinarianNetwork.ShadowRadius * s * 2f;
                    bool onT = selArea == "T";
                    float md = Vector2.Distance(e.mousePosition, sq.center);
                    bool inPath = false;
                    for (int a2 = 0; a2 < LinarianNetwork.Areas.Length; a2++)
                        if (Vector2.Distance(e.mousePosition, U(LinarianNetwork.RingCentreOf(a2))) < LinarianNetwork.RingRadius * s) inPath = true;
                    bool overT = Mouse.IsOver(local) && md < sr * 0.5f && !inPath;
                    Color tc = AreaColor["T"];
                    GUI.color = new Color(0.40f, 0.18f, 0.62f, onT ? 0.34f : overT ? 0.28f : 0.22f);
                    GUI.DrawTexture(new Rect(sq.center.x - sr * 0.6f, sq.center.y - sr * 0.6f, sr * 1.2f, sr * 1.2f), SongArt.Glow);
                    Texture2D tr = SongArt.PathRing22 ?? SongArt.PathRing;
                    if (tr != null)
                    {
                        float W = sr * 0.5f / SongArt.PathRingRadius;
                        GUI.color = new Color(tc.r, tc.g, tc.b, onT ? 1f : 0.8f);
                        GUI.DrawTexture(new Rect(sq.center.x - W / 2f, sq.center.y - W / 2f, W, W), tr);
                    }
                    else
                    {
                        GUI.color = new Color(0.70f, 0.45f, 0.95f, 0.55f);
                        GUI.DrawTexture(new Rect(sq.center.x - sr / 2f, sq.center.y - sr / 2f, sr, sr), SongArt.Ring);
                    }
                    if (overT && e.type == EventType.MouseUp && e.button == 0 && dragged < 6f && !starHit)
                    {
                        selArea = "T"; sel = null; voicesPage = false; rightScroll = Vector2.zero;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    }
                }
                for (int a = 0; a < LinarianNetwork.Areas.Length; a++)
                {
                    string area = LinarianNetwork.Areas[a];
                    Vector2 c = U(LinarianNetwork.RingCentreOf(a));
                    float rr = LinarianNetwork.RingRadius * s * 2f;
                    Color col = AreaColor[area];
                    bool on = selArea == area;
                    Rect ringR = new Rect(c.x - rr / 2f, c.y - rr / 2f, rr, rr);
                    bool overRing = Mouse.IsOver(local) && Vector2.Distance(e.mousePosition, c) < rr * 0.5f;
                    GUI.color = new Color(col.r, col.g, col.b, on ? 0.22f : overRing ? 0.13f : 0.07f);
                    GUI.DrawTexture(ringR.ExpandedBy(rr * 0.1f), SongArt.Glow);
                    Texture2D emb = LinarianTex.Path(area);
                    if (emb != null && (on || overRing)) { GUI.color = new Color(1f, 1f, 1f, 0.25f); GUI.DrawTexture(ringR.ContractedBy(rr * 0.2f), emb, ScaleMode.ScaleToFit); }
                    if (SongArt.PathRing != null)
                    {
                        // the ring holds its stars: its four star-points sit where the rim stars are, so it turns with its ring
                        float W = rr * 0.5f / SongArt.PathRingRadius;
                        GUI.color = new Color(col.r, col.g, col.b, on ? 1f : 0.8f);
                        GUI.DrawTexture(new Rect(c.x - W / 2f, c.y - W / 2f, W, W), a % 2 == 1 && SongArt.PathRing45 != null ? SongArt.PathRing45 : SongArt.PathRing);
                    }
                    else { GUI.color = new Color(col.r, col.g, col.b, on ? 0.95f : 0.5f); GUI.DrawTexture(ringR, SongArt.Ring); }
                    if (overRing && e.type == EventType.MouseUp && e.button == 0 && dragged < 6f && !starHit)
                    {
                        selArea = area; sel = null; voicesPage = false; rightScroll = Vector2.zero;
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    }
                }
                foreach (LinarianNode n in LinarianNetwork.Nodes)
                    foreach (string k in LinarianNetwork.Neighbours(n.key))
                    {
                        LinarianNode m = LinarianNetwork.Get(k);
                        if (m == null || string.CompareOrdinal(n.key, k) > 0) continue;
                        if (m.area == n.area && !n.Shadow) continue;
                        if ((n.Shadow || m.Shadow) && !g.shadow) continue;
                        bool lit = g.Has(n.key) && g.Has(k);
                        Widgets.DrawLine(Pos(n), Pos(m), lit ? new Color(Lit.r, Lit.g, Lit.b, 0.85f) : new Color(Line.r, Line.g, Line.b, 0.4f), lit ? 2f : 1f);
                    }

                float size = Mathf.Clamp(s * 0.036f, 20f, 110f);
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.realtimeSinceStartup * 2.4f);
                foreach (LinarianNode n in LinarianNetwork.Nodes)
                {
                    if (n.Shadow && !g.shadow) continue;
                    Vector2 v = Pos(n);
                    float sz = n.Crown ? size * 1.25f : size;
                    Rect nr = new Rect(v.x - sz / 2f, v.y - sz / 2f, sz, sz);
                    bool open = g.Has(n.key);
                    bool can = Mine && LinarianNetwork.CannotOpen(g, n) == null;
                    Color col = AreaColor[n.area];
                    int state = open ? 2 : can ? 1 : 0;
                    Texture2D star = SongArt.Star(state);
                    if (star != null)
                    {
                        if (n.Crown)
                        {
                            // the crown of a ring: a halo in the path's colour and a second star turned behind the first - eight points
                            GUI.color = new Color(col.r, col.g, col.b, state == 2 ? 0.55f : 0.3f);
                            GUI.DrawTexture(nr.ExpandedBy(sz * 0.8f), SongArt.Glow);
                            GUI.color = new Color(1f, 1f, 1f, state == 1 ? 0.6f * pulse : state == 2 ? 0.85f : 0.5f);
                            Texture2D turned = SongArt.Star45(state);
                            if (turned != null) GUI.DrawTexture(nr.ExpandedBy(sz * 0.6f), turned);
                        }
                        GUI.color = state == 1 ? new Color(1f, 1f, 1f, pulse) : Color.white;
                        GUI.DrawTexture(nr.ExpandedBy(sz * 0.42f), star);
                    }
                    else
                    {
                        Color glow = open ? Lit : can ? Silver : Faint;
                        GUI.color = new Color(glow.r, glow.g, glow.b, open ? 0.75f : can ? 0.45f * pulse : 0.22f);
                        GUI.DrawTexture(nr.ExpandedBy(sz * 0.75f), SongArt.Glow);
                        GUI.color = new Color(col.r, col.g, col.b, open ? 0.95f : 0.45f);
                        GUI.DrawTexture(nr, SongArt.Ring);
                    }
                    GUI.color = new Color(1f, 1f, 1f, open ? 1f : can ? 0.6f : 0.22f);
                    GUI.DrawTexture(nr.ContractedBy(sz * (star != null ? 0.06f : 0.1f)), LinarianTex.Tactic(n.unlocks[0]), ScaleMode.ScaleToFit);
                    if (sel == n.key) { GUI.color = Bright; GUI.DrawTexture(nr.ExpandedBy(sz * 0.36f), SongArt.Ring); }
                    if (Mouse.IsOver(nr))
                    {
                        GUI.color = new Color(col.r, col.g, col.b, 0.9f);
                        GUI.DrawTexture(nr.ExpandedBy(sz * 0.3f), SongArt.Ring);
                        TooltipHandler.TipRegion(nr, n.label.CapitalizeFirst() + "  (" + LinarianNetwork.AreaLabel(n.area) + ")");
                        if (e.type == EventType.MouseUp && e.button == 0 && dragged < 6f)
                        {
                            sel = n.key; selArea = null; voicesPage = false; rightScroll = Vector2.zero;
                            SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        }
                    }
                }
            }
            finally { Widgets.EndGroup(); }
            if (zoom != 1f || pan != Vector2.zero)
            {
                Rect reset = new Rect(r.xMax - 96f, r.y + 6f, 90f, 26f);
                if (DarkButton(reset, "Centre")) { zoom = 1f; pan = Vector2.zero; }
            }
            GUI.color = Color.white;
        }

        // ---------------------------------------------------------------- the chant

        private void DrawChant(Rect r, Gene_LinarianSong g)
        {
            Text.Font = GameFont.Small;
            float gap = 16f;
            float slot = Mathf.Min(64f, (r.width - gap * (Gene_LinarianSong.Slots - 1) - 16f) / Gene_LinarianSong.Slots);
            float total = Gene_LinarianSong.Slots * slot + (Gene_LinarianSong.Slots - 1) * gap;
            float x0 = r.center.x - total / 2f;
            for (int i = 0; i < Gene_LinarianSong.Slots; i++)
            {
                Rect sr = new Rect(x0 + i * (slot + gap), r.y + 10f, slot, slot);
                string t = g.Slot(i);
                int warm = g.WarmingFor(i);
                if (selSlot == i) { GUI.color = new Color(Warm.r, Warm.g, Warm.b, 0.6f); GUI.DrawTexture(sr.ExpandedBy(20f), SongArt.Glow); }
                Texture2D frame = SongArt.Slot;
                if (frame != null) { GUI.color = Color.white; GUI.DrawTexture(sr.ExpandedBy(12f), frame); }
                else
                {
                    Widgets.DrawBoxSolid(sr, new Color(0f, 0f, 0f, 0.35f));
                    GUI.color = Mouse.IsOver(sr) || selSlot == i ? Silver : Line;
                    Widgets.DrawBox(sr, 1);
                }
                if (t != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, warm > 0 ? 0.6f : 1f);
                    GUI.DrawTexture(sr.ContractedBy(5f), LinarianTex.Tactic(t), ScaleMode.ScaleToFit);
                    if (warm > 0) Widgets.DrawBoxSolid(new Rect(sr.x, sr.yMax - 4f, sr.width * (1f - warm / (float)Gene_LinarianSong.WarmTicks), 4f), Warm);
                }
                else
                {
                    Text.Anchor = TextAnchor.MiddleCenter; GUI.color = Faint;
                    Widgets.Label(sr, (i + 1).ToString());
                    Text.Anchor = TextAnchor.UpperLeft;
                }
                Text.Anchor = TextAnchor.UpperCenter;
                GUI.color = t == null ? Faint : warm > 0 ? Warm : Soft;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(sr.x - gap / 2f - 4f, sr.yMax + 6f, sr.width + gap + 8f, 32f), t == null ? "Empty" : warm > 0 ? warm.ToStringTicksToPeriod(true, true) : LinarianTactics.ChantLabel(t).CapitalizeFirst());
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                if (t != null) TooltipHandler.TipRegion(sr, LinarianTactics.ChantLabel(t).CapitalizeFirst() + "\n\n" + string.Join("\n\n", LinarianTactics.Buttons(t).Select(x => LinarianTactics.Brief(x, g)))
                    + (warm > 0 ? "\n\nSinging herself into it: half strength for " + warm.ToStringTicksToPeriod() + "." : ""));
                if (!Mine) continue;
                if (Mouse.IsOver(sr) && Event.current.type == EventType.MouseDown)
                {
                    if (Event.current.button == 1 || (Event.current.button == 0 && Event.current.clickCount == 2 && t != null)) { g.SetSlot(i, null); selSlot = -1; }
                    else if (Event.current.button == 0)
                    {
                        selSlot = selSlot == i ? -1 : i;
                        leftTab = 1;
                        if (t != null) { sel = LinarianNetwork.ForTactic(t)?.key ?? sel; selArea = null; voicesPage = false; }
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    }
                    Event.current.Use();
                }
            }
            GUI.color = Color.white;
        }

        // ---------------------------------------------------------------- right: her Aria, the star, the voices

        private void DrawRight(Rect r, Gene_LinarianSong g)
        {
            Widgets.DrawBoxSolid(r, Pane);
            GUI.color = Line; Widgets.DrawBox(r, 1);
            float x = r.x + 12f, w = r.width - 24f, y = r.y + 8f;
            // her points: the one number that matters most here
            Text.Font = GameFont.Medium;
            GUI.color = g.PointsFree > 0 ? Lit : Bright;
            Widgets.Label(new Rect(x, y, w, 34f), g.PointsFree > 0 ? g.PointsFree + (g.PointsFree == 1 ? " free point" : " free points") : "No free points");
            Text.Font = GameFont.Small; GUI.color = Soft;
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(x, y + 8f, w, 24f), g.Points + " in all");
            Text.Anchor = TextAnchor.UpperLeft;
            y += 36f;
            GUI.color = Bright;
            Widgets.Label(new Rect(x, y, w, 24f), "Power of the Aria: x" + g.Power.ToString("0.00"));
            TooltipHandler.TipRegion(new Rect(x, y, w, 24f), "How strongly she sings: it grows with the depth of her Aria and her psychic sensitivity. It decides how many minds a tactic takes and how surely they obey.");
            y += 26f;
            if (g.songLevel < Gene_LinarianSong.MaxLevel)
            {
                Rect bar = new Rect(x, y, w, 22f);
                Widgets.DrawBoxSolid(bar, new Color(0f, 0f, 0f, 0.45f));
                Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(g.songXp / Mathf.Max(1f, g.XpToNext)), bar.height), Warm);
                GUI.color = Line; Widgets.DrawBox(bar, 1);
                Text.Anchor = TextAnchor.MiddleCenter; GUI.color = Bright;
                Widgets.Label(bar, "Next point: " + g.songXp.ToString("0") + " / " + g.XpToNext.ToString("0"));
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(bar, "The Aria deepens as she sings and hides, uses her tactics, devours and hunts by her aria. Every point lights one star.");
                y += 28f;
            }
            if (g.store > 0.01f) { GUI.color = Soft; Widgets.Label(new Rect(x, y, w, 24f), "Stored essence: " + g.store.ToStringPercent()); y += 24f; }
            y += 4f;
            GUI.color = Line; Widgets.DrawLineHorizontal(x, y, w); y += 6f;
            // the chant
            GUI.color = Bright;
            Widgets.Label(new Rect(x, y, w, 24f), "The chant");
            TooltipHandler.TipRegion(new Rect(x, y, w, 24f), "The tactics at hand: a button each below the pawn. A tactic new to the chant sings at half strength for two hours while she sings herself into it.\n\nClick a place to choose it, then pick a tactic in the list on the left. Right click empties a place.");
            y += 22f;
            float slot = Mathf.Min(64f, (w - 16f * 4f - 16f) / 5f);
            DrawChant(new Rect(x, y, w, slot + 48f), g);
            y += slot + 52f;
            GUI.color = Line; Widgets.DrawLineHorizontal(x, y, w); y += 8f;

            float tw = (w - 6f) / 2f;
            bool starPage = !voicesPage;
            if (DarkButton(new Rect(x, y, tw, 28f), selArea != null ? "The path" : "The star", starPage)) voicesPage = false;
            if (DarkButton(new Rect(x + tw + 6f, y, tw, 28f), "Voices (" + g.voices.Count + ")", voicesPage)) voicesPage = true;
            y += 36f;
            Rect body = new Rect(x, y, w, r.yMax - y - 10f);
            if (voicesPage) DrawVoices(body, g);
            else if (selArea != null) DrawArea(body, g, selArea);
            else DrawStar(body, g);
        }

        /// <summary>A path's page: what it is about, how far it is lit, and how strongly its tactics sing now.</summary>
        private void DrawArea(Rect r, Gene_LinarianSong g, string a)
        {
            var nodes = LinarianNetwork.Nodes.Where(n => n.area == a).ToList();
            int lit = nodes.Count(n => g.Has(n.key));
            int minds = LinarianTactics.Count(g);
            float chance = Mathf.Clamp01(0.2f + 0.45f * g.Power);
            string text = "\u201C" + LinarianTexts.AreaVoice(a) + "\u201D\n\n" + LinarianNetwork.AreaSummary(a)
                + "\n\nLit: " + lit + " of " + nodes.Count + " stars."
                + "\n\nNow, with the Aria's power x" + g.Power.ToString("0.00") + ":"
                + "\n- a tactic that takes minds takes up to " + minds + (minds == 1 ? " mind" : " minds") + " at a time;"
                + "\n- an ordinary mind obeys her " + chance.ToStringPercent() + " of the time (sensitive minds more, dull ones less; the deaf, machines and other Singers never);"
                + "\n- a tactic new to the chant sings at half that for two hours.";
            string dark = a != "T" && g.shadow ? LinarianNetwork.ShadowGift(a) : null;
            if (dark != null) text += "\n\nIn the shadow (with its outer star lit): " + dark;
            float w = r.width - 16f;
            Text.Font = GameFont.Small;
            float h = 84f + Text.CalcHeight(text, w) + 16f + nodes.Count * 52f;
            Widgets.BeginScrollView(r, ref rightScroll, new Rect(0f, 0f, w, h));
            try
            {
                Texture2D em = LinarianTex.Path(a);
                if (em != null) { GUI.color = Color.white; GUI.DrawTexture(new Rect(0f, 0f, 72f, 72f), em, ScaleMode.ScaleToFit); }
                Text.Font = GameFont.Medium; GUI.color = AreaColor[a];
                Widgets.Label(new Rect(82f, 18f, w - 82f, 40f), LinarianNetwork.AreaLabel(a));
                Text.Font = GameFont.Small;
                float y = 84f;
                GUI.color = Soft;
                float th = Text.CalcHeight(text, w);
                Widgets.Label(new Rect(0f, y, w, th), text);
                y += th + 16f;
                foreach (LinarianNode n in nodes.OrderBy(n => n.Index))
                {
                    Rect row = new Rect(0f, y, w, 48f);
                    if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.06f));
                    bool on = g.Has(n.key);
                    GUI.color = new Color(1f, 1f, 1f, on ? 1f : 0.5f);
                    GUI.DrawTexture(new Rect(2f, y + 2f, 44f, 44f), LinarianTex.Tactic(n.unlocks[0]), ScaleMode.ScaleToFit);
                    GUI.color = on ? Bright : Faint;
                    Widgets.Label(new Rect(54f, y + 4f, w - 54f, 22f), n.label.CapitalizeFirst() + (n.Crown ? " (crown)" : ""));
                    GUI.color = on ? Lit : Faint;
                    Widgets.Label(new Rect(54f, y + 24f, w - 54f, 22f), on ? "lit" : "dark");
                    if (DoubleClicked(row)) { FocusStar(n); Event.current.Use(); }
                    if (Widgets.ButtonInvisible(row)) { sel = n.key; selArea = null; rightScroll = Vector2.zero; }
                    y += 52f;
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawStar(Rect r, Gene_LinarianSong g)
        {
            LinarianNode n = sel != null ? LinarianNetwork.Get(sel) : null;
            if (n == null)
            {
                GUI.color = Soft;
                Widgets.Label(r, "Choose a star in the sky or in the list of tactics.\n\nEvery star is a stone of her Aria: a tactic she sings and a gift that stays with her. The star of each ring that faces the centre can be lit from the start; the others once a star joined to them is lit - the side stars join the neighbouring rings. A ring's crown, in its middle, wakes when three stars of its ring are lit.\n\nThe wheel brings the sky closer; a drag moves it.");
                return;
            }
            string dark = n.Index == 4 && g.shadow ? LinarianNetwork.ShadowGift(n.area) : null;
            string tactics = string.Join("\n\n", n.unlocks.Select(t => LinarianTactics.Label(t).CapitalizeFirst() + ": " + LinarianTactics.Brief(t, g)));
            string voice = LinarianTexts.Lore(n.key);
            voice = voice != null ? "\u201C" + voice + "\u201D" : null;
            string text = "Tactic\n" + tactics + "\n\nGift (always with her once lit)\n" + n.passive.CapitalizeFirst() + (dark != null ? "\n\nIn the shadow\n" + dark : "");
            float w = r.width - 16f;
            Text.Font = GameFont.Small;
            float vh = voice != null ? Text.CalcHeight(voice, w) + 12f : 0f;
            float h = 84f + vh + Text.CalcHeight(text, w) + 140f;
            Widgets.BeginScrollView(r, ref rightScroll, new Rect(0f, 0f, w, h));
            try
            {
                Rect ic = new Rect(0f, 0f, 72f, 72f);
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                GUI.DrawTexture(ic.ExpandedBy(8f), SongArt.Glow);
                GUI.color = Color.white;
                GUI.DrawTexture(ic, LinarianTex.Tactic(n.unlocks[0]), ScaleMode.ScaleToFit);
                Text.Font = GameFont.Medium; GUI.color = Bright;
                Widgets.Label(new Rect(82f, 2f, w - 82f, 46f), n.label.CapitalizeFirst());
                Text.Font = GameFont.Small; GUI.color = AreaColor[n.area];
                Widgets.Label(new Rect(82f, 46f, w - 82f, 24f), LinarianNetwork.AreaLabel(n.area) + (n.Crown ? ", the crown" : ""));
                float y = 84f;
                if (voice != null) { GUI.color = Warm; Widgets.Label(new Rect(0f, y, w, vh), voice); y += vh; }
                GUI.color = Soft;
                float th = Text.CalcHeight(text, w);
                Widgets.Label(new Rect(0f, y, w, th), text);
                y += th + 12f;
                if (Mine && (n.key == "L4" || n.key == "L5"))
                {
                    bool own = g.lullOwn;
                    GUI.color = Bright;
                    Widgets.CheckboxLabeled(new Rect(0f, y, w, 26f), "Her own fall asleep too", ref own);
                    g.lullOwn = own;
                    TooltipHandler.TipRegion(new Rect(0f, y, w, 26f), "On: her lullaby takes her own people round the point as well (not the drafted) - to rest them by force, or to give her sleepers of her own for the Dreaming path.");
                    y += 32f;
                }
                if (Mine && (n.key == "C1" || n.key == "C4"))
                {
                    bool mass = n.key == "C1" ? g.adoreMass : g.lastOrderMass;
                    GUI.color = Bright;
                    string lab = n.key == "C1" ? "A whole crowd (round a point)" : "Every fervent cultist round a point";
                    Widgets.CheckboxLabeled(new Rect(0f, y, w, 26f), lab, ref mass);
                    if (n.key == "C1") g.adoreMass = mass; else g.lastOrderMass = mass;
                    TooltipHandler.TipRegion(new Rect(0f, y, w, 26f), n.key == "C1"
                        ? "Off: one mind - a stream of worship that grows by the hour (a second sowing doubles it). On: everyone round a point, each by chance - a day and a half of worship, lengthened by the night."
                        : "Off: one fervent cultist turns maniac among his own - every blow twice as hard, at the neck, his comrades first. On: every fervent cultist round a point (keep a recruit out of it), half again the first blow.");
                    y += 32f;
                }
                if (Mine && (n.key == "T2" || n.key == "T4"))
                {
                    bool alt = n.key == "T2" ? g.ageMode : g.flareMode;
                    GUI.color = Bright;
                    string lab = n.key == "T2" ? (g.ageReserve >= 0 ? "Keep her age (set aside: take it back)" : "Keep her age (set it aside)") : "The flare, not the fog";
                    Widgets.CheckboxLabeled(new Rect(0f, y, w, 26f), lab, ref alt);
                    if (n.key == "T2") g.ageMode = alt; else g.flareMode = alt;
                    TooltipHandler.TipRegion(new Rect(0f, y, w, 26f), n.key == "T2"
                        ? "Off: she drinks the years of foes in her zone. On: the first singing sets her own age aside; the next takes it back - younger than she set it, she grows into it and gains what those years would have taught (about 200 experience a year in every skill); older, she sheds the years, loses what they taught, and every wound of hers is gone."
                        : "Off: a black fog rises round the point. On: the sun flares - every light and machine on the land dies until dawn.");
                    y += 32f;
                }
                if (Mine && n.key == "S1")
                {
                    bool mass = g.rendMass;
                    GUI.color = Bright;
                    Widgets.CheckboxLabeled(new Rect(0f, y, w, 26f), "Everyone round a point", ref mass);
                    g.rendMass = mass;
                    TooltipHandler.TipRegion(new Rect(0f, y, w, 26f), "Off: one who bears her pain - a long stun, and the scars often run on up the limb. On: everyone bearing it round a point - stunned, the scars rarely spread. It never wounds: only the pain.");
                    y += 32f;
                }
                if (Mine && n.key == "S2")
                {
                    bool lift = g.preyLift;
                    GUI.color = Bright;
                    Widgets.CheckboxLabeled(new Rect(0f, y, w, 26f), "Mercy: lift the pain instead", ref lift);
                    g.preyLift = lift;
                    TooltipHandler.TipRegion(new Rect(0f, y, w, 26f), "Off: the one she points at is her prey - every blow lands deeper, and by chance against their mind the scars burn past bearing. On: she lifts her phantasmal pain from them instead (with flaying's star her other phantasms go with it, and a failing mind clears).");
                    y += 32f;
                }
                if (Mine && n.key == "F3")
                {
                    bool dump = g.storeDump;
                    GUI.color = Bright;
                    Widgets.CheckboxLabeled(new Rect(0f, y, w, 26f), "Pour out all of it (" + Mathf.RoundToInt(g.store) + " / " + Mathf.RoundToInt(LinarianFeast.Cap(g)) + ")", ref dump);
                    g.storeDump = dump;
                    TooltipHandler.TipRegion(new Rect(0f, y, w, 26f), "Off: releasing the store only feeds her hunger. On: all of it is poured out - her hunger filled, the rest into her aria's experience (1.5 a point of essence); once her aria has nothing left to learn, into a surge that doubles her aria, 3.6 hours for every 100.");
                    y += 32f;
                }
                if (Mine)
                {
                    string why = LinarianNetwork.CannotOpen(g, n);
                    if (g.Has(n.key))
                    {
                        foreach (string t in LinarianTactics.ChantKeys(n))
                        {
                            int at = g.SlotOf(t);
                            string pre = LinarianTactics.ChantKeys(n).Count() > 1 ? LinarianTactics.ChantLabel(t).CapitalizeFirst() + ": " : "";
                            if (at >= 0) { if (DarkButton(new Rect(0f, y, w, 30f), pre + "in the chant, place " + (at + 1) + " - remove")) g.SetSlot(at, null); }
                            else
                            {
                                int target = selSlot >= 0 ? selSlot : Enumerable.Range(0, Gene_LinarianSong.Slots).FirstOrDefault(i => g.Slot(i) == null);
                                bool full = selSlot < 0 && Enumerable.Range(0, Gene_LinarianSong.Slots).All(i => g.Slot(i) != null);
                                if (DarkButton(new Rect(0f, y, w, 30f), full ? pre + "the chant is full - choose a place below" : pre + "into the chant, place " + (target + 1), false, !full)) g.SetSlot(target, t);
                            }
                            y += 36f;
                        }
                    }
                    else if (DarkButton(new Rect(0f, y, w, 30f), why == null ? "Light this star (1 point)" : why.CapitalizeFirst(), false, why == null))
                    {
                        g.opened.Add(n.key);
                        g.Opened(n);
                        SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                    }
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        private void DrawVoices(Rect r, Gene_LinarianSong g)
        {
            GUI.color = Soft;
            if (g.voices.Count == 0)
            {
                Widgets.Label(r, "Her collection is empty. Every one she devours leaves her a voice: the skill they were best at, which the dead man's voice lends her for a day (five times a voice), and a little more power to her aria.");
                return;
            }
            var chosen = LinarianFeast.Chosen(g);
            var rest = g.voices.Where(v => !chosen.Contains(v) && !v.forgotten).OrderByDescending(v => v.level).ToList();
            var gone = g.voices.Where(v => !chosen.Contains(v) && v.forgotten).OrderByDescending(v => v.level).ToList();
            float w = r.width - 16f, row = 28f;
            float h = 26f + row * chosen.Count + 34f + row * rest.Count + 34f + row * Mathf.Max(1, gone.Count) + 30f;
            Widgets.BeginScrollView(r, ref rightScroll, new Rect(0f, 0f, w, h));
            try
            {
                float y = 0f;
                GUI.color = Lit;
                Widgets.Label(new Rect(0f, y, w, 24f), "The chosen - " + LinarianFeast.Count(g) + " voices in all, store " + Mathf.RoundToInt(g.store));
                TooltipHandler.TipRegion(new Rect(0f, y, w, 24f), "One voice for every skill: the best, unless you choose another (click a row). Only the chosen lend their skill, and only they do not fade.");
                y += 26f;
                foreach (SongVoice v in chosen)
                {
                    Rect rr = new Rect(0f, y, w, row);
                    bool pinned = g.pins.TryGetValue(v.skill.defName, out int pid) && pid == v.id;
                    if (Mouse.IsOver(rr)) Widgets.DrawBoxSolid(rr, new Color(1f, 1f, 1f, 0.06f));
                    GUI.color = Bright;
                    Widgets.Label(new Rect(4f, y + 4f, w * 0.34f, row), v.skill.LabelCap + " " + v.level + (pinned ? "  •" : ""));
                    GUI.color = v.worth > 1f ? Lit : Soft;
                    Widgets.Label(new Rect(w * 0.34f, y + 4f, w * 0.48f, row), (v.merged ? "merging: " : "") + v.name);
                    GUI.color = Soft;
                    Text.Anchor = TextAnchor.UpperRight;
                    Widgets.Label(new Rect(w * 0.82f, y + 4f, w * 0.18f - 4f, row), v.uses + " left");
                    Text.Anchor = TextAnchor.UpperLeft;
                    if (Widgets.ButtonInvisible(rr))
                    {
                        SkillDef sk = v.skill;
                        var opts = new List<FloatMenuOption> { new FloatMenuOption("The best, chosen by itself", () => g.pins.Remove(sk.defName)) };
                        foreach (SongVoice c in g.voices.Where(x => x.Usable && x.skill == sk).OrderByDescending(x => x.level))
                        {
                            SongVoice cc = c;
                            opts.Add(new FloatMenuOption((cc.merged ? "merging: " : "") + cc.name + " - " + sk.LabelCap + " " + cc.level + ", " + cc.uses + " left", () => g.pins[sk.defName] = cc.id));
                        }
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                    y += row;
                }
                y += 8f;
                GUI.color = Lit;
                Widgets.Label(new Rect(0f, y, w, 24f), "The rest of her collection");
                TooltipHandler.TipRegion(new Rect(0f, y, w, 24f), "Not chosen voices fade to half-forgotten in two months. Half-forgotten ones are only names: a quarter of a voice in her aria, whole in the bonus for every ten.");
                y += 26f;
                foreach (SongVoice v in rest)
                {
                    Rect rr = new Rect(0f, y, w, row);
                    if (Mouse.IsOver(rr)) Widgets.DrawBoxSolid(rr, new Color(1f, 1f, 1f, 0.06f));
                    GUI.color = v.forgotten ? Faint : v.worth > 1f ? Lit : Bright;
                    Widgets.Label(new Rect(4f, y + 4f, w * 0.6f, row), (v.merged ? "merging: " : "") + v.name + (v.worth > 1f ? "  ✦" : ""));
                    GUI.color = v.forgotten ? Faint : Soft;
                    Text.Anchor = TextAnchor.UpperRight;
                    Widgets.Label(new Rect(w * 0.6f, y + 4f, w * 0.4f - 4f, row), v.forgotten ? "half-forgotten" : v.skill.LabelCap + " " + v.level + ", " + v.uses + " left");
                    Text.Anchor = TextAnchor.UpperLeft;
                    if (v.worth > 1f) TooltipHandler.TipRegion(rr, "A great voice: it weighs three times as much in her aria.");
                    y += row;
                }
                y += 8f;
                GUI.color = Lit;
                Widgets.Label(new Rect(0f, y, w, 24f), "Half-forgotten (" + gone.Count + ")");
                TooltipHandler.TipRegion(new Rect(0f, y, w, 24f), "A voice is half-forgotten once its skill has been lent as many times as the settings allow, or when it is left unchosen too long. It lends no skill any more: only its name stays, a share of a voice in her aria, whole in the bonus for every ten.");
                y += 26f;
                if (gone.Count == 0)
                {
                    GUI.color = Faint;
                    Widgets.Label(new Rect(4f, y + 4f, w, row), "None yet.");
                    y += row;
                }
                foreach (SongVoice v in gone)
                {
                    GUI.color = Faint;
                    Widgets.Label(new Rect(4f, y + 4f, w * 0.6f, row), v.name + (v.worth > 1f ? "  ✦" : ""));
                    Text.Anchor = TextAnchor.UpperRight;
                    Widgets.Label(new Rect(w * 0.6f, y + 4f, w * 0.4f - 4f, row), "was " + v.skill.LabelCap + " " + v.level);
                    Text.Anchor = TextAnchor.UpperLeft;
                    y += row;
                }
            }
            finally { Widgets.EndScrollView(); GUI.color = Color.white; }
        }

        private static bool DarkButton(Rect r, string label, bool active = false, bool enabled = true)
        {
            bool over = enabled && Mouse.IsOver(r);
            Widgets.DrawBoxSolid(r, active ? new Color(0.34f, 0.27f, 0.54f, 0.92f) : over ? new Color(0.22f, 0.19f, 0.36f, 0.92f) : new Color(0.12f, 0.10f, 0.22f, 0.92f));
            GUI.color = enabled ? (over || active ? Silver : Line) : new Color(Line.r, Line.g, Line.b, 0.35f);
            Widgets.DrawBox(r, 1);
            GUI.color = enabled ? Bright : Faint;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (!enabled) return false;
            if (Widgets.ButtonInvisible(r)) { SoundDefOf.Click.PlayOneShotOnCamera(); return true; }
            return false;
        }
    }

    /// <summary>A name for a preset of the chant.</summary>
    public class Dialog_SongPresetName : Window
    {
        private readonly SongPreset preset;
        private string name;

        public Dialog_SongPresetName(SongPreset preset)
        {
            this.preset = preset;
            name = preset.name;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        public override Vector2 InitialSize => new Vector2(340f, 150f);

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(0f, 0f, inRect.width, 26f), "Name of the preset");
            name = Widgets.TextField(new Rect(0f, 32f, inRect.width, 30f), name);
            if (Widgets.ButtonText(new Rect(inRect.width - 120f, inRect.height - 34f, 120f, 32f), "OK") ||
                Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                if (!name.NullOrEmpty()) preset.name = name.Trim();
                Close();
            }
        }
    }

    /// <summary>The sky's art: soft-loaded, painted here while it is not drawn.</summary>
    [StaticConstructorOnStartup]
    public static class SongArt
    {
        public static readonly Texture2D Bg = ContentFinder<Texture2D>.Get("UI/Avir/Herald/SongTab_Bg", false);
        public static readonly Texture2D Slot = ContentFinder<Texture2D>.Get("UI/Avir/Herald/ChantSlot", false);
        public static readonly Texture2D PathRing = ContentFinder<Texture2D>.Get("UI/Avir/Herald/PathRing", false);
        /// <summary>The same art turned an eighth: the diagonal rings and the crowns' second star. Turning it while
        /// drawing made it wander off its place, so it is turned in the file instead.</summary>
        public static readonly Texture2D PathRing45 = ContentFinder<Texture2D>.Get("UI/Avir/Herald/PathRing_45", false);
        public static readonly Texture2D PathRing22 = ContentFinder<Texture2D>.Get("UI/Avir/Herald/PathRing_22", false);
        private static readonly Texture2D[] stars45 =
        {
            ContentFinder<Texture2D>.Get("UI/Avir/Herald/Star_Locked_45", false),
            ContentFinder<Texture2D>.Get("UI/Avir/Herald/Star_Ready_45", false),
            ContentFinder<Texture2D>.Get("UI/Avir/Herald/Star_Lit_45", false),
        };
        public static Texture2D Star45(int state) => stars45[state];
        /// <summary>Radius of the ring's line, as a share of the texture's width (measured on the art).</summary>
        public const float PathRingRadius = 0.381f;
        private static readonly Texture2D[] stars =
        {
            ContentFinder<Texture2D>.Get("UI/Avir/Herald/Star_Locked", false),
            ContentFinder<Texture2D>.Get("UI/Avir/Herald/Star_Ready", false),
            ContentFinder<Texture2D>.Get("UI/Avir/Herald/Star_Lit", false),
        };
        public static readonly Texture2D Glow = MakeGlow(64);
        public static readonly Texture2D Ring = MakeRing(256, 0.94f);

        public static Texture2D Star(int state) => stars[0] != null && stars[1] != null && stars[2] != null ? stars[state] : null;

        private static Texture2D MakeGlow(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            t.Apply();
            return t;
        }

        private static Texture2D MakeRing(int n, float inner)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float w = (1f - inner) / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - (1f - w)) / w);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        private static readonly Vector3[] dust = Enumerable.Range(0, 160).Select(i =>
        {
            var r = new System.Random(i * 7919 + 13);
            return new Vector3((float)r.NextDouble(), (float)r.NextDouble(), (float)r.NextDouble());
        }).ToArray();

        /// <summary>A scatter of faint stars over the painted sky.</summary>
        public static void Dust(Rect r, int seed)
        {
            foreach (Vector3 d in dust)
            {
                float s = 1.5f + d.z * 3f;
                GUI.color = new Color(0.85f, 0.85f, 1f, 0.10f + d.z * 0.25f);
                GUI.DrawTexture(new Rect(r.x + d.x * r.width - s, r.y + d.y * r.height - s, s * 2f, s * 2f), Glow);
            }
            GUI.color = Color.white;
        }
    }
}
