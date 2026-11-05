using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Traits
{
    /// <summary>
    /// Lists Find.Maps. On selection, hands off to ShroudgatesUtility.BeginDestinationTargeting
    /// instead of picking a cell automatically - that jumps the camera to the chosen map and lets
    /// the player click a specific spot, validated for darkness on click.
    /// </summary>
    public class Dialog_ShroudgatesDestination : Window
    {
        private readonly List<Pawn> group;

        public override Vector2 InitialSize => new Vector2(420f, 480f);

        public Dialog_ShroudgatesDestination(List<Pawn> group)
        {
            this.group = group;
            this.doCloseX = true;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 40f), "Choose a destination map");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 40f, inRect.width, 24f), "You'll then click a specific dark spot on that map.");

            float y = 72f;
            List<Map> maps = Find.Maps;

            if (maps == null || maps.Count == 0)
            {
                Widgets.Label(new Rect(0f, y, inRect.width, 32f), "No maps available.");
                return;
            }

            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null) continue;

                string label = map.Parent != null ? map.Parent.LabelCap : ("Map " + i);
                Rect rowRect = new Rect(0f, y, inRect.width, 32f);

                if (Widgets.ButtonText(rowRect, label))
                {
                    ShroudgatesUtility.BeginDestinationTargeting(this.group, map);
                    this.Close();
                }

                y += 36f;
            }
        }
    }
}
