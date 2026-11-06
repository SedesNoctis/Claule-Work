using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The mage's scales over the pawn's panel at the left (the author and his brother): not among the
    /// gizmos but a long plaque of Arcana right above the panel's tabs (or above an open tab), and under it the scales
    /// of Life - each one there while it has a spell in the book or anything left in it. A new scale takes the place
    /// nearest the panel, the others rise quietly to make room.</summary>
    public static class MagicBars
    {
        public static bool On => AvirMod.Settings == null || AvirMod.Settings.magicBarsOnPane;

        public class Scale { public string key, label, tip; public float value, max; public Color fill; }

        private const float ArcanaH = 64f, ScaleH = 26f, Gap = 3f;
        private static readonly Dictionary<string, float> shownY = new Dictionary<string, float>();
        private static int pawnShown = -1;
        private static readonly System.Reflection.PropertyInfo tabRect = AccessTools.Property(typeof(InspectTabBase), "TabRect");
        private static Texture2D empty;
        private static readonly Dictionary<string, Texture2D> fills = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Texture2D> frames = new Dictionary<string, Texture2D>();

        /// <summary>The scales of Life this mage shows. One store today; the list is ready for a store of each meeting.</summary>
        public static List<Scale> Scales(Hediff_ArcaneCore core)
        {
            var list = new List<Scale>();
            if (core.LifeShown)
                list.Add(new Scale { key = "Life", label = "Life", value = core.vita, max = Mathf.Max(1f, core.VitaMax), fill = new Color(0.55f, 0.72f, 0.46f),
                    tip = "The store of life: what Withering took, ready to be given. Life makes nothing - it only moves (Symbiosis): no spell of Life gives anything with the store empty. It fades by 2% an hour, and what fades mends the mage and greens the plants by them. Past the top it spills over (Overflow)." });
            return list;
        }

        public static void Draw(IInspectPane pane)
        {
            if (!On || !AvirMagic.On || !(pane is MainTabWindow_Inspect) || Find.Selector.NumSelected != 1) return;
            if (!(Find.Selector.SingleSelectedThing is Pawn p) || p.Faction != Faction.OfPlayer) return;
            Hediff_ArcaneCore core = Mages.Core(p);
            if (core == null) return;
            if (pawnShown != p.thingIDNumber) { pawnShown = p.thingIDNumber; shownY.Clear(); }
            // above the tabs, or above the tab that is open
            float top = pane.PaneTopY - InspectPaneUtility.TabHeight;
            if (pane.OpenTabType != null)
            {
                InspectTabBase open = pane.CurTabs?.FirstOrDefault(t => t.GetType() == pane.OpenTabType);
                if (open != null && tabRect != null) top = Mathf.Min(top, ((Rect)tabRect.GetValue(open, null)).y);
            }
            float w = Mathf.Min(InspectPaneUtility.PaneWidthFor(pane), 480f);
            List<Scale> scales = Scales(core);
            float h = ArcanaH + scales.Count * (ScaleH + Gap);
            float y0 = top - h - 2f;
            if (y0 < 40f) return;   // no room (a tall tab open on a small screen)
            new Gizmo_Arcana { core = core }.DrawIn(new Rect(0f, y0, w, ArcanaH));
            // the scales of Life under the Arcana; the newest nearest the panel, the rest rise to make room
            float dt = Mathf.Min(0.1f, Time.deltaTime);
            for (int i = 0; i < scales.Count; i++)
            {
                Scale sc = scales[i];
                float want = y0 + ArcanaH + Gap + i * (ScaleH + Gap);
                float at = shownY.TryGetValue(sc.key, out float was) ? Mathf.MoveTowards(was, want, 160f * dt) : want + 12f;
                shownY[sc.key] = at;
                DrawScale(new Rect(0f, at, w, ScaleH), sc);
            }
            foreach (string gone in shownY.Keys.Where(k => !scales.Any(s => s.key == k)).ToList()) shownY.Remove(gone);
        }

        private static void DrawScale(Rect r, Scale sc)
        {
            if (empty == null) empty = SolidColorMaterials.NewSolidColorTexture(new Color(0.07f, 0.08f, 0.07f, 0.92f));
            if (!fills.TryGetValue(sc.key, out Texture2D fill)) fills[sc.key] = fill = SolidColorMaterials.NewSolidColorTexture(sc.fill);
            if (!frames.TryGetValue(sc.key, out Texture2D frame)) frames[sc.key] = frame = ContentFinder<Texture2D>.Get("UI/Magic/LifeBar_" + sc.key, false);
            Rect bar;
            if (frame != null)
            {
                // its own slim plaque (Лист 27): the window for the bar is 8-92% across, 25-75% down
                bar = new Rect(r.x + r.width * 0.08f, r.y + r.height * 0.25f, r.width * 0.84f, r.height * 0.5f);
                Widgets.FillableBar(bar, Mathf.Clamp01(sc.value / sc.max), fill, empty, false);
                GUI.DrawTexture(r, frame, ScaleMode.StretchToFill);
            }
            else
            {
                Widgets.DrawBoxSolid(r, new Color(0.13f, 0.11f, 0.08f, 0.92f));
                Widgets.DrawBoxSolid(new Rect(r.x, r.y, r.width, 1f), new Color(0.75f, 0.6f, 0.3f, 0.8f));
                Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 1f, r.width, 1f), new Color(0.75f, 0.6f, 0.3f, 0.8f));
                bar = new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f);
                Widgets.FillableBar(bar, Mathf.Clamp01(sc.value / sc.max), fill, empty, false);
            }
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.95f, 0.97f, 0.9f);
            Widgets.Label(bar, sc.label + "  " + Mathf.FloorToInt(sc.value) + " / " + Mathf.FloorToInt(sc.max));
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            if (!sc.tip.NullOrEmpty()) TooltipHandler.TipRegion(r, sc.tip);
        }
    }

    [HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.ExtraOnGUI))]
    public static class Patch_MagicBars
    {
        public static void Postfix(IInspectPane pane)
        {
            try { MagicBars.Draw(pane); }
            catch (Exception e) { Log.ErrorOnce("[Avir] magic bars: " + e, 0x3A61B5); }
        }
    }

    public static partial class LifeMagic
    {
        /// <summary>A spell with Life in it: a great circle of Life, or Life as its element.</summary>
        public static bool IsLifeSpell(RitualSchema s) =>
            s != null && (s.weave != null ? s.weave.All().Any(c => Is(c.element)) : Is(Spells.Element(s)));
    }
}
