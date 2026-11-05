using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.UI
{
    /// <summary>
    /// THE LOOM WINDOW, second design.
    ///
    /// WHAT CHANGED AND WHY. The first version drew the ornate PanelFrame five times - once for the
    /// window and once for each of four columns. That frame has a 96px inset, so every nested copy
    /// ate 96 pixels of carving on all four sides, and the borders ended up outweighing the
    /// contents. Here the frame is used ONCE, for the window itself. Interior zones are separated by
    /// a flat well fill and thin divider rules, and the only raised object is the selected-item
    /// card - because it is the only thing that should look raised.
    ///
    /// The fourth column went with the order queue. Crafting is immediate again and the button says
    /// CRAFT rather than QUEUE, which is what it said before I put a step in between. The "maintain
    /// a stock" mode survives as a checkbox in the detail panel, where it belongs; it never needed a
    /// column of its own.
    ///
    /// ONE ORDER AT A TIME. Building_AvirLoom still holds a list, but this window replaces its
    /// contents rather than appending, and shows what is running above the button. A queue is only
    /// worth its interface when jobs are long and numerous; here a craft is an hour and the player
    /// is standing right there.
    ///
    /// IMMEDIATE MODE, AS ALWAYS: every rectangle is computed as we go and nothing persists between
    /// frames. The arithmetic below IS the layout.
    /// </summary>
    public class Dialog_AvirLoom : Window
    {
        private const float Pad = 24f;
        private const float TopBar = 46f;
        private const float Gap = 18f;
        private const float ColCats = 168f;
        private const float ColDetail = 316f;
        private const float RowH = 30f;
        private const int MaxCraftCount = 100000;

        private readonly Building_AvirLoom loom;

        private ThingCategoryDef selectedCategory;
        private ThingDef selectedDef;
        private ThingDef selectedStuff;
        private QualityCategory selectedQuality = QualityCategory.Normal;
        private int craftCount = 1;
        private string craftCountBuffer;
        private bool maintain;
        private bool toStockpile = true;

        private string search = "";
        private string lastSearch;
        private ThingCategoryDef lastCategory;
        private List<ThingDef> filtered = new List<ThingDef>();
        private List<ThingDef> validStuffs = new List<ThingDef>();

        private Vector2 catScroll, listScroll;

        public override Vector2 InitialSize => new Vector2(1180f, 640f);

        public Dialog_AvirLoom(Building_AvirLoom loom)
        {
            this.loom = loom;
            doCloseX = false;
            doCloseButton = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            draggable = true;
            preventCameraMotion = false;
            doWindowBackground = false;
            drawShadow = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            AvirUI.Panel(new Rect(inRect.x - Margin, inRect.y - Margin,
                inRect.width + Margin * 2f, inRect.height + Margin * 2f));

            Rect area = inRect.ContractedBy(Pad);

            DrawTopBar(new Rect(area.x, area.y, area.width, TopBar));

            float py = area.y + TopBar;
            float ph = area.height - TopBar;
            float listW = area.width - ColCats - ColDetail - Gap * 2f;

            DrawCategories(new Rect(area.x, py, ColCats, ph));
            DrawList(new Rect(area.x + ColCats + Gap, py, listW, ph));
            DrawDetail(new Rect(area.xMax - ColDetail, py, ColDetail, ph));
        }

        private void DrawTopBar(Rect r)
        {
            AvirUI.Label(new Rect(r.x, r.y, 200f, 28f), "Avir_LoomTitle".Translate(),
                AvirUI.Gold, GameFont.Medium);

            Rect searchRect = new Rect(r.x + 190f, r.y + 1f, 300f, 26f);
            AvirUI.Well(searchRect);
            search = Widgets.TextField(searchRect.ContractedBy(4f), search);

            if (AvirUI.Button(new Rect(searchRect.xMax + 6f, r.y + 1f, 28f, 26f), "x"))
            {
                search = "";
                GUI.FocusControl(null);
            }

            if (AvirUI.Button(new Rect(searchRect.xMax + 40f, r.y + 1f, 140f, 26f),
                    "Avir_TransmuteButton".Translate()))
            {
                Find.WindowStack.Add(new Dialog_AvirTransmuteList(loom, SelectDef));
            }

            Rect close = new Rect(r.xMax - 26f, r.y + 1f, 26f, 23f);
            GUI.DrawTexture(close, AvirUI.CloseX);
            if (Widgets.ButtonInvisible(close)) Close();

            AvirUI.DividerAt(new Rect(r.x, r.yMax - 12f, r.width, 10f));
        }

        // ------------------------------------------------------------ categories

        private void DrawCategories(Rect r)
        {
            AvirUI.Well(r);
            AvirUI.Label(new Rect(r.x + 10f, r.y + 8f, r.width - 20f, 20f),
                "Avir_LoomCategories".Translate(), AvirUI.Gold, GameFont.Tiny);

            List<ThingCategoryDef> cats = RootCategories();
            Rect listRect = new Rect(r.x, r.y + 30f, r.width, r.height - 34f);
            Rect view = new Rect(0f, 0f, listRect.width - 18f, (cats.Count + 1) * 24f);

            Widgets.BeginScrollView(listRect, ref catScroll, view);

            float y = 0f;
            if (CategoryRow(new Rect(0f, y, view.width, 22f), "Avir_LoomAll".Translate(),
                    selectedCategory == null))
            {
                selectedCategory = null;
            }
            y += 24f;

            foreach (ThingCategoryDef cat in cats)
            {
                if (CategoryRow(new Rect(0f, y, view.width, 22f), cat.LabelCap, selectedCategory == cat))
                {
                    selectedCategory = selectedCategory == cat ? null : cat;
                }
                y += 24f;
            }

            Widgets.EndScrollView();
        }

        private bool CategoryRow(Rect r, string label, bool selected)
        {
            if (selected) AvirUI.Highlight(r);
            AvirUI.Label(new Rect(r.x + 12f, r.y + 2f, r.width - 12f, r.height), label,
                selected ? AvirUI.Gold : AvirUI.Parchment);
            return Widgets.ButtonInvisible(r);
        }

        /// <summary>Only categories with something craftable in them. A heading that leads to an
        /// empty list costs the player a click to discover.</summary>
        private List<ThingCategoryDef> RootCategories()
        {
            return DefDatabase<ThingCategoryDef>.AllDefsListForReading
                .Where(c => c.DescendantThingDefs != null
                            && c.DescendantThingDefs.Any(AvirCrafterFilter.CanCraft))
                .OrderBy(c => c.label)
                .ToList();
        }

        // ------------------------------------------------------------ list

        private void DrawList(Rect r)
        {
            AvirUI.Well(r);
            RebuildIfNeeded();

            AvirUI.Label(new Rect(r.x + 10f, r.y + 8f, 200f, 20f),
                "Avir_LoomItems".Translate(), AvirUI.Gold, GameFont.Tiny);
            AvirUI.Label(new Rect(r.xMax - 140f, r.y + 8f, 130f, 20f),
                "Avir_LoomCount".Translate(filtered.Count), AvirUI.Dim, GameFont.Tiny,
                TextAnchor.UpperRight);

            Rect listRect = new Rect(r.x, r.y + 30f, r.width, r.height - 34f);
            Rect view = new Rect(0f, 0f, listRect.width - 18f, filtered.Count * RowH);

            Widgets.BeginScrollView(listRect, ref listScroll, view);

            for (int i = 0; i < filtered.Count; i++)
            {
                Rect row = new Rect(0f, i * RowH, view.width, RowH - 2f);

                // Culling. Without it a two-thousand-row list draws every icon every frame.
                if (row.yMax < listScroll.y - RowH || row.y > listScroll.y + listRect.height + RowH)
                {
                    continue;
                }

                ThingDef def = filtered[i];
                if (def == selectedDef) AvirUI.Highlight(row);

                Rect icon = new Rect(row.x + 10f, row.y + 2f, 24f, 24f);
                AvirUI.Well(icon);
                Widgets.ThingIcon(icon.ContractedBy(2f), def);

                AvirUI.Label(new Rect(row.x + 42f, row.y, row.width - 150f, 16f), def.LabelCap,
                    def == selectedDef ? AvirUI.Gold : AvirUI.Parchment);
                AvirUI.Label(new Rect(row.x + 42f, row.y + 14f, row.width - 150f, 14f),
                    def.thingCategories?.FirstOrDefault()?.LabelCap ?? "", AvirUI.Dim, GameFont.Tiny);

                // Transmutable rows are priced in Feignallite, which is the constraint the player
                // plans around; showing watt-days for them would bury it.
                AvirTransmuteEntry entry = AvirTransmute.EntryFor(def);
                string right = entry != null
                    ? entry.feignallite + " FG"
                    : Building_AvirLoom.CostWattDays(def, null, QualityCategory.Normal).ToString("N0") + " Wd";

                AvirUI.Label(new Rect(row.xMax - 106f, row.y + 6f, 96f, 16f), right,
                    entry != null ? new Color(0.62f, 0.82f, 0.72f) : AvirUI.Amber,
                    GameFont.Tiny, TextAnchor.MiddleRight);

                if (Widgets.ButtonInvisible(row)) SelectDef(def);
            }

            Widgets.EndScrollView();
        }

        private void RebuildIfNeeded()
        {
            if (lastSearch == search && lastCategory == selectedCategory) return;
            lastSearch = search;
            lastCategory = selectedCategory;

            IEnumerable<ThingDef> source = selectedCategory != null
                ? selectedCategory.DescendantThingDefs
                : DefDatabase<ThingDef>.AllDefsListForReading;

            string q = search?.Trim().ToLowerInvariant();

            filtered = source
                .Where(AvirCrafterFilter.CanCraft)
                .Where(d => q.NullOrEmpty() || d.label.ToLowerInvariant().Contains(q))
                .Distinct()
                .OrderBy(d => d.label)
                .ToList();
        }

        private void SelectDef(ThingDef def)
        {
            selectedDef = def;
            validStuffs = def.MadeFromStuff
                ? GenStuff.AllowedStuffsFor(def).Where(AvirCrafterFilter.CanUseStuff).ToList()
                : new List<ThingDef>();
            selectedStuff = validStuffs.FirstOrDefault();
        }

        // ------------------------------------------------------------ detail

        private void DrawDetail(Rect r)
        {
            if (selectedDef == null)
            {
                AvirUI.Well(r);
                AvirUI.Label(r, "Avir_LoomNothingSelected".Translate(), AvirUI.Dim,
                    GameFont.Small, TextAnchor.MiddleCenter);
                return;
            }

            AvirTransmuteEntry transmute = AvirTransmute.EntryFor(selectedDef);
            float perCraft = Building_AvirLoom.CostWattDays(selectedDef, selectedStuff, selectedQuality);

            Rect card = new Rect(r.x, r.y, r.width, 104f);
            AvirUI.Card(card);
            Rect icon = new Rect(card.x + 18f, card.y + 20f, 62f, 62f);
            AvirUI.Well(icon);
            Widgets.ThingIcon(icon.ContractedBy(4f), selectedDef, selectedStuff);

            AvirUI.Label(new Rect(card.x + 92f, card.y + 20f, card.width - 104f, 20f),
                selectedDef.LabelCap, AvirUI.Gold);
            AvirUI.Label(new Rect(card.x + 92f, card.y + 42f, card.width - 104f, 20f),
                "Avir_LoomPowerCost".Translate(perCraft.ToString("N0")),
                loom.StoredOnNet >= perCraft ? AvirUI.Amber : new Color(0.80f, 0.45f, 0.40f));
            AvirUI.Label(new Rect(card.x + 92f, card.y + 62f, card.width - 104f, 18f),
                "Avir_LoomWorkTime".Translate(), AvirUI.Dim, GameFont.Tiny);

            float y = r.y + 120f;

            if (selectedDef.MadeFromStuff && validStuffs.Count > 0)
            {
                AvirUI.Label(new Rect(r.x, y, r.width, 16f), "Avir_LoomMaterial".Translate(),
                    AvirUI.Dim, GameFont.Tiny);
                y += 18f;
                if (AvirUI.Button(new Rect(r.x, y, r.width, 26f), selectedStuff?.LabelCap ?? "-"))
                {
                    Find.WindowStack.Add(new FloatMenu(validStuffs.Select(sd =>
                        new FloatMenuOption(sd.LabelCap, () => selectedStuff = sd)).ToList()));
                }
                y += 32f;
            }

            if (selectedDef.HasComp(typeof(CompQuality)))
            {
                AvirUI.Label(new Rect(r.x, y, r.width, 16f), "Avir_LoomQuality".Translate(),
                    AvirUI.Dim, GameFont.Tiny);
                y += 18f;

                QualityCategory[] all = (QualityCategory[])System.Enum.GetValues(typeof(QualityCategory));
                float bw = (r.width - 6f) / 4f;
                for (int i = 0; i < 4 && i < all.Length; i++)
                {
                    if (AvirUI.Button(new Rect(r.x + i * (bw + 2f), y, bw, 24f),
                            all[i].GetLabelShort().CapitalizeFirst(), all[i] == selectedQuality,
                            null, GameFont.Tiny))
                    {
                        selectedQuality = all[i];
                    }
                }
                y += 28f;

                float bw2 = (r.width - 8f) / 3f;
                for (int i = 4; i < all.Length; i++)
                {
                    if (AvirUI.Button(new Rect(r.x + (i - 4) * (bw2 + 4f), y, bw2, 24f),
                            all[i].GetLabelShort().CapitalizeFirst(), all[i] == selectedQuality,
                            null, GameFont.Tiny))
                    {
                        selectedQuality = all[i];
                    }
                }
                y += 36f;
            }

            AvirUI.Label(new Rect(r.x, y, r.width, 16f),
                transmute != null ? "Avir_LoomCraftsLabel".Translate() : "Avir_LoomCountLabel".Translate(),
                AvirUI.Dim, GameFont.Tiny);
            y += 18f;

            if (AvirUI.Button(new Rect(r.x, y, 30f, 26f), "-"))
            {
                craftCount = Mathf.Max(1, craftCount - 1);
                craftCountBuffer = null;
            }

            // Typable, with no practical ceiling. The count used to be a plain label, so the slider
            // was the only way to set it and its maximum was therefore a hard cap.
            Rect countRect = new Rect(r.x + 34f, y, r.width - 68f, 26f);
            AvirUI.Well(countRect);
            Widgets.TextFieldNumeric(countRect.ContractedBy(3f), ref craftCount, ref craftCountBuffer,
                1f, MaxCraftCount);

            if (AvirUI.Button(new Rect(r.xMax - 30f, y, 30f, 26f), "+"))
            {
                craftCount++;
                craftCountBuffer = null;
            }
            y += 32f;

            // The slider's span follows what was typed rather than clamping it; a fixed maximum
            // would silently drag a typed 3000 back down the moment the slider drew.
            int slid = Mathf.RoundToInt(
                AvirUI.Slider(new Rect(r.x, y, r.width, 22f), craftCount, 1f, Mathf.Max(100f, craftCount)));
            if (slid != craftCount)
            {
                craftCount = slid;
                craftCountBuffer = null;
            }
            y += 28f;

            // The count means different things for the two kinds of item, so the total is spelled
            // out. For a transmutation it counts CRAFTS and each yields a whole batch: 50 there is
            // 1750 gold, not 50. Leaving the player to work that out from three separate rows is
            // how 35,000 Wd came to look like the price of fifty gold.
            int crafts = Mathf.Max(1, craftCount);
            AvirUI.Label(new Rect(r.x, y, r.width, 16f),
                transmute != null
                    ? "Avir_LoomOrderTotalsTransmute".Translate(crafts, crafts * transmute.count,
                        selectedDef.label, crafts * transmute.feignallite,
                        (perCraft * crafts).ToString("N0"))
                    : "Avir_LoomOrderTotals".Translate(crafts, selectedDef.label,
                        (perCraft * crafts).ToString("N0")),
                AvirUI.Dim, GameFont.Tiny);
            y += 20f;

            if (transmute != null)
            {
                int cap = loom.Tank?.Props != null ? Mathf.RoundToInt(loom.Tank.Props.fuelCapacity) : 0;
                if (cap > 0 && crafts * transmute.feignallite > cap)
                {
                    AvirUI.Label(new Rect(r.x, y, r.width, 16f),
                        "Avir_LoomTankRefills".Translate(
                            Mathf.CeilToInt(crafts * transmute.feignallite / (float)cap)),
                        new Color(0.80f, 0.65f, 0.40f), GameFont.Tiny);
                    y += 18f;
                }
            }

            y += 6f;
            AvirUI.Checkbox(new Rect(r.x, y, r.width, 22f), ref maintain,
                "Avir_LoomMaintain".Translate(craftCount));
            y += 26f;

            if (AvirUI.Radio(new Rect(r.x, y, r.width / 2f, 20f), toStockpile,
                    "Avir_LoomToStockpile".Translate()))
            {
                toStockpile = true;
            }

            if (AvirUI.Radio(new Rect(r.x + r.width / 2f, y, r.width / 2f, 20f), !toStockpile,
                    "Avir_LoomDropHere".Translate()))
            {
                toStockpile = false;
            }

            DrawFooter(r);
        }

        /// <summary>
        /// Resource readouts and the craft button, pinned to the bottom of the panel so they do not
        /// shift as the middle grows and shrinks with the selected item.
        ///
        /// Both resources are shown together because either can block the loom, and until now the
        /// Feignallite level was only visible on the building's gizmo out on the map.
        /// </summary>
        private void DrawFooter(Rect r)
        {
            AvirUI.DividerAt(new Rect(r.x, r.yMax - 96f, r.width, 10f));

            AvirUI.Label(new Rect(r.x, r.yMax - 80f, r.width / 2f, 14f),
                "Avir_LoomBatteries".Translate(), AvirUI.Dim, GameFont.Tiny);
            AvirUI.Label(new Rect(r.x, r.yMax - 66f, r.width / 2f, 20f),
                loom.StoredOnNet.ToString("N0") + " Wd", AvirUI.Amber);

            int fuel = AvirTransmute.AvailableIn(loom.Tank);
            int cap = loom.Tank?.Props != null ? Mathf.RoundToInt(loom.Tank.Props.fuelCapacity) : 0;
            AvirUI.Label(new Rect(r.x + r.width / 2f, r.yMax - 80f, r.width / 2f, 14f),
                "Avir_LoomFuelLabel".Translate(), AvirUI.Dim, GameFont.Tiny);
            AvirUI.Label(new Rect(r.x + r.width / 2f, r.yMax - 66f, r.width / 2f, 20f),
                fuel + " / " + cap, new Color(0.62f, 0.82f, 0.72f));

            AvirLoomOrder running = loom.Current;
            if (running?.thingDef != null)
            {
                AvirUI.Label(new Rect(r.x, r.yMax - 44f, r.width - 70f, 16f),
                    "Avir_LoomRunning".Translate(running.thingDef.LabelCap,
                        Mathf.RoundToInt(loom.ProgressPercent * 100f)),
                    AvirUI.Parchment, GameFont.Tiny);

                if (AvirUI.Button(new Rect(r.xMax - 64f, r.yMax - 48f, 64f, 22f),
                        "Avir_LoomCancel".Translate(), false, new Color(0.78f, 0.50f, 0.45f),
                        GameFont.Tiny))
                {
                    loom.orders.Clear();
                }
            }

            // CRAFT replaces whatever was running. With one order at a time there is nothing to
            // append to, and queueing silently behind an unseen job is exactly the confusion the old
            // fourth column produced.
            if (AvirUI.Button(new Rect(r.x, r.yMax - 22f, r.width, 22f),
                    "Avir_LoomCraft".Translate(), true, AvirUI.Parchment))
            {
                loom.orders.Clear();
                loom.orders.Add(new AvirLoomOrder
                {
                    thingDef = selectedDef,
                    stuffDef = selectedStuff,
                    quality = selectedQuality,
                    targetCount = craftCount,
                    maintain = maintain,
                    toStockpile = toStockpile,
                });
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
        }
    }
}
