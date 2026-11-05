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
    /// The ability codex drawn on the artificer's parchment: ability list (left), metal list
    /// (right, steel included as the pure ability), and in the centre what the chosen ability does
    /// and how the chosen metal changes it. Shared by the Stones sheet (which can order stones) and
    /// the standalone codex window.
    /// </summary>
    public class AbilityCodexView
    {
        public ArtifactAbilityDef ability;
        public ArtifactMetalDef metal;
        private Vector2 abilityScroll, metalScroll, textScroll;

        public static readonly Color Ink = new Color(0.03f, 0.018f, 0.008f);
        public static readonly Color InkSoft = new Color(0.10f, 0.06f, 0.025f);
        public static readonly Color InkGrey = new Color(0.22f, 0.17f, 0.12f);
        public static readonly Color Gold = new Color(0.70f, 0.50f, 0.12f);
        public static readonly Color Good = new Color(0.12f, 0.33f, 0.08f);
        public static readonly Color Warn = new Color(0.50f, 0.05f, 0.02f);
        private const float RowH = 52f;

        public static List<ArtifactAbilityDef> Abilities =>
            DefDatabase<ArtifactAbilityDef>.AllDefsListForReading.OrderBy(d => d.uiOrder).ToList();

        public void EnsureDefaults()
        {
            if (ability == null) ability = Abilities.FirstOrDefault();
            if (metal == null) metal = ArtifactoricsDatabase.Metals.FirstOrDefault(m => !m.allowedAsStone) ?? ArtifactoricsDatabase.Metals.FirstOrDefault();
        }

        public static void Header(ref Rect r, string title, string sub)
        {
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(r.x + 4f, r.y, r.width, 34f), title);
            Text.Font = GameFont.Small; GUI.color = InkSoft;
            ArtInk.Label(new Rect(r.x + 4f, r.y + 32f, r.width, 22f), sub);
            GUI.color = Color.white;
            r.yMin += 60f;
        }

        private static void Selection(Rect r)
        {
            Widgets.DrawBoxSolid(r, new Color(Gold.r, Gold.g, Gold.b, 0.20f));
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.95f);
            Widgets.DrawBox(r, 1);
            GUI.color = Color.white;
        }

        public void DrawAbilityList(Rect r)
        {
            EnsureDefaults();
            Header(ref r, "Ability", "what the stone will do");
            List<ArtifactAbilityDef> list = Abilities;
            Rect view = new Rect(0f, 0f, r.width - (list.Count * RowH > r.height ? 16f : 0f), list.Count * RowH);
            Widgets.BeginScrollView(r, ref abilityScroll, view);
            float y = 0f;
            foreach (ArtifactAbilityDef d in list)
            {
                Rect row = new Rect(0f, y, view.width, RowH - 4f);
                if (d == ability) Selection(row);
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(row.x + 2f, row.y + 2f, 44f, 44f), d.Icon, ScaleMode.ScaleToFit);
                GUI.color = Ink;
                ArtInk.Label(new Rect(row.x + 52f, row.y + 3f, row.width - 52f, 22f), d.LabelCap);
                GUI.color = InkSoft; Text.Font = GameFont.Tiny;
                ArtInk.Label(new Rect(row.x + 52f, row.y + 24f, row.width - 52f, 20f), d.combat ? "combat" : "peaceful");
                Text.Font = GameFont.Small; GUI.color = Color.white;
                if (Widgets.ButtonInvisible(row)) { ability = d; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                y += RowH;
            }
            Widgets.EndScrollView();
        }

        /// <summary>Simple metal list for the codex window (no stock lines).</summary>
        public void DrawMetalList(Rect r)
        {
            EnsureDefaults();
            Header(ref r, "Stone metal", "what it adds");
            List<ArtifactMetalDef> metals = ArtifactoricsDatabase.Metals;
            Rect view = new Rect(0f, 0f, r.width - (metals.Count * RowH > r.height ? 16f : 0f), metals.Count * RowH);
            Widgets.BeginScrollView(r, ref metalScroll, view);
            float y = 0f;
            foreach (ArtifactMetalDef m in metals)
            {
                Rect row = new Rect(0f, y, view.width, RowH - 4f);
                if (m == metal) Selection(row);
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                AbilityStoneUI.Draw(new Rect(row.x + 4f, row.y + 6f, 36f, 36f), m, m.allowedAsStone ? 1f : 0.55f);
                GUI.color = Ink;
                ArtInk.Label(new Rect(row.x + 46f, row.y + 3f, row.width - 46f, 22f), m.LabelCap);
                GUI.color = m.allowedAsStone ? InkSoft : InkGrey; Text.Font = GameFont.Tiny;
                ArtInk.Label(new Rect(row.x + 46f, row.y + 24f, row.width - 46f, 22f),
                    m.allowedAsStone ? ArtifactAbilities.MetalBonusText(m) : "pure ability - preview only");
                Text.Font = GameFont.Small; GUI.color = Color.white;
                if (Widgets.ButtonInvisible(row)) { metal = m; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                y += RowH;
            }
            Widgets.EndScrollView();
        }

        public static string TitleFor(ArtifactAbilityDef a, ArtifactMetalDef m)
        {
            string t = a?.LabelCap.ToString() ?? "Choose an ability";
            if (m != null) t += " - " + (m.allowedAsStone ? m.LabelCap.ToString() : "pure");
            return t;
        }

        /// <summary>Stone, title, description, and the numbers as the metal changes them.
        /// Returns the bottom of what was drawn.</summary>
        public void DrawDetail(Rect inner, float reserveBottom)
        {
            EnsureDefaults();
            float sz = Mathf.Min(120f, inner.height * 0.24f);
            Rect gem = new Rect(inner.center.x - sz / 2f, inner.y, sz, sz);
            AbilityStoneUI.Draw(gem, metal, 1f);
            if (ability != null)
                GUI.DrawTexture(new Rect(gem.center.x - sz * 0.3f, gem.center.y - sz * 0.3f, sz * 0.6f, sz * 0.6f), ability.Icon, ScaleMode.ScaleToFit);
            float y = gem.yMax + 6f;

            Text.Font = GameFont.Medium; GUI.color = Ink; Text.Anchor = TextAnchor.UpperCenter;
            ArtInk.Label(new Rect(inner.x, y, inner.width, 32f), TitleFor(ability, metal));
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            y += 34f;

            Rect textR = new Rect(inner.x, y, inner.width, inner.yMax - reserveBottom - y);
            string text = DetailText(ability, metal);
            float th = Text.CalcHeight(text, textR.width - 16f);
            Rect view = new Rect(0f, 0f, textR.width - 16f, th);
            GUI.color = InkSoft;
            Widgets.BeginScrollView(textR, ref textScroll, view);
            ArtInk.Label(view, text);
            Widgets.EndScrollView();
            GUI.color = Color.white;
        }

        public static string DetailText(ArtifactAbilityDef a, ArtifactMetalDef m)
        {
            if (a == null) return "";
            var sb = new StringBuilder();
            sb.Append(a.description);
            sb.Append("\n\n").Append(Changed(a, m));
            sb.Append("\n\nWorks in ").Append(a.WorksInText).Append('.');
            if (!a.onlyWeapons.NullOrEmpty())
                sb.Append(" Only in: ").Append(string.Join(", ", a.onlyWeapons.Select(w => w.label).Distinct())).Append('.');
            if (a.usesEnergy)
            {
                sb.Append("\n\nEnergy - chosen when the stone is made, fixed for good:");
                foreach (SpearEnergyDef e in DefDatabase<SpearEnergyDef>.AllDefsListForReading.OrderBy(x => x.uiOrder))
                    sb.Append("\n- ").Append(e.LabelCap).Append(" (").Append(e.metal?.label).Append("): ").Append(e.description).Append(' ').Append(e.NumbersText()).Append('.');
            }
            if (a.canAuto) sb.Append(" Can be set to use itself in a fight (stone menu), above the owner's charge threshold.");
            if (m != null && m.allowedAsStone)
            {
                foreach (ArtifactCarrier c in new[] { ArtifactCarrier.Apparel, ArtifactCarrier.Weapon })
                {
                    var focus = ArtifactoricsDatabase.ArmsFor(c)
                        .Select(x => new { x, f = ArtifactFocus.StoneFocus(m, x) }).Where(v => v.f > 0.0001f).ToList();
                    sb.Append(c == ArtifactCarrier.Apparel ? "\n\nTo the artifact in armour: " : "\nIn a weapon: ")
                      .Append(focus.Count > 0 ? string.Join(", ", focus.Select(v => v.x.label + " " + ArtifactFocus.Signed(v.f))) : "nothing").Append('.');
                }
            }
            else sb.Append("\n\nSteel cannot be an ability stone; this is the ability with no metal at all.");
            sb.Append("\n\nPower in play = stone quality x metal x the owner's psychic sensitivity x the artifact's development (+1% per point spent). "
                      + "Numbers above: a normal stone, an owner at 100% sensitivity, a fresh artifact.");
            return sb.ToString();
        }

        /// <summary>The ability's numbers with this metal, next to the pure numbers.</summary>
        public static string Changed(ArtifactAbilityDef a, ArtifactMetalDef m)
        {
            bool pure = m == null || !m.allowedAsStone;
            float power = pure ? 1f : m.stonePower * (a.combat ? m.stoneCombatPower : 1f);
            float cost = pure ? 1f : m.stoneCost;
            float cd = pure ? 1f : m.stoneCooldown;
            float rad = pure ? 1f : m.stoneRadius;
            float dur = pure ? 1f : m.stoneDuration;
            var lines = new List<string>();
            string Pair(float baseV, float k, string unit, string fmt = "0.##")
            {
                string b = baseV.ToString(fmt) + unit;
                if (Mathf.Abs(k - 1f) < 0.001f) return b;
                return (baseV * k).ToString(fmt) + unit + " (pure " + b + ")";
            }
            lines.Add("Power: x" + power.ToString("0.##") + (pure ? "" : (Mathf.Abs(power - 1f) < 0.001f ? "" : " (pure x1)")));
            if (a.chargeCost > 0f) lines.Add("Charge: " + Pair(a.chargeCost, cost, ""));
            if (a.chargePerSecond > 0f) lines.Add("Charge while held: " + Pair(a.chargePerSecond, cost, " per second"));
            if (a.baseCooldownTicks > 0) lines.Add("Cooldown: " + Pair(a.baseCooldownTicks / 60f, cd, " s", "0.#"));
            if (a.baseRadius > 0f) lines.Add("Radius: " + Pair(a.baseRadius, rad, " cells", "0.#"));
            if (a.baseDurationSeconds > 0f) lines.Add("Duration: " + Pair(a.baseDurationSeconds, dur, " s", "0.#"));
            string rule = pure ? null : ArtifactAbilities.PsychicRuleText(m);
            if (rule != null) lines.Add("Psychic metal: " + rule + ".");
            if (!pure && Mathf.Abs(m.stonePsychicScale - 1f) > 0.001f)
                lines.Add("The owner's psychic sensitivity counts x" + m.stonePsychicScale.ToString("0.##") + " - far stronger in a sensitive owner.");
            if (!a.numbers.NullOrEmpty()) lines.Add("\n" + a.numbers);
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// Stage 11 - the "Stones" sheet of the artificer's panel: the codex plus ordering. The metal
    /// list here shows stock and skill; steel can be picked to see the pure ability but not ordered.
    /// </summary>
    public partial class Dialog_ArtifactCreation
    {
        private bool stoneMode;
        private readonly AbilityCodexView codex = new AbilityCodexView();
        private Vector2 stoneMetalScroll;
        private SpearEnergyDef stoneEnergy;

        private void DrawAbilityColumn(Rect r) => codex.DrawAbilityList(r);

        private void DrawStoneMetalColumn(Rect r)
        {
            codex.EnsureDefaults();
            Header(ref r, "Stone metal", "the idea: intellectual");
            List<ArtifactMetalDef> metals = ArtifactoricsDatabase.Metals;
            Rect view = ViewFor(r, metals.Count);
            Widgets.BeginScrollView(r, ref stoneMetalScroll, view);
            float y = 0f;
            foreach (ArtifactMetalDef m in metals)
            {
                Rect row = new Rect(0f, y, view.width, RowH - 4f);
                RecipeDef rec = ArtifactCraftUtility.StoneRecipe(m);
                if (rec != null && !rec.AvailableNow) continue;   // not researched yet: not shown
                if (!m.allowedAsStone || rec == null)
                {
                    // preview only: the pure ability
                    if (m == codex.metal) DrawSelection(row);
                    else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                    AbilityStoneUI.Draw(new Rect(row.x + 4f, row.y + (row.height - 30f) / 2f, 30f, 30f), m, 0.55f);
                    GUI.color = Ink;
                    ArtInk.Label(new Rect(row.x + 40f, row.y + 1f, row.width - 40f, 22f), m.LabelCap);
                    GUI.color = InkGrey;
                    ArtInk.Label(new Rect(row.x + 40f, row.y + 22f, row.width - 40f, 22f), "pure ability - preview only");
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(row, "Steel cannot be an ability stone. Pick it to see the ability with no metal at all.");
                    if (Widgets.ButtonInvisible(row)) { codex.metal = m; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                }
                else if (MetalRow(row, m, rec, ArtifactCraftStep.Stone, m == codex.metal, false, null))
                    codex.metal = m;
                y += RowH;
            }
            Widgets.EndScrollView();
        }

        private void DrawStoneCentre(Rect r, float flourish)
        {
            ModeTabs(new Rect(r.x, r.y, r.width, 34f));
            Rect inner = new Rect(r.x + flourish, r.y + 44f, r.width - flourish * 2f, r.height - 44f);
            bool energyStone = codex.ability != null && codex.ability.usesEnergy;
            float reserve = energyStone ? 196f : 150f;
            codex.DrawDetail(inner, reserve);

            float bottom = inner.yMax - reserve;
            float bw = Mathf.Min(260f, inner.width - 20f);

            // stage 12d: the energy of a burning spear stone is chosen here and fixed for good
            List<SpearEnergyDef> energies = energyStone ? SpearEnergyDef.For(codex.metal).ToList() : null;
            if (energyStone)
            {
                if (stoneEnergy == null || !energies.Contains(stoneEnergy)) stoneEnergy = energies.FirstOrDefault();
                Rect er = new Rect(inner.x, bottom + 6f, inner.width, 34f);
                if (energies.Count == 0)
                {
                    GUI.color = Warn; Text.Anchor = TextAnchor.MiddleCenter;
                    ArtInk.Label(er, "Only an essentium or tisserium stone carries this energy.");
                    Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                }
                else
                {
                    float ew = Mathf.Min(200f, (er.width - 6f * (energies.Count - 1)) / energies.Count);
                    float ex = er.center.x - (ew * energies.Count + 6f * (energies.Count - 1)) / 2f;
                    foreach (SpearEnergyDef e in energies)
                    {
                        Rect b = new Rect(ex, er.y, ew, er.height);
                        bool on = e == stoneEnergy;
                        if (on) DrawSelection(b);
                        else if (Mouse.IsOver(b)) Widgets.DrawBoxSolid(b, new Color(1f, 0.9f, 0.6f, 0.14f));
                        Widgets.DrawBoxSolid(new Rect(b.x + 6f, b.y + 11f, 12f, 12f), e.color);
                        GUI.color = on ? Ink : InkSoft; Text.Anchor = TextAnchor.MiddleLeft;
                        ArtInk.Label(new Rect(b.x + 24f, b.y, b.width - 26f, b.height), e.LabelCap);
                        Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                        TooltipHandler.TipRegion(b, e.LabelCap + "\n\n" + e.description + "\n\n" + e.NumbersText() + " (x power).");
                        if (!on && Widgets.ButtonInvisible(b)) { stoneEnergy = e; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                        ex += ew + 6f;
                    }
                }
                bottom += 44f;
            }

            Rect order = new Rect(inner.center.x - bw / 2f, bottom + 6f, bw, 40f);
            RecipeDef rec = ArtifactCraftUtility.StoneRecipe(codex.metal);
            bool can = codex.ability != null && rec != null && codex.metal != null && codex.metal.allowedAsStone
                       && (!energyStone || stoneEnergy != null);
            if (ParchmentButton(order, can ? "Order stone" : energyStone && codex.metal != null && codex.metal.allowedAsStone ? "Needs essentium or tisserium" : "Choose a stone metal", can))
            {
                spot.BillStack.AddBill(new Bill_AbilityStone(rec, codex.ability, energyStone ? stoneEnergy : null));
                Messages.Message("Ordered: " + codex.ability.label + (energyStone ? " (" + stoneEnergy.label + ")" : "") + " stone of " + codex.metal.label + ".",
                    spot, MessageTypeDefOf.SilentInput, false);
            }
            DrawQueue(new Rect(inner.x, order.yMax + 8f, inner.width, inner.yMax - order.yMax - 8f));
        }
    }

    /// <summary>The ability codex on its own, in the same parchment frame.</summary>
    public class Dialog_AbilityCodex : Window
    {
        private readonly AbilityCodexView view = new AbilityCodexView();

        public Dialog_AbilityCodex(ArtifactAbilityDef ability = null, ArtifactMetalDef metal = null)
        {
            view.ability = ability;
            view.metal = metal;
            forcePause = true;
            doCloseX = false;
            doWindowBackground = false;
            drawShadow = false;
            draggable = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
        }

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

        private static Rect Sub(Rect r, float x0, float y0, float x1, float y1)
        {
            return new Rect(r.x + r.width * x0, r.y + r.height * y0, r.width * (x1 - x0), r.height * (y1 - y0));
        }

        public override void DoWindowContents(Rect inRect)
        {
            GUI.DrawTexture(inRect, ArtifactoricsTex.Panel, ScaleMode.StretchToFill);
            Rect close = new Rect(inRect.xMax - inRect.width * 0.052f - 26f, inRect.y + inRect.height * 0.085f, 24f, 24f);
            Rect left = Sub(inRect, 0.056f, 0.165f, 0.232f, 0.845f);
            Rect mid = Sub(inRect, 0.262f, 0.120f, 0.740f, 0.890f);
            Rect right = Sub(inRect, 0.770f, 0.165f, 0.944f, 0.845f);
            Color oc = GUI.color; GameFont of = Text.Font; TextAnchor oa = Text.Anchor;
            try
            {
                view.DrawAbilityList(left);
                view.DrawMetalList(right);
                float fl = inRect.width * 0.07f;
                GUI.color = AbilityCodexView.Ink; Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.UpperCenter;
                ArtInk.Label(new Rect(mid.x, mid.y + 38f, mid.width, 34f), "Ability codex");
                Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                view.DrawDetail(new Rect(mid.x + fl, mid.y + 80f, mid.width - fl * 2f, mid.height - 90f), 10f);
            }
            finally { GUI.color = oc; Text.Font = of; Text.Anchor = oa; }
            if (Widgets.CloseButtonFor(close)) Close();
        }
    }
}
