using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Developer actions for what Fix251 made: the deluge's own rains, the typhoon's eye and walk, the spreading
    /// emerald.</summary>
    public static class SecondDev251
    {
        private static SecondFields F => Find.CurrentMap?.GetComponent<SecondFields>();
        private static Pawn Mage => Find.CurrentMap?.mapPawns.FreeColonistsSpawned.FirstOrDefault();
        private static List<IntVec3> Round(float r) => GenRadial.RadialCellsAround(Verse.UI.MouseCell(), r, true).Where(x => x.InBounds(Find.CurrentMap)).ToList();

        [DebugAction("Magic", "Deluge: mending rain here (2h)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Mending()
        {
            SecondFields f = F; if (f == null) return;
            f.Add(SecondKind.Deluge, Round(5f), GenDate.TicksPerHour * 2, Mage, 1.5f);
            f.fields[f.fields.Count - 1].heal = true;
        }

        [DebugAction("Magic", "Deluge: watering rain here (2h)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Watering()
        {
            SecondFields f = F; if (f == null) return;
            f.Add(SecondKind.Deluge, Round(5f), GenDate.TicksPerHour * 2, Mage, 1f);
            f.fields[f.fields.Count - 1].grow = true;
        }

        [DebugAction("Magic", "Typhoon: the eye here (hold)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Eye()
        {
            SecondFields f = F; if (f == null) return;
            f.AddMoving(SecondKind.Typhoon, Verse.UI.MouseCell(), Vector3.forward, 0f, 1250, Mage, 1f);
            SecondFields.Fld fl = f.fields[f.fields.Count - 1];
            fl.eye = true; fl.cells = new HashSet<IntVec3>(Round(SecondFields.EyeRadius));
        }

        [DebugAction("Magic", "Typhoon: walking east from here (with Lift)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Walk()
        {
            SecondFields f = F; if (f == null) return;
            f.AddMoving(SecondKind.Typhoon, Verse.UI.MouseCell(), Vector3.right, 1f, 900, Mage, 1f);
            f.fields[f.fields.Count - 1].lift = true;
        }

        [DebugAction("Magic", "Emerald: spreading crust on a pawn (8h)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Spreading(Pawn p) => LifeMagic.Crust(p, 0.15f, GenDate.TicksPerHour * 8);
    }
}
