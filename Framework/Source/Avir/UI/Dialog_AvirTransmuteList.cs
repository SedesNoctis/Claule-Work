using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.UI
{
    /// <summary>
    /// A reference sheet: everything the loom can make from Feignallite, and what each costs.
    ///
    /// WHY A SEPARATE WINDOW WHEN THE DETAIL PANEL ALREADY SHOWS THE COST. Because the detail panel
    /// only answers the question once you have already found the item. Transmutable things are a
    /// handful scattered through two thousand entries with nothing marking them from the outside,
    /// so without this the only way to learn that silver is even possible is to happen to click on
    /// it. This is the list you consult BEFORE searching, not after.
    ///
    /// It is deliberately read-only. Clicking a row selects it in the main window rather than
    /// queueing anything, so nothing here can spend a colony's Feignallite by accident.
    /// </summary>
    public class Dialog_AvirTransmuteList : Window
    {
        private readonly Building_AvirLoom loom;
        private readonly System.Action<ThingDef> onPick;
        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(520f, 560f);

        public Dialog_AvirTransmuteList(Building_AvirLoom loom, System.Action<ThingDef> onPick)
        {
            this.loom = loom;
            this.onPick = onPick;
            doCloseX = false;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            draggable = true;
            doWindowBackground = false;
            drawShadow = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            AvirUI.Panel(new Rect(inRect.x - Margin, inRect.y - Margin,
                inRect.width + Margin * 2f, inRect.height + Margin * 2f));

            Rect area = inRect.ContractedBy(14f);

            AvirUI.Label(new Rect(area.x, area.y, area.width - 40f, 28f),
                "Avir_TransmuteTitle".Translate(), AvirUI.Gold, GameFont.Medium);

            if (AvirUI.Button(new Rect(area.xMax - 34f, area.y, 34f, 26f), "x")) Close();

            int have = AvirTransmute.AvailableIn(loom.Tank);
            int cap = loom.Tank?.Props?.fuelCapacity != null
                ? Mathf.RoundToInt(loom.Tank.Props.fuelCapacity)
                : 0;

            AvirUI.Label(new Rect(area.x, area.y + 30f, area.width, 18f),
                "Avir_TransmuteTank".Translate(have, cap), AvirUI.Amber, GameFont.Tiny);

            AvirUI.DividerAt(new Rect(area.x, area.y + 52f, area.width, 10f));

            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(AvirTransmute.IsTransmutable)
                .OrderBy(d => AvirTransmute.EntryFor(d).feignallite)
                .ThenBy(d => d.label)
                .ToList();

            Rect listRect = new Rect(area.x, area.y + 66f, area.width, area.height - 66f);
            Rect view = new Rect(0f, 0f, listRect.width - 18f, defs.Count * 44f);

            Widgets.BeginScrollView(listRect, ref scroll, view);

            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                AvirTransmuteEntry e = AvirTransmute.EntryFor(def);
                Rect row = new Rect(0f, i * 44f, view.width, 40f);

                bool affordable = have >= e.feignallite;
                if (Mouse.IsOver(row)) AvirUI.Highlight(row);

                Rect icon = new Rect(row.x + 4f, row.y + 6f, 28f, 28f);
                AvirUI.Well(icon);
                Widgets.ThingIcon(icon.ContractedBy(2f), def);

                AvirUI.Label(new Rect(row.x + 40f, row.y + 3f, row.width - 190f, 18f),
                    def.LabelCap, AvirUI.Parchment);
                AvirUI.Label(new Rect(row.x + 40f, row.y + 20f, row.width - 190f, 16f),
                    "Avir_TransmutePower".Translate(e.powerMultiplier.ToString("0.#")),
                    AvirUI.Dim, GameFont.Tiny);

                // Green when the tank can already pay for it, red when it cannot. The number itself
                // never changes - only whether the colony can act on it right now.
                AvirUI.Label(new Rect(row.xMax - 146f, row.y + 10f, 140f, 20f),
                    e.feignallite + "  ->  " + e.count,
                    affordable ? new Color(0.62f, 0.82f, 0.72f) : new Color(0.80f, 0.45f, 0.40f),
                    GameFont.Small, TextAnchor.MiddleRight);

                if (Widgets.ButtonInvisible(row))
                {
                    onPick?.Invoke(def);
                    Close();
                }
            }

            Widgets.EndScrollView();
        }
    }
}
