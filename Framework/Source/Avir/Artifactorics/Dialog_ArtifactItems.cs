using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Artifactorics
{
    /// <summary>Recipes of the artificer's table listed in its "Items" tab (crystal batteries,
    /// essentium, tisserium, cloudcloth...). A new one shows up by this extension alone.</summary>
    public class ArtificerItemRecipe : DefModExtension { }

    /// <summary>
    /// Items and Buildings tabs of the artificer's sheet.
    ///   Items     - list of item recipes (left), materials and hands (right) - for the battery
    ///               charge, the chargers by psychic sensitivity, best first - description and
    ///               order (centre).
    ///   Buildings - totems and idols: not made yet, a placeholder.
    /// </summary>
    public partial class Dialog_ArtifactCreation
    {
        private bool itemsMode, buildingsMode;
        private RecipeDef item;
        private bool allChargers;
        private int itemCount = 1;
        private Vector2 itemScroll, itemTextScroll, itemMatScroll;

        /// <summary>Marked item recipes, and the Avir's own weapons made at the table (their recipes are made by the game
        /// from the weapons' recipeMaker, so they cannot carry the mark).</summary>
        public static bool IsItemRecipe(RecipeDef r) =>
            r != null && (r.GetModExtension<ArtificerItemRecipe>() != null
                          || (r.ProducedThingDef is ThingDef d && d.IsWeapon && d.modContentPack != null && d.modContentPack == AvirMod.Pack));

        // unresearched ones are listed too, greyed with what they need, rather than missing without a word
        private List<RecipeDef> ItemRecipes => spot.def.AllRecipes.Where(r => IsItemRecipe(r) && (r.AvailableNow || MissingResearch(r) != null)).ToList();

        private void SetMode(int m)
        {
            inlayMode = m == 1; stoneMode = m == 2; amuletMode = m == 3; itemsMode = m == 4; buildingsMode = m == 5;
        }

        private bool IsCharge(RecipeDef r) => r == ArtifactBatteryDefOf.Avir_ChargeCrystalBattery;

        // ------------------------------------------------------------------ left

        private void DrawItemColumn(Rect r)
        {
            Header(ref r, "Items", "made at this table");
            List<RecipeDef> list = ItemRecipes;
            if (item == null || !list.Contains(item)) item = list.FirstOrDefault();
            Rect view = ViewFor(r, list.Count);
            Widgets.BeginScrollView(r, ref itemScroll, view);
            float y = 0f;
            foreach (RecipeDef rec in list)
            {
                Rect row = new Rect(0f, y, view.width, RowH - 4f);
                string missing = MissingResearch(rec);
                if (rec == item) DrawSelection(row);
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                ThingDef prod = rec.ProducedThingDef;
                GUI.color = missing != null ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
                if (prod != null) Widgets.DrawTextureFitted(new Rect(row.x + 2f, row.y + 2f, 40f, 40f), prod.uiIcon, 1f);
                GUI.color = missing != null ? InkGrey : Ink;
                ArtInk.Label(new Rect(row.x + 46f, row.y + 1f, row.width - 46f, 22f), rec.LabelCap.ToString().Truncate(row.width - 46f));
                Text.Font = GameFont.Tiny;
                GUI.color = missing != null ? Warn : InkSoft;
                string sub = missing != null ? "needs " + missing
                    : IsCharge(rec) ? "a charger gives " + ArtifactBattery.Cost.ToStringPercent() + " psychic sensitivity"
                    : prod != null ? prod.LabelCap.ToString() : "";
                ArtInk.Label(new Rect(row.x + 46f, row.y + 22f, row.width - 46f, 20f), sub);
                Text.Font = GameFont.Small; GUI.color = Color.white;
                if (Widgets.ButtonInvisible(row)) { item = rec; itemTextScroll = Vector2.zero; itemCount = 1; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                y += RowH;
            }
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------ right

        private void DrawItemRight(Rect r)
        {
            if (item != null && IsCharge(item)) { DrawChargers(r); return; }
            Header(ref r, "Materials", "what the order takes");
            if (item == null) return;
            var lines = new List<(string label, int need, int have, Texture2D icon)>();
            foreach (IngredientCount ing in item.ingredients)
            {
                ThingDef d = ing.filter.AnyAllowedDef;
                if (d == null) continue;
                lines.Add((ing.filter.Summary.CapitalizeFirst(), ing.CountRequiredOfFor(d, item), HaveOf(d), d.uiIcon));
            }
            Rect view = ViewFor(r, lines.Count + 2);
            Widgets.BeginScrollView(r, ref itemMatScroll, view);
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
            string skill = SkillText(item);
            Pawn best = BestAmuletMaker(item);
            GUI.color = InkSoft; Text.Font = GameFont.Tiny;
            ArtInk.Label(new Rect(4f, y + 4f, view.width - 8f, RowH * 2f - 8f),
                (skill != null ? "Skill: " + skill + "\n" : "") + (best != null ? "Best hands: " + best.LabelShort : "Nobody can make it yet."));
            Text.Font = GameFont.Small; GUI.color = Color.white;
            Widgets.EndScrollView();
        }

        /// <summary>Who can charge: colonists by psychic sensitivity, the best first (the list opens
        /// to everyone on request). The work is not tied to one pawn - whoever can give takes it.</summary>
        private void DrawChargers(Rect r)
        {
            Header(ref r, "Chargers", "psychic sensitivity, best first");
            if (Map == null) return;
            List<Pawn> all = Map.mapPawns.FreeColonists.OrderByDescending(p => ArtifactFocus.Sensitivity(p)).ToList();
            List<Pawn> shown = allChargers ? all : all.Take(1).ToList();
            Rect view = ViewFor(r, shown.Count + 1);
            Widgets.BeginScrollView(r, ref itemMatScroll, view);
            float y = 0f;
            foreach (Pawn p in shown)
            {
                Rect row = new Rect(0f, y, view.width, RowH - 4f);
                if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                Widgets.ThingIcon(new Rect(row.x + 2f, row.y + 2f, 40f, 40f), p);
                float give = ArtifactBattery.CanGive(p);
                float n = give / ArtifactBattery.Cost;
                GUI.color = Ink;
                ArtInk.Label(new Rect(row.x + 46f, row.y + 1f, row.width - 46f, 22f), p.LabelShort + " - " + ArtifactFocus.Sensitivity(p).ToStringPercent());
                Text.Font = GameFont.Tiny;
                GUI.color = ArtifactBattery.CanCharge(p) ? InkSoft : Warn;
                ArtInk.Label(new Rect(row.x + 46f, row.y + 22f, row.width - 46f, 20f),
                    ArtifactBattery.CanCharge(p) ? "can give " + give.ToStringPercent() + " now: " + (n * 100f).ToString("0") + "% of a battery" : "nothing to give now");
                Text.Font = GameFont.Small; GUI.color = Color.white;
                y += RowH;
            }
            Rect more = new Rect(4f, y + 4f, view.width - 8f, 24f);
            if (all.Count > 1 && Widgets.ButtonText(more, allChargers ? "Show only the best" : "Show everyone (" + all.Count + ")", false))
                allChargers = !allChargers;
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------ centre

        private void DrawItemCentre(Rect r, float flourish)
        {
            ModeTabs(new Rect(r.x, r.y, r.width, 34f));
            Rect inner = new Rect(r.x + flourish, r.y + 48f, r.width - flourish * 2f, r.height - 48f);
            if (item == null)
            {
                GUI.color = InkSoft; Text.Anchor = TextAnchor.MiddleCenter;
                ArtInk.Label(inner, "No item recipes at this table.");
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                return;
            }
            ThingDef prod = item.ProducedThingDef;
            float sz = Mathf.Min(110f, inner.height * 0.22f);
            if (prod != null) Widgets.DrawTextureFitted(new Rect(inner.center.x - sz / 2f, inner.y, sz, sz), prod.uiIcon, 1f);
            float y = inner.y + sz + 4f;
            Text.Font = GameFont.Medium; GUI.color = Ink; Text.Anchor = TextAnchor.UpperCenter;
            ArtInk.Label(new Rect(inner.x, y, inner.width, 32f), item.LabelCap);
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            y += 34f;

            const float Reserve = 190f;
            Rect textR = new Rect(inner.x, y, inner.width, inner.yMax - Reserve - y);
            var sb = new StringBuilder();
            // a weapon's own recipe text is the game's generic "Make ..." - the weapon's description and figures say more
            if (prod != null && prod.IsWeapon) sb.Append(prod.description).Append("\n\n").Append(WeaponStats(prod));
            else sb.Append(item.description.NullOrEmpty() ? prod?.description : item.description);
            if (prod == ArtifactBatteryDefOf.Avir_CrystalBattery || prod == ArtifactBatteryDefOf.Avir_CrystalBatteryEmpty)
                sb.Append("\n\nHolds ").Append(CompCrystalBattery.Capacity.ToString("0")).Append(" charge. Carried in the inventory (stacks by 5); ")
                  .Append("used by itself when an artifact drops below the wearer's recharge threshold, or at once by order. ")
                  .Append("It fills the emptiest artifact first, then the others. Empty batteries are put down and can be charged again here. A new battery comes off the table empty.");
            if (IsCharge(item))
                sb.Append("\n\nWhoever can give takes the work: ").Append(ArtifactBattery.Cost.ToStringPercent())
                  .Append(" psychic sensitivity fills a battery, less fills it partly (another can top it up later). What is given is lost for a day - like a recharge, never below 10% of the giver's own.");
            string text = sb.ToString();
            float th = Text.CalcHeight(text, textR.width - 16f);
            Rect view = new Rect(0f, 0f, th > textR.height ? textR.width - 16f : textR.width, th);
            GUI.color = InkSoft;
            Widgets.BeginScrollView(textR, ref itemTextScroll, view);
            ArtInk.Label(view, text);
            Widgets.EndScrollView();
            GUI.color = Color.white;

            // how many
            float cy = inner.yMax - Reserve + 6f;
            Rect cnt = new Rect(inner.center.x - 70f, cy, 140f, 26f);
            if (Widgets.ButtonText(new Rect(cnt.x, cnt.y, 26f, 26f), "-", false)) itemCount = Mathf.Max(1, itemCount - 1);
            GUI.color = Ink; Text.Anchor = TextAnchor.MiddleCenter;
            ArtInk.Label(new Rect(cnt.x + 26f, cnt.y, cnt.width - 52f, 26f), "x" + itemCount);
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            if (Widgets.ButtonText(new Rect(cnt.xMax - 26f, cnt.y, 26f, 26f), "+", false)) itemCount = Mathf.Min(30, itemCount + 1);

            string missing = MissingResearch(item);
            bool chargeOk = true;
            float bw = Mathf.Min(260f, inner.width - 20f);
            Rect order = new Rect(inner.center.x - bw / 2f, cy + 32f, bw, 40f);
            string label = missing != null ? "Research: " + missing
                : "Order";
            if (ParchmentButton(order, label, missing == null && chargeOk))
            {
                Bill_Production bill = (Bill_Production)item.MakeNewBill(null);
                bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
                bill.repeatCount = itemCount;
                spot.BillStack.AddBill(bill);
                Messages.Message("Ordered: " + item.label + " x" + itemCount + ".", spot, MessageTypeDefOf.SilentInput, false);
            }
            DrawQueue(new Rect(inner.x, order.yMax + 8f, inner.width, inner.yMax - order.yMax - 8f));
        }

        /// <summary>The weapon's figures at normal quality: each melee attack, a ranged shot if it has one, mass and value.</summary>
        private static string WeaponStats(ThingDef d)
        {
            var sb = new StringBuilder();
            if (d.tools != null && d.tools.Count > 0)
            {
                sb.Append("Melee (normal quality):");
                foreach (Tool t in d.tools)
                {
                    string kind = t.capacities == null ? "" : string.Join("/", t.capacities.Select(c =>
                        DefDatabase<ManeuverDef>.AllDefsListForReading.FirstOrDefault(m => m.requiredCapacity == c)?.verb?.meleeDamageDef?.label ?? c.label));
                    sb.Append("\n  - ").Append(kind.CapitalizeFirst()).Append(": ").Append(t.power.ToString("0.#"))
                      .Append(" damage, every ").Append(t.cooldownTime.ToString("0.0#")).Append(" s");
                    if (t.armorPenetration >= 0f) sb.Append(", armor penetration ").Append(t.armorPenetration.ToStringPercent());
                }
            }
            if (d.Verbs != null)
                foreach (VerbProperties v in d.Verbs)
                {
                    ProjectileProperties pr = v.defaultProjectile?.projectile;
                    if (v.IsMeleeAttack || pr == null) continue;
                    if (sb.Length > 0) sb.Append("\n\n");
                    sb.Append(v.verbClass == typeof(Verb_SpearFire) ? "Spear fire (with a burning spear stone in its artifact):" : "Ranged:");
                    sb.Append("\n  - ").Append((pr.damageDef?.label ?? "damage").CapitalizeFirst()).Append(": ").Append(pr.GetDamageAmount(d, null))
                      .Append(", armor penetration ").Append(pr.GetArmorPenetration(null).ToStringPercent())
                      .Append("\n  - Range ").Append(v.range.ToString("0")).Append(", aim ").Append(v.warmupTime.ToString("0.0#")).Append(" s");
                    if (v.burstShotCount > 1) sb.Append(", burst of ").Append(v.burstShotCount);
                }
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append("Mass ").Append(d.GetStatValueAbstract(StatDefOf.Mass).ToString("0.##")).Append(" kg, value ")
              .Append(d.GetStatValueAbstract(StatDefOf.MarketValue).ToStringMoney());
            return sb.ToString();
        }

        // ------------------------------------------------------------------ buildings (reference)
        // Built from the Architect (artifactorics); the tab tells what there is, what it needs and does.

        private ThingDef building;
        private Vector2 buildScroll, buildTextScroll;

        private static List<ThingDef> Buildings => DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.GetCompProperties<CompProperties_ArtifactHost>() != null && BuildingMissing(d) == null)
            .OrderBy(d => d.researchPrerequisites?.FirstOrDefault()?.baseCost ?? 0f).ThenBy(d => d.label).ToList();

        private static string BuildingMissing(ThingDef d) =>
            d.researchPrerequisites?.FirstOrDefault(rp => !rp.IsFinished)?.LabelCap;

        private void DrawBuildingsLeft(Rect r)
        {
            Header(ref r, "Buildings", "raised from the Architect");
            List<ThingDef> list = Buildings;
            if (building == null || !list.Contains(building)) building = list.FirstOrDefault();
            Rect view = ViewFor(r, list.Count);
            Widgets.BeginScrollView(r, ref buildScroll, view);
            float y = 0f;
            foreach (ThingDef d in list)
            {
                Rect row = new Rect(0f, y, view.width, RowH - 4f);
                string missing = BuildingMissing(d);
                if (d == building) DrawSelection(row);
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                GUI.color = missing != null ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
                Widgets.DrawTextureFitted(new Rect(row.x + 2f, row.y + 2f, 40f, 40f), d.uiIcon, 1f);
                GUI.color = missing != null ? InkGrey : Ink;
                ArtInk.Label(new Rect(row.x + 46f, row.y + 1f, row.width - 46f, 22f), d.LabelCap.ToString().Truncate(row.width - 46f));
                Text.Font = GameFont.Tiny;
                CompProperties_ArtifactHost hp = d.GetCompProperties<CompProperties_ArtifactHost>();
                GUI.color = missing != null ? Warn : InkSoft;
                ArtInk.Label(new Rect(row.x + 46f, row.y + 22f, row.width - 46f, 20f),
                    missing != null ? "needs " + missing : hp.sockets + " socket" + (hp.sockets == 1 ? "" : "s") + (hp.era.NullOrEmpty() ? "" : " · " + hp.era));
                Text.Font = GameFont.Small; GUI.color = Color.white;
                if (Widgets.ButtonInvisible(row)) { building = d; buildTextScroll = Vector2.zero; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                y += RowH;
            }
            Widgets.EndScrollView();
        }

        private void DrawBuildingsRight(Rect r)
        {
            Header(ref r, "To build", "materials and research");
            if (building == null) return;
            var sb = new System.Text.StringBuilder();
            if (building.MadeFromStuff)
                sb.Append(building.costStuffCount).Append(" of any ")
                  .Append(string.Join(" / ", building.stuffCategories.Select(c => c.label))).Append('\n');
            if (building.costList != null)
                foreach (ThingDefCountClass c in building.costList) sb.Append(c.count).Append(" ").Append(c.thingDef.label).Append('\n');
            if (building.researchPrerequisites != null)
                foreach (ResearchProjectDef rp in building.researchPrerequisites)
                    sb.Append("\nResearch: ").Append(rp.LabelCap).Append(rp.IsFinished ? " (done)" : " (not yet)");
            sb.Append("\n\nRaised empty from the Architect (artifactorics). Artifacts go in afterwards: select the building - Artifacts... An attuner brings and inlays them.");
            GUI.color = InkSoft;
            ArtInk.Label(new Rect(r.x + 4f, r.y, r.width - 8f, r.height), sb.ToString());
            GUI.color = Color.white;
        }

        private void DrawBuildingsCentre(Rect r, float flourish)
        {
            ModeTabs(new Rect(r.x, r.y, r.width, 34f));
            Rect inner = new Rect(r.x + flourish, r.y + 48f, r.width - flourish * 2f, r.height - 48f);
            if (building == null)
            {
                GUI.color = InkSoft; Text.Anchor = TextAnchor.MiddleCenter;
                ArtInk.Label(inner, "No artifact buildings yet.");
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
                return;
            }
            CompProperties_ArtifactHost hp = building.GetCompProperties<CompProperties_ArtifactHost>();
            GUI.color = Ink; Text.Font = GameFont.Medium;
            ArtInk.Label(new Rect(inner.x, inner.y, inner.width, 32f), building.LabelCap);
            Text.Font = GameFont.Small;
            float pic = Mathf.Min(inner.width * 0.4f, 160f);
            GUI.color = Color.white;
            Widgets.DrawTextureFitted(new Rect(inner.center.x - pic / 2f, inner.y + 38f, pic, pic), building.uiIcon, 1f);
            string text = building.description
                          + "\n\nSockets: " + hp.sockets + (hp.era.NullOrEmpty() ? "" : " (" + hp.era + ")")
                          + (hp.effectSummary.NullOrEmpty() ? "" : "\n\n" + hp.effectSummary)
                          + "\n\nAny artifact with a heart fits. Its heart quality and its metals decide how well the building works; it runs on the artifact's charge, far more slowly than when worn. When the building is destroyed or deconstructed its artifacts drop out whole.";
            Rect tr = new Rect(inner.x, inner.y + 46f + pic, inner.width, inner.yMax - inner.y - 46f - pic);
            float th = Text.CalcHeight(text, tr.width - 16f);
            Rect view = new Rect(0f, 0f, th > tr.height ? tr.width - 16f : tr.width, th);
            GUI.color = InkSoft;
            Widgets.BeginScrollView(tr, ref buildTextScroll, view);
            ArtInk.Label(view, text);
            Widgets.EndScrollView();
            GUI.color = Color.white;
        }
    }
}
