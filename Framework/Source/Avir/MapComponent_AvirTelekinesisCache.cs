using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Auto-attached to every map (vanilla instantiates every MapComponent subclass it finds - no
    /// XML registration needed, same as ManipulatorBeam's own scan-cache component).
    ///
    /// Rebuilds a ThingDef -> List&lt;Thing&gt; bucket of everything listerHaulables currently
    /// considers "potentially needing hauling", once every RefreshIntervalTicks, SHARED across
    /// every telekinetic hauler on the map. Without this, TryFindBillIngredientTransfer was
    /// doing a fresh O(all haulables) scan for every unmet ingredient on every bench, every
    /// pawn's own refresh cycle - with several amulet-wearers and a busy base, those scans
    /// stack up fast. Bucketing by def turns "scan everything, filter by def" into "look up the
    /// bucket for this exact def", which is the actual hot path in ingredient matching (a
    /// RecipeDef's IngredientCount almost always narrows to a short, known list of ThingDefs via
    /// ThingFilter.AllowedThingDefs).
    /// </summary>
    public class MapComponent_AvirTelekinesisCache : MapComponent
    {
        private const int RefreshIntervalTicks = 30;

        private int nextRefreshTick;
        private readonly Dictionary<ThingDef, List<Thing>> haulablesByDef = new Dictionary<ThingDef, List<Thing>>();

        // Teleports requested from Harmony postfixes on vanilla/other-mod methods are NEVER
        // performed synchronously - those methods can be running from deep inside another mod's
        // (or vanilla's own) active enumeration over map.listerHaulables or similar, and
        // DeSpawn()/GenPlace mutate exactly that kind of collection. Doing it there caused a
        // "Collection was modified" crash inside WorkGiver_HaulGeneral. Instead, the postfix just
        // records what it wants done, and MapComponentTick() - which always runs at a safe point
        // between pawns' own turns, never from inside someone else's search - drains the queue.
        private readonly List<PendingTeleport> pendingTeleports = new List<PendingTeleport>();

        private struct PendingTeleport
        {
            public Thing thing;
            public int count; // <=0 means "move the whole stack"
            public IntVec3 destinationCell; // used when benchThing is null
            public Thing benchThing; // if set, deliver next to this bench instead of a fixed cell
        }

        public MapComponent_AvirTelekinesisCache(Map map) : base(map)
        {
        }

        /// <summary>
        /// THINGS THAT HAVE BEEN PROMISED A TELEPORT BUT HAVE NOT MOVED YET.
        ///
        /// THE BUG THIS EXISTS FOR, IN MAXIMUS'S WORDS: "he pulls the wood to himself, and then
        /// walks to fetch it." That is the single most absurd symptom in the whole telekinesis
        /// system and it comes straight out of the one-tick gap below.
        ///
        /// The storage intercept cancels the walking job outright and queues the move. But the
        /// move happens on the NEXT tick, and the pawn picks its replacement job THIS tick - while
        /// the wood is still lying in its old spot. So it cheerfully takes a second job aimed at
        /// the old position, sets off, and the teleport fires behind it. It ends up standing on an
        /// empty square while the wood sits where it was sent.
        ///
        /// Nothing was wrong with either half. The job cancel is right; the deferred teleport is
        /// right and must stay deferred - the comment on pendingTeleports records that doing it
        /// synchronously crashed WorkGiver_HaulGeneral mid-enumeration. What was missing is that
        /// for one tick the thing is neither where it was nor where it is going, and during that
        /// tick nothing should be allowed to plan around it.
        ///
        /// So a promised thing is off-limits until the queue drains. One tick of invisibility
        /// costs nothing; planning against a stale position costs a whole wasted trip.
        /// </summary>
        private static readonly HashSet<Thing> pendingThings = new HashSet<Thing>();

        /// <summary>
        /// Is a teleport already promised for this thing? Callers that pick things to move or to
        /// walk to must ask this first.
        /// </summary>
        public static bool IsAwaitingTeleport(Thing thing)
        {
            return thing != null && pendingThings.Contains(thing);
        }

        public void EnqueueTeleportToCell(Thing thing, IntVec3 destination, int count = -1)
        {
            if (thing != null && destination.IsValid)
            {
                pendingTeleports.Add(new PendingTeleport { thing = thing, destinationCell = destination, count = count });
                pendingThings.Add(thing);
            }
        }

        public void EnqueueTeleportNearBench(Thing thing, Thing bench, int count = -1)
        {
            if (thing != null && bench != null)
            {
                pendingTeleports.Add(new PendingTeleport { thing = thing, benchThing = bench, count = count });
                pendingThings.Add(thing);
            }
        }

        // Cheap presence check, on its own much slower cadence than the rebuild itself - the
        // rebuild only has a reason to exist if at least one pawn on THIS map currently wears an
        // Avir telekinesis amulet. Without this gate, every map in the game was paying the full
        // haulables-bucketing cost every 30 ticks forever, even in games that never touch this
        // feature at all - on a large, item-dense, long-running colony that's a real, continuous,
        // always-on cost for zero benefit.
        private const int WearerCheckIntervalTicks = 250; // vanilla's own "rare tick" cadence
        private int nextWearerCheckTick;
        private bool anyWearerPresent;

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            ProcessPendingTeleports();

            if (Find.TickManager.TicksGame >= nextWearerCheckTick)
            {
                nextWearerCheckTick = Find.TickManager.TicksGame + WearerCheckIntervalTicks;
                anyWearerPresent = AnyTelekinesisWearerPresent();
            }

            if (!anyWearerPresent)
            {
                return; // nobody here can use this cache - don't pay to rebuild it
            }

            if (Find.TickManager.TicksGame < nextRefreshTick)
            {
                return;
            }

            Rebuild();
        }

        private bool AnyTelekinesisWearerPresent()
        {
            IReadOnlyList<Pawn> spawned = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                Pawn pawn = spawned[i];
                if (pawn?.apparel == null)
                {
                    continue;
                }

                List<Apparel> worn = pawn.apparel.WornApparel;
                for (int a = 0; a < worn.Count; a++)
                {
                    if (worn[a].TryGetComp<Comp_Telekinesis>() != null)
                    {
                        return true;
                    }
                }

                if (Artifactorics.ArtifactTelekinesis.Radius(pawn) > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private void ProcessPendingTeleports()
        {
            if (pendingTeleports.Count == 0)
            {
                return;
            }

            for (int i = 0; i < pendingTeleports.Count; i++)
            {
                PendingTeleport pending = pendingTeleports[i];
                Thing thing = pending.thing;
                if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map != map)
                {
                    continue;
                }

                if (pending.benchThing != null)
                {
                    if (!pending.benchThing.Destroyed && pending.benchThing.Spawned)
                    {
                        AvirTelekinesisUtility.TryTeleportIngredientNear(thing, pending.benchThing, map, pending.count);
                    }
                }
                else
                {
                    AvirTelekinesisUtility.TryTeleportThingDirectly(thing, pending.destinationCell, map, pending.count);
                }
            }

            // The gap is over: everything promised has now actually moved, so the world is
            // consistent again and these things can be planned around normally.
            for (int i = 0; i < pendingTeleports.Count; i++)
            {
                if (pendingTeleports[i].thing != null) pendingThings.Remove(pendingTeleports[i].thing);
            }

            pendingTeleports.Clear();
        }

        /// <summary>
        /// Bucketed haulables as of the last refresh (up to RefreshIntervalTicks stale - that
        /// staleness is intentional and fine here: worst case a pawn's search briefly misses an
        /// item that appeared a few ticks ago, or briefly considers one that was just claimed by
        /// someone else, both of which the normal CanHaul/claim checks downstream catch anyway).
        /// </summary>
        public Dictionary<ThingDef, List<Thing>> HaulablesByDef
        {
            get
            {
                if (nextRefreshTick == 0)
                {
                    // First access before this map component has ever ticked (e.g. right after
                    // load) - build once immediately so the very first search isn't empty.
                    Rebuild();
                }

                return haulablesByDef;
            }
        }

        private void Rebuild()
        {
            nextRefreshTick = Find.TickManager.TicksGame + RefreshIntervalTicks;
            haulablesByDef.Clear();

            foreach (Thing thing in map.listerHaulables.ThingsPotentiallyNeedingHauling())
            {
                ThingDef def = thing?.def;
                if (def == null)
                {
                    continue;
                }

                if (!haulablesByDef.TryGetValue(def, out List<Thing> list))
                {
                    list = new List<Thing>();
                    haulablesByDef[def] = list;
                }

                list.Add(thing);
            }
        }
    }
}
