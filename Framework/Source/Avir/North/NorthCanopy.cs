using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.North
{
    // =====================================================================================
    // A DENSE NORTHERN FOREST. Vanilla prints every tree on the Building layer, so rock chunks and items (a layer
    // higher) show through the crowns, and the picture's bottom edge (not the foot of the trunk) sits on the cell.
    // Our trees print themselves instead:
    //  - the foot of the trunk stands on the tree's own cell (NorthPlant.trunkBase);
    //  - the crown covers the ground, low plants, chunks and items, but stays under pawns;
    //  - if a building stands anywhere under the crown, the whole tree drops below the buildings instead, so
    //    walls and furniture are never hidden; it is printed again whenever a building comes or goes there;
    //  - crowns sort by row: a tree further south covers the one behind it;
    //  - a grown shelter tree with open wings prints its round shade on the ground beneath (by day).
    // =====================================================================================
    [StaticConstructorOnStartup]
    public static class NorthCanopy
    {
        // just under the pawns: a pawn or animal north of a trunk, under its crown, is put below that crown
        // (Patch_PawnBehindCrowns), any other stands over it; plants at the foot of a trunk are put over it (PlantY)
        public static readonly float HighBase = AltitudeLayer.Pawn.AltitudeFor() - 0.12f;
        // under walls, doors and furniture, over low plants and their shadows
        private static readonly float LowBase = AltitudeLayer.Shadows.AltitudeFor() + 0.02f;
        // the trees among themselves, by row (south over north); the whole span stays under the pawns
        public const float RowSpan = 0.06f;

        public static readonly Material ShadeMat = MakeShadeMat();
        /// <summary>Set while the selection bracket is drawn (Patch_TreeBracketOnTrunk).</summary>
        public static bool drawingBracket;
        private static readonly Color32[] colors = new Color32[4];
        /// <summary>The size (in cells) at which a tree keeps vanilla's sway weight.</summary>
        private const float WindRefSize = 2.5f;

        public static void PrintTree(Plant_AvirNorth tree, SectionLayer layer)
        {
            Map map = tree.Map;
            IntVec3 pos = tree.Position;
            float scale = tree.def.plant.visualSizeRange.LerpThroughRange(tree.Growth);
            float size = tree.def.graphicData.drawSize.x * scale;
            float trunkBase = tree.Ext?.trunkBase ?? 0.05f;

            Rand.PushState();
            try { PrintTreeInner(tree, layer, map, pos, size, trunkBase); }
            finally { Rand.PopState(); }
        }

        private static void PrintTreeInner(Plant_AvirNorth tree, SectionLayer layer, Map map, IntVec3 pos, float size, float trunkBase)
        {
            Rand.Seed = pos.GetHashCode();
            Vector3 c = new Vector3(pos.x + 0.5f, 0f, pos.z + 0.5f) + Gen.RandomHorizontalVector(0.05f);
            c.z += size * (0.5f - trunkBase) - 0.2f;          // foot of the trunk a little below the cell's middle

            // the variant and its side are picked first: the picture is moved so its trunk stands on the cell
            Graphic g = tree.Graphic;
            int variant = g is Graphic_Random gr0 && gr0.SubGraphicsCount > 0 ? Rand.Range(0, gr0.SubGraphicsCount) : -1;
            bool flip = Rand.Bool;
            List<float> shifts = tree.Ext?.trunkShift;
            if (variant >= 0 && shifts != null && variant < shifts.Count)
                c.x -= shifts[variant] * size * (flip ? -1f : 1f);

            // the cells the crown covers (its transparent margins left out)
            tree.canopyRect = CellRect.FromLimits(
                Mathf.FloorToInt(c.x - size * 0.4f), Mathf.FloorToInt(c.z - size * 0.5f + 0.3f),
                Mathf.FloorToInt(c.x + size * 0.4f), Mathf.FloorToInt(c.z + size * 0.45f)).ClipInsideMap(map);

            c.y = TreeY(tree, map, size);
            CanopyDepth.Dirty(map);

            Material mat = variant >= 0 ? ((Graphic_Random)g).SubGraphicAtIndex(variant).MatSingle : g.MatSingleFor(tree);
            mat = Swaying(mat);
            PlantUtility.SetWindExposureColors(colors, tree);
            // the plant shader sways the top edge by a fixed distance (in cells) times this weight - on a crown ten
            // and more cells tall that is lost; the weight grows with the size, so a big tree visibly sways
            colors[1].a = colors[2].a = (byte)Mathf.Min(255f, PlantUtility.GetWindExposure(tree) * Mathf.Clamp(size / WindRefSize, 1f, 2f));
            // only a plant drawn from the static atlas sways (as vanilla's Plant.Print does it); the textures are kept
            // under 512 px so they get into it (NorthAtlas)
            Graphic.TryGetTextureAtlasReplacementInfo(mat, TextureAtlasGroup.Plant, flip, false, out mat, out Vector2[] uvs, out _);
            float bias = SwayMode == 1 ? 0.1f : 0.002f; float payload = tree.HashOffset() % 1024;
            float split = tree.Ext?.rootSplit ?? 0.3f;
            if (c.y >= HighBase - 0.001f && split > 0f && split < 1f)
            {
                // two pieces: the trunk and roots lie with the low plants, sorted by row (a bush south of the trunk is
                // in front of it, one north behind), the crown stands over everything
                if (uvs == null) uvs = flip ? FlippedUvs : PlainUvs;
                Vector2 m0 = Vector2.Lerp(uvs[0], uvs[1], split), m3 = Vector2.Lerp(uvs[3], uvs[2], split);
                byte top = colors[1].a, mid = (byte)(top * split);
                lowUvs[0] = uvs[0]; lowUvs[1] = m0; lowUvs[2] = m3; lowUvs[3] = uvs[3];
                highUvs[0] = m0; highUvs[1] = uvs[1]; highUvs[2] = uvs[2]; highUvs[3] = m3;
                lowCols[0] = colors[0]; lowCols[3] = colors[3]; lowCols[1] = colors[1]; lowCols[2] = colors[2]; lowCols[1].a = lowCols[2].a = mid;
                highCols[0] = colors[0]; highCols[3] = colors[3]; highCols[1] = colors[1]; highCols[2] = colors[2]; highCols[0].a = highCols[3].a = mid;
                float lowH = size * split, highH = size - lowH;
                Vector3 lc = new Vector3(c.x, RootY(tree, map), c.z - size * 0.5f + lowH * 0.5f);
                Vector3 hc = new Vector3(c.x, c.y, c.z + size * 0.5f - highH * 0.5f);
                Printer_Plane.PrintPlane(layer, lc, new Vector2(size, lowH), mat, 0f, flip, lowUvs, lowCols, 0f, payload);
                Printer_Plane.PrintPlane(layer, hc, new Vector2(size, highH), mat, 0f, flip, highUvs, highCols, bias, payload);
            }
            else Printer_Plane.PrintPlane(layer, c, new Vector2(size, size), mat, 0f, flip, uvs, colors, bias, payload);
            lastMat = mat;

            // at the edge of an unexplored mountain the fog would swallow the crown: the fog is not drawn under the
            // crown of a tree that can be seen (Patch_FogUnderCrowns), so the tree is lit like everything around it
            bool edge = FogEdge(tree, map);
            if (edge != tree.fogEdge) DirtyFog(tree.canopyRect, map);
            tree.fogEdge = edge;
            MapComponent_AvirShade.Of(map)?.SetFogEdge(tree, edge);
        }

        /// <summary>The crown's depth: by row (south over north); under the buildings when one stands by its trunk (the
        /// trunk and roots, about the lower third of the picture) - a building under the edge of the crown is covered.</summary>
        public static float TreeY(Plant_AvirNorth tree, Map map, float size)
        {
            IntVec3 pos = tree.Position;
            float row = (map.Size.z - pos.z) / (float)Mathf.Max(1, map.Size.z);
            CellRect trunkArea = CellRect.FromLimits(pos.x - 1, pos.z - 1, pos.x + 1, pos.z + Mathf.Max(1, Mathf.RoundToInt(size * 0.35f))).ClipInsideMap(map);
            return (BuildingUnder(trunkArea, map) ? LowBase : HighBase) + row * RowSpan;
        }

        private static readonly Vector2[] PlainUvs = { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
        private static readonly Vector2[] FlippedUvs = { new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f) };
        private static readonly Vector2[] lowUvs = new Vector2[4], highUvs = new Vector2[4];
        private static readonly Color32[] lowCols = new Color32[4], highCols = new Color32[4];

        /// <summary>The trunk and roots: the low plants' own depth at half a row north of the trunk's row, so the plants
        /// of the trunk's row and south of it are in front, those north of it behind.</summary>
        public static float RootY(Plant tree, Map map) =>
            AltitudeLayer.LowPlant.AltitudeFor() + (map.Size.z - (tree.Position.z + 0.5f)) / (float)Mathf.Max(1, map.Size.z) * 0.03f;

        /// <summary>A low plant's depth: its own layer, by row (south over north) within a fraction of a step; at the foot
        /// of a trunk, over that tree.</summary>
        public static float PlantY(Plant p)
        {
            Map map = p.Map; IntVec3 pos = p.Position;
            float y = p.def.Altitude + (map.Size.z - pos.z) / (float)Mathf.Max(1, map.Size.z) * 0.03f;
            return y;
        }

        /// <summary>Dev test of the wind sway (Avir dev action "North trees: sway test"): 0 - as now, 1 - the top edge
        /// raised as vanilla does (0.1), 2 - the trees printed by vanilla's own Plant.Print.</summary>
        public static int SwayMode;
        public static Material lastMat;

        private static readonly Dictionary<Material, Material> swayMats = new Dictionary<Material, Material>();
        private static bool swayLogged;

        /// <summary>Only the plant shader sways in the wind (the wind manager drives the materials made with it): whatever
        /// shader the tree's graphic ended up with (a seasonal or night variant, or another mod's), its crown is printed
        /// with the plant shader.</summary>
        private static Material Swaying(Material mat)
        {
            if (mat == null || mat.shader == ShaderDatabase.CutoutPlant) return mat;
            if (!swayMats.TryGetValue(mat, out Material m))
            {
                m = mat.mainTexture is Texture2D tex ? MaterialPool.MatFrom(tex, ShaderDatabase.CutoutPlant, mat.color) : mat;
                swayMats[mat] = m;
                if (!swayLogged && Prefs.DevMode)
                {
                    swayLogged = true;
                    Log.Message("[Avir north] tree material had shader " + mat.shader?.name + " - printed with CutoutPlant so it sways.");
                }
            }
            return m;
        }

        /// <summary>The trunk can be seen, but some of the crown lies over fogged (unexplored) cells.</summary>
        public static bool FogEdge(Plant_AvirNorth tree, Map map)
        {
            FogGrid fog = map.fogGrid;
            if (fog.IsFogged(tree.Position)) return false;
            foreach (IntVec3 cell in tree.canopyRect)
                if (fog.IsFogged(cell)) return true;
            return false;
        }

        /// <summary>Crowns printed while the map's sections are still being built (the first drawing of a map): their
        /// fog is marked dirty once all the sections are there (MapComponent_AvirShade.MapComponentUpdate).</summary>
        public static readonly List<(Map map, CellRect r)> pendingFog = new List<(Map, CellRect)>();

        public static void DirtyFog(CellRect r, Map map)
        {
            if (!SectionsReady(map)) { pendingFog.Add((map, r)); return; }
            for (int x = r.minX; x <= r.maxX; x += 8)
                for (int z = r.minZ; z <= r.maxZ; z += 8)
                    map.mapDrawer.MapMeshDirty(new IntVec3(x, 0, z), MapMeshFlagDefOf.FogOfWar);
            map.mapDrawer.MapMeshDirty(new IntVec3(r.maxX, 0, r.maxZ), MapMeshFlagDefOf.FogOfWar);
        }

        private static readonly AccessTools.FieldRef<MapDrawer, Section[,]> sectionsOf = AccessTools.FieldRefAccess<MapDrawer, Section[,]>("sections");

        /// <summary>Every section of the map's drawer exists (during the first drawing of a map they are made one by one,
        /// and marking a missing one dirty throws).</summary>
        public static bool SectionsReady(Map map)
        {
            Section[,] s = map?.mapDrawer == null ? null : sectionsOf(map.mapDrawer);
            if (s == null) return false;
            foreach (Section x in s) if (x == null) return false;
            return true;
        }

        /// <summary>Anything built (or being built) under the crown - natural rock does not count.</summary>
        private static bool BuildingUnder(CellRect r, Map map)
        {
            foreach (IntVec3 cell in r)
            {
                var list = map.thingGrid.ThingsListAtFast(cell);
                for (int i = 0; i < list.Count; i++)
                {
                    ThingDef d = list[i].def;
                    if (d.category == ThingCategory.Building && !(list[i] is Plant) && d.altitudeLayer >= AltitudeLayer.DoorMoveable
                        && !(d.building?.isNaturalRock ?? false))
                        return true;
                }
            }
            return false;
        }

        private static Material MakeShadeMat()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "AvirShelterShade" };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.5f, 1f, r)) * 0.5f;
                    px[y * n + x] = new Color(0.07f, 0.05f, 0.13f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return MaterialPool.MatFrom(tex, ShaderDatabase.Transparent, Color.white);
        }
    }

    /// <summary>The fog is not drawn over the crown of a tree whose trunk can be seen: for the length of the fog's
    /// redraw those cells read as clear (the grid itself is untouched - nothing is revealed to the game).</summary>
    [HarmonyPatch(typeof(SectionLayer_FogOfWar), nameof(SectionLayer_FogOfWar.Regenerate))]
    public static class Patch_FogUnderCrowns
    {
        private static readonly AccessTools.FieldRef<SectionLayer, Section> sectionOf = AccessTools.FieldRefAccess<SectionLayer, Section>("section");
        private static readonly System.Reflection.FieldInfo gridField = AccessTools.Field(typeof(FogGrid), "fogGrid");
        private static System.Reflection.MethodInfo setBit;

        public static void Prefix(SectionLayer_FogOfWar __instance, out List<int> __state)
        {
            __state = null;
            Section sec = sectionOf(__instance);
            Map map = sec?.map;
            MapComponent_AvirShade comp = MapComponent_AvirShade.Of(map);
            if (comp == null || gridField == null || !comp.AnyFogEdge) return;
            CellRect rect = sec.CellRect.ExpandedBy(1);
            foreach (Plant_AvirNorth t in comp.FogEdges)
            {
                if (!t.Spawned || !t.canopyRect.Overlaps(rect)) continue;
                foreach (IntVec3 c in t.canopyRect)
                    if (rect.Contains(c) && map.fogGrid.IsFogged(c))
                        (__state ?? (__state = new List<int>())).Add(map.cellIndices.CellToIndex(c));
            }
            if (__state != null) SetBits(map, __state, false);
        }

        public static void Postfix(SectionLayer_FogOfWar __instance, List<int> __state)
        {
            if (__state != null) SetBits(sectionOf(__instance).map, __state, true);
        }

        private static void SetBits(Map map, List<int> cells, bool value)
        {
            object grid = gridField.GetValue(map.fogGrid);    // a NativeBitArray: a handle onto the same memory
            if (setBit == null) setBit = AccessTools.Method(grid.GetType(), "Set", new[] { typeof(int), typeof(bool) });
            if (setBit == null) return;
            foreach (int i in cells) setBit.Invoke(grid, new object[] { i, value });
        }
    }

    /// <summary>The shelter canopy is a roof to the weather and to the eye, not to the light: the sky still reaches
    /// what grows and walks under it.</summary>
    [HarmonyPatch(typeof(GlowGrid), nameof(GlowGrid.GroundGlowAt))]
    public static class Patch_CanopyLetsLightThrough
    {
        private static readonly AccessTools.FieldRef<GlowGrid, Map> mapOf = AccessTools.FieldRefAccess<GlowGrid, Map>("map");

        public static void Postfix(GlowGrid __instance, IntVec3 c, bool ignoreSky, ref float __result)
        {
            if (ignoreSky || __result >= 1f) return;
            RoofDef canopy = NorthDefOf.Avir_CanopyRoof;
            if (canopy == null) return;
            Map map = mapOf(__instance);
            if (map == null || map.roofGrid.RoofAt(c) != canopy) return;
            float sky = map.skyManager.CurSkyGlow;
            if (sky > __result) __result = sky;
        }
    }

    /// <summary>The crown is for clicking; the selection bracket stays on the trunk's cell.</summary>
    [HarmonyPatch(typeof(SelectionDrawer), nameof(SelectionDrawer.DrawSelectionBracketFor))]
    public static class Patch_TreeBracketOnTrunk
    {
        public static void Prefix() => NorthCanopy.drawingBracket = true;
        public static System.Exception Finalizer(System.Exception __exception) { NorthCanopy.drawingBracket = false; return __exception; }
    }

    /// <summary>A building that comes or goes under a crown makes that tree print itself again.</summary>
    [HarmonyPatch(typeof(Building), nameof(Building.SpawnSetup))]
    public static class Patch_CanopyBuildingSpawn
    {
        public static void Postfix(Building __instance, Map map)
        {
            if (__instance.def.altitudeLayer >= AltitudeLayer.DoorMoveable)
                MapComponent_AvirShade.Of(map)?.NotifyBuildingChanged(__instance.OccupiedRect());
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.DeSpawn))]
    public static class Patch_CanopyBuildingDespawn
    {
        public static void Prefix(Building __instance, out (Map, CellRect) __state)
        {
            __state = (__instance.Map, __instance.OccupiedRect());
        }

        public static void Postfix(Building __instance, (Map, CellRect) __state)
        {
            if (__state.Item1 != null && __instance.def.altitudeLayer >= AltitudeLayer.DoorMoveable)
                MapComponent_AvirShade.Of(__state.Item1)?.NotifyBuildingChanged(__state.Item2);
        }
    }

    /// <summary>Felling a giant takes hours; a chopper called away (to eat, to sleep) leaves the progress on the tree
    /// and whoever comes next carries on from there.</summary>
    [HarmonyPatch(typeof(JobDriver_PlantWork), nameof(JobDriver_PlantWork.WorkDonePerTick))]
    public static class Patch_ChopResume
    {
        private static readonly AccessTools.FieldRef<JobDriver_PlantWork, float> workDone =
            AccessTools.FieldRefAccess<JobDriver_PlantWork, float>("workDone");

        public static void Postfix(Pawn actor, Plant plant)
        {
            if (!(plant is Plant_AvirNorth t) || t.savedChopWork <= 0f) return;
            if (actor?.jobs?.curDriver is JobDriver_PlantWork d)
            {
                if (workDone(d) < t.savedChopWork) workDone(d) = t.savedChopWork;
                t.savedChopWork = 0f;
            }
        }

        public static void Save(JobDriver_PlantWork d)
        {
            if (d.job?.targetA.Thing is Plant_AvirNorth t && t.Spawned && t.def.plant.IsTree)
                t.savedChopWork = Mathf.Max(t.savedChopWork, workDone(d));
        }
    }

    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.Cleanup))]
    public static class Patch_ChopResumeSave
    {
        public static void Prefix(JobDriver __instance, JobCondition condition)
        {
            if (condition != JobCondition.Succeeded && __instance is JobDriver_PlantWork d) Patch_ChopResume.Save(d);
        }
    }

    /// <summary>An Avir sleeps well in a shelter grove, holding on to the tree: +20% rest anywhere under its crown.</summary>
    [HarmonyPatch(typeof(Need_Rest), nameof(Need_Rest.TickResting))]
    public static class Patch_GroveSleep
    {
        public const float Bonus = 1.2f;
        private static readonly AccessTools.FieldRef<Need, Pawn> needPawn = AccessTools.FieldRefAccess<Need, Pawn>("pawn");

        public static void Prefix(Need_Rest __instance, ref float restEffectiveness)
        {
            if (restEffectiveness <= 0f) return;
            Pawn p = needPawn(__instance);
            if (p == null || !p.Spawned || !AvirBody.IsAvir(p)) return;
            if (MapComponent_AvirShade.Of(p.Map)?.InGrove(p.Position) == true) restEffectiveness *= Bonus;
        }
    }

    /// <summary>Where the crowns stand in depth, per cell: the lowest crown a pawn here is behind (north of its trunk,
    /// under it), and the highest crown whose roots a plant here stands in front of (at the trunk's foot).</summary>
    public class CanopyDepth
    {
        private static readonly Dictionary<Map, CanopyDepth> all = new Dictionary<Map, CanopyDepth>();
        private float[] behind, front;
        private int builtFrame = -1, builtTick = -99999;
        private bool dirty = true;

        public static CanopyDepth Of(Map map)
        {
            if (map == null) return null;
            if (!all.TryGetValue(map, out CanopyDepth d))
            {
                if (all.Count > 8) all.Clear();
                all[map] = d = new CanopyDepth();
            }
            return d;
        }

        public static void Dirty(Map map) { CanopyDepth d = Of(map); if (d != null) d.dirty = true; }

        private void Ensure(Map map)
        {
            int n = map.cellIndices.NumGridCells;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (behind != null && behind.Length == n && (!dirty || builtFrame == Time.frameCount) && now - builtTick < 2500 && now >= builtTick) return;
            dirty = false; builtFrame = Time.frameCount; builtTick = now;
            if (behind == null || behind.Length != n) { behind = new float[n]; front = new float[n]; }
            for (int i = 0; i < n; i++) { behind[i] = float.MaxValue; front[i] = float.MinValue; }
            MapComponent_AvirShade shade = MapComponent_AvirShade.Of(map);
            if (shade == null) return;
            foreach (Plant_AvirNorth t in shade.Canopies)
            {
                if (!t.Spawned) continue;
                IntVec3 p = t.Position;
                float size = t.def.graphicData.drawSize.x * t.def.plant.visualSizeRange.LerpThroughRange(t.Growth);
                float y = NorthCanopy.TreeY(t, map, size);
                float cz = p.z + 0.5f + size * (0.5f - (t.Ext?.trunkBase ?? 0.05f)) - 0.2f;
                // the whole picture (not just the crown's solid middle): a plant under the crown's very edge is behind it too
                CellRect r = CellRect.FromLimits(Mathf.FloorToInt(p.x + 0.5f - size * 0.5f - 1f), Mathf.FloorToInt(cz - size * 0.5f),
                    Mathf.FloorToInt(p.x + 0.5f + size * 0.5f + 1f), Mathf.FloorToInt(cz + size * 0.5f + 1f)).ClipInsideMap(map);
                bool high = y >= NorthCanopy.HighBase - 0.001f;
                int root = Mathf.Max(1, Mathf.RoundToInt(size * 0.3f));
                foreach (IntVec3 c in r)
                {
                    int i = map.cellIndices.CellToIndex(c);
                    if (high && c.z > p.z && behind[i] > y) behind[i] = y;
                    if (c.z <= p.z && c.z >= p.z - 1 && Mathf.Abs(c.x - p.x) <= root && front[i] < y) front[i] = y;
                }
            }
        }

        public float Behind(Map map, IntVec3 c) { Ensure(map); return c.InBounds(map) ? behind[map.cellIndices.CellToIndex(c)] : float.MaxValue; }
        public float Front(Map map, IntVec3 c) { Ensure(map); return c.InBounds(map) ? front[map.cellIndices.CellToIndex(c)] : float.MinValue; }
    }

    /// <summary>A pawn or animal north of a trunk and under its crown is drawn below that crown; anywhere else it stands
    /// over the crowns (the crowns lie just under the pawn layer).</summary>
    [HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.DrawPos), MethodType.Getter)]
    public static class Patch_PawnBehindCrowns
    {
        private static readonly AccessTools.FieldRef<Pawn_DrawTracker, Pawn> pawnOf = AccessTools.FieldRefAccess<Pawn_DrawTracker, Pawn>("pawn");

        public static void Postfix(Pawn_DrawTracker __instance, ref Vector3 __result)
        {
            Pawn p = pawnOf(__instance);
            Map map = p?.MapHeld;
            if (map == null || !p.Spawned || p.GetPosture() != PawnPosture.Standing || p.def.altitudeLayer != AltitudeLayer.Pawn) return;
            if (MapComponent_AvirShade.Of(map)?.AnyCanopy != true) return;
            float b = CanopyDepth.Of(map).Behind(map, new IntVec3(Mathf.FloorToInt(__result.x), 0, Mathf.FloorToInt(__result.z)));
            if (b < float.MaxValue) __result.y = b - 0.06f + (p.thingIDNumber % 97) * 0.00002f;
        }
    }

    /// <summary>Our plants are sorted by row (south over north), so a big bush is not cut by the grass behind it, and
    /// a plant at the foot of a trunk is drawn over the roots. Set around Plant.Print (Plant_AvirNorth.Print).</summary>
    [HarmonyPatch(typeof(GenThing), nameof(GenThing.TrueCenter), new System.Type[] { typeof(Thing) })]
    public static class Patch_PlantLift
    {
        public static Thing liftThing;
        public static float liftY;

        public static void Postfix(Thing t, ref Vector3 __result)
        {
            if (liftThing != null && t == liftThing) __result.y = liftY;
        }
    }
}
