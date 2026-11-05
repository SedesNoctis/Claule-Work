using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Avir.Artifactorics
{
    /// <summary>
    /// Inlay mode of the artificer's sheet:
    ///   left   - loose artifacts on the map
    ///   centre - the chosen pair, order buttons (inlay / extract) and this spot's orders
    ///   right  - garments: worn by colonists (default, a short list) or in storage (filtered:
    ///            no corpse gear, quality normal+, 50%+ hit points, best first, with a search)
    /// </summary>
    public partial class Dialog_ArtifactCreation
    {
        private bool inlayMode;
        private bool showStorage;
        private string search = "";
        private Thing pickArtifact;
        private Thing pickGarment;
        private Vector2 artScroll, garmentScroll, orderScroll;

        private const float StorageMinHp = 0.5f;

        private void ModeTabs(Rect r)
        {
            // The wax seal sits over the middle of the sheet and the corner flourishes over its
            // ends: two tabs between the left flourish and the seal, two between the seal and
            // the right flourish.
            float flourish = r.width * 0.15f;
            float seal = 58f;
            float span = r.width / 2f - flourish - seal;
            float w = span / 3f;
            Rect T(int k) => k < 3 ? new Rect(r.x + flourish + k * w, r.y, w, r.height)
                                   : new Rect(r.center.x + seal + (k - 3) * w, r.y, w, r.height);
            string[] names = { "Artifact", "Inlay", "Stones", "Amulets", "Items", "Buildings" };
            int cur = inlayMode ? 1 : stoneMode ? 2 : amuletMode ? 3 : itemsMode ? 4 : buildingsMode ? 5 : 0;
            Text.Font = GameFont.Medium;
            tabFont = names.All(n => Text.CalcSize(n).x + 8f <= w) ? GameFont.Medium : GameFont.Small;
            for (int k = 0; k < names.Length; k++)
            {
                int m = k;
                Tab(T(k), names[k], cur == k, () => SetMode(m));
            }
        }

        private GameFont tabFont = GameFont.Medium;

        private void Tab(Rect r, string label, bool active, System.Action onClick)
        {
            Text.Font = tabFont;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = active ? Ink : InkGrey;
            Widgets.Label(r, label);
            if (active)
            {
                GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.9f);
                Widgets.DrawLineHorizontal(r.x + 20f, r.yMax - 2f, r.width - 40f);
            }
            else if (Mouse.IsOver(r))
            {
                GUI.color = new Color(Ink.r, Ink.g, Ink.b, 0.4f);
                Widgets.DrawLineHorizontal(r.x + 20f, r.yMax - 2f, r.width - 40f);
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            if (!active && Widgets.ButtonInvisible(r))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                onClick();
            }
        }

        private MapComponent_ArtifactOrders Orders => MapComponent_ArtifactOrders.For(Map);

        // ------------------------------------------------------------------ left: artifacts

        private List<Thing> LooseArtifacts()
        {
            MapComponent_ArtifactOrders o = Orders;
            return Map.listerThings.ThingsOfDef(ArtifactoricsDefOf.Avir_Artifact)
                .Where(t => t.Spawned && (o == null || !o.HasOrderFor(t)))
                .OrderByDescending(t => t.MarketValue).ToList();
        }

        private void DrawArtifactColumn(Rect r)
        {
            Header(ref r, "Artifacts", "loose, on this map");
            List<Thing> arts = LooseArtifacts();
            if (pickArtifact != null && !arts.Contains(pickArtifact)) pickArtifact = null;
            if (arts.Count == 0)
            {
                GUI.color = InkSoft;
                Widgets.Label(new Rect(r.x + 4f, r.y, r.width - 8f, 60f), "No finished artifacts here. Make one in the creation sheet.");
                GUI.color = Color.white;
                return;
            }
            Rect view = ViewFor(r, arts.Count);
            Widgets.BeginScrollView(r, ref artScroll, view);
            float y = 0f;
            foreach (Thing t in arts)
            {
                Rect row = new Rect(0f, y, view.width, RowH - 4f);
                ArtifactState st = t.TryGetComp<CompArtifact>()?.state;
                if (t == pickArtifact) DrawSelection(row);
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                if (st != null) DrawMiniHeart(new Rect(row.x + 2f, row.y + (row.height - 34f) / 2f, 34f, 34f), st);
                GUI.color = Ink;
                Widgets.Label(new Rect(row.x + 40f, row.y + 1f, row.width - 40f, 22f),
                    st?.heartMetal != null ? st.heartMetal.LabelCap.ToString() + " heart" : "Artifact");
                GUI.color = InkSoft;
                Widgets.Label(new Rect(row.x + 40f, row.y + 22f, row.width - 40f, 22f),
                    st?.rimMetal != null ? st.rimMetal.label + " rim, " + st.settingQuality.GetLabel() : "");
                GUI.color = Color.white;
                if (st != null) TooltipHandler.TipRegion(row, st.InspectLines());
                if (Widgets.ButtonInvisible(row)) { pickArtifact = t; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                y += RowH;
            }
            Widgets.EndScrollView();
        }

        private static void DrawMiniHeart(Rect r, ArtifactState st)
        {
            if (st.rimMetal != null)
            {
                GUI.color = st.rimMetal.RimColor;
                GUI.DrawTexture(r, ArtifactoricsTex.HeartRimMask, ScaleMode.ScaleToFit);
            }
            if (st.heartMetal != null)
            {
                GUI.color = st.heartMetal.GemColorAt(Time.realtimeSinceStartup);
                GUI.DrawTexture(r, ArtifactoricsTex.GemBase, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
                GUI.DrawTexture(r, ArtifactoricsTex.GemHighlight, ScaleMode.ScaleToFit);
            }
            GUI.color = st.rimMetal != null ? st.rimMetal.RimColor : Color.white;
            GUI.DrawTexture(r, st.rimMetal != null ? ArtifactoricsTex.HeartSettingMask : ArtifactoricsTex.HeartSetting, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ right: garments

        private struct GarmentEntry
        {
            public Thing apparel;   // a garment or a melee weapon (stage 12)
            public Pawn wearer;
        }

        private List<GarmentEntry> Garments()
        {
            var list = new List<GarmentEntry>();
            string q = search?.Trim().ToLowerInvariant() ?? "";
            if (!showStorage)
            {
                foreach (Pawn p in Map.mapPawns.FreeColonistsSpawned.OrderBy(p => p.LabelShort))
                {
                    ThingWithComps weapon = p.equipment?.Primary;
                    if (weapon?.Socket() != null
                        && (q.Length == 0 || weapon.LabelNoCount.ToLowerInvariant().Contains(q) || p.LabelShort.ToLowerInvariant().Contains(q)))
                        list.Add(new GarmentEntry { apparel = weapon, wearer = p });
                    if (p.apparel == null) continue;
                    foreach (Apparel a in p.apparel.WornApparel)
                    {
                        if (a.Socket() == null) continue;
                        if (q.Length > 0 && !a.LabelNoCount.ToLowerInvariant().Contains(q) && !p.LabelShort.ToLowerInvariant().Contains(q)) continue;
                        list.Add(new GarmentEntry { apparel = a, wearer = p });
                    }
                }
                return list;
            }
            foreach (Thing a in Map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel)
                         .Concat(Map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)))
            {
                if (a == null || !a.Spawned || a.Socket() == null) continue;
                if ((a as Apparel)?.WornByCorpse == true || a.IsForbidden(Faction.OfPlayer)) continue;
                if (a.def.useHitPoints && (float)a.HitPoints / a.MaxHitPoints < StorageMinHp) continue;
                if (a.TryGetQuality(out QualityCategory qc) && qc < QualityCategory.Normal) continue;
                if (q.Length > 0 && !a.LabelNoCount.ToLowerInvariant().Contains(q)) continue;
                list.Add(new GarmentEntry { apparel = a });
            }
            return list
                .OrderByDescending(e => e.apparel.Socket().HasArtifact)
                .ThenByDescending(e => e.apparel.TryGetQuality(out QualityCategory qq) ? (int)qq : -1)
                .ThenByDescending(e => e.apparel.MarketValue)
                .Take(300).ToList();
        }

        private void DrawGarmentColumn(Rect r)
        {
            Header(ref r, "Item", "armour or a melee weapon");

            // Worn / storage switch
            float half = r.width / 2f;
            SmallToggle(new Rect(r.x, r.y - 6f, half - 2f, 24f), "Worn", !showStorage, () => showStorage = false);
            SmallToggle(new Rect(r.x + half + 2f, r.y - 6f, half - 2f, 24f), "In storage", showStorage, () => showStorage = true);
            r.yMin += 22f;
            search = Widgets.TextField(new Rect(r.x, r.y, r.width, 24f), search ?? "");
            if (search.NullOrEmpty())
            {
                GUI.color = InkGrey;
                Widgets.Label(new Rect(r.x + 6f, r.y + 2f, r.width, 22f), "search...");
                GUI.color = Color.white;
            }
            r.yMin += 30f;

            List<GarmentEntry> items = Garments();
            if (pickGarment != null && !items.Any(e => e.apparel == pickGarment)) pickGarment = null;
            if (items.Count == 0)
            {
                GUI.color = InkSoft;
                Widgets.Label(new Rect(r.x + 4f, r.y, r.width - 8f, 60f), showStorage ? "Nothing suitable in storage." : "No colonist wears anything here.");
                GUI.color = Color.white;
                return;
            }
            Rect view = ViewFor(r, items.Count);
            Widgets.BeginScrollView(r, ref garmentScroll, view);
            float y = 0f;
            MapComponent_ArtifactOrders orders = Orders;
            foreach (GarmentEntry e in items)
            {
                Rect row = new Rect(0f, y, view.width, RowH - 4f);
                Thing a = e.apparel;
                bool has = a.Socket().HasArtifact;
                bool ordered = orders != null && orders.HasOrderFor(a);
                if (a == pickGarment) DrawSelection(row);
                else if (Mouse.IsOver(row)) Widgets.DrawBoxSolid(row, new Color(1f, 0.9f, 0.6f, 0.14f));
                Widgets.ThingIcon(new Rect(row.x + 4f, row.y + (row.height - 30f) / 2f, 30f, 30f), a, 1f);
                GUI.color = Ink;
                Widgets.Label(new Rect(row.x + 40f, row.y + 1f, row.width - 40f, 22f), a.def.LabelCap);
                string sub = e.wearer != null ? e.wearer.LabelShort : "";
                if (ArtifactCarrierUtility.CarrierOf(a) == ArtifactCarrier.Weapon) sub = "weapon" + (sub.Length > 0 ? ", " : "") + sub;
                if (a.TryGetQuality(out QualityCategory qc)) sub += (sub.Length > 0 ? ", " : "") + qc.GetLabel();
                if (!showStorage && a.def.useHitPoints) { }
                else if (a.def.useHitPoints) sub += ", " + (100f * a.HitPoints / a.MaxHitPoints).ToString("0") + "%";
                if (has) sub = "artifact - " + sub;
                if (ordered) sub = "ordered - " + sub;
                GUI.color = has ? new Color(Gold.r * 0.8f, Gold.g * 0.7f, Gold.b * 0.5f) : InkSoft;
                Widgets.Label(new Rect(row.x + 40f, row.y + 22f, row.width - 40f, 22f), sub);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(row, a.LabelCap + (has ? "\n\n" + a.Socket().inlaid.InspectLines() : ""));
                if (Widgets.ButtonInvisible(row)) { pickGarment = a; SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
                y += RowH;
            }
            Widgets.EndScrollView();
        }

        private void SmallToggle(Rect r, string label, bool active, System.Action onClick)
        {
            if (active) DrawSelection(r);
            else if (Mouse.IsOver(r)) Widgets.DrawBoxSolid(r, new Color(1f, 0.9f, 0.6f, 0.14f));
            GUI.color = active ? Ink : InkSoft;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (!active && Widgets.ButtonInvisible(r)) { onClick(); SoundDefOf.Tick_High.PlayOneShotOnCamera(); }
        }

        // ------------------------------------------------------------------ centre

        private void DrawInlayCentre(Rect r, float flourish)
        {
            ModeTabs(new Rect(r.x, r.y, r.width, 34f));

            Thing garment = pickGarment;
            CompArtifactSocket socket = garment?.Socket();
            bool extracting = socket != null && socket.HasArtifact;
            ArtifactState st = extracting ? socket.inlaid : pickArtifact?.TryGetComp<CompArtifact>()?.state;

            float size = Mathf.Min(r.width * 0.44f, r.height * 0.36f);
            Rect heart = new Rect(r.center.x - size / 2f, r.y + 46f, size, size);
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.DrawTexture(heart.ExpandedBy(size * 0.18f), ArtifactoricsTex.HeartGuides, ScaleMode.ScaleToFit);
            GUI.color = Color.white;
            if (st != null) DrawMiniHeart(heart, st);
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.3f);
                GUI.DrawTexture(heart, ArtifactoricsTex.HeartSetting, ScaleMode.ScaleToFit);
                GUI.color = Color.white;
            }
            if (garment != null)
            {
                Rect gi = new Rect(heart.xMax + 10f, heart.yMax - 64f, 64f, 64f);
                Widgets.ThingIcon(gi, garment, 1f);
            }

            float y = heart.yMax + 14f;
            Rect text = new Rect(r.x + flourish * 0.6f, y, r.width - flourish * 1.2f, 70f);
            string line;
            bool warn = false;
            if (garment == null && pickArtifact == null) line = "Choose an artifact on the left and a garment or melee weapon on the right.";
            else if (garment == null) line = "Choose the garment or melee weapon it goes into.";
            else if (extracting)
                line = garment.LabelCap + " carries an artifact (" + st.ShortLabel() + ").\nAn Avir draws it out cleanly. Anyone else ruins the " + ArtifactCarrierUtility.Noun(garment) + ", and the artifact loses its outer cells (7-9).";
            else if (pickArtifact == null) line = "Choose the artifact for " + garment.LabelNoCount + ".";
            else
                line = "Inlay into " + garment.LabelNoCount + (EntryWearer(garment) != null ? ", " + (garment is Apparel ? "worn" : "wielded") + " by " + EntryWearer(garment).LabelShort + ": they will bring it to the table and take it back afterwards." : ".")
                       + (ArtifactCarrierUtility.CarrierOf(garment) == ArtifactCarrier.Weapon ? " In a weapon the artifact uses its weapon spiral and learns from Melee only." : "");
            MapComponent_ArtifactOrders orders = Orders;
            if (garment != null && orders != null && orders.HasOrderFor(garment)) { line = "There is already an order for " + garment.LabelNoCount + "."; warn = true; }
            GUI.color = warn ? Warn : InkSoft;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(text, line);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            float by = text.yMax + 4f;
            const float Gap = 24f;
            float bw = Mathf.Min(230f, (r.width - 2f * flourish - Gap) / 2f);
            float bh = bw * (117f / 480f);
            bool free = garment != null && (orders == null || !orders.HasOrderFor(garment));
            if (extracting)
            {
                if (ParchmentButton(new Rect(r.center.x - Gap / 2f - bw, by, bw, bh), "Extract (Avir only)", free))
                    PlaceOrder(new ArtifactOrder { kind = ArtifactOrderKind.Extract, target = garment, spot = spot, allowNonAvir = false });
                if (ParchmentButton(new Rect(r.center.x + Gap / 2f, by, bw, bh), "Extract (anyone)", free))
                    PlaceOrder(new ArtifactOrder { kind = ArtifactOrderKind.Extract, target = garment, spot = spot, allowNonAvir = true });
            }
            else
            {
                if (ParchmentButton(new Rect(r.center.x - bw / 2f, by, bw, bh), "Order inlay", free && pickArtifact != null))
                {
                    PlaceOrder(new ArtifactOrder { kind = ArtifactOrderKind.Inlay, artifact = pickArtifact, target = garment, spot = spot });
                    pickArtifact = null;
                }
            }

            float qy = by + bh + 8f;
            DrawOrderQueue(new Rect(r.x + flourish, qy, r.width - 2f * flourish, r.yMax - qy));
        }

        private Pawn EntryWearer(Thing a)
        {
            return ArtifactCarrierUtility.HolderOf(a);
        }

        private void PlaceOrder(ArtifactOrder o)
        {
            MapComponent_ArtifactOrders c = Orders;
            if (c == null) return;
            c.Add(o);
            SoundDefOf.Click.PlayOneShotOnCamera();
            Messages.Message("Ordered: " + o.Describe(), spot, MessageTypeDefOf.SilentInput, false);
        }

        private void DrawOrderQueue(Rect r)
        {
            MapComponent_ArtifactOrders c = Orders;
            List<ArtifactOrder> list = c == null ? new List<ArtifactOrder>() : c.orders.Where(o => o.spot == spot).ToList();
            GUI.color = InkSoft;
            Widgets.Label(new Rect(r.x, r.y, r.width, 22f), list.Count == 0 ? "No inlay orders here yet." : "Inlay orders here:");
            GUI.color = Color.white;
            if (list.Count == 0) return;
            Rect outer = new Rect(r.x, r.y + 22f, r.width, r.height - 22f);
            float h = list.Count * 22f;
            Rect view = new Rect(0f, 0f, h > outer.height ? outer.width - 16f : outer.width, h);
            Widgets.BeginScrollView(outer, ref orderScroll, view);
            float y = 0f;
            ArtifactOrder remove = null;
            foreach (ArtifactOrder o in list)
            {
                Rect line = new Rect(0f, y, view.width, 22f);
                GUI.color = Ink;
                string d = o.Describe();
                Widgets.Label(new Rect(line.x, line.y, line.width - 24f, 22f), d.Truncate(line.width - 24f));
                TooltipHandler.TipRegion(line, d);
                GUI.color = Color.white;
                if (Widgets.ButtonImage(new Rect(line.xMax - 20f, line.y + 3f, 16f, 16f), TexButton.Delete)) remove = o;
                y += 22f;
            }
            Widgets.EndScrollView();
            if (remove != null) c.Cancel(remove);
        }
    }
}
