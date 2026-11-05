using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE AVIR PYLON. One building doing three jobs that vanilla splits across three:
    /// generator, climate control, and roof support - plus concealing what stands beneath it.
    ///
    /// The generator half needs no code at all: vanilla CompPowerPlant already checks
    /// refuelableComp.HasFuel before producing, so CompPowerPlant + CompRefuelable with Essence
    /// Extract as the fuel filter is the whole thing. Verified in CompPowerPlant
    /// .UpdateDesiredPowerOutput. Climate is CompAvirClimate; roof and wealth are patches.
    ///
    /// WHAT THIS CLASS ITSELF IS FOR: a per-map registry, so the roof patch and the wealth patch
    /// can ask "is there a pylon near this cell" without walking every building on the map. The
    /// roof question in particular is asked from inside vanilla's collapse checks, which run over
    /// large cell batches - a lister walk there would be felt.
    /// </summary>
    public class Building_AvirPylon : Building
    {
        /// <summary>Concealment and roof support share one radius on purpose. Two different
        /// numbers would mean a band around every pylon where the roof holds but the wealth is
        /// visible, and no player would ever work out where that band was.</summary>
        public const float PylonRadius = 13.8f;

        private static readonly Dictionary<Map, List<Building_AvirPylon>> byMap =
            new Dictionary<Map, List<Building_AvirPylon>>();

        public static List<Building_AvirPylon> PylonsOn(Map map)
        {
            if (map == null) return null;
            return byMap.TryGetValue(map, out List<Building_AvirPylon> list) ? list : null;
        }

        /// <summary>Cheap enough to call per cell: almost every map has a single-digit number of
        /// pylons, and the squared-distance test avoids a square root.</summary>
        public static bool CellIsCovered(IntVec3 cell, Map map)
        {
            List<Building_AvirPylon> list = PylonsOn(map);
            if (list == null) return false;

            float radiusSq = PylonRadius * PylonRadius;
            for (int i = 0; i < list.Count; i++)
            {
                Building_AvirPylon pylon = list[i];
                if (pylon == null || !pylon.Spawned) continue;
                if ((pylon.Position - cell).LengthHorizontalSquared <= radiusSq) return true;
            }

            return false;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            if (!byMap.TryGetValue(map, out List<Building_AvirPylon> list))
            {
                list = new List<Building_AvirPylon>();
                byMap[map] = list;
            }

            if (!list.Contains(this)) list.Add(this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = Map;

            if (map != null && byMap.TryGetValue(map, out List<Building_AvirPylon> list))
            {
                list.Remove(this);
                if (list.Count == 0) byMap.Remove(map);
            }

            base.DeSpawn(mode);

            DropUnsupportedRoofs(map, Position);
        }

        /// <summary>
        /// CLEANS UP THE ROOF THIS PYLON WAS HOLDING BEYOND VANILLA'S REACH.
        ///
        /// Vanilla does notify the collapse finder when a roof holder despawns - Building.DeSpawn
        /// does that already, because our def sets holdsRoof. But the sweep it performs is sized
        /// by RoofCollapseUtility.RoofSupportRadialCellsCount, which is built from 6.9. Every cell
        /// this pylon was holding between 6.9 and 13.8 is outside that sweep and would simply
        /// never be re-examined: roof left hanging over nothing, permanently, with no error.
        ///
        /// That is the same shape of bug as the pawns stranded in deep water - a rule applied in
        /// one place and not in the other - so it gets handled at the source rather than waited on.
        ///
        /// The roof is REMOVED, not collapsed. Vanilla's collapse drops rock on whatever is
        /// underneath, and reproducing that faithfully means calling into collapse internals whose
        /// signatures are not worth guessing at. Quietly losing the roof is the gentler failure and
        /// the one that cannot corrupt anything.
        /// </summary>
        private static void DropUnsupportedRoofs(Map map, IntVec3 center)
        {
            if (map?.roofGrid == null) return;

            int cellCount = GenRadial.NumCellsInRadius(PylonRadius);
            for (int i = 0; i < cellCount; i++)
            {
                IntVec3 cell = center + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map)) continue;
                if (!map.roofGrid.Roofed(cell)) continue;

                RoofDef roof = map.roofGrid.RoofAt(cell);
                if (roof == null || roof.isNatural) continue; // Never touch rock overhead.

                if (RoofCollapseUtility.WithinRangeOfRoofHolder(cell, map, false)) continue;
                if (RoofCollapseUtility.ConnectedToRoofHolder(cell, map, false)) continue;

                map.roofGrid.SetRoof(cell, null);
            }
        }

        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            string extra = "Avir_PylonCoverage".Translate(Mathf.RoundToInt(PylonRadius));
            return baseString.NullOrEmpty() ? extra : baseString + "\n" + extra;
        }
    }
}
