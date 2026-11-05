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
    /// The "Amulets" sheet of the artificer's panel, two views:
    ///   Avir amulet  - the traditional amulet: necklace (left), core (right), caste and what the
    ///                  metals give its paths (centre), like the artifact sheet;
    ///   Named amulets - the amulets with their own recipes (moved off the sculpting table): list
    ///                  (left), materials and hands (right), effects and order (centre).
    /// A named amulet is any recipe of this table whose product carries telekinesis - a new one
    /// shows up by its XML alone. The traditional amulet is made by the worker, so it has no product.
    /// </summary>
    public partial class Dialog_ArtifactCreation
    {
        private bool amuletMode;
        private bool namedAmulets;            // false = the traditional amulet
        private ArtifactMetalDef amNeck, amCore;
        private AmuletCasteDef amCaste;
        private Vector2 amNeckScroll, amCoreScroll, amPreviewScroll;
        private RecipeDef amulet;
        private Vector2 amuletScroll, amuletTextScroll, amuletMatScroll;

        public static bool IsAmuletRecipe(RecipeDef r)
        {
            return r?.ProducedThingDef?.GetCompProperties<CompProperties_Telekinesis>() != null;
        }

        private List<RecipeDef> AmuletRecipes =>
            spot.def.AllRecipes.Where(r => IsAmuletRecipe(r) && r.AvailableNow).ToList();

        private static string MissingResearch(RecipeDef r)
        {
            var need = new List<ResearchProjectDef>();
            if (r.researchPrerequisite != null) need.Add(r.researchPrerequisite);
            if (r.researchPrerequisites != null) need.AddRange(r.researchPrerequisites);
            var missing = need.Where(p => !p.IsFinished).Select(p => p.label).Distinct().ToList();
            return missing.Count == 0 ? null : string.Join(", ", missing);
        }

        private static string SkillText(RecipeDef r)
        {
            if (r.skillRequirements.NullOrEmpty()) return null;
            return string.Join(", ", r.skillRequirements.Select(s => s.skill.label + " " + s.minLevel + "+"));
        }

        private Pawn BestAmuletMaker(RecipeDef r)
        {
            if (Map == null) return null;
            return Map.mapPawns.FreeColonists
                .Where(p => !p.WorkTypeIsDisabled(WorkTypeDefOf.Crafting) && r.PawnSatisfiesSkillRequirements(p))
                .OrderByDescending(p => r.workSkill != null ? p.skills.GetSkill(r.workSkill).Level : 0)
                .FirstOrDefault();
        }

        private int HaveOf(ThingDef d)
        {
            if (Map == null || d == null) return 0;
            if (d.CountAsResource) return Map.resourceCounter.GetCount(d);
            return Map.listerThings.ThingsOfDef(d).Sum(t => t.stackCount);
        }

        // ------------------------------------------------------------------ dispatch

        private void DrawAmuletLeft(Rect r) { if (namedAmulets) DrawAmuletColumn(r); else DrawNecklaceColumn(r); }
        private void DrawAmuletRight(Rect r) { if (namedAmulets) DrawAmuletMaterials(r); else DrawCoreColumn(r); }
        private void DrawAmuletMiddle(Rect r, float flourish) { if (namedAmulets) DrawAmuletCentre(r, flourish); else DrawTraditionalCentre(r, flourish); }

        private void ViewSwitch(Rect r)
        {
            float w = Mathf.Min(170f, r.width / 2f - 4f);
            Rect a = new Rect(r.center.x - w - 4f, r.y, w, r.height);
            Rect b = new Rect(r.center.x + 4f, r.y, w, r.height);
            Tab(a, "Avir amulet", !namedAmulets, () => namedAmulets = false);
            Tab(b, "Named amulets", namedAmulets, () => namedAmulets = true);
        }

        // ------------------------------------------------------------------ traditional amulet

        private string AmuletMetalTip(ArtifactMetalDef m, ArtifactCraftStep step)
        {
            if (step == ArtifactCraftStep.Necklace)
                return "As a necklace: " + ArtifactFocus.Signed(m.amuletAllBonus) + " to every path of the amulet."
                       + (m.amuletMulticlass ? " The A'Shaign metal: with an essentium or tisserium core the amulet is A'Shaign, with any other an ordinary caste amulet." : " The A'Schialis metal.");
            var sb = new StringBuilder("As a core:");
            if (m.amuletAllBonus > 0f) sb.Append(" every path ").Append(ArtifactFocus.Signed(m.amuletAllBonus)).Append('.');
            foreach (AmuletFocusEntry e in m.amuletFocus.Where(e => e.subclass != null && e.factor > 0f).OrderByDescending(e => e.factor))
                sb.Append("\n  ").Append(e.subclass.LabelCap).Append(" (").Append(e.subclass.caste.label).Append(") ").Append(ArtifactFocus.Signed(e.factor));
            if (m.psychic != null) sb.Append('\n').Append(AmuletFocus.PsychicText(m).Trim());
            if (m.amuletNeedsNecklace != null) sb.Append("\nNeeds a ").Append(m.amuletNeedsNecklace.label).Append(" necklace.");
            return sb.ToString();
        }

        private void DrawNecklaceColumn(Rect r)
        {
            Header(ref r, "Necklace", "jeweller: crafting + artistic");
            List<ArtifactMetalDef> metals = ArtifactoricsDatabase.Metals.Where(m => m.allowedAsNecklace && AvirAmuletCraft.NecklaceRecipe(m) != null && AvirAmuletCraft.NecklaceRecipe(m).AvailableNow).ToList();
            if (amNeck == null || !metals.Contains(amNeck)) amNeck = metals.FirstOrDefault();
            Rect view = ViewFor(r, metals.Count);
            Widgets.BeginScrollView(r, ref amNeckScroll, view);
            float y = 0f;
            foreach (ArtifactMetalDef m in metals)
            {
                if (MetalRow(new Rect(0f, y, view.width, RowH - 4f), m, AvirAmuletCraft.NecklaceRecipe(m), ArtifactCraftStep.Necklace, m == amNeck, false, null))
                {
                    amNeck = m;
                    if (AvirAmuletCraft.CoreBlockReason(amNeck, amCore) != null) amCore = null;
                }
                y += RowH;
            }
            Widgets.EndScrollView();
        }

        private void DrawCoreColumn(Rect r)
        {
            Header(ref r, "Core", "the idea: intellectual");
            List<ArtifactMetalDef> metals = ArtifactoricsDatabase.Metals.Where(m => m.allowedAsAmuletCore && AvirAmuletCraft.CoreRecipe(m) != null && AvirAmuletCraft.CoreRecipe(m).AvailableNow).ToList();
            Rect view = ViewFor(r, metals.Count);
            Widgets.BeginScrollView(r, ref amCoreScroll, view);
            float y = 0f;
            foreach (ArtifactMetalDef m in metals)
            {
                string why = AvirAmuletCraft.CoreBlockReason(amNeck, m);
                if (MetalRow(new Rect(0f, y, view.width, RowH - 4f), m, AvirAmuletCraft.CoreRecipe(m), ArtifactCraftStep.AmuletCore, m == amCore, why != null, why))
                    amCore = m;
                y += RowH;
            }
            Widgets.EndScrollView();
        }

        private void DrawTraditionalCentre(Rect r, float flourish)
        {
            ModeTabs(new Rect(r.x, r.y, r.width, 34f));
            float y = r.y + 38f;
            ViewSwitch(new Rect(r.x + flourish, y, r.width - 2f * flourish, 28f));
            y += 34f;

            // caste: chosen with the core, for good - except the A'Shaign, who have none
            bool ashai = amNeck != null && amNeck.MulticlassWith(amCore);
            List<AmuletCasteDef> castes = ashai ? new List<AmuletCasteDef>() : AmuletCasteDef.All;
            if (amCaste == null && !ashai) amCaste = castes.FirstOrDefault();
            if (ashai)
            {
                GUI.color = Ink; Text.Anchor = TextAnchor.MiddleCenter;
                ArtInk.Label(new Rect(r.x + flourish, y, r.width - 2f * flourish, 30f), "A'Shaign - every path, the main one chosen later");
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
            float cw = Mathf.Min(150f, (r.width - 2f * flourish - 8f * (castes.Count - 1)) / Mathf.Max(1, castes.Count));
            float cx = r.center.x - (cw * castes.Count + 8f * (castes.Count - 1)) / 2f;
            foreach (AmuletCasteDef c in castes)
            {
                Rect b = new Rect(cx, y, cw, 30f);
                bool on = c == amCaste;
                if (on) DrawSelection(b);
                else if (Mouse.IsOver(b)) Widgets.DrawBoxSolid(b, new Color(1f, 0.9f, 0.6f, 0.14f));
                GUI.color = on ? Ink : InkSoft; Text.Anchor = TextAnchor.MiddleCenter;
                ArtInk.Label(b, c.LabelCap);
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                TooltipHandler.TipRegion(b, c.LabelCap + "\n\n" + c.description + "\n\nChosen with the core; it never changes.");
                if (!on && Widgets.ButtonInvisible(b)) { amCaste = c; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                cx += cw + 8f;
            }
            y += 38f;

            // the amulet: necklace as the rim, core as the gem
            float size = Mathf.Min(r.width * 0.36f, r.height * 0.20f);
            Rect pic = new Rect(r.center.x - size / 2f, y, size, size);
            Texture2D neckTex = ArtifactoricsTex.AmuletNecklace ?? ArtifactoricsTex.HeartRimMask;
            GUI.color = amNeck != null ? amNeck.RimColor : new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(pic, neckTex, ScaleMode.ScaleToFit);
            if (ArtifactoricsTex.AmuletGem != null)
            {
                GUI.color = amCore != null ? amCore.GemColorAt(Time.realtimeSinceStartup) : new Color(0.22f, 0.22f, 0.24f);
                GUI.DrawTexture(pic, ArtifactoricsTex.AmuletGem, ScaleMode.ScaleToFit);
            }
            else if (amCore != null)
            {
                GUI.color = amCore.GemColorAt(Time.realtimeSinceStartup);
                GUI.DrawTexture(pic, ArtifactoricsTex.GemBase, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                GUI.DrawTexture(pic, ArtifactoricsTex.GemHighlight, ScaleMode.ScaleToFit);
            }
            GUI.color = Color.white;
            y = pic.yMax + 6f;

            // what it will give
            const float Reserve = 150f;
            Rect textR = new Rect(r.x + flourish, y, r.width - 2f * flourish, r.yMax - Reserve - y);
            string text = TraditionalText();
            float th = Text.CalcHeight(text, textR.width - 16f);
            Rect view = new Rect(0f, 0f, th > textR.height ? textR.width - 16f : textR.width, th);
            GUI.color = InkSoft;
            Widgets.BeginScrollView(textR, ref amPreviewScroll, view);
            ArtInk.Label(view, text);
            Widgets.EndScrollView();
            GUI.color = Color.white;

            // orders
            RecipeDef neckRec = AvirAmuletCraft.NecklaceRecipe(amNeck);
            RecipeDef coreRec = amCore != null && AvirAmuletCraft.CoreBlockReason(amNeck, amCore) == null ? AvirAmuletCraft.CoreRecipe(amCore) : null;
            const float Gap = 24f;
            float bw = Mathf.Min(230f, (r.width - 2f * flourish - Gap) / 2f);
            float bh = bw * (117f / 480f);
            float by = r.yMax - Reserve + 6f;
            if (ParchmentButton(new Rect(r.center.x - Gap / 2f - bw, by, bw, bh), "Order necklace", neckRec != null))
                OrderSetting(neckRec);
            if (ParchmentButton(new Rect(r.center.x + Gap / 2f, by, bw, bh), "Order necklace + core", neckRec != null && coreRec != null && (ashai || amCaste != null)))
            {
                AmuletCasteDef caste = ashai ? null : amCaste;
                OrderSetting(neckRec);
                spot.BillStack.AddBill(new Bill_AmuletCore(coreRec, amNeck, caste));
                Messages.Message("Ordered: " + coreRec.label + (caste != null ? " (" + caste.label + ")" : " (A'Shaign)") + ".", spot, MessageTypeDefOf.SilentInput, false);
            }
            DrawQueue(new Rect(r.x + flourish, by + bh + 8f, r.width - 2f * flourish, r.yMax - by - bh - 8f));
        }

        private string TraditionalText()
        {
            var sb = new StringBuilder();
            sb.Append(amNeck != null ? amNeck.LabelCap + " necklace" : "No necklace").Append(" · ")
              .Append(amCore != null ? amCore.LabelCap + " core" : "no core yet").Append(" · ")
              .Append(amNeck != null && amNeck.MulticlassWith(amCore) ? "A'Shaign" : amCaste != null ? amCaste.LabelCap.ToString() : "no caste");
            if (amNeck != null && amNeck.MulticlassWith(amCore))
            {
                sb.Append("\n\nA'Shaign amulet: no caste. It writes every path of every caste; the main one is chosen when its development is first opened.");
                sb.Append(" Only an Avir can wear it. A silver necklace is A'Shaign only with an essentium or tisserium core; with any other core it is an ordinary caste amulet.");
                if (amCore?.psychic != null) sb.Append("\n").Append(AmuletFocus.PsychicText(amCore));
            }
            else if (amCaste != null)
            {
                sb.Append("\n\nPaths it will favour (necklace and core together):\n");
                sb.Append(AmuletFocus.PreviewText(amNeck, amCore, amCaste));
                if (amCore == null) sb.Append("\n  Choose a core: it gives most of the focus.");
            }
            sb.Append("\n\nThe subclass is chosen when the amulet's development is first opened. Focus raises the strength of that path's bonuses.");
            sb.Append("\nEvery Avir amulet: telekinetic reach 10, subspace satchel 10 kg, work speed x1.1.");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ named amulets

        private void DrawAmuletColumn(Rect r)
        {
            Header(ref r, "Amulet", "the jeweller: crafting");
            List<RecipeDef> list = AmuletRecipes;
            if (amulet == null || !list.Contains(amulet)) amulet = list.FirstOrDefault();
            Rect view = ViewFor(r, list.Count);
            Widgets.BeginScrollView(r, ref amuletScroll, view);
            float y = 0f;
            foreach (RecipeDef rec in list)
            {
                Rect row = new Rect(0f, y, view.width, RowH - 4f);
                string missing = MissingResearch(rec);
                if (rec == amulet) DrawSelection(row);
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                ThingDef prod = rec.ProducedThingDef;
                GUI.color = missing != null ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
                Widgets.DrawTextureFitted(new Rect(row.x + 2f, row.y + 2f, 40f, 40f), prod.uiIcon, 1f);
                GUI.color = missing != null ? InkGrey : Ink;
                ArtInk.Label(new Rect(row.x + 46f, row.y + 1f, row.width - 46f, 22f), prod.LabelCap.Truncate(row.width - 46f));
                Text.Font = GameFont.Tiny;
                GUI.color = missing != null ? Warn : InkSoft;
                CompProperties_Telekinesis tk = prod.GetCompProperties<CompProperties_Telekinesis>();
                ArtInk.Label(new Rect(row.x + 46f, row.y + 22f, row.width - 46f, 20f),
                    missing != null ? "needs " + missing : tk.tierLabel + " · reach " + tk.radius.ToString("0"));
                Text.Font = GameFont.Small; GUI.color = Color.white;
                if (Widgets.ButtonInvisible(row)) { amulet = rec; amuletTextScroll = Vector2.zero; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                y += RowH;
            }
            Widgets.EndScrollView();
        }

        private void DrawAmuletMaterials(Rect r)
        {
            Header(ref r, "Materials", "what the order takes");
            if (amulet == null) return;
            var lines = new List<(string label, int need, int have, Texture2D icon)>();
            foreach (IngredientCount ing in amulet.ingredients)
            {
                ThingDef d = ing.filter.AnyAllowedDef;
                if (d == null) continue;
                lines.Add((ing.filter.Summary.CapitalizeFirst(), ing.CountRequiredOfFor(d, amulet), HaveOf(d), d.uiIcon));
            }
            Rect view = ViewFor(r, lines.Count + 2);
            Widgets.BeginScrollView(r, ref amuletMatScroll, view);
            float y = 0f;
            foreach (var l in lines)
            {
                Rect row = new Rect(0f, y, view.width, RowH - 4f);
                GUI.color = Color.white;
                if (l.icon != null) Widgets.DrawTextureFitted(new Rect(row.x + 4f, row.y + 7f, 30f, 30f), l.icon, 1f);
                GUI.color = Ink;
                ArtInk.Label(new Rect(row.x + 40f, row.y + 1f, row.width - 40f, 22f), l.label.Truncate(row.width - 40f));
                GUI.color = l.have < l.need ? Warn : InkSoft;
                ArtInk.Label(new Rect(row.x + 40f, row.y + 21f, row.width - 40f, 22f), "need " + l.need + ", have " + l.have);
                y += RowH;
            }
            string skill = SkillText(amulet);
            Pawn best = BestAmuletMaker(amulet);
            GUI.color = InkSoft; Text.Font = GameFont.Tiny;
            ArtInk.Label(new Rect(4f, y + 4f, view.width - 8f, RowH * 2f - 8f),
                (skill != null ? "Skill: " + skill + "\n" : "")
                + (best != null ? "Best hands: " + best.LabelShort : "Nobody can make it yet."));
            Text.Font = GameFont.Small; GUI.color = Color.white;
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------ centre

        private void DrawAmuletCentre(Rect r, float flourish)
        {
            ModeTabs(new Rect(r.x, r.y, r.width, 34f));
            ViewSwitch(new Rect(r.x + flourish, r.y + 38f, r.width - 2f * flourish, 28f));
            Rect inner = new Rect(r.x + flourish, r.y + 72f, r.width - flourish * 2f, r.height - 72f);
            if (amulet == null)
            {
                GUI.color = InkSoft; Text.Anchor = TextAnchor.MiddleCenter;
                ArtInk.Label(inner, "No amulet recipes at this table.");
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                return;
            }
            ThingDef prod = amulet.ProducedThingDef;

            float sz = Mathf.Min(110f, inner.height * 0.22f);
            Widgets.DrawTextureFitted(new Rect(inner.center.x - sz / 2f, inner.y, sz, sz), prod.uiIcon, 1f);
            float y = inner.y + sz + 4f;
            Text.Font = GameFont.Medium; GUI.color = Ink; Text.Anchor = TextAnchor.UpperCenter;
            ArtInk.Label(new Rect(inner.x, y, inner.width, 32f), prod.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            y += 34f;

            const float Reserve = 170f;
            Rect textR = new Rect(inner.x, y, inner.width, inner.yMax - Reserve - y);
            string text = AmuletText(prod);
            float th = Text.CalcHeight(text, textR.width - 16f);
            Rect view = new Rect(0f, 0f, th > textR.height ? textR.width - 16f : textR.width, th);
            GUI.color = InkSoft;
            Widgets.BeginScrollView(textR, ref amuletTextScroll, view);
            ArtInk.Label(view, text);
            Widgets.EndScrollView();
            GUI.color = Color.white;

            string missing = MissingResearch(amulet);
            float bw = Mathf.Min(260f, inner.width - 20f);
            Rect order = new Rect(inner.center.x - bw / 2f, inner.yMax - Reserve + 8f, bw, 40f);
            if (ParchmentButton(order, missing == null ? "Order amulet" : "Research: " + missing, missing == null))
            {
                spot.BillStack.AddBill(amulet.MakeNewBill(null));
                Messages.Message("Ordered: " + amulet.label + ".", spot, MessageTypeDefOf.SilentInput, false);
            }
            DrawQueue(new Rect(inner.x, order.yMax + 8f, inner.width, inner.yMax - order.yMax - 8f));
        }

        private static string AmuletText(ThingDef prod)
        {
            var sb = new StringBuilder();
            sb.Append(prod.description);
            CompProperties_Telekinesis tk = prod.GetCompProperties<CompProperties_Telekinesis>();
            if (tk != null)
            {
                sb.Append("\n\n").Append(tk.tierLabel).Append(':');
                sb.Append("\n  Telekinetic reach: ").Append(tk.radius.ToString("0")).Append(" cells - things whose storage is also in reach move by themselves.");
                if (Mathf.Abs(tk.workSpeedFactor - 1f) > 0.001f)
                    sb.Append("\n  Work speed: x").Append(tk.workSpeedFactor.ToString("0.##")).Append('.');
                if (tk.satchelCapacityKg > 0f)
                    sb.Append("\n  Subspace satchel: ").Append(tk.satchelCapacityKg.ToString("0.#"))
                      .Append(" kg outside carrying capacity. Picks up loose things in passing and sets them into storage the wearer walks by.");
            }
            return sb.ToString();
        }
    }
}
