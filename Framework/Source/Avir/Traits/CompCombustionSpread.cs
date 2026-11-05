using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir.Traits
{
    public class CompProperties_CombustionSpread : CompProperties
    {
        public int spreadIntervalTicks = 45;
        public float igniteChancePerCell = 0.55f;
        public int maxGenerations = 8;
        public int maxTotalTiles = 60;

        public CompProperties_CombustionSpread()
        {
            this.compClass = typeof(CompCombustionSpread);
        }
    }

    /// <summary>
    /// Every spreadIntervalTicks, looks at the current "frontier" of already-ignited blood-filth
    /// cells, checks their neighbors for MORE blood filth (matching ANY def in bloodFilthDefs,
    /// not one exact def - see CompAbilityEffect_Combustion's defName-independent detection), and
    /// ignites what it finds. Self-destroys once the frontier runs dry or either cap is hit.
    /// </summary>
    public class CompCombustionSpread : ThingComp
    {
        private static readonly IntVec3[] EightWayOffsets =
        {
            new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0), new IntVec3(0, 0, 1), new IntVec3(0, 0, -1),
            new IntVec3(1, 0, 1), new IntVec3(1, 0, -1), new IntVec3(-1, 0, 1), new IntVec3(-1, 0, -1)
        };

        public CompProperties_CombustionSpread Props => (CompProperties_CombustionSpread)this.props;

        private List<IntVec3> frontier = new List<IntVec3>();
        private HashSet<IntVec3> alreadyProcessed = new HashSet<IntVec3>();
        private int generation;
        private HashSet<ThingDef> bloodFilthDefs;

        public void Initialize(IEnumerable<IntVec3> initialFrontier, HashSet<ThingDef> bloodFilthDefsIn)
        {
            this.frontier = new List<IntVec3>(initialFrontier);
            this.alreadyProcessed = new HashSet<IntVec3>(this.frontier);
            this.bloodFilthDefs = bloodFilthDefsIn;
        }

        public override void CompTick()
        {
            base.CompTick();

            Map map = this.parent.Map;
            if (map == null || this.bloodFilthDefs == null || this.bloodFilthDefs.Count == 0)
            {
                this.parent.Destroy();
                return;
            }

            if (this.Props.spreadIntervalTicks <= 0 || Find.TickManager.TicksGame % this.Props.spreadIntervalTicks != 0) return;

            if (this.frontier.Count == 0
                || this.generation >= this.Props.maxGenerations
                || this.alreadyProcessed.Count >= this.Props.maxTotalTiles)
            {
                this.parent.Destroy();
                return;
            }

            List<IntVec3> nextFrontier = new List<IntVec3>();

            foreach (IntVec3 cell in this.frontier)
            {
                if (this.alreadyProcessed.Count >= this.Props.maxTotalTiles) break;

                for (int i = 0; i < EightWayOffsets.Length; i++)
                {
                    if (this.alreadyProcessed.Count >= this.Props.maxTotalTiles) break;

                    IntVec3 neighbor = cell + EightWayOffsets[i];
                    if (!neighbor.InBounds(map) || this.alreadyProcessed.Contains(neighbor)) continue;

                    bool hasBlood = false;
                    List<Thing> things = neighbor.GetThingList(map);
                    for (int t = 0; t < things.Count; t++)
                    {
                        if (this.bloodFilthDefs.Contains(things[t].def)) { hasBlood = true; break; }
                    }
                    if (!hasBlood) continue;

                    this.alreadyProcessed.Add(neighbor);

                    if (Rand.Chance(this.Props.igniteChancePerCell))
                    {
                        bool started = FireUtility.TryStartFireIn(neighbor, map, Rand.Range(0.3f, 0.7f), null);
                        if (started)
                        {
                            nextFrontier.Add(neighbor);
                            CombustionUtility.ConsumeBloodFilthAt(neighbor, map, this.bloodFilthDefs);
                        }
                    }
                }
            }

            this.frontier = nextFrontier;
            this.generation++;

            if (this.frontier.Count == 0) this.parent.Destroy();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref this.frontier, "frontier", LookMode.Value);
            Scribe_Collections.Look(ref this.alreadyProcessed, "alreadyProcessed", LookMode.Value);
            Scribe_Values.Look(ref this.generation, "generation", 0);
            Scribe_Collections.Look(ref this.bloodFilthDefs, "bloodFilthDefs", LookMode.Def);

            if (this.frontier == null) this.frontier = new List<IntVec3>();
            if (this.alreadyProcessed == null) this.alreadyProcessed = new HashSet<IntVec3>();
            if (this.bloodFilthDefs == null) this.bloodFilthDefs = new HashSet<ThingDef>();
        }
    }
}
