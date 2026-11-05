using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Avir.Spire
{
    // =====================================================================================
    // THE LIFT ZONE. Not everything in the spire's domain flies - only what the player marks out.
    // The zone is painted within the domain (the cradle's and its pylons' reach). First made, it takes
    // every building and worked floor in the domain and the ground caught between them, and leaves bare
    // soil out; the player stretches or trims it from there. With no zone the spire refuses to fly.
    // =====================================================================================

    public class MapComponent_SpireLift : MapComponent, ICellBoolGiver
    {
        private HashSet<IntVec3> cells = new HashSet<IntVec3>();
        private readonly HashSet<IntVec3> domain = new HashSet<IntVec3>();
        private int domainTick = -9999;
        private CellBoolDrawer drawer;

        public MapComponent_SpireLift(Map map) : base(map) { }

        public static MapComponent_SpireLift Of(Map map) => map?.GetComponent<MapComponent_SpireLift>();

        public IReadOnlyCollection<IntVec3> Cells => cells;
        public bool Empty => cells.Count == 0;
        public bool Contains(IntVec3 c) => cells.Contains(c);

        public CompSpireCradle Cradle =>
            map.listerBuildings.allBuildingsColonist.Select(b => b.TryGetComp<CompSpireCradle>()).FirstOrDefault(c => c != null);

        /// <summary>The spire's domain on this map (refreshed every two seconds).</summary>
        public HashSet<IntVec3> Domain
        {
            get
            {
                CompSpireCradle c = Cradle;
                int sig = c?.DomainSignature ?? 0;
                if (sig != domainTick || (c != null && domain.Count == 0))
                {
                    domainTick = sig;
                    if (c != null) c.DomainCells(domain, 40000); else domain.Clear();
                }
                return domain;
            }
        }

        public void Set(IntVec3 c, bool on)
        {
            if (on ? cells.Add(c) : cells.Remove(c)) Drawer.SetDirty();
        }

        /// <summary>The first zone: the colony's rooms in the domain - their floors, walls and doors - with the cradle
        /// and its pylons, and the ground caught between them. Ruins, wrecks and geysers stay where they are.</summary>
        public void AutoMark()
        {
            HashSet<IntVec3> d = Domain;
            var mark = new HashSet<IntVec3>();
            Faction us = Faction.OfPlayer;
            var rooms = new HashSet<Room>();
            foreach (IntVec3 c in d)
            {
                Room r = c.GetRoom(map);
                if (r == null || r.IsDoorway || r.PsychologicallyOutdoors || r.TouchesMapEdge || !rooms.Add(r)) continue;
            }
            foreach (Room r in rooms)
            {
                // ours: walled by the colony (an ancient room keeps no one's walls)
                int ours = 0, all = 0;
                foreach (IntVec3 b in r.BorderCells)
                {
                    Building e = b.InBounds(map) ? b.GetEdifice(map) : null;
                    if (e == null) continue;
                    all++;
                    if (e.Faction == us) ours++;
                }
                if (all == 0 || ours * 2 < all) continue;
                foreach (IntVec3 c in r.Cells) if (d.Contains(c)) mark.Add(c);
                foreach (IntVec3 b in r.BorderCells)
                    if (d.Contains(b) && b.InBounds(map) && b.GetEdifice(map)?.Faction == us) mark.Add(b);
            }
            // the cradle and the pylons always go - the cradle with a deck around it to stand on
            CompSpireCradle cradle = Cradle;
            if (cradle != null)
            {
                foreach (IntVec3 c in cradle.parent.OccupiedRect().ExpandedBy(4))
                    if (d.Contains(c)) mark.Add(c);
                foreach (CompSpirePylon p in cradle.Pylons)
                    foreach (IntVec3 c in p.parent.OccupiedRect()) mark.Add(c);
            }
            // bare ground closed in by what was marked joins it; open soil stays behind
            for (int pass = 0; pass < 4; pass++)
            {
                var add = new List<IntVec3>();
                foreach (IntVec3 c in d)
                {
                    if (mark.Contains(c)) continue;
                    int n = 0;
                    for (int i = 0; i < 4; i++) if (mark.Contains(c + GenAdj.CardinalDirections[i])) n++;
                    if (n >= 3) add.Add(c);
                }
                if (add.Count == 0) break;
                mark.UnionWith(add);
            }
            cells = mark;
            Drawer.SetDirty();
        }

        /// <summary>Cells in the zone (and the domain) a pawn can stand on: room aboard.</summary>
        public int StandableCount() => cells.Count(c => Domain.Contains(c) && c.Standable(map));

        /// <summary>What rises: the zone within the domain, and always the cradle (and any gravship part standing on it).</summary>
        public void LiftCells(Building_GravEngine engine, HashSet<IntVec3> into, int maxCells)
        {
            into.Clear();
            HashSet<IntVec3> d = Domain;
            foreach (IntVec3 c in engine.OccupiedRect()) into.Add(c);
            if (engine.def.hasInteractionCell) into.Add(engine.InteractionCell);   // where the pilot stands
            foreach (Thing t in engine.GravshipComponents.Select(g => g.parent))
                foreach (IntVec3 c in t.OccupiedRect()) into.Add(c);
            foreach (IntVec3 c in cells)
            {
                if (into.Count >= maxCells) break;
                if (d.Contains(c)) into.Add(c);
            }
        }

        // ---------------------------------------------------------------- drawing
        private CellBoolDrawer Drawer => drawer ?? (drawer = new CellBoolDrawer(this, map.Size.x, map.Size.z, 3650, 0.33f));
        public Color Color => new Color(0.72f, 0.5f, 1f);
        public bool GetCellBool(int index) => cells.Contains(map.cellIndices.IndexToCell(index));
        public Color GetCellExtraColor(int index) => Color.white;

        public void MarkForDraw() => Drawer.MarkForDraw();

        public override void MapComponentUpdate()
        {
            if (Find.CurrentMap != map) return;
            if (Find.DesignatorManager.SelectedDesignator is Designator_SpireLift) MarkForDraw();
            drawer?.CellBoolDrawerUpdate();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            List<IntVec3> list = Scribe.mode == LoadSaveMode.Saving ? cells.ToList() : null;
            Scribe_Collections.Look(ref list, "liftZone", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars) cells = list != null ? new HashSet<IntVec3>(list) : new HashSet<IntVec3>();
        }
    }

    /// <summary>Paint the lift zone (within the spire's domain) or trim it.</summary>
    public class Designator_SpireLift : Designator_Cells
    {
        private readonly bool add;

        public Designator_SpireLift(bool add)
        {
            this.add = add;
            defaultLabel = add ? "Lift zone: expand" : "Lift zone: shrink";
            defaultDesc = add
                ? "Mark what rises with the spire. Only the marked part of its domain flies - stretch the zone to anything you want to take."
                : "Leave ground and buildings behind: trimmed cells stay on the planet when the spire flies.";
            icon = ContentFinder<Texture2D>.Get(add ? "UI/Commands/Avir/LiftZoneExpand" : "UI/Commands/Avir/LiftZoneShrink", false)
                   ?? ContentFinder<Texture2D>.Get(add ? "UI/Designators/ZoneCreate_Stockpile" : "UI/Designators/ZoneDelete", false) ?? BaseContent.BadTex;
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            useMouseIcon = true;
        }

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.Areas;
        public override bool DragDrawMeasurements => true;

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            MapComponent_SpireLift z = MapComponent_SpireLift.Of(Map);
            if (z == null || !c.InBounds(Map)) return false;
            if (add)
            {
                if (z.Contains(c)) return false;
                if (!z.Domain.Contains(c)) return "Outside the spire's domain: raise a spire pylon nearer to reach it.";
                return true;
            }
            return z.Contains(c);
        }

        public override void DesignateSingleCell(IntVec3 c) => MapComponent_SpireLift.Of(Map)?.Set(c, add);

        public override void SelectedUpdate()
        {
            GenUI.RenderMouseoverBracket();
            MapComponent_SpireLift z = MapComponent_SpireLift.Of(Map);
            if (z == null) return;
            z.MarkForDraw();
            GenDraw.DrawFieldEdges(z.Domain.ToList(), new Color(0.75f, 0.55f, 1f, 0.6f));
        }
    }
}
