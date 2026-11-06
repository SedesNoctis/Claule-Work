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

        private const float ArcanaH = 84f, ScaleH = 40f, Gap = 2f;
        // the slim scales (Лист 26): the ends keep their shape, the length stretches; the band the bar lies on (measured)
        private static readonly float[] SlimCutsF = { 0f, 0.12f, 0.88f, 1f };
        private static readonly bool[] SlimStretch = { false, true, false };
        // the window cut in each slim scale (x0, y0, x1, y1 of the picture), the fill lies under it
        private static readonly Dictionary<string, Vector4> Win = new Dictionary<string, Vector4>
        {
            { "Life", new Vector4(0.115f, 0.2845f, 0.885f, 0.5915f) }, { "WildFlame", new Vector4(0.115f, 0.3071f, 0.885f, 0.6179f) },
            { "Blood", new Vector4(0.115f, 0.3162f, 0.885f, 0.6218f) }, { "Emerald", new Vector4(0.115f, 0.2758f, 0.885f, 0.6482f) },
            { "Pestilence", new Vector4(0.115f, 0.2818f, 0.885f, 0.5912f) },
        };
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
            foreach (RuneKind k in LifeMagic.AllKinds)
            {
                if (!core.StoreShown(k)) continue;
                list.Add(new Scale { key = k == LifeMagic.None ? "Life" : LifeMagic.PairName(k).Replace(" ", ""), label = LifeMagic.StoreName(k), value = core.Store(k), max = Mathf.Max(1f, core.VitaMax),
                    fill = k == LifeMagic.None ? new Color(0.55f, 0.72f, 0.46f) : LifeMagic.PairColor(k), tip = StoreTip(k) });
            }
            return list;
        }

        private static string StoreTip(RuneKind k)
        {
            string own = k == LifeMagic.None ? "The store of life: what Withering took, ready to be given."
                : "The store of " + LifeMagic.PairName(k) + ": what its Withering took, for its own giving.";
            return own + " Life makes nothing - it only moves (Symbiosis): no spell gives with its store empty. It fades by 2% an hour, and what fades mends the mage and greens the plants by them. Past the top it spills over (Overflow)."
                + "\nShown while a spell of it is in the book, or while anything is left in it. When its spells leave the book, half passes into the store that stays - the rest breaks out of the mage.";
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
                // its own slim plaque with a window: the fill under it, the frame over it, the words over all
                float[] cuts = SlimCutsF.Select(f => f * frame.width).ToArray();
                Vector4 wn = Win.TryGetValue(sc.key, out Vector4 w0) ? w0 : new Vector4(0.115f, 0.3f, 0.885f, 0.6f);
                float x0 = PlaqueArt.MapX(r, frame, cuts, SlimStretch, wn.x * frame.width), x1 = PlaqueArt.MapX(r, frame, cuts, SlimStretch, wn.z * frame.width);
                bar = new Rect(x0 - 2f, r.y + r.height * wn.y - 2f, x1 - x0 + 4f, r.height * (wn.w - wn.y) + 4f);
                GlassBar.Under(bar, sc.value / sc.max, sc.fill);
                PlaqueArt.Draw(r, frame, cuts, SlimStretch);
            }
            else
            {
                Widgets.DrawBoxSolid(r, new Color(0.13f, 0.11f, 0.08f, 0.92f));
                Widgets.DrawBoxSolid(new Rect(r.x, r.y, r.width, 1f), new Color(0.75f, 0.6f, 0.3f, 0.8f));
                Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 1f, r.width, 1f), new Color(0.75f, 0.6f, 0.3f, 0.8f));
                bar = new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f);
                GlassBar.Draw(bar, sc.value / sc.max, sc.fill);
            }
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            string words = sc.label + "  " + Mathf.FloorToInt(sc.value) + " / " + Mathf.FloorToInt(sc.max);
            Rect tr = new Rect(bar.x, bar.center.y - 10f, bar.width, 20f);
            GUI.color = new Color(0f, 0f, 0f, 0.85f); Widgets.Label(new Rect(tr.x + 1f, tr.y + 1f, tr.width, tr.height), words);
            GUI.color = new Color(0.95f, 0.97f, 0.9f); Widgets.Label(tr, words);
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
