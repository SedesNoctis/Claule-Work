using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.North
{
    // =====================================================================================
    // NORTHERN PLANTS. Most of it is XML (cold growth temperatures are a vanilla field now). The class adds:
    //  - colour variants: each plant takes one of its tints (pale, pale lilac, silver, pale blue, pale emerald) by its
    //    own id, so a meadow is mottled and nothing needs saving;
    //  - night growth: rests by day, grows by night, and feeds on moonlight in the groves of shelter trees;
    //  - shade tolerance: the shelter trees' daytime shade does not slow it.
    // =====================================================================================

    public class PlantTint
    {
        public string label;
        public Color color = Color.white;
        public float weight = 1f;
    }

    public class NorthPlant : DefModExtension
    {
        public List<PlantTint> tints;
        /// <summary>Rests by day, grows by night (crystal plants, night herbs).</summary>
        public bool nightGrowth;
        /// <summary>At night, unroofed, in a shelter grove: growth times this.</summary>
        public float moonlitFactor = 1.6f;
        /// <summary>The shelter trees' shade does not slow it.</summary>
        public bool shadeTolerant;
        /// <summary>Trees: how far up the picture (a fraction of its height) the foot of the trunk is; it is put on the tree's cell.</summary>
        public float trunkBase = 0.05f;
        /// <summary>Trees: where the trunk's foot stands in each variant (A, B, C...), as a fraction of the picture's
        /// width from its middle (+ right); the picture is moved so the trunk is on the tree's cell.</summary>
        public List<float> trunkShift;
        /// <summary>Trees: the share of the picture from its foot that is trunk and roots (drawn with the low plants;
        /// the rest, the crown, over everything).</summary>
        public float rootSplit = 0.3f;
        /// <summary>Wild: within `nearWater` cells of water it comes up `nearWaterFactor` times as often, farther off
        /// `farFactor` times (willows by the ponds, reeds mostly on the shores).</summary>
        /// <summary>Wild: never on sand (beaches, dunes) - the shelter trees.</summary>
        public bool avoidSand;
        public int nearWater;
        public float nearWaterFactor = 4f, farFactor = 0.25f;
        /// <summary>Leaves of the season (folders like the main one): spring and autumn colours; winter is the vanilla
        /// leafless picture, summer the main one.</summary>
        public string springTexPath, autumnTexPath;
        private Graphic spring, autumn;
        /// <summary>Below this growth the plant shows `youngTexPath` (a shrub before its flowers), with "_Spring" and
        /// "_Autumn" folders beside it for the seasons.</summary>
        public string youngTexPath;
        public float youngBelow = 0.6f;
        private readonly Dictionary<string, Graphic> young = new Dictionary<string, Graphic>();

        public Graphic Young(ThingDef d, Season s)
        {
            if (youngTexPath == null) return null;
            string path = youngTexPath;
            if (s == Season.Spring && springTexPath != null) path += "_Spring";
            else if (s == Season.Fall && autumnTexPath != null) path += "_Autumn";
            if (!young.TryGetValue(path, out Graphic g)) young[path] = g = Make(d, path);
            return g;
        }

        public bool Seasonal => springTexPath != null || autumnTexPath != null;

        public Graphic ForSeason(ThingDef d, Season s)
        {
            if (s == Season.Spring && springTexPath != null) return spring ?? (spring = Make(d, springTexPath));
            if (s == Season.Fall && autumnTexPath != null) return autumn ?? (autumn = Make(d, autumnTexPath));
            return null;
        }

        private static Graphic Make(ThingDef d, string path) =>
            GraphicDatabase.Get(d.graphicData.graphicClass, path, d.graphicData.shaderType?.Shader ?? ShaderDatabase.Cutout,
                d.graphicData.drawSize, d.graphicData.color, d.graphicData.colorTwo);

        public static NorthPlant Of(ThingDef d) => d?.GetModExtension<NorthPlant>();
    }

    public class Plant_AvirNorth : Plant
    {
        private static readonly Dictionary<(Graphic, Color), Graphic> tinted = new Dictionary<(Graphic, Color), Graphic>();

        public NorthPlant Ext => NorthPlant.Of(def);

        public PlantTint Tint
        {
            get
            {
                List<PlantTint> ts = Ext?.tints;
                if (ts.NullOrEmpty()) return null;
                float total = 0f;
                foreach (PlantTint t in ts) total += t.weight;
                float r = Rand.ValueSeeded(thingIDNumber ^ 0x7A11E5) * total;
                foreach (PlantTint t in ts) { r -= t.weight; if (r <= 0f) return t; }
                return ts[ts.Count - 1];
            }
        }

        public static Graphic Tinted(Graphic g, PlantTint t)
        {
            if (g == null || t == null) return g;
            if (!tinted.TryGetValue((g, t.color), out Graphic c))
                tinted[(g, t.color)] = c = g.GetColoredVersion(g.Shader, g.Color * t.color, g.ColorTwo);
            return c;
        }

        public override Graphic Graphic => Tinted(BaseGraphic, Tint);

        /// <summary>The season this plant is drawn in (its own, a few days ahead of or behind the calendar), remembered
        /// so the map knows when to redraw it.</summary>
        [Unsaved] public Season shownSeason = Season.Undefined;
        public Season ShownSeason() => shownSeason = AvirSeason.For(this);

        /// <summary>The untinted picture (a shelter tree swaps it by the time of day).</summary>
        protected virtual Graphic BaseGraphic
        {
            get
            {
                Graphic g = base.Graphic;
                NorthPlant e = Ext;
                if (e?.youngTexPath != null && Spawned && Growth < e.youngBelow && LifeStage != PlantLifeStage.Sowing)
                    return e.Young(def, ShownSeason()) ?? g;
                // the leaves of the season, where the plant shows its ordinary picture (not leafless, sown or young)
                if (e != null && e.Seasonal && Spawned && g == def.graphic)
                    return e.ForSeason(def, ShownSeason()) ?? g;
                return g;
            }
        }

        public override string LabelNoCount
        {
            get
            {
                PlantTint t = Tint;
                return t?.label == null ? base.LabelNoCount : t.label + " " + base.LabelNoCount;
            }
        }

        public static bool IsNight(Map map)
        {
            float d = GenLocalDate.DayPercent(map);
            return d < 0.25f || d > 0.8f;
        }

        /// <summary>Dark enough for the night plants: night, or under a roof with less than 50% light from anything
        /// but the plants' own glow (night glowers count as cave plants, so the glow grid can leave them out).</summary>
        public static bool IsDark(Thing t)
        {
            if (!t.Spawned) return false;
            Map map = t.Map;
            if (IsNight(map)) return true;
            return t.Position.Roofed(map) && map.glowGrid.GroundGlowAt(t.Position, ignoreCavePlants: true) < 0.49f;
        }

        protected override bool Resting
        {
            get
            {
                NorthPlant e = Ext;
                if (e == null || !e.nightGrowth) return base.Resting;
                return Spawned && !IsDark(this);
            }
        }

        /// <summary>The Avir's shade and night plants (growMinGlow 0) need no light at all: the dark is their
        /// home. Vanilla scales growth from 0 at no light, so in a dark room - a pot in an Avir burrow - they never
        /// grew a day. For them the light factor counts as full.</summary>
        public bool GrowsInDark => def.plant != null && def.plant.growMinGlow <= 0f;

        protected override bool HasEnoughLightToGrow => GrowsInDark || base.HasEnoughLightToGrow;

        public override float GrowthRate
        {
            get
            {
                float r = base.GrowthRate;
                if (GrowsInDark && !Blighted && (!Spawned || PlantUtility.GrowthSeasonNow(Position, Map, def)))
                    r = GrowthRateFactor_Fertility * GrowthRateFactor_Temperature * GrowthRateFactor_NoxiousHaze * GrowthRateFactor_Drought;
                NorthPlant e = Ext;
                if (e != null && e.nightGrowth && Spawned && IsNight(Map) && !Position.Roofed(Map)
                    && MapComponent_AvirShade.Of(Map)?.InGrove(Position) == true)
                    r *= e.moonlitFactor;
                return r;
            }
        }

        // ---------------------------------------------------------------- trees: a forest you can see into
        /// <summary>Chopping progress kept on the tree when the chopper is called away, so a giant is felled over several visits.</summary>
        public float savedChopWork;
        /// <summary>The cells the crown covers, as last printed.</summary>
        public CellRect canopyRect = CellRect.Empty;
        /// <summary>Part of the crown lies over fog: the fog is not drawn under it (see NorthCanopy).</summary>
        public bool fogEdge;

        /// <summary>A tree is picked by a click anywhere on its crown, not only on the cell of its trunk.</summary>
        public override CellRect? CustomRectForSelector =>
            def.plant.IsTree && Spawned && canopyRect.Area > 0 && !NorthCanopy.drawingBracket ? canopyRect : (CellRect?)null;

        public override void Print(SectionLayer layer)
        {
            if (def.plant.IsTree && NorthCanopy.SwayMode == 2) base.Print(layer);
            else if (def.plant.IsTree) NorthCanopy.PrintTree(this, layer);
            else if (Spawned && Position.GetFirstThing<Building_PlantGrower>(Map) is Building_PlantGrower pot) PrintInPot(layer, pot);
            else if (Spawned)
            {
                Patch_PlantLift.liftThing = this; Patch_PlantLift.liftY = NorthCanopy.PlantY(this);
                try { base.Print(layer); }
                finally { Patch_PlantLift.liftThing = null; }
            }
            else base.Print(layer);
        }

        private static readonly Color32[] potColors = new Color32[4];

        /// <summary>In a pot (some pots are drawn above the plant layer, which hid our plants): drawn just above the pot.</summary>
        private void PrintInPot(SectionLayer layer, Building_PlantGrower pot)
        {
            float size = def.graphicData.drawSize.x * def.plant.visualSizeRange.LerpThroughRange(Growth);
            Vector3 c = Position.ToVector3Shifted();
            c.z += size * 0.5f - 0.35f;
            c.y = Mathf.Max(def.Altitude, pot.def.Altitude) + 0.03f;
            Rand.PushState(Position.GetHashCode());
            Graphic g = Graphic;
            Material mat = g.MatSingleFor(this);
            if (g is Graphic_Random gr && gr.SubGraphicsCount > 0) mat = gr.SubGraphicAtIndex(thingIDNumber % gr.SubGraphicsCount).MatSingle;
            Graphic.TryGetTextureAtlasReplacementInfo(mat, TextureAtlasGroup.Plant, false, false, out mat, out Vector2[] uvs, out _);
            // the wind weights as vanilla gives a plant: without them every corner - the foot too - got the full sway and
            // the flower was flung from side to side in its pot
            PlantUtility.SetWindExposureColors(potColors, this);
            Printer_Plane.PrintPlane(layer, c, new Vector2(size, size), mat, 0f, false, uvs, potColors, 0.01f, this.HashOffset() % 1024);
            Rand.PopState();
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (def.plant.IsTree) MapComponent_AvirShade.Of(map)?.RegisterCanopy(this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (def.plant.IsTree) MapComponent_AvirShade.Of(Map)?.UnregisterCanopy(this);
            base.DeSpawn(mode);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref savedChopWork, "avirSavedChopWork", 0f);
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            NorthPlant e = Ext;
            if (e == null || !Spawned) return s;
            MapComponent_AvirShade shade = MapComponent_AvirShade.Of(Map);
            if (e.nightGrowth) s += "\nGrows by night" + (IsNight(Map) && shade?.InGrove(Position) == true && !Position.Roofed(Map) ? " - moonlit grove: x" + e.moonlitFactor.ToString("0.#") : "");
            else if (shade?.Shaded(Position) == true) s += "\nUnder a shelter tree's canopy";
            return s;
        }
    }

    /// <summary>A shelter tree: its wings open by day (a shade under them), close at night (the grove under them opens
    /// to the moon). The closed picture comes from the extension.</summary>
    public class ShelterTree : DefModExtension
    {
        public string closedTexPath;
        public float radius = 4.2f;
        /// <summary>Grown this far, it casts shade.</summary>
        public float minGrowth = 0.4f;
        private Graphic closed;

        public Graphic Closed(ThingDef d)
        {
            if (closed == null && closedTexPath != null)
                closed = GraphicDatabase.Get(d.graphicData.graphicClass, closedTexPath, d.graphicData.shaderType?.Shader ?? ShaderDatabase.Cutout,
                    d.graphicData.drawSize, d.graphicData.color, d.graphicData.colorTwo);
            return closed;
        }

        /// <summary>The great shelter tree of the ordinary lands changes its leaves with the season: these folders
        /// hold a Winter, Spring, Summer and Autumn subfolder each (silver, pale blue, green, rust/red/gold).</summary>
        public string seasonalTexPath, seasonalClosedTexPath;
        private readonly Dictionary<string, Graphic> seasonal = new Dictionary<string, Graphic>();

        public static string SeasonFolder(Season s)
        {
            switch (s)
            {
                case Season.Winter: case Season.PermanentWinter: return "Winter";
                case Season.Spring: return "Spring";
                case Season.Fall: return "Autumn";
                default: return "Summer";
            }
        }

        public Graphic Seasonal(ThingDef d, Season season, bool isClosed)
        {
            string root = isClosed ? seasonalClosedTexPath ?? seasonalTexPath : seasonalTexPath;
            if (root == null) return null;
            string path = root + "/" + SeasonFolder(season);
            if (!seasonal.TryGetValue(path, out Graphic g))
                seasonal[path] = g = GraphicDatabase.Get(d.graphicData.graphicClass, path, d.graphicData.shaderType?.Shader ?? ShaderDatabase.Cutout,
                    d.graphicData.drawSize, d.graphicData.color, d.graphicData.colorTwo);
            return g;
        }
    }

    public class Plant_ShelterTree : Plant_AvirNorth
    {
        public ShelterTree Shelter => def.GetModExtension<ShelterTree>();
        public bool Open => !Spawned || !IsNight(Map);
        public bool CastsShade => Growth >= (Shelter?.minGrowth ?? 0.4f);

        /// <summary>The canopy grows with the tree: from half its radius when it first casts shade to the full one
        /// (9 cells across for 4.9) at 90% grown.</summary>
        public float CanopyRadius
        {
            get
            {
                float full = Shelter?.radius ?? 4.9f, min = Shelter?.minGrowth ?? 0.4f;
                if (Growth >= 0.9f) return full;
                return Mathf.Max(1.5f, full * Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(min, 0.9f, Growth)));
            }
        }

        /// <summary>The canopy: a circle around the trunk, roots and all.</summary>
        public IEnumerable<IntVec3> CanopyCells()
        {
            foreach (IntVec3 c in GenRadial.RadialCellsAround(Position, CanopyRadius, true))
                if (!Spawned || c.InBounds(Map)) yield return c;
        }

        protected override Graphic BaseGraphic
        {
            get
            {
                ShelterTree sh = Shelter;
                if (LifeStage == PlantLifeStage.Sowing) return base.BaseGraphic;
                if (sh?.seasonalTexPath != null && Spawned)
                    return sh.Seasonal(def, ShownSeason(), !Open) ?? base.BaseGraphic;
                if (Open) return base.BaseGraphic;
                return sh?.Closed(def) ?? base.BaseGraphic;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            MapComponent_AvirShade.Of(map)?.Register(this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            MapComponent_AvirShade.Of(Map)?.Unregister(this);
            base.DeSpawn(mode);
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (CastsShade) GenDraw.DrawFieldEdges(new List<IntVec3>(CanopyCells()), new Color(0.7f, 0.6f, 0.95f));
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            if (!Spawned) return s;
            return s + (Open ? "\nWings open: a canopy roof over the grove (" : "\nWings closed: the grove beneath is open to the moon (") + "radius " + CanopyRadius.ToString("0.#") + (Growth < 0.9f ? " of " + (Shelter?.radius ?? 4.9f).ToString("0.#") : "") + ")"
                   + (CastsShade ? "" : " - too young yet");
        }
    }

    /// <summary>Which cells lie under a grown shelter tree (the grove), and whether it is day (so the grove is shade).
    /// Rebuilt only when it can change: at dawn and dusk, when a tree comes or goes, and every 2000 ticks for growth.</summary>
    public class MapComponent_AvirShade : MapComponent
    {
        private readonly List<Plant_ShelterTree> trees = new List<Plant_ShelterTree>();
        private bool[] grove;
        private bool dirty = true, day, roofStale = true, groveCleared;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref groveCleared, "avirGroveCleared", false);
        }

        /// <summary>What may grow under a shelter tree's canopy. The wings let slivers of light through: enough for grass,
        /// herbs, shrubs and the night plants, not enough for trees - only the glowing night trees (and the shelter
        /// trees themselves) may stand under it. Other mods' grasses and herbs are allowed too: no flag needed.</summary>
        public static bool AllowedUnderCanopy(ThingDef plant)
        {
            if (plant?.plant == null) return true;
            if (typeof(Plant_ShelterTree).IsAssignableFrom(plant.thingClass)) return true;
            if (!plant.plant.IsTree) return true;                           // grass, herbs, bushes, night plants
            NorthPlant e = NorthPlant.Of(plant);
            return e != null && e.nightGrowth;                              // only the glowing night trees
        }

        // ---------------------------------------------------------------- the undergrowth
        /// <summary>Share of the canopy's cells that carries low plants (grass, herbs) when it is full.</summary>
        private const float UndergrowthShare = 0.7f;   // glimpses of light: enough for grass and night plants, not for young trees

        /// <summary>The biome's own low wild plants that may grow in the shade (weighted by the biome's commonality).</summary>
        private List<ThingDef> UndergrowthPool()
        {
            var pool = new List<ThingDef>();
            if (map.Biome == null) return pool;
            foreach (ThingDef d in map.Biome.AllWildPlants)
                if (d?.plant != null && !d.plant.IsTree && !d.plant.cavePlant && AllowedUnderCanopy(d) && map.Biome.CommonalityOfPlant(d) > 0f)
                    pool.Add(d);
            return pool;
        }

        private bool SpawnUndergrowth(IntVec3 c, List<ThingDef> pool, Plant_ShelterTree tree, float growth)
        {
            if (!c.InBounds(map) || (c - tree.Position).LengthHorizontal < 1.2f) return false;
            ThingDef d = pool.RandomElementByWeight(x => map.Biome.CommonalityOfPlant(x));
            if (c.GetPlant(map) != null || c.GetEdifice(map) != null || c.GetTerrain(map).IsWater) return false;
            if (!d.CanEverPlantAt(c, map) || !PlantUtility.SnowAllowsPlanting(c, map)) return false;
            Plant p = (Plant)ThingMaker.MakeThing(d);
            p.Growth = growth;
            GenSpawn.Spawn(p, c, map);
            return true;
        }

        /// <summary>A new map: every grown shelter tree gets its undergrowth at once (grass and herbs in patches).</summary>
        private void SeedUndergrowth(Plant_ShelterTree t, List<IntVec3> cells, List<ThingDef> pool)
        {
            if (pool.Count == 0) return;
            int want = Mathf.RoundToInt(cells.Count * UndergrowthShare * Rand.Range(0.8f, 1f));
            for (int k = 0; k < want * 3 && want > 0; k++)
                if (SpawnUndergrowth(cells.RandomElement(), pool, t, Rand.Range(0.3f, 1f))) want--;
        }

        /// <summary>Now and then: a shelter tree whose canopy is thin gets a young plant under it (so the undergrowth also
        /// returns after a fire, a grazing herd or on a map made before this was added).</summary>
        private void Undergrow()
        {
            if (trees.Count == 0) return;
            List<ThingDef> pool = UndergrowthPool();
            if (pool.Count == 0) return;
            foreach (Plant_ShelterTree t in trees)
            {
                if (!t.Spawned || !t.CastsShade) continue;
                var cells = new List<IntVec3>(t.CanopyCells());
                int low = 0;
                foreach (IntVec3 c in cells)
                {
                    Plant p = c.GetPlant(map);
                    if (p != null && !p.def.plant.IsTree) low++;
                }
                if (low >= cells.Count * UndergrowthShare) continue;
                int grow = 3;   // the undergrowth comes back quickly
                for (int k = 0; k < 18 && grow > 0; k++)
                    if (SpawnUndergrowth(cells.RandomElement(), pool, t, 0.05f)) grow--;
            }
        }

        private bool PlantAt(IntVec3 c, string defName, Plant_ShelterTree tree, float minFromTrunk)
        {
            ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (d == null || !c.InBounds(map) || (c - tree.Position).LengthHorizontal < minFromTrunk) return false;
            if (c.GetPlant(map) != null || c.GetEdifice(map) != null || c.GetTerrain(map).IsWater || !d.CanEverPlantAt(c, map)) return false;
            Plant p = (Plant)ThingMaker.MakeThing(d);
            p.Growth = Rand.Range(0.5f, 1f);
            GenSpawn.Spawn(p, c, map);
            return true;
        }

        /// <summary>Once, on a new map: the wild plants the canopies would not allow are cleared from under them.</summary>
        private void ClearUnderCanopies()
        {
            int n = 0;
            for (int i = 0; i < grove.Length; i++)
            {
                if (!grove[i]) continue;
                Plant p = map.thingGrid.ThingAt<Plant>(map.cellIndices.IndexToCell(i));
                if (p != null && !p.sown && !AllowedUnderCanopy(p.def)) { p.Destroy(); n++; }
            }
            if (n > 0 && Prefs.DevMode) Log.Message("[Avir] cleared " + n + " plants from under the shelter canopies");
            // and the groves get their glow: night plants around every grown shelter tree, now and then a night tree
            string[] small = { "Avir_PlantRimebloom", "Avir_PlantMoonshard", "Avir_PlantStarbell", "Avir_PlantSnowbalm" };
            string[] big = { "Avir_TreeFrostglass", "Avir_TreeRimecrown" };
            List<ThingDef> pool = UndergrowthPool();
            foreach (Plant_ShelterTree t in trees)
            {
                if (!t.Spawned || !t.CastsShade) continue;
                var cells = new List<IntVec3>(t.CanopyCells());
                SeedUndergrowth(t, cells, pool);                                   // grass and herbs - the great one too
                if (t.Shelter?.seasonalTexPath != null) continue;                  // the glow is for the northern groves
                if (map.Biome?.workerClass != typeof(BiomeWorker_AvirNorth)) continue;
                int want = Rand.RangeInclusive(2, 4);
                for (int k = 0; k < 40 && want > 0; k++)
                    if (PlantAt(cells.RandomElement(), small.RandomElement(), t, 1.2f)) want--;
                if (Rand.Chance(0.35f))
                    for (int k = 0; k < 20; k++)
                        if (PlantAt(cells.RandomElement(), big.RandomElement(), t, 2.5f)) break;
            }
        }
        private Season lastSeason = Season.Undefined;
        private int seasonCursor;
        private bool seasonSweeping;
        private const int SeasonSliceSize = 1500;

        /// <summary>One slice of the season pass: redraws the plants (and resets the glow of the night plants) whose own
        /// season has turned since they were last drawn.</summary>
        private void SeasonSlice()
        {
            Vector2 ll = Find.WorldGrid.LongLatOf(map.Tile);
            bool eternal = map.Biome?.GetModExtension<EternalSeason>() != null;
            if (seasonCursor == 0)
            {
                foreach (Plant_ShelterTree t in trees)
                    if (t.Spawned && t.Shelter?.seasonalTexPath != null && t.shownSeason != (eternal ? AvirSeason.For(t) : AvirSeason.For(t, ll)))
                        map.mapDrawer.MapMeshDirty(t.Position, MapMeshFlagDefOf.Things);
                foreach (CompNightGlower g in new List<CompNightGlower>(nightGlowers))
                    if (g.parent.Spawned && g.Glows && ((CompProperties_NightGlower)g.props).seasonalShare > 0f && g.TurnedSeason())
                        g.ForceRegister(map);
            }
            List<Thing> plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
            int end = System.Math.Min(plants.Count, seasonCursor + SeasonSliceSize);
            for (int i = seasonCursor; i < end; i++)
                if (plants[i] is Plant_AvirNorth pn && pn.shownSeason != Season.Undefined
                    && (pn.Ext?.Seasonal == true || pn.Ext?.youngTexPath != null)
                    && pn.shownSeason != (eternal ? AvirSeason.For(pn) : AvirSeason.For(pn, ll)))
                    map.mapDrawer.MapMeshDirty(pn.Position, MapMeshFlagDefOf.Things);
            seasonCursor = end;
            if (seasonCursor >= plants.Count) seasonSweeping = false;
        }

        public MapComponent_AvirShade(Map map) : base(map) { }

        public static MapComponent_AvirShade Of(Map m) => m?.GetComponent<MapComponent_AvirShade>();

        private readonly HashSet<CompNightGlower> nightGlowers = new HashSet<CompNightGlower>();
        private readonly HashSet<Plant_NightWaking> wakers = new HashSet<Plant_NightWaking>();
        public void RegisterNight(CompNightGlower g) => nightGlowers.Add(g);
        public void UnregisterNight(CompNightGlower g) => nightGlowers.Remove(g);
        public void RegisterWaker(Plant_NightWaking p) => wakers.Add(p);
        public void UnregisterWaker(Plant_NightWaking p) => wakers.Remove(p);

        private readonly HashSet<Plant_AvirNorth> canopies = new HashSet<Plant_AvirNorth>();
        public bool AnyCanopy => canopies.Count > 0;
        public IEnumerable<Plant_AvirNorth> Canopies => canopies;
        public void RegisterCanopy(Plant_AvirNorth t) { canopies.Add(t); CanopyDepth.Dirty(map); }
        public void UnregisterCanopy(Plant_AvirNorth t) { canopies.Remove(t); CanopyDepth.Dirty(map); if (fogEdges.Remove(t)) NorthCanopy.DirtyFog(t.canopyRect, map); }
        private readonly HashSet<Plant_AvirNorth> fogEdges = new HashSet<Plant_AvirNorth>();
        public void SetFogEdge(Plant_AvirNorth t, bool on) { if (on) fogEdges.Add(t); else fogEdges.Remove(t); }
        public bool AnyFogEdge => fogEdges.Count > 0;
        public IEnumerable<Plant_AvirNorth> FogEdges => fogEdges;

        /// <summary>A building came or went at these cells: every crown over them is printed again (above or below the buildings).</summary>
        public void NotifyBuildingChanged(CellRect r)
        {
            foreach (Plant_AvirNorth t in canopies)
                if (t.Spawned && t.canopyRect.Overlaps(r)) map.mapDrawer.MapMeshDirty(t.Position, MapMeshFlagDefOf.Things);
        }

        public bool Any => trees.Count > 0;
        public void Register(Plant_ShelterTree t) { if (!trees.Contains(t)) { trees.Add(t); dirty = true; } }
        public void Unregister(Plant_ShelterTree t) { if (trees.Remove(t)) dirty = true; }

        public bool InGrove(IntVec3 c)
        {
            if (trees.Count == 0) return false;
            if (dirty || grove == null) Rebuild();
            return c.InBounds(map) && grove[map.cellIndices.CellToIndex(c)];
        }

        /// <summary>In the daytime shade of a shelter tree.</summary>
        public bool Shaded(IntVec3 c) => day && InGrove(c);

        private void Rebuild()
        {
            dirty = false;
            roofStale = true;
            if (grove == null || grove.Length != map.cellIndices.NumGridCells) grove = new bool[map.cellIndices.NumGridCells];
            else System.Array.Clear(grove, 0, grove.Length);
            foreach (Plant_ShelterTree t in trees)
            {
                if (!t.Spawned || !t.CastsShade) continue;
                foreach (IntVec3 c in t.CanopyCells())
                    if (c.InBounds(map)) grove[map.cellIndices.CellToIndex(c)] = true;
            }
        }

        public override void MapComponentUpdate()
        {
            List<(Map map, CellRect r)> pend = NorthCanopy.pendingFog;
            if (pend.Count == 0 || !NorthCanopy.SectionsReady(map)) return;
            for (int i = pend.Count - 1; i >= 0; i--)
                if (pend[i].map == map) { CellRect r = pend[i].r; pend.RemoveAt(i); NorthCanopy.DirtyFog(r, map); }
                else if (pend[i].map == null || pend[i].map.Disposed) pend.RemoveAt(i);
        }

        public override void MapComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 250 != 77 || trees.Count == 0 && nightGlowers.Count == 0 && wakers.Count == 0 && canopies.Count == 0) return;
            bool d = !Plant_AvirNorth.IsNight(map);
            // the seasons turn plant by plant: around a change of season (a few days either side) every seasonal plant
            // is checked once an hour and redrawn when its own season has turned (AvirSeason.For - early, on time, late)
            // In slices: every 250 ticks the next 1500 plants are looked at, so a big forest costs a little each time
            // rather than one hitch. Only while a change of season is near; nothing at all the rest of the year.
            Season season = AvirSeason.Of(map);
            if (season != lastSeason) { lastSeason = season; seasonCursor = 0; seasonSweeping = true; }
            if (now % 2500 == 77 && !seasonSweeping && AvirSeason.NearChange(map)) { seasonCursor = 0; seasonSweeping = true; }
            if (seasonSweeping) SeasonSlice();
            // the canopy roof follows the grove: whenever the grove was rebuilt (a tree came, went or grew), on load,
            // and at dawn and dusk. (InGrove rebuilds lazily, so "dirty" alone cannot tell us - roofStale does.)
            if (dirty) Rebuild();
            if (!groveCleared && grove != null) { groveCleared = true; ClearUnderCanopies(); }
            if (d != day || roofStale) { roofStale = false; SyncCanopyRoof(d); }
            if (now % 1500 == 77) Undergrow();
            if (d != day)
            {
                day = d;
                // the wings open or close: redraw the trees
                foreach (Plant_ShelterTree t in trees)
                    if (t.Spawned) map.mapDrawer.MapMeshDirty(t.Position, MapMeshFlagDefOf.Things);
            }
            // night plants wake and sleep by their own darkness: night, or a dim room (a pot indoors)
            foreach (Plant_NightWaking p in wakers)
            {
                if (!p.Spawned) continue;
                bool dark = Plant_AvirNorth.IsDark(p);
                if (dark != p.awakeShown) { p.awakeShown = dark; map.mapDrawer.MapMeshDirty(p.Position, MapMeshFlagDefOf.Things); }
            }
            foreach (CompNightGlower g in new List<CompNightGlower>(nightGlowers))
            {
                if (!g.parent.Spawned) continue;
                g.UpdateLit(map);
                if (((CompProperties_NightGlower)g.props).maxLight > 0f)
                {
                    int step = Mathf.RoundToInt(g.LightFactor * 10f);
                    if (step != g.lightStep) { bool first = g.lightStep < 0; g.lightStep = step; if (!first && g.Glows) g.ForceRegister(map); }
                }
            }
            // a fog edge moves as the mountain is opened up: those trees print themselves again
            foreach (Plant_AvirNorth t in fogEdges)
                if (t.fogEdge && t.Spawned && !NorthCanopy.FogEdge(t, map)) map.mapDrawer.MapMeshDirty(t.Position, MapMeshFlagDefOf.Things);
            if (now % 2000 == 77) dirty = true;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            day = !Plant_AvirNorth.IsNight(map);
            // the canopy roof is saved with the map's roofs: take the cells back under our care
            canopyRoofed.Clear();
            RoofDef canopy = NorthDefOf.Avir_CanopyRoof;
            if (canopy != null)
                foreach (IntVec3 c in map.AllCells)
                    if (map.roofGrid.RoofAt(c) == canopy) canopyRoofed.Add(map.cellIndices.CellToIndex(c));
        }

        // ---------------------------------------------------------------- the canopy roof
        // By day a grown shelter tree with open wings is a roof over its grove: the ground under it lies in shade
        // as under any roof, no rain or snow falls there, and the blizzard cannot reach whoever stands in it. It is
        // its own roof (Avir_CanopyRoof): it never collapses, and the sky's light still reaches the plants and pawns
        // under it (Patch_CanopyLetsLightThrough) - only the look is shade. At night the wings fold and it is gone.
        private readonly HashSet<int> canopyRoofed = new HashSet<int>();

        private void SyncCanopyRoof(bool isDay)
        {
            RoofDef canopy = NorthDefOf.Avir_CanopyRoof;
            if (canopy == null) return;
            if (grove == null) Rebuild();
            var want = new HashSet<int>();
            if (isDay && grove != null)
                for (int i = 0; i < grove.Length; i++)
                    if (grove[i]) want.Add(i);
            RoofGrid roofs = map.roofGrid;
            foreach (int i in new List<int>(canopyRoofed))
                if (!want.Contains(i))
                {
                    if (roofs.RoofAt(i) == canopy) roofs.SetRoof(map.cellIndices.IndexToCell(i), null);
                    canopyRoofed.Remove(i);
                }
            foreach (int i in want)
            {
                if (canopyRoofed.Contains(i)) continue;
                IntVec3 c = map.cellIndices.IndexToCell(i);
                if (roofs.RoofAt(i) != null) continue;              // a real roof stays as it is
                roofs.SetRoof(c, canopy);
                canopyRoofed.Add(i);
            }
        }
    }

    /// <summary>Cold soil takes seed even under snow.</summary>
    [HarmonyPatch(typeof(PlantUtility), nameof(PlantUtility.SnowAllowsPlanting))]
    public static class Patch_ColdSoilUnderSnow
    {
        public static void Postfix(IntVec3 c, Map map, ref bool __result)
        {
            if (!__result && c.InBounds(map) && c.GetTerrain(map)?.HasModExtension<SowsUnderSnow>() == true) __result = true;
        }
    }

    public class SowsUnderSnow : DefModExtension { }
}

namespace Avir.North
{
    /// <summary>Giants keep their distance: a wild plant with minSpacingBetweenSamePlant (vanilla reads it only for
    /// planted spawners) does not come up within that distance of another of its kind - shelter trees stand apart.</summary>
    [HarmonyLib.HarmonyPatch(typeof(RimWorld.WildPlantSpawner), "PlantChoiceWeight")]
    public static class Patch_WildSpacing
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<RimWorld.WildPlantSpawner, Verse.Map> mapOf =
            HarmonyLib.AccessTools.FieldRefAccess<RimWorld.WildPlantSpawner, Verse.Map>("map");

        public static void Postfix(RimWorld.WildPlantSpawner __instance, Verse.ThingDef plantDef, Verse.IntVec3 c, ref float __result)
        {
            // under a shelter tree's canopy only small night plants (MapComponent_AvirShade.AllowedUnderCanopy)
            if (__result > 0f && plantDef != null && !MapComponent_AvirShade.AllowedUnderCanopy(plantDef))
            {
                Verse.Map m = mapOf(__instance);
                if (MapComponent_AvirShade.Of(m)?.InGrove(c) == true) { __result = 0f; return; }
            }
            NorthPlant np = __result > 0f ? NorthPlant.Of(plantDef) : null;
            if (np != null && np.avoidSand)
            {
                Verse.Map sm = mapOf(__instance);
                Verse.TerrainDef t = sm != null && c.InBounds(sm) ? c.GetTerrain(sm) : null;
                if (t != null && t.defName.IndexOf("Sand", System.StringComparison.OrdinalIgnoreCase) >= 0) { __result = 0f; return; }
            }
            if (np != null && np.nearWater > 0)
            {
                Verse.Map wm = mapOf(__instance);
                if (wm != null) __result *= WaterNearness.Near(wm, c, np.nearWater) ? np.nearWaterFactor : np.farFactor;
            }
            float d = plantDef?.plant?.minSpacingBetweenSamePlant ?? 0f;
            if (__result <= 0f || d <= 0f) return;
            Verse.Map map = mapOf(__instance);
            if (map == null) return;
            foreach (Verse.IntVec3 x in Verse.GenRadial.RadialCellsAround(c, d, true))
                if (x.InBounds(map) && x.GetPlant(map)?.def == plantDef) { __result = 0f; return; }
        }
    }
}

namespace Avir.North
{
    using System.Collections.Generic;
    using RimWorld;
    using UnityEngine;
    using Verse;

    /// <summary>A glow that wakes at night: the northern herbs and crystals light up after dusk and go dark at dawn.</summary>
    public class CompProperties_NightGlower : CompProperties_Glower
    {
        /// <summary>Grown this far before it glows.</summary>
        public float minGrowth = 0.3f;
        /// <summary>Each plant picks one of these (by its id) - the def's own glowColor too; empty = the default palette.</summary>
        public List<ColorInt> glowColors;
        /// <summary>This share of the plants glows in the season's colour outside summer (by its id): icy blue in
        /// winter, green and pink in spring, amber and red in autumn.</summary>
        public float seasonalShare;
        /// <summary>If set: the light at its brightest (0..1 of full white) - reached at `fullGrowth`; from `minGrowth`
        /// it starts at half of that.</summary>
        public float maxLight;
        public float fullGrowth = 0.9f;

        // soft, like the north's own glows (about 180 at the brightest), not lamps
        public static readonly ColorInt[] WinterGlow = { new ColorInt(105, 140, 180, 0), new ColorInt(130, 150, 180, 0), new ColorInt(85, 115, 180, 0) };
        public static readonly ColorInt[] SpringGlow = { new ColorInt(90, 165, 110, 0), new ColorInt(170, 115, 150, 0), new ColorInt(130, 170, 90, 0) };
        public static readonly ColorInt[] AutumnGlow = { new ColorInt(180, 120, 55, 0), new ColorInt(180, 80, 50, 0), new ColorInt(170, 140, 65, 0) };
        public CompProperties_NightGlower() { compClass = typeof(CompNightGlower); overrideIsCavePlant = true; }

        public static readonly ColorInt[] Palette =
        {
            new ColorInt(150, 120, 200, 0), // pale lilac
            new ColorInt(110, 170, 140, 0), // pale green
            new ColorInt(190, 150, 90, 0),  // pale amber
            new ColorInt(110, 140, 200, 0), // pale blue
            new ColorInt(185, 120, 160, 0), // pale rose
            new ColorInt(160, 162, 178, 0), // silvery white
        };
    }

    public class CompNightGlower : CompGlower
    {
        private CompProperties_NightGlower NightProps => (CompProperties_NightGlower)props;
        [Unsaved] private Season glowSeason = Season.Undefined;

        /// <summary>True once when this plant's own season has turned since its glow was last set.</summary>
        public bool TurnedSeason()
        {
            Season s = AvirSeason.For(parent);
            if (s == glowSeason) return false;
            glowSeason = s;
            return true;
        }

        protected override bool ShouldBeLitNow
        {
            get
            {
                if (!parent.Spawned || !Plant_AvirNorth.IsDark(parent)) return false;
                if (parent is Plant p && p.Growth < NightProps.minGrowth) return false;
                return base.ShouldBeLitNow;
            }
        }

        /// <summary>One hue per plant: the def's own colour or one of the palette, picked by the plant's id.</summary>
        /// <summary>How strong the glow is now (growth), in tenths; the map component re-lights the plant when it changes.</summary>
        public int lightStep = -1;
        public float LightFactor
        {
            get
            {
                CompProperties_NightGlower np = NightProps;
                if (!(parent is Plant p) || p.Growth >= np.fullGrowth) return 1f;
                return Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(np.minGrowth, np.fullGrowth, p.Growth));
            }
        }

        public override ColorInt GlowColor
        {
            get
            {
                ColorInt c = RawGlowColor;
                CompProperties_NightGlower np = NightProps;
                if (np.maxLight <= 0f) return c;
                float top = Mathf.Max(1, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
                float k = 255f * np.maxLight * LightFactor / top;
                return new ColorInt(Mathf.RoundToInt(c.r * k), Mathf.RoundToInt(c.g * k), Mathf.RoundToInt(c.b * k), 0);
            }
            set => base.GlowColor = value;
        }

        private ColorInt RawGlowColor
        {
            get
            {
                CompProperties_NightGlower np = NightProps;
                if (BurningGlow.TryGet(parent, out ColorInt hot)) return hot;
                if (np.seasonalShare > 0f && parent.Spawned && Rand.ValueSeeded(parent.thingIDNumber ^ 0x5EA5) < np.seasonalShare)
                {
                    ColorInt[] pal = null;
                    switch (AvirSeason.For(parent))
                    {
                        case Season.Winter: case Season.PermanentWinter: pal = CompProperties_NightGlower.WinterGlow; break;
                        case Season.Spring: pal = CompProperties_NightGlower.SpringGlow; break;
                        case Season.Fall: pal = CompProperties_NightGlower.AutumnGlow; break;
                    }
                    if (pal != null) return pal[Mathf.Abs(parent.thingIDNumber * 31 + 7) % pal.Length];
                }
                List<ColorInt> own = NightProps.glowColors;
                int n = (own?.Count ?? CompProperties_NightGlower.Palette.Length) + 2;
                int i = Mathf.Abs(parent.thingIDNumber * 7919 + 13) % n;
                if (i < 2) return base.GlowColor;
                return own != null ? own[i - 2] : CompProperties_NightGlower.Palette[i - 2];
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            MapComponent_AvirShade.Of(parent.Map)?.RegisterNight(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            MapComponent_AvirShade.Of(map)?.UnregisterNight(this);
            base.PostDeSpawn(map, mode);
        }
    }

    /// <summary>A plant that sleeps by day and wakes at night: another picture after dusk (its crystals open and shine).</summary>
    public class NightWaking : DefModExtension
    {
        public string nightTexPath;
        /// <summary>Grown this far before it wakes at night (its flowers come).</summary>
        public float minGrowth = 0.3f;
        private Graphic night;

        public Graphic Night(ThingDef d)
        {
            if (night == null && nightTexPath != null)
                night = GraphicDatabase.Get(d.graphicData.graphicClass, nightTexPath, d.graphicData.shaderType?.Shader ?? ShaderDatabase.Cutout,
                    d.graphicData.drawSize, d.graphicData.color, d.graphicData.colorTwo);
            return night;
        }
    }

    public class Plant_NightWaking : Plant_AvirNorth
    {
        /// <summary>Awake in the picture now; flipped by the map component when the dark comes or goes.</summary>
        public bool awakeShown;

        protected override Graphic BaseGraphic
        {
            get
            {
                NightWaking nw = def.GetModExtension<NightWaking>();
                if (Spawned && awakeShown && LifeStage != PlantLifeStage.Sowing && Growth >= (nw?.minGrowth ?? 0.3f))
                    return nw?.Night(def) ?? base.BaseGraphic;
                return base.BaseGraphic;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            awakeShown = IsDark(this);
            MapComponent_AvirShade.Of(map)?.RegisterWaker(this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            MapComponent_AvirShade.Of(Map)?.UnregisterWaker(this);
            base.DeSpawn(mode);
        }
    }
}
