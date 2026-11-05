using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Artifactorics
{
    /// <summary>
    /// Development sheet of an Avir traditional amulet, on the artificer's parchment.
    ///   left   - the amulet: metals, caste, focus of each path, the amulet's own growth, the slot
    ///   centre - first the caste (only when the core was ordered without one) and the subclass,
    ///            both for good; then the pattern: one branch of stones per subclass of the caste
    ///   right  - the selected stone, points and experience, Invest
    /// </summary>
    public class Dialog_AmuletDevelopment : Window
    {
        private readonly CompAvirAmulet amulet;
        private AmuletSubclassDef selBranch;
        private int selStone;
        private AmuletSubclassDef hovBranch;
        private int hovStone;
        /// <summary>The core is the slot: selected / hovered.</summary>
        private bool selSlot, hovSlot;
        private Vector2 leftScroll, textScroll;

        private static readonly Color Ink = new Color(0.03f, 0.018f, 0.008f);
        private static readonly Color InkSoft = new Color(0.10f, 0.06f, 0.025f);
        private static readonly Color InkGrey = new Color(0.22f, 0.17f, 0.12f);
        private static readonly Color Warn = new Color(0.50f, 0.05f, 0.02f);
        private static readonly Color Gold = new Color(0.70f, 0.50f, 0.12f);
        private static readonly Color Good = new Color(0.12f, 0.33f, 0.08f);

        private static AmuletSettingsDef S => AmuletSettingsDef.Main;

        public Dialog_AmuletDevelopment(CompAvirAmulet amulet)
        {
            this.amulet = amulet;
            forcePause = true;
            doCloseX = false;
            doWindowBackground = false;
            drawShadow = false;
            draggable = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
        }

        private bool sheetMax;
        private Rect sheetNormal;

        public override Vector2 InitialSize
        {
            get
            {
                float w = Mathf.Min(1280f, Verse.UI.screenWidth - 40f);
                float h = w * (941f / 1672f);
                if (h > Verse.UI.screenHeight - 40f) { h = Verse.UI.screenHeight - 40f; w = h * (1672f / 941f); }
                return new Vector2(w, h);
            }
        }

        protected override float Margin => 0f;

        private Pawn Wearer => (amulet.parent as Apparel)?.Wearer;

        private static Rect Sub(Rect r, float x0, float y0, float x1, float y1) =>
            new Rect(r.x + r.width * x0, r.y + r.height * y0, r.width * (x1 - x0), r.height * (y1 - y0));

        public override void PostOpen()
        {
            base.PostOpen();
            SheetWindow.StartMaximized(this, ref sheetMax, ref sheetNormal);   // whole screen by default
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (amulet?.parent == null || amulet.parent.Destroyed || !amulet.HasCore || S == null) { Close(false); return; }
            GUI.DrawTexture(inRect, ArtifactoricsTex.Panel, ScaleMode.StretchToFill);
            Rect close = new Rect(inRect.xMax - inRect.width * 0.052f - 26f, inRect.y + inRect.height * 0.085f, 24f, 24f);
            SheetWindow.MaximizeButton(this, close, ref sheetMax, ref sheetNormal);
            // the left page ends above the corner flourish, so its text never runs under the frame
            Rect left = Sub(inRect, 0.056f, 0.165f, 0.232f, 0.80f);
            Rect mid = Sub(inRect, 0.262f, 0.120f, 0.740f, 0.890f);
            Rect right = Sub(inRect, 0.770f, 0.165f, 0.944f, 0.845f);
            Color oc = GUI.color; GameFont of = Text.Font; TextAnchor oa = Text.Anchor;
            try
            {
                DrawLeft(left);
                float fl = inRect.width * 0.07f;
                if (confirmText != null) { Title(mid, "Choose", ""); DrawConfirm(mid, fl); }
                else if ((amulet.IsMulticlass || amulet.IsCustom) && amulet.subclass == null) DrawMulticlassPick(mid, fl);
                else if (amulet.caste == null && !amulet.IsMulticlass && !amulet.IsCustom) DrawCastePick(mid, fl);
                else if (amulet.subclass == null) DrawSubclassPick(mid, fl);
                else { DrawPattern(mid, fl); DrawRight(right); }
            }
            finally { GUI.color = oc; Text.Font = of; Text.Anchor = oa; }
            if (SheetWindow.CloseButton(close)) Close();
        }

        // ================================================================== left

        private string leftText;
        private bool leftItself;

        private void LeftTab(Rect r, string label, bool active, System.Action onClick)
        {
            GUI.color = active ? Ink : InkGrey; Text.Anchor = TextAnchor.MiddleCenter;
            ArtInk.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = active ? new Color(Gold.r, Gold.g, Gold.b, 0.9f) : Mouse.IsOver(r) ? new Color(Ink.r, Ink.g, Ink.b, 0.4f) : new Color(0, 0, 0, 0);
            Widgets.DrawLineHorizontal(r.x + 10f, r.yMax - 2f, r.width - 20f);
            GUI.color = Color.white;
            if (!active && Widgets.ButtonInvisible(r)) { onClick(); SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
        }

        /// <summary>The amulet's own page: its telekinesis circle (sockets bought with points) and what it gives.</summary>
        private void DrawAmuletItself(Rect r)
        {
            float cs = Mathf.Min(r.width * 0.75f, 190f);
            Rect c = new Rect(r.center.x - cs / 2f, r.y + 6f, cs, cs);
            DrawTkCircle(c);
            float y = c.yMax + 26f;
            Comp_Telekinesis tk = amulet.parent.TryGetComp<Comp_Telekinesis>();
            float q = AmuletDevelopment.NecklaceQualityFactor(amulet);
            bool maker = AmuletDevelopment.MainCaste(amulet) == S.makerCaste;
            float g = maker ? S.makerGrowthFactor : 1f;
            var sb = new StringBuilder();
            if (tk != null)
                sb.Append("Telekinetic reach ").Append(tk.Radius.ToString("0.#"))
                  .Append("\nSubspace satchel ").Append(tk.SatchelKg.ToString("0.#")).Append(" kg")
                  .Append("\nWork speed x").Append(tk.Props.workSpeedFactor.ToString("0.##"));
            sb.Append("\n\nEvery stone: +").Append((S.radiusPerStone * g * q).ToString("0.##")).Append(" reach, +")
              .Append((S.satchelPerStone * g * q).ToString("0.##")).Append(" kg").Append(maker ? " (maker: grows the furthest)" : "");
            sb.Append("\nNecklace quality: x").Append(q.ToString("0.##")).Append(" on all of it.");
            sb.Append("\n\nSockets (click the circle to open the next):");
            for (int i = 0; i < S.tkSocketCosts.Count; i++)
                sb.Append("\n  ").Append(i < amulet.tkLevel ? "opened" : "socket").Append(" ").Append(i + 1).Append(" - ")
                  .Append(S.tkSocketCosts[i]).Append(" pt: +").Append((S.tkSocketReach[i] * q).ToString("0.#")).Append(" reach, +")
                  .Append((S.tkSocketSatchel[i] * q).ToString("0.#")).Append(" kg");
            GUI.color = InkSoft; Text.Font = GameFont.Small;
            ArtInk.Label(new Rect(r.x, y, r.width, r.yMax - y), sb.ToString());
        }
        private float leftAt = -99f;

        private void DrawLeft(Rect r)
        {
            // the numbers behind this text are not cheap (strength, bonds...): rebuilt twice a second
            if (leftText == null || Time.realtimeSinceStartup - leftAt > 0.5f)
            {
                var sb = new StringBuilder();
                sb.Append("Necklace: ").Append(amulet.necklaceMetal?.LabelCap ?? "-").Append(", ").Append(amulet.necklaceQuality.GetLabel());
                sb.Append("\nCore: ").Append(amulet.coreMetal.LabelCap).Append(", ").Append(amulet.coreQuality.GetLabel());
                if (amulet.IsMulticlass) sb.Append("\nA'Shaign: every path of every caste");
                else if (amulet.IsCustom) sb.Append("\nPaths: ").Append(string.Join(", ", amulet.Props.branches.Select(b => b.LabelFor(amulet))));
                else sb.Append("\nCaste: ").Append(amulet.caste?.LabelCap.ToString() ?? "not chosen");
                sb.Append("\nMain path: ").Append(amulet.subclass?.LabelCapFor(amulet) ?? "not chosen");
                if (amulet.AvirOnly)
                    sb.Append(Wearer != null && !AmuletDevelopment.WorksFor(amulet, Wearer) ? "\nDead on this wearer: only an Avir can use it." : "\nOnly an Avir can wear it.");

                List<AmuletSubclassDef> paths = AmuletDevelopment.Branches(amulet);
                if (paths.Count > 0)
                {
                    sb.Append("\n\nStrength of the paths:");
                    foreach (AmuletSubclassDef b in paths)
                    {
                        sb.Append("\n  ").Append(b.LabelCapFor(amulet)).Append(" x").Append(AmuletDevelopment.Strength(amulet, b, Wearer).ToString("0.##"));
                        if (b == amulet.subclass) sb.Append(" (main)");
                        float adj = AmuletDevelopment.AdjacencyBonus(amulet, b);
                        if (adj > 0f) sb.Append(", bonds +").Append(adj.ToStringPercent("0.#"));
                    }
                    if (amulet.coreMetal.psychic != null && Wearer == null) sb.Append("\n  Psychic core: counted on the wearer.");
                    var bonds = AmuletDevelopment.ActiveCombos(amulet).ToList();
                    sb.Append("\n\nBonds of neighbouring paths: every stone open on two neighbours adds +")
                      .Append(S.adjacencyPerStone.ToStringPercent("0.#")).Append(" strength to both; at ")
                      .Append(S.comboNode).Append(" on both a bond wakes, at ").Append(S.comboUpgrade).Append(" it doubles.");
                    foreach (var (combo, rank) in bonds)
                        sb.Append("\n  ").Append(combo.LabelCap).Append(rank > 1 ? " (doubled)" : "");
                    if (amulet.IsMulticlass)
                    {
                        float vf = AmuletDevelopment.HasVolley(amulet) ? S.tkVolleyFactor : 1f;
                        sb.Append("\n\nA'Shaign warriors fight with telekinesis (any warrior path at ").Append(S.battleTelekinesisNode)
                          .Append("). No damage; small chances, x the wearer's psychic sensitivity and the path's strength")
                          .Append(vf > 1f ? ", x" + vf.ToString("0.##") + " by the telekinetic volley" : "").Append(":")
                          .Append("\n  stop a projectile in flight ").Append(S.tkDeflectChance.ToStringPercent())
                          .Append("\n  wrest the weapon from the enemy the wearer runs at ").Append(S.tkWrestChance.ToStringPercent())
                          .Append("\n  disarm a shooter aiming at the wearer ").Append(S.tkDisarmShooterChance.ToStringPercent())
                          .Append("\n  knock an enemy off its feet for ").Append((S.tkKnockdownTicks / 60f).ToString("0.#")).Append(" s ").Append(S.tkKnockdownChance.ToStringPercent())
                          .Append("\n  Each has its own window; a target that lost its weapon or fell is safe from it for a while. The force is physical: it works on the psychically deaf too; big bodies are harder to knock down; mechanoid weapons are built in and cannot be wrested.");
                    }
                }

                if (amulet.subclass != null && !amulet.subclass.slotLabel.NullOrEmpty())
                {
                    sb.Append("\n\nSlot - ").Append(amulet.subclass.slotPassive ? "passive" : "active").Append(": ")
                      .Append(amulet.subclass.slotLabel.CapitalizeFirst())
                      .Append(amulet.slotOpen ? " (awake)" : " (asleep: " + S.slotCost + " points, the core of the pattern)")
                      .Append("\n").Append(amulet.subclass.slotDescription);
                }
                leftText = sb.ToString();
                leftAt = Time.realtimeSinceStartup;
            }
            string text = leftText;
            // the full name: medium if it fits in two lines, small otherwise - never cut off
            string title = amulet.parent.LabelCapNoCount;
            GUI.color = Ink;
            Text.Font = GameFont.Medium;
            if (Text.CalcHeight(title, r.width - 8f) > 60f) Text.Font = GameFont.Small;
            float titleH = Mathf.Min(64f, Text.CalcHeight(title, r.width - 8f));
            ArtInk.Label(new Rect(r.x + 4f, r.y, r.width - 8f, titleH), title);
            // two pages: the paths, and the amulet itself (its telekinesis)
            float ty = r.y + titleH + 4f, tw = (r.width - 8f) / 2f;
            Text.Font = GameFont.Small;
            LeftTab(new Rect(r.x + 4f, ty, tw, 26f), "Paths", !leftItself, () => leftItself = false);
            LeftTab(new Rect(r.x + 4f + tw, ty, tw, 26f), "Amulet itself", leftItself, () => leftItself = true);
            Rect outer = new Rect(r.x + 4f, ty + 32f, r.width - 8f, r.yMax - ty - 32f);
            if (leftItself) { DrawAmuletItself(outer); GUI.color = Color.white; return; }
            Text.Font = GameFont.Small;
            float th = Text.CalcHeight(text, outer.width - 16f);
            Rect view = new Rect(0f, 0f, th > outer.height ? outer.width - 16f : outer.width, th);
            GUI.color = InkSoft;
            Widgets.BeginScrollView(outer, ref leftScroll, view);
            ArtInk.Label(view, text);
            Widgets.EndScrollView();
            Text.Font = GameFont.Small; GUI.color = Color.white;
        }

        // ================================================================== picks

        private void Title(Rect mid, string title, string sub)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.UpperCenter;
            ArtInk.Label(new Rect(mid.x, mid.y + 6f, mid.width, 34f), title);
            Text.Font = GameFont.Small; GUI.color = InkSoft;
            ArtInk.Label(new Rect(mid.x, mid.y + 40f, mid.width, 24f), sub);
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
        }

        /// <summary>Cards side by side; returns the clicked index or -1.</summary>
        private int Cards(Rect area, List<(string title, string body)> cards)
        {
            int n = cards.Count;
            float gap = 10f;
            float w = (area.width - gap * (n - 1)) / Mathf.Max(1, n);
            int clicked = -1;
            for (int i = 0; i < n; i++)
            {
                Rect c = new Rect(area.x + i * (w + gap), area.y, w, area.height);
                if (Mouse.IsOver(c)) Widgets.DrawBoxSolid(c, new Color(1f, 0.9f, 0.6f, 0.16f));
                GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.8f);
                Widgets.DrawBox(c, 1);
                GUI.color = Ink; Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.UpperCenter;
                ArtInk.Label(new Rect(c.x + 4f, c.y + 6f, c.width - 8f, 32f), cards[i].title);
                Text.Anchor = TextAnchor.UpperLeft; Text.Font = n > 3 ? GameFont.Tiny : GameFont.Small; GUI.color = InkSoft;
                ArtInk.Label(new Rect(c.x + 8f, c.y + 42f, c.width - 16f, c.height - 48f), cards[i].body);
                Text.Font = GameFont.Small; GUI.color = Color.white;
                if (Widgets.ButtonInvisible(c)) clicked = i;
            }
            return clicked;
        }

        // ---- confirmation on the parchment (instead of the game's black box) ----
        private GameFont pickFont = GameFont.Small;
        private string confirmText;
        private System.Action confirmAction;

        private void Confirm(string text, System.Action action)
        {
            confirmText = text; confirmAction = action;
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
        }

        /// <summary>Drawn over the centre page while a choice waits for its confirmation; true while it is up.</summary>
        private bool DrawConfirm(Rect mid, float fl)
        {
            if (confirmText == null) return false;
            Rect box = new Rect(mid.x + fl, mid.y + mid.height * 0.25f, mid.width - 2f * fl, Mathf.Min(220f, mid.height * 0.5f));
            ArtifactoricsTex.TileParchment(box);
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
            Widgets.DrawBox(box, 2);
            GUI.color = Ink; Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.UpperCenter;
            ArtInk.Label(new Rect(box.x + 16f, box.y + 14f, box.width - 32f, 32f), "For good");
            Text.Font = GameFont.Small; GUI.color = InkSoft;
            ArtInk.Label(new Rect(box.x + 24f, box.y + 52f, box.width - 48f, box.height - 110f), confirmText);
            Text.Anchor = TextAnchor.UpperLeft;
            float bw = Mathf.Min(180f, (box.width - 60f) / 2f);
            Rect back = new Rect(box.center.x - bw - 10f, box.yMax - 52f, bw, 40f);
            Rect ok = new Rect(box.center.x + 10f, box.yMax - 52f, bw, 40f);
            if (Button(back, "Go back", true)) { confirmText = null; confirmAction = null; }
            if (Button(ok, "Confirm", true))
            {
                System.Action a = confirmAction;
                confirmText = null; confirmAction = null;
                a?.Invoke();
            }
            GUI.color = Color.white;
            return true;
        }

        private void DrawCastePick(Rect mid, float fl)
        {
            Title(mid, "Choose the caste", "The core was set without one. The caste is chosen once and never changes.");
            List<AmuletCasteDef> castes = AmuletCasteDef.All;
            var cards = castes.Select(c => (c.LabelCap.ToString(), c.description + "\n\nPaths:\n" + AmuletFocus.PreviewText(amulet.necklaceMetal, amulet.coreMetal, c))).ToList();
            int i = Cards(new Rect(mid.x + fl, mid.y + 76f, mid.width - 2f * fl, mid.height - 96f), cards);
            if (i >= 0)
            {
                AmuletCasteDef c = castes[i];
                Confirm("Bind this amulet to the " + c.label + " caste? It can never be changed.", () => { amulet.caste = c; SoundDefOf.Quest_Accepted.PlayOneShotOnCamera(); });
            }
        }

        private void DrawSubclassPick(Rect mid, float fl)
        {
            Title(mid, "Choose the path", "The " + amulet.caste.label + " caste. The chosen path is the amulet's own: its stones cost the least, and it holds the slot. Chosen for good.");
            List<AmuletSubclassDef> subs = amulet.caste.Subclasses;
            var cards = subs.Select(b =>
            {
                var sb = new StringBuilder(b.description);
                List<SkillDef> skills = amulet.caste.xpSkills.Concat(b.xpSkills).Distinct().ToList();
                sb.Append("\n\nLearns from: ").Append(string.Join(", ", skills.Select(k => k.label)));
                sb.Append("\nFocus: ").Append(ArtifactFocus.Signed(AmuletFocus.Fixed(amulet.necklaceMetal, amulet.coreMetal, b)));
                if (!b.cellStats.NullOrEmpty())
                    sb.Append("\nEvery stone: ").Append(string.Join(", ", b.cellStats.Where(c => c.stat != null).Select(c => c.stat.label)));
                if (!b.slotLabel.NullOrEmpty())
                    sb.Append("\n\n").Append(b.slotPassive ? "Passive" : "Active").Append(": ").Append(b.slotLabel.CapitalizeFirst());
                foreach (ArtifactMilestoneInfo m in b.milestones.OrderBy(m => m.node))
                    sb.Append("\n").Append(m.node).Append(" - ").Append(m.label.CapitalizeFirst());
                return (b.LabelCapFor(amulet), sb.ToString());
            }).ToList();
            int i = Cards(new Rect(mid.x + fl * 0.5f, mid.y + 76f, mid.width - fl, mid.height - 96f), cards);
            if (i >= 0)
            {
                AmuletSubclassDef b = subs[i];
                Confirm("Make " + b.LabelFor(amulet) + " this amulet's path? It can never be changed. The caste's other paths stay open at a higher price.", () => { amulet.subclass = b; selBranch = b; selStone = 1; SoundDefOf.Quest_Accepted.PlayOneShotOnCamera(); });
            }
        }

        /// <summary>A'Shaign: every path is theirs; only the main one is chosen (cheapest, holds the slot).</summary>
        private void DrawMulticlassPick(Rect mid, float fl)
        {
            // one size for every name: Medium if all of them fit their column, else Small for all
            {
                float colW = (mid.width - fl) / Mathf.Max(1, AmuletCasteDef.All.Count) - 12f - 76f;
                Text.Font = GameFont.Medium;
                pickFont = AmuletCasteDef.All.SelectMany(c2 => c2.Subclasses).All(b2 => Text.CalcSize(b2.LabelCapFor(amulet)).x <= colW) ? GameFont.Medium : GameFont.Small;
                Text.Font = GameFont.Small;
            }
            Title(mid, "Choose the main path", "A'Shaign. Every path of every caste is yours; the main one costs the least and holds the slot. For good.");
            List<AmuletCasteDef> castes = AmuletCasteDef.All;
            Rect area = new Rect(mid.x + fl * 0.5f, mid.y + 76f, mid.width - fl, mid.height - 96f);
            float cw = area.width / Mathf.Max(1, castes.Count);
            for (int ci = 0; ci < castes.Count; ci++)
            {
                AmuletCasteDef c = castes[ci];
                float x = area.x + ci * cw, y = area.y;
                GUI.color = Ink; Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.UpperCenter;
                ArtInk.Label(new Rect(x, y, cw, 32f), c.LabelCap);
                Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
                y += 38f;
                foreach (AmuletSubclassDef b in c.Subclasses.Where(x2 => !amulet.IsCustom || amulet.Props.branches.Contains(x2)))
                {
                    Rect row = new Rect(x + 6f, y, cw - 12f, 70f);
                    if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.2f));
                    GUI.color = Color.white;
                    if (b.SlotIcon != null) GUI.DrawTexture(new Rect(row.x + 2f, row.y + 3f, 64f, 64f), b.SlotIcon, ScaleMode.ScaleToFit);
                    GUI.color = Ink; Text.Anchor = TextAnchor.MiddleLeft;
                    Text.Font = pickFont;
                    ArtInk.Label(new Rect(row.x + 74f, row.y, row.width - 74f, row.height), b.LabelCapFor(amulet));
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                    TooltipHandler.TipRegion(row, b.LabelCapFor(amulet) + "\n\n" + b.description
                        + (b.slotLabel.NullOrEmpty() ? "" : "\n\n" + (b.slotPassive ? "Passive" : "Active") + ": " + b.slotLabel.CapitalizeFirst() + " - " + b.slotDescription));
                    if (Widgets.ButtonInvisible(row))
                    {
                        AmuletSubclassDef chosen = b;
                        Confirm("Make " + chosen.LabelFor(amulet) + " the main path of this A'Shaign amulet? It can never be changed.", () => { amulet.subclass = chosen; selBranch = chosen; selStone = 1; SoundDefOf.Quest_Accepted.PlayOneShotOnCamera(); });
                    }
                    y += 74f;
                }
            }
        }

        // ================================================================== pattern

        private enum StoneState { Locked, Available, Opened }

        private StoneState StateOf(AmuletSubclassDef b, int stone)
        {
            int open = AmuletDevelopment.Opened(amulet, b);
            if (stone <= open) return StoneState.Opened;
            return stone == open + 1 ? StoneState.Available : StoneState.Locked;
        }

        // Avir writing runs in a circle, from the centre outward. A ring every few stones round the
        // core; a path walks along a ring through its sector and steps out at its end, turning back
        // - a meander, the whole sheet a round labyrinth closed by one circle.
        //  - A'Schialis (castes): every milestone, a finished thought, is closed in a circle.
        //  - A'Shaign: every path in one labyrinth.
        // Like the artifact spiral: drawn in pattern units, scaled by the zoom (wheel) and moved by
        // dragging; milestones carry the path's emblem. Heavy drawing only on repaint.
        private AmuletSubclassDef lastHoverArm;
        private Rect tkRect;
        private Vector2 pan;
        private float zoom = -1f;
        private bool mouseDownInPattern, dragging;
        private Vector2 dragStart;
        private const float ZoomMin = 0.12f, ZoomMax = 1.6f;

        // geometry in pattern units
        private float PR => amulet.IsMulticlass ? 1500f : 900f;         // outer ring
        private float PR0 => amulet.IsMulticlass ? 300f : 210f;         // core ring
        private const float StoneU = 34f, MilestoneU = 78f, CoreU = 330f;

        private void DrawPattern(Rect mid, float fl)
        {
            List<AmuletSubclassDef> branches = AmuletDevelopment.Branches(amulet);
            bool ashai = amulet.IsMulticlass;
            string head = (ashai ? "A'Shaign" : amulet.IsCustom ? amulet.parent.def.LabelCap.ToString() : amulet.caste.LabelCap.ToString()) + " - " + amulet.subclass.LabelCapFor(amulet);
            Title(mid, head, "Hover a stone to follow its path · click the core for the slot");
            Rect area = new Rect(mid.x + fl * 0.3f, mid.y + 64f, mid.width - fl * 0.6f, mid.height - 68f);
            if (zoom < 0f) zoom = Mathf.Clamp(Mathf.Min(area.width, area.height) / (2f * (PR + 220f)), ZoomMin, ZoomMax);
            HandlePatternInput(area);

            bool paint = Event.current.type == EventType.Repaint;
            int n = branches.Count, count = S.stonesPerBranch;
            float sector = 360f / Mathf.Max(1, n);
            float sweep = sector * (ashai ? 0.76f : 0.84f);
            int per = Mathf.Max(1, S.rowStones);
            var knots = new List<int>();
            for (int k = 0; k < count; k += per) knots.Add(k);
            knots.Add(count);
            int rings = knots.Count - 1;

            GUI.BeginGroup(area);
            try
            {
                Rect local = new Rect(0f, 0f, area.width, area.height);
                Vector2 c = local.center + pan;
                float z = zoom;
                float R = PR * z, r0 = PR0 * z;
                float Rad(float f)
                {
                    int row = 0;
                    while (row < rings - 1 && f > knots[row + 1]) row++;
                    return r0 + (R - r0) * (row + 1) / rings;
                }
                Vector2 XY(float deg, float r) => new Vector2(c.x + Mathf.Cos(deg * Mathf.Deg2Rad) * r, c.y + Mathf.Sin(deg * Mathf.Deg2Rad) * r);
                int SegOf(int st)
                {
                    int seg = 0;
                    while (seg < knots.Count - 2 && st > knots[seg + 1]) seg++;
                    return seg;
                }
                void Ends(int arm, int seg, out float from, out float to)
                {
                    float lo = -90f + arm * sector - sweep / 2f, hi = -90f + arm * sector + sweep / 2f;
                    if (seg % 2 == 0) { from = lo; to = hi; } else { from = hi; to = lo; }
                }
                Vector2 StonePos(int arm, int st)
                {
                    int seg = SegOf(st);
                    float a = knots[seg], zz = knots[seg + 1];
                    Ends(arm, seg, out float from, out float to);
                    return XY(Mathf.Lerp(from, to, (st - a) / (zz - a)), Rad(zz));
                }

                Vector2 mouse = Event.current.mousePosition;
                bool inside = local.Contains(mouse) && !dragging;
                AmuletSubclassDef litArm = lastHoverArm;
                if (paint) { hovBranch = null; hovStone = 0; hovSlot = false; }
                Color inkFaint = new Color(InkGrey.r, InkGrey.g, InkGrey.b, 0.22f);
                Color done = new Color(Gold.r, Gold.g, Gold.b, 0.95f);

                // ---- rings and the closing circle ----
                if (paint)
                {
                    for (int k = 1; k < knots.Count; k++) Circle(c, r0 + (R - r0) * k / rings, inkFaint, 1f);
                    Circle(c, R + 40f * z, new Color(Gold.r, Gold.g, Gold.b, 0.85f), 3f);
                }

                // ---- paths ----
                for (int bi = 0; bi < n; bi++)
                {
                    AmuletSubclassDef b = branches[bi];
                    bool main = b == amulet.subclass;
                    bool lit = b == litArm;
                    int open = AmuletDevelopment.Opened(amulet, b);
                    Color todo = lit ? new Color(0.62f, 0.08f, 0.04f, 0.85f) : new Color(InkGrey.r, InkGrey.g, InkGrey.b, ashai ? 0.45f : 0.6f);
                    if (paint)
                    {
                        Ends(bi, 0, out float startDeg, out _);
                        float prevDeg = startDeg, prevR = r0;
                        for (int seg = 0; seg < knots.Count - 1; seg++)
                        {
                            float a = knots[seg], zz = knots[seg + 1], r = Rad(zz);
                            Ends(bi, seg, out float from, out float to);
                            bool stepDone = open > a;
                            AvirLine.Draw(XY(prevDeg, prevR), XY(from, r), stepDone ? done : todo, stepDone || lit ? 3f : 1.6f);
                            // the opened part of the ring in gold, the rest in ink
                            float cut = Mathf.Clamp01((open - a) / (zz - a));
                            float mid2 = Mathf.Lerp(from, to, cut);
                            if (cut > 0f) Arc(c, r, from, mid2, done, 3f);
                            if (cut < 1f) Arc(c, r, mid2, to, todo, lit ? 3f : 1.6f);
                            prevDeg = to; prevR = r;
                        }
                    }

                    for (int st = 1; st <= count; st++)
                    {
                        bool ms = S.IsMilestone(st);
                        float sz = (ms ? MilestoneU : StoneU) * z;
                        Vector2 p = StonePos(bi, st);
                        Rect cell = Snap(p, sz);
                        if (!cell.Overlaps(local)) continue;
                        bool hov = inside && (mouse - p).sqrMagnitude <= Mathf.Max(64f, sz * sz * 0.3f);
                        if (paint && hov) { hovBranch = b; hovStone = st; }
                        if (!paint) continue;
                        StoneState state = StateOf(b, st);
                        bool sel = !selSlot && b == selBranch && st == selStone;
                        if (hov || sel)
                        {
                            GUI.color = sel ? new Color(0.75f, 0.12f, 0.05f, 0.45f) : new Color(1f, 0.82f, 0.35f, 0.5f);
                            GUI.DrawTexture(cell.ExpandedBy(sz * 0.45f), ArtifactoricsTex.Glow, ScaleMode.StretchToFill);
                        }
                        // dark stones that read at a glance: ink when closed, gold once opened; no heavy outlines
                        Color inkDark = new Color(0.10f, 0.07f, 0.05f, 0.92f);
                        Color goldSoft = new Color(Gold.r, Gold.g, Gold.b, 0.9f);
                        if (ms)
                        {
                            GUI.color = new Color(0.95f, 0.89f, 0.74f, 1f);
                            GUI.DrawTexture(cell, ArtifactoricsTex.Disc, ScaleMode.StretchToFill);
                            Texture2D emblem = b.SlotIcon;
                            if (emblem != null)
                            {
                                GUI.color = state == StoneState.Opened ? Color.white : new Color(1f, 1f, 1f, state == StoneState.Available ? 0.85f : 0.55f);
                                GUI.DrawTexture(Inset(cell, sz * 0.06f), emblem, ScaleMode.StretchToFill);
                            }
                            if (state != StoneState.Locked)
                            {
                                GUI.color = goldSoft;
                                GUI.DrawTexture(cell.ExpandedBy(sz * 0.08f), ArtifactoricsTex.Ring, ScaleMode.StretchToFill);
                            }
                        }
                        else if (StoneTex(state) is Texture2D stex)
                        {
                            // the stone's own picture, when the textures are there
                            GUI.color = Color.white;
                            GUI.DrawTexture(Snap(p, sz * 1.25f), stex, ScaleMode.StretchToFill);
                        }
                        else
                        {
                            GUI.color = state == StoneState.Opened ? done : inkDark;
                            GUI.DrawTexture(cell, ArtifactoricsTex.Disc, ScaleMode.StretchToFill);
                            if (state == StoneState.Available)
                            {
                                GUI.color = goldSoft;
                                GUI.DrawTexture(cell.ExpandedBy(sz * 0.3f), ArtifactoricsTex.Ring, ScaleMode.StretchToFill);
                            }
                        }
                        if (sel)
                        {
                            GUI.color = new Color(0.62f, 0.08f, 0.04f, 0.95f);
                            GUI.DrawTexture(cell.ExpandedBy(sz * 0.18f), ArtifactoricsTex.Ring, ScaleMode.StretchToFill);
                        }
                        GUI.color = Color.white;
                    }

                    // the path's name past the outer circle, only for the path under the cursor
                    if (paint && lit)
                    {
                        Vector2 lp = XY(-90f + bi * sector, R + 72f * z);
                        string lbl = b.LabelCapFor(amulet) + (main ? " (main)" : "");
                        Text.Font = GameFont.Small;
                        float lw = Text.CalcSize(lbl).x + 10f;
                        Rect lr = new Rect(lp.x - lw / 2f, lp.y - 11f, lw, 22f);
                        Widgets.DrawBoxSolid(lr, new Color(1f, 0.93f, 0.75f, 0.9f));
                        GUI.color = Ink; Text.Anchor = TextAnchor.MiddleCenter;
                        ArtInk.Label(lr, lbl);
                        Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                    }
                }

                // ---- bonds: bridges between neighbouring paths, on the rings of the bond's stones ----
                if (n > 1)
                {
                    for (int bi = 0; bi < n; bi++)
                    {
                        int bj = (bi + 1) % n;
                        if (n == 2 && bi == 1) break;
                        AmuletSubclassDef x = branches[bi], y = branches[bj];
                        AmuletComboDef combo = AmuletDevelopment.ComboOf(amulet, x, y);
                        int rank = combo != null ? AmuletDevelopment.ComboRank(amulet, combo) : 0;
                        float from = -90f + bi * sector + sweep / 2f, to = -90f + (bi + 1) * sector - sweep / 2f;
                        Color off = new Color(0.10f, 0.07f, 0.05f, 0.8f);
                        // the bonds' own band, past the circle that closes the writing: each bond sits
                        // between the two paths it joins, its 9 on the inner line and its 18 outside
                        float mid3 = (from + to) / 2f;
                        float rIn = R + 110f * z;
                        Vector2 m1 = XY(mid3, rIn);
                        float ds = MilestoneU * 0.8f * z;
                        int shared = AmuletDevelopment.SharedStones(amulet, x, y);
                        if (paint)
                        {
                            Color line = rank >= 1 ? done : new Color(InkGrey.r, InkGrey.g, InkGrey.b, 0.6f);
                            AvirLine.Draw(XY(from, R + 40f * z), m1, line, rank >= 1 ? 3f : 1.4f);
                            AvirLine.Draw(XY(to, R + 40f * z), m1, line, rank >= 1 ? 3f : 1.4f);
                            Rect dr = Snap(m1, ds);
                            GUI.color = rank >= 1 ? done : new Color(0.95f, 0.89f, 0.74f, 1f);
                            GUI.DrawTexture(dr, ArtifactoricsTex.Disc, ScaleMode.StretchToFill);
                            if (rank >= 2) { GUI.color = done; GUI.DrawTexture(dr.ExpandedBy(ds * 0.18f), ArtifactoricsTex.Ring, ScaleMode.StretchToFill); }
                            else { GUI.color = new Color(InkGrey.r, InkGrey.g, InkGrey.b, 0.7f); GUI.DrawTexture(dr.ExpandedBy(ds * 0.08f), ArtifactoricsTex.Ring, ScaleMode.StretchToFill); }
                            // progress toward the bond: both paths' shared stones out of 9, then out of 18
                            GUI.color = new Color(0.10f, 0.07f, 0.05f, 1f);
                            Text.Font = ds > 34f ? GameFont.Small : GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
                            ArtInk.Label(dr, rank >= 2 ? "x2" : shared + "/" + (rank >= 1 ? S.comboUpgrade : S.comboNode));
                            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                            GUI.color = Color.white;
                        }
                        {
                            Rect hr = Snap(m1, ds * 1.4f);
                            if (inside && Mouse.IsOver(hr)) TooltipHandler.TipRegion(hr, BondTip(x, y, combo, rank));
                        }
                    }
                }

                // ---- the core: the heart of the amulet, and its slot ----
                float hs = CoreU * z;
                Rect heart = Snap(c, hs);
                if (paint && inside && (mouse - c).sqrMagnitude <= hs * hs * 0.16f) hovSlot = true;
                if (paint)
                {
                    bool awake = amulet.slotOpen;
                    if (hovSlot || selSlot)
                    {
                        GUI.color = selSlot ? new Color(0.75f, 0.12f, 0.05f, 0.45f) : new Color(1f, 0.82f, 0.35f, 0.45f);
                        GUI.DrawTexture(heart.ExpandedBy(hs * 0.12f), ArtifactoricsTex.Glow, ScaleMode.StretchToFill);
                    }
                    if (amulet.necklaceMetal != null)
                    {
                        GUI.color = amulet.necklaceMetal.RimColor;
                        GUI.DrawTexture(heart, ArtifactoricsTex.HeartRimMask, ScaleMode.ScaleToFit);
                    }
                    GUI.color = amulet.coreMetal.GemColorAt(Time.realtimeSinceStartup);
                    GUI.DrawTexture(heart, ArtifactoricsTex.GemBase, ScaleMode.ScaleToFit);
                    GUI.color = Color.white;
                    GUI.DrawTexture(heart, ArtifactoricsTex.GemHighlight, ScaleMode.ScaleToFit);
                    GUI.color = amulet.necklaceMetal != null ? amulet.necklaceMetal.RimColor : Color.white;
                    GUI.DrawTexture(heart, amulet.necklaceMetal != null ? ArtifactoricsTex.HeartSettingMask : ArtifactoricsTex.HeartSetting, ScaleMode.ScaleToFit);
                    Texture2D slotTex = amulet.subclass?.SlotIcon;
                    if (slotTex != null)
                    {
                        GUI.color = new Color(1f, 1f, 1f, awake ? 1f : 0.4f);
                        GUI.DrawTexture(Inset(heart, hs * 0.3f), slotTex, ScaleMode.StretchToFill);
                    }
                    GUI.color = Color.white;
                    lastHoverArm = hovBranch ?? (selSlot ? null : selBranch);
                }
            }
            finally { GUI.EndGroup(); }

            // the telekinesis circle: its own field, fixed in the corner like the artifact's

            GUI.color = new Color(InkSoft.r, InkSoft.g, InkSoft.b, 0.7f);
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.LowerRight;
            ArtInk.Label(new Rect(area.x, area.yMax - 20f, area.width - 10f, 20f), "Drag to move · wheel to zoom");
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;
        }

        /// <summary>Like the artifact's telekinesis circle: five sockets bought with points, each
        /// adding reach and satchel (x necklace quality) on top of what the stones grow.</summary>
        private void DrawTkCircle(Rect c)
        {
            int slots = S.tkSocketCosts.Count;
            string why = AmuletDevelopment.TkBlockReason(amulet);
            bool over = Mouse.IsOver(c);
            // a soft parchment glow behind, so the pattern's lines do not run through the field
            GUI.color = new Color(0.95f, 0.89f, 0.74f, 0.9f);
            GUI.DrawTexture(c.ExpandedBy(c.width * 0.12f), ArtifactoricsTex.Glow, ScaleMode.StretchToFill);
            if (over && why == null)
            {
                GUI.color = new Color(1f, 0.82f, 0.35f, 0.35f);
                GUI.DrawTexture(c.ExpandedBy(c.width * 0.1f), ArtifactoricsTex.Glow, ScaleMode.StretchToFill);
            }
            GUI.color = amulet.tkLevel > 0 ? Color.white : new Color(1f, 1f, 1f, 0.75f);
            GUI.DrawTexture(c, ArtifactoricsTex.CircleTelekinesis, ScaleMode.ScaleToFit);
            Color gem = amulet.coreMetal.GemColorAt(Time.realtimeSinceStartup);
            float sz = c.width * 0.13f;
            for (int i = 0; i < slots; i++)
            {
                Vector2 pos = ArtifactoricsTex.TkSocketPos(c, i, slots);
                Rect sock = new Rect(pos.x - sz / 2f, pos.y - sz / 2f, sz, sz);
                // the same stones as on the pattern: opened, the next one, the rest
                StoneState stt = i < amulet.tkLevel ? StoneState.Opened : i == amulet.tkLevel && why == null ? StoneState.Available : StoneState.Locked;
                Texture2D stex = StoneTex(stt);
                if (stex != null)
                {
                    GUI.color = Color.white;
                    GUI.DrawTexture(Snap(pos, sz * 1.25f), stex, ScaleMode.StretchToFill);
                }
                else if (i < amulet.tkLevel)
                {
                    GUI.color = gem;
                    GUI.DrawTexture(Snap(pos, sz), ArtifactoricsTex.Disc, ScaleMode.StretchToFill);
                }
            }
            GUI.color = Color.white;

            GUI.color = Ink; Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperCenter;
            ArtInk.Label(new Rect(c.x - 20f, c.yMax + 4f, c.width + 40f, 18f), "Telekinesis " + amulet.tkLevel + "/" + slots);
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small; GUI.color = Color.white;

            float q = AmuletDevelopment.NecklaceQualityFactor(amulet);
            var tip = new StringBuilder();
            tip.Append("Telekinesis circle: ").Append(amulet.tkLevel).Append(" of ").Append(slots).Append(" sockets.")
               .Append("\nEvery next socket gives more (necklace quality x").Append(q.ToString("0.##")).Append("):");
            for (int i = 0; i < slots; i++)
                tip.Append("\n  ").Append(i < amulet.tkLevel ? "[x] " : "[ ] ").Append(i + 1).Append(": ").Append(S.tkSocketCosts[i]).Append(" pt - +")
                   .Append((S.tkSocketReach[i] * q).ToString("0.#")).Append(" reach, +").Append((S.tkSocketSatchel[i] * q).ToString("0.#")).Append(" kg");
            tip
               .Append("\nThe stones grow the amulet too: +").Append((S.radiusPerStone * q).ToString("0.##")).Append(" reach and +")
               .Append((S.satchelPerStone * q).ToString("0.##")).Append(" kg each.");
            Comp_Telekinesis tk = amulet.parent.TryGetComp<Comp_Telekinesis>();
            if (tk != null) tip.Append("\n\nNow: reach ").Append(tk.Radius.ToString("0.#")).Append(", satchel ").Append(tk.SatchelKg.ToString("0.#")).Append(" kg.");
            if (amulet.tkLevel < slots)
            {
                int cost = S.tkSocketCosts[amulet.tkLevel];
                tip.Append("\n\nNext socket: ").Append(cost).Append(cost == 1 ? " point." : " points.")
                   .Append(why == null ? "\nClick to open it." : "\nCannot open: " + why + ".");
            }
            TooltipHandler.TipRegion(c, tip.ToString());
            if (why == null && Widgets.ButtonInvisible(c, false) && AmuletDevelopment.TryOpenTk(amulet))
            {
                SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                leftText = null;
            }
        }

        /// <summary>A square on whole pixels around a point (even size), so rings and emblems drawn
        /// in it and in its insets stay concentric - fractional rects round differently.</summary>
        private static Rect Snap(Vector2 p, float size)
        {
            float s2 = Mathf.Max(2f, Mathf.Round(size / 2f) * 2f);
            return new Rect(Mathf.Round(p.x) - s2 / 2f, Mathf.Round(p.y) - s2 / 2f, s2, s2);
        }

        // Pictures for the ordinary stones (UI/Artifactorics/Amulet/Stone_Locked, _Available,
        // _Opened). Until they exist, the stones are drawn as plain discs.
        private static Texture2D stoneLocked, stoneAvailable, stoneOpened;
        private static bool stoneTexLoaded;

        private static Texture2D StoneTex(StoneState st)
        {
            if (!stoneTexLoaded)
            {
                stoneTexLoaded = true;
                stoneLocked = ContentFinder<Texture2D>.Get("UI/Artifactorics/Amulet/Stone_Locked", false);
                stoneAvailable = ContentFinder<Texture2D>.Get("UI/Artifactorics/Amulet/Stone_Available", false) ?? stoneLocked;
                stoneOpened = ContentFinder<Texture2D>.Get("UI/Artifactorics/Amulet/Stone_Opened", false);
            }
            return st == StoneState.Opened ? stoneOpened : st == StoneState.Available ? stoneAvailable : stoneLocked;
        }

        private static Rect Inset(Rect r, float m)
        {
            float mm = Mathf.Round(m);
            return new Rect(r.x + mm, r.y + mm, r.width - 2f * mm, r.height - 2f * mm);
        }

        private string BondTip(AmuletSubclassDef x, AmuletSubclassDef y, AmuletComboDef combo, int rank)
        {
            int shared = AmuletDevelopment.SharedStones(amulet, x, y);
            var tip = new StringBuilder();
            tip.Append(x.LabelCapFor(amulet)).Append(" + ").Append(y.LabelCapFor(amulet))
               .Append("\n\nThe number on the bond: stones open on both paths (the lower of the two), out of ")
               .Append(S.comboNode).Append(" to wake it, then out of ").Append(S.comboUpgrade).Append(" to double it. It opens by itself - just develop both paths.")
               .Append("\n\nShared stones: ").Append(shared)
               .Append(" - both paths +").Append((shared * S.adjacencyPerStone).ToStringPercent("0.#")).Append(" strength.");
            if (combo != null)
            {
                tip.Append("\n\n").Append(combo.LabelCap).Append(rank == 2 ? " (doubled)" : rank == 1 ? " (awake)" : " (asleep: both paths to " + S.comboNode + ")")
                   .Append("\n").Append(combo.description);
                foreach (AmuletComboStat cs in combo.stats)
                    if (cs.stat != null)
                        tip.Append("\n  ").Append(cs.stat.LabelCap).Append(" ")
                           .Append(cs.offset != 0f ? cs.stat.ValueToString(cs.offset, ToStringNumberSense.Offset) : "+" + cs.factor.ToStringPercent());
                if (combo.satchelKg > 0f) tip.Append("\n  Subspace satchel +").Append(combo.satchelKg.ToString("0.#")).Append(" kg");
                tip.Append("\nDoubles when both paths reach ").Append(S.comboUpgrade).Append(".");
            }
            return tip.ToString();
        }

        private void HandlePatternInput(Rect area)
        {
            Event e = Event.current;
            bool over = area.Contains(e.mousePosition);
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (over && e.button == 0) { mouseDownInPattern = true; dragging = false; dragStart = e.mousePosition; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (mouseDownInPattern)
                    {
                        if (!dragging && (e.mousePosition - dragStart).sqrMagnitude > 16f) dragging = true;
                        if (dragging) { pan += e.delta; ClampPan(area); e.Use(); }
                    }
                    break;
                case EventType.MouseUp:
                    if (mouseDownInPattern && e.button == 0)
                    {
                        if (!dragging && over)
                        {
                            if (hovSlot) { selSlot = true; textScroll = Vector2.zero; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                            else if (hovBranch != null)
                            {
                                selBranch = hovBranch; selStone = hovStone; selSlot = false; textScroll = Vector2.zero;
                                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                            }
                        }
                        mouseDownInPattern = false; dragging = false;
                        e.Use();
                    }
                    break;
                case EventType.ScrollWheel:
                    if (over)
                    {
                        float old = zoom;
                        zoom = Mathf.Clamp(zoom * (e.delta.y > 0f ? 0.9f : 1.1f), ZoomMin, ZoomMax);
                        Vector2 rel = e.mousePosition - (area.center + pan);
                        pan -= rel * (zoom / old - 1f);
                        ClampPan(area);
                        e.Use();
                    }
                    break;
            }
        }

        private void ClampPan(Rect area)
        {
            float reach = (PR + 220f) * zoom;
            float mx = Mathf.Max(0f, reach - area.width * 0.3f), my = Mathf.Max(0f, reach - area.height * 0.3f);
            pan.x = Mathf.Clamp(pan.x, -mx, mx);
            pan.y = Mathf.Clamp(pan.y, -my, my);
        }

        private static void Arc(Vector2 c, float r, float fromDeg, float toDeg, Color col, float width)
        {
            // a segment every ~10 px of arc
            float len = Mathf.Abs(toDeg - fromDeg) * Mathf.Deg2Rad * r;
            int segs = Mathf.Clamp(Mathf.CeilToInt(len / 10f), 2, 120);
            Vector2 prev = Vector2.zero;
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, (float)i / segs) * Mathf.Deg2Rad;
                Vector2 p = new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
                if (i > 0) AvirLine.Draw(prev, p, col, width);
                prev = p;
            }
        }

        private static void Circle(Vector2 c, float r, Color col, float width) => Arc(c, r, 0f, 360f, col, width);

        // ================================================================== right

        private void DrawRight(Rect r)
        {
            AmuletSubclassDef b = hovBranch ?? selBranch;
            int stone = hovBranch != null ? hovStone : selStone;
            float y = r.y;
            bool showSlot = hovSlot || (selSlot && hovBranch == null);
            if (showSlot && amulet.subclass != null)
            {
                AmuletSubclassDef m = amulet.subclass;
                GUI.color = Ink; Text.Font = GameFont.Medium;
                ArtInk.Label(new Rect(r.x + 4f, y, r.width - 8f, 34f), (m.slotLabel ?? "slot").CapitalizeFirst());
                y += 34f;
                Text.Font = GameFont.Tiny; GUI.color = InkSoft;
                ArtInk.Label(new Rect(r.x + 4f, y, r.width - 8f, 18f), "Slot of the " + m.LabelFor(amulet) + " path · " + (m.slotPassive ? "passive" : "active")
                    + " · " + (amulet.slotOpen ? "awake" : S.slotCost + " points"));
                y += 22f;
                Text.Font = GameFont.Small;
                if (m.SlotIcon != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, amulet.slotOpen ? 1f : 0.5f);
                    GUI.DrawTexture(new Rect(r.x + r.width / 2f - 32f, y, 64f, 64f), m.SlotIcon, ScaleMode.ScaleToFit);
                    y += 70f;
                }
                GUI.color = InkSoft;
                string st2 = m.slotDescription + (amulet.slotOpen ? "" : "\n\nAsleep until " + S.slotCost + " points are put into it - only then does "
                    + (m.slotPassive ? "it work." : "the wearer get the ability."));
                Rect outer = new Rect(r.x + 4f, y, r.width - 8f, r.yMax - 240f - y);
                float th = Text.CalcHeight(st2, outer.width - 16f);
                Rect view = new Rect(0f, 0f, th > outer.height ? outer.width - 16f : outer.width, th);
                Widgets.BeginScrollView(outer, ref textScroll, view);
                ArtInk.Label(view, st2);
                Widgets.EndScrollView();
            }
            else if (b != null && stone > 0)
            {
                GUI.color = Ink; Text.Font = GameFont.Medium;
                ArtInk.Label(new Rect(r.x + 4f, y, r.width - 8f, 34f), b.LabelCapFor(amulet));
                y += 34f;
                Text.Font = GameFont.Tiny; GUI.color = InkSoft;
                int cost = AmuletDevelopment.StoneCost(amulet, b, stone);
                StoneState st = StateOf(b, stone);
                ArtInk.Label(new Rect(r.x + 4f, y, r.width - 8f, 18f), "Stone " + stone + " of " + S.stonesPerBranch
                    + (S.IsMilestone(stone) ? " · milestone" : "") + " · " + (st == StoneState.Opened ? "opened" : st == StoneState.Available ? "available" : "locked")
                    + " · " + cost + " point" + (cost == 1 ? "" : "s"));
                y += 22f;
                Text.Font = GameFont.Small;
                string text = StoneText(b, stone);
                Rect outer = new Rect(r.x + 4f, y, r.width - 8f, r.yMax - 240f - y);
                float th = Text.CalcHeight(text, outer.width - 16f);
                Rect view = new Rect(0f, 0f, th > outer.height ? outer.width - 16f : outer.width, th);
                Widgets.BeginScrollView(outer, ref textScroll, view);
                ArtInk.Label(view, text);
                Widgets.EndScrollView();
            }

            float py = r.yMax - 230f;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(r.x + 2f, py, 30f, 30f), ArtifactoricsTex.PointSeal, ScaleMode.ScaleToFit);
            GUI.color = Ink; Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleLeft;
            ArtInk.Label(new Rect(r.x + 38f, py, r.width - 38f, 30f), "Points: " + AmuletDevelopment.PointsAvailable(amulet));
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;
            py += 32f;
            AmuletDevelopment.NextPointProgress(amulet, out float have, out float need);
            GUI.color = InkSoft; Text.Font = GameFont.Tiny;
            ArtInk.Label(new Rect(r.x + 4f, py, r.width - 8f, 18f), "Experience: " + have.ToString("N0") + " / " + need.ToString("N0"));
            py += 18f;
            Rect bar = new Rect(r.x + 4f, py, r.width - 8f, 10f);
            Widgets.DrawBoxSolid(bar, new Color(0.25f, 0.17f, 0.08f, 0.30f));
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * (need > 0f ? Mathf.Clamp01(have / need) : 0f), bar.height), new Color(Gold.r, Gold.g, Gold.b, 0.9f));
            GUI.color = InkSoft; Widgets.DrawBox(bar, 1);
            py += 14f;
            List<SkillDef> skills = (AmuletDevelopment.MainCaste(amulet)?.xpSkills ?? new List<SkillDef>()).Concat(amulet.subclass.xpSkills).Distinct().ToList();
            ArtInk.Label(new Rect(r.x + 4f, py, r.width - 8f, 32f), "Learns from: " + string.Join(", ", skills.Select(k => k.label)));
            Text.Font = GameFont.Small;
            py += 40f;

            string why = selSlot ? AmuletDevelopment.SlotBlockReason(amulet)?.CapitalizeFirst() : InvestBlockReason();
            Rect b1 = new Rect(r.x, py, r.width, Mathf.Min(44f, r.width * 0.24f));
            if (Button(b1, selSlot ? "Awaken slot (" + S.slotCost + " points)" : "Invest point", why == null))
            {
                if (selSlot)
                {
                    if (AmuletDevelopment.TryOpenSlot(amulet)) SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                }
                else if (AmuletDevelopment.TryOpenNext(amulet, selBranch))
                {
                    SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                    if (selStone < S.stonesPerBranch) selStone++;
                }
            }
            if (why != null) TooltipHandler.TipRegion(b1, why);
            GUI.color = Color.white;
            // dev mode: points and experience at once
            if (Prefs.DevMode)
            {
                float dy = b1.yMax + 6f, dw = (r.width - 8f) / 3f;
                if (Widgets.ButtonText(new Rect(r.x, dy, dw, 24f), "DEV +1 pt")) DevPoints(1);
                if (Widgets.ButtonText(new Rect(r.x + dw + 4f, dy, dw, 24f), "DEV +10 pts")) DevPoints(10);
                if (Widgets.ButtonText(new Rect(r.x + 2f * (dw + 4f), dy, dw, 24f), "DEV +5k xp")) amulet.xpEarned += 5000f;
            }
        }

        private void DevPoints(int n)
        {
            int target = AmuletDevelopment.PointsEarned(amulet) + n;
            for (int g = 0; g < 100000 && AmuletDevelopment.PointsEarned(amulet) < target; g++) amulet.xpEarned += 250f;
        }

                private string InvestBlockReason()
        {
            if (selBranch == null) return "Select a stone first.";
            int next = AmuletDevelopment.Opened(amulet, selBranch) + 1;
            if (selStone < next) return "This stone is already open.";
            if (selStone > next && next <= S.stonesPerBranch) return "Stones open in order: the next one on this path is stone " + next + ".";
            string why = AmuletDevelopment.CannotOpenReason(amulet, selBranch);
            return why == null ? null : why.CapitalizeFirst() + ".";
        }

        private string StoneText(AmuletSubclassDef b, int stone)
        {
            var sb = new StringBuilder();
            float k = AmuletDevelopment.Strength(amulet, b, Wearer);
            ArtifactMilestoneInfo mi = S.IsMilestone(stone) ? b.MilestoneAt(stone) : null;
            if (mi != null) sb.Append("Milestone - ").Append(mi.label.CapitalizeFirst()).Append(":\n").Append(mi.description).Append("\n\n");
            if (!b.cellStats.NullOrEmpty())
            {
                int open = AmuletDevelopment.Opened(amulet, b);
                int total = Mathf.Max(open, stone);
                sb.Append(mi != null ? "The stone itself also gives" : "This stone gives").Append(" (strength x").Append(k.ToString("0.##")).Append("):");
                foreach (ArtifactCellStat c in b.cellStats) if (c.stat != null) sb.Append("\n  ").Append(c.stat.LabelCap).Append(' ').Append(Bonus(c, k));
                sb.Append(stone <= open ? "\n\nThe path now gives (" : "\n\nWith this stone the path gives (").Append(total).Append(" stones):");
                foreach (ArtifactCellStat c in b.cellStats) if (c.stat != null) sb.Append("\n  ").Append(c.stat.LabelCap).Append(' ').Append(Bonus(c, k * total));
            }
            if (!b.description.NullOrEmpty()) sb.Append("\n\nThe path: ").Append(b.description);
            for (int n = stone + 1; n <= S.stonesPerBranch; n++)
            {
                if (!S.IsMilestone(n)) continue;
                ArtifactMilestoneInfo next = b.MilestoneAt(n);
                if (next != null) sb.Append("\n\nNext milestone (stone ").Append(n).Append("): ").Append(next.label.CapitalizeFirst()).Append(" - ").Append(next.description);
                break;
            }
            bool maker = amulet.caste == S.makerCaste;
            sb.Append("\n\nEvery stone also grows the amulet: +").Append((S.radiusPerStone * (maker ? S.makerGrowthFactor : 1f)).ToString("0.##"))
              .Append(" telekinetic reach, +").Append((S.satchelPerStone * (maker ? S.makerGrowthFactor : 1f)).ToString("0.##")).Append(" kg satchel.");
            return sb.ToString();
        }

        private static string Bonus(ArtifactCellStat c, float k) =>
            c.offsetPerCell != 0f ? c.stat.ValueToString(c.offsetPerCell * k, ToStringNumberSense.Offset) : ArtifactFocus.Signed(c.factorPerCell * k);

        private static bool Button(Rect r, string label, bool enabled)
        {
            bool over = enabled && Mouse.IsOver(r);
            Texture2D tex = !over ? ArtifactoricsTex.ButtonNormal : Input.GetMouseButton(0) ? ArtifactoricsTex.ButtonPressed : ArtifactoricsTex.ButtonHover;
            GUI.color = enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            GUI.color = enabled ? Ink : InkGrey;
            Text.Anchor = TextAnchor.MiddleCenter;
            ArtInk.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (!enabled) return false;
            if (Widgets.ButtonInvisible(r)) { SoundDefOf.Click.PlayOneShotOnCamera(); return true; }
            return false;
        }
    }
}
