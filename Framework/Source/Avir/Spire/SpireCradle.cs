using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Artifactorics;
using Avir.Ritualistics;

namespace Avir.Spire
{
    /// <summary>
    /// THE CRADLE OF THE SPIRE. The base of the spire, one on a map. It holds a small centre core of its own that
    /// charges itself, slowly - far too little for a whole spire: the spire's might comes from the up to nine spire
    /// cores sunk into it, which carry its energy, and from fifteen sockets for essentium artifacts.
    ///  - Cores: their number and strengths set the barrier, the flight range and the store.
    ///  - Sockets: each working essentium artifact adds to all of it by its power.
    ///  - On a stream network it draws from the keepers into its cores; what a core cannot hold of the flow is lost
    ///    to the map's stream (ritualism - closing runes when a core is kindled - is what makes this efficient).
    ///  - In space (orbit) its centre core charges five times faster: there is far more energy there.
    /// The flight itself, the ship's shield and the soil are the next steps.
    /// </summary>
    public class CompProperties_SpireCradle : CompProperties
    {
        public int maxCores = 9;
        public float centreCapacity = 300f;
        /// <summary>Energy the centre core makes by itself every rare tick.</summary>
        public float centreGeneration = 2f;
        public float orbitFactor = 5f;
        /// <summary>Energy a sunk core makes every rare tick, per point of its power.</summary>
        public float coreGeneration = 1.5f;
        /// <summary>Energy the spire gives out to the stream network per rare tick, at most (once its store is half full).</summary>
        public float networkDraw = 12f;

        public CompProperties_SpireCradle() { compClass = typeof(CompSpireCradle); }
    }

    public class CompSpireCradle : ThingComp, ISpireCoreHolder, IThingHolder
    {
        public ThingOwner<Thing> cores;
        public float centre;
        public bool barrierOn;
        /// <summary>In orbit (vacuum) the barrier rises by itself and holds the air.</summary>
        public bool autoOrbitBarrier = true;
        public float barrierHp;
        private BarrierDome dome;

        /// <summary>The spire's domain: what rises with it in flight (Odyssey). Wider with every core and its reach.</summary>
        public float DomainRadius => 10f + 3f * Cores.Sum(c => c.reach);
        public Color barrierColor = new Color(0.78f, 0.62f, 1f);

        /// <summary>The barrier's reach: wider with every core and its reach.</summary>
        public float BarrierRadius => Cores.Any() ? 14f + 2.5f * Cores.Sum(c => c.reach) : 0f;
        /// <summary>A pylon's share of it: the radius of its own dome and of its piece of the domain.</summary>
        public float PylonBarrierRadius => Mathf.Max(8f, BarrierRadius * 0.6f);
        public float PylonDomainRadius => Mathf.Max(6f, DomainRadius * 0.6f);
        /// <summary>Energy the raised barrier costs each rare tick just to stand (every pylon's dome too).</summary>
        public float BarrierUpkeep => (0.5f + 0.05f * BarrierRadius) * (1f + 0.5f * Pylons.Count);

        /// <summary>The spire pylons on the cradle's stream network: they carry the barrier and the domain further.</summary>
        public List<CompSpirePylon> Pylons
        {
            get
            {
                if (!parent.Spawned) return new List<CompSpirePylon>();
                if (Find.TickManager.TicksGame - pylonsTick >= 60 || pylonsCache == null)
                {
                    pylonsTick = Find.TickManager.TicksGame;
                    StreamNet net = MapComponent_StreamNetwork.Of(parent.Map)?.NetOf(parent);
                    pylonsCache = net == null ? new List<CompSpirePylon>() : net.nodes.Select(n => n.TryGetComp<CompSpirePylon>()).Where(p => p != null && p.parent.Spawned).ToList();
                }
                return pylonsCache;
            }
        }
        private List<CompSpirePylon> pylonsCache;
        private int pylonsTick = -9999;

        /// <summary>The domain: what rises with the spire - around the cradle and around every pylon on its network.</summary>
        public void DomainCells(HashSet<IntVec3> cells, int maxCells)
        {
            cells.Clear();
            if (!parent.Spawned) return;
            Map map = parent.Map;
            void Add(IntVec3 centre, float r)
            {
                foreach (IntVec3 c in GenRadial.RadialCellsAround(centre, Mathf.Min(r, GenRadial.MaxRadialPatternRadius - 1f), true))
                {
                    if (cells.Count >= maxCells) return;
                    if (!c.InBounds(map)) continue;
                    Building e = c.GetEdifice(map);
                    if (e != null && e.def.building != null && e.def.building.isNaturalRock) continue;   // the mountain stays
                    cells.Add(c);
                }
            }
            Add(parent.Position, DomainRadius);
            foreach (CompSpirePylon p in Pylons) Add(p.parent.Position, PylonDomainRadius);
        }

        private readonly HashSet<IntVec3> shownDomain = new HashSet<IntVec3>();

        /// <summary>Changes whenever the domain does: the cores' reach, the pylons, where they stand.</summary>
        public int DomainSignature
        {
            get
            {
                int h = Mathf.RoundToInt(DomainRadius * 10f) * 31 + parent.Position.GetHashCode();
                foreach (CompSpirePylon p in Pylons) h = h * 17 + p.parent.Position.GetHashCode();
                return h;
            }
        }
        private int shownTick = -9999;

        /// <summary>Selected, the cradle shows what would rise with it, as a gravship shows its substructure.</summary>
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            // redrawn when the domain changes (a core sunk, a pylon raised) - even on pause
            int sig = DomainSignature;
            if (sig != shownTick || shownDomain.Count == 0)
            {
                shownTick = sig;
                DomainCells(shownDomain, 20000);
            }
            GenDraw.DrawFieldEdges(shownDomain.ToList(), new Color(0.75f, 0.55f, 1f, 0.8f));
            MapComponent_SpireLift.Of(parent.Map)?.MarkForDraw();
        }

        public CompProperties_SpireCradle Props => (CompProperties_SpireCradle)props;

        public CompSpireCradle() { cores = new ThingOwner<Thing>(this, false); }

        public ThingOwner GetDirectlyHeldThings() => cores;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());

        public IEnumerable<CompSpireCore> Cores => cores.InnerListForReading.Select(t => t.TryGetComp<CompSpireCore>()).Where(c => c != null);
        public CompArtifactHost Sockets => parent.TryGetComp<CompArtifactHost>();

        public bool InSpace => parent.Spawned && parent.Map.Biome != null && parent.Map.Biome.inVacuum;

        // ------------------------------------------------------------------ the spire's figures

        public float Capacity => Props.centreCapacity + Cores.Sum(c => c.capacity);
        public float Stored => centre + Cores.Sum(c => c.stored);
        public float Efficiency => Cores.Any() ? Cores.Average(c => c.efficiency) : 0.5f;

        /// <summary>The working essentium artifacts' summed power (each about 1, more for a fine heart).</summary>
        public float SocketPower
        {
            get
            {
                CompArtifactHost h = Sockets;
                if (h == null) return 0f;
                float s = 0f;
                for (int i = 0; i < h.slots.Count; i++) if (h.Works(i)) s += CompArtifactHost.StonePower(h.slots[i]);
                return s;
            }
        }

        public float Barrier => Cores.Sum(c => c.power * c.ward) * 100f * (1f + 0.05f * SocketPower);
        public int Range => Mathf.RoundToInt(Cores.Sum(c => 3f * c.reach) + SocketPower);
        /// <summary>
        /// The spire makes energy: the centre core a little, every sunk core by its power - the cores are carriers and
        /// sources at once. The sockets strengthen all of it; in space it is five times as much.
        /// </summary>
        public float Generation => (Props.centreGeneration + Props.coreGeneration * Cores.Sum(c => c.power)) * (1f + 0.1f * SocketPower) * (InSpace ? Props.orbitFactor : 1f);

        // ------------------------------------------------------------------ energy

        public float Give(float amount)
        {
            float left = amount;
            foreach (CompSpireCore c in Cores.OrderBy(c => c.stored / Mathf.Max(1f, c.capacity)))
            {
                if (left <= 0.001f) break;
                left -= c.Charge(left);
            }
            float g = Mathf.Min(left, Props.centreCapacity - centre);
            centre += g; left -= g;
            return amount - left;
        }

        public float Take(float want)
        {
            float got = Mathf.Min(want, centre);
            centre -= got;
            foreach (CompSpireCore c in Cores.OrderByDescending(c => c.stored))
            {
                if (got >= want - 0.001f) break;
                got += c.Take(want - got);
            }
            return got;
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!parent.Spawned) return;
            // the centre core charges the system by itself, slowly
            Give(Generation);
            if (autoOrbitBarrier && InSpace && !barrierOn && BarrierRadius > 0f && Stored >= BarrierUpkeep * 4f)
            {
                barrierOn = true;
                Messages.Message("In the void the spire raises its barrier by itself.", parent, MessageTypeDefOf.NeutralEvent, false);
            }
            BarrierTick();
            // the spire feeds the stream network: once its own store is half full, its surplus goes out to the
            // keepers on its wire - through the cores' efficiency; what is lost runs off into the map's stream
            if (Stored < Capacity * 0.5f) return;
            StreamNet net = MapComponent_StreamNetwork.Of(parent.Map)?.NetOf(parent);
            if (net == null) return;
            float room = net.Keepers.Sum(k => k.Capacity - k.stored);
            if (room <= 0.5f) return;
            float send = Mathf.Min(Props.networkDraw, Stored - Capacity * 0.5f, room / Mathf.Max(0.1f, Efficiency));
            float taken = Take(send);
            float left = taken * Efficiency;
            foreach (CompEnergyKeeper k in net.Keepers.OrderBy(k => k.stored / Mathf.Max(1f, k.Capacity)))
            {
                if (left <= 0.01f) break;
                left -= k.Give(left);
            }
            float lost = taken - taken * Efficiency + Mathf.Max(0f, left);
            if (left > 0.01f) Give(left);
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(parent.Map);
            if (ext != null && lost > 0f) ext.deeds += lost * 40f;
        }

        // ------------------------------------------------------------------ the barrier (the ship's shield)

        /// <summary>
        /// The spire's barrier: a dome over the spire that stops shots from outside, holds the air in space (no
        /// vacuum under it) and keeps the storms of the stream off - lightning, pressure on minds, artifacts losing
        /// charge. It stands on the cores' energy; its strength is the spire's barrier figure, and every blow is
        /// paid from it. Out of energy or broken, it falls.
        /// </summary>
        private void BarrierTick()
        {
            if (!barrierOn || BarrierRadius <= 0f) { DropBarrier(); return; }
            if (Take(BarrierUpkeep) < BarrierUpkeep - 0.001f)
            {
                barrierOn = false;
                DropBarrier();
                Messages.Message("The spire's barrier falls: no energy left to hold it.", parent, MessageTypeDefOf.NegativeEvent, false);
                return;
            }
            // it mends itself from the cores
            float max = Barrier;
            if (barrierHp < max)
            {
                float mend = Mathf.Min(max - barrierHp, max * 0.02f);
                barrierHp += Take(mend * 0.5f) * 2f;
            }
            if (dome == null)
            {
                dome = new BarrierDome { map = parent.Map, center = parent.Position, radius = BarrierRadius, sealsVacuum = true, hit = Hit, group = this };
                BarrierDome.All.Add(dome);
            }
            dome.radius = BarrierRadius;
            // the pylons carry it further: each holds a dome of its own, one barrier with the cradle's
            List<CompSpirePylon> pylons = Pylons;
            foreach (CompSpirePylon p in pylons) p.Hold(this);
            foreach (BarrierDome d in BarrierDome.All.Where(d => d.group == this && d != dome && !pylons.Any(p => p.dome == d)).ToList()) BarrierDome.All.Remove(d);
        }

        private bool HitInner(Projectile p)
        {
            float dmg = Mathf.Max(1f, p.DamageAmount);
            if (barrierHp >= dmg) { barrierHp -= dmg; return true; }
            barrierHp = 0f;
            barrierOn = false;
            DropBarrier();
            Messages.Message("The spire's barrier breaks.", parent, MessageTypeDefOf.NegativeEvent, false);
            return false;
        }

        private void DropBarrier()
        {
            BarrierDome.All.RemoveAll(d => d.group == this);
            dome = null;
            if (pylonsCache != null) foreach (CompSpirePylon p in pylonsCache) p.dome = null;
        }

        public bool Hit(Projectile p) => HitInner(p);

        /// <summary>The barrier's look: soft, mostly clear, breathing slowly; brighter as it is hurt.</summary>
        public void DrawDome(BarrierDome d)
        {
            if (d == null) return;
            Vector3 c = d.center.ToVector3Shifted(); c.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            float hurt = Barrier > 0f ? 1f - Mathf.Clamp01(barrierHp / Barrier) : 1f;
            float a = 0.16f + 0.03f * Mathf.Sin(Find.TickManager.TicksGame / 90f) + 0.22f * hurt;
            Color col = new Color(barrierColor.r, barrierColor.g, barrierColor.b, Mathf.Clamp01(a));
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(c, Quaternion.identity, new Vector3(d.radius * 2f, 1f, d.radius * 2f)),
                MaterialPool.MatFrom(FlowTex.Dome ?? BaseContent.WhiteTex, ShaderDatabase.Transparent, col), 0);
        }

        internal static readonly (string label, Color c)[] BarrierColors =
        {
            ("Starlight (violet)", new Color(0.78f, 0.62f, 1f)),
            ("White gold", new Color(1f, 0.88f, 0.58f)),
            ("Thought (deep purple)", new Color(0.55f, 0.22f, 0.85f)),
            ("Starry space", new Color(0.55f, 0.55f, 1f)),
            ("Sand", new Color(0.9f, 0.78f, 0.5f)),
            ("Silver", new Color(0.85f, 0.88f, 0.95f)),
            ("Moonlit blue", new Color(0.5f, 0.75f, 1f)),
            ("Verdant", new Color(0.5f, 0.85f, 0.45f)),
            ("Crimson", new Color(0.95f, 0.35f, 0.4f)),
            ("Night", new Color(0.35f, 0.3f, 0.5f)),
        };

        public override void PostDraw()
        {
            base.PostDraw();
            DrawDome(dome);
        }

        // ------------------------------------------------------------------ cores in and out

        public bool CanSink(Thing t) => t?.TryGetComp<CompSpireCore>()?.kindled == true && cores.Count < Props.maxCores;

        public void Sink(Thing core)
        {
            if (!CanSink(core)) return;
            if (core.Spawned) core.DeSpawn();
            core.holdingOwner?.Remove(core);
            cores.TryAdd(core, false);
            if (parent.Spawned) RitualFx.Ripple(parent.DrawPos, parent.Map, 3f);
            Messages.Message("A spire core sinks into the cradle (" + cores.Count + " / " + Props.maxCores + ").", parent, MessageTypeDefOf.PositiveEvent, false);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;
            // what rises with the spire is painted, within its domain
            if (ModsConfig.OdysseyActive && parent.Faction == Faction.OfPlayer)
            {
                yield return new Designator_SpireLift(true);
                yield return new Designator_SpireLift(false);
            }
            // flight lives on the grav-engine cradle (Odyssey); an older cradle keeps its old class until rebuilt
            if (!(parent is Building_GravEngine) && parent.Faction == Faction.OfPlayer)
            {
                var fly = new Command_Action
                {
                    defaultLabel = "Fly the spire",
                    defaultDesc = "Lift the spire: everything in its domain rises with it.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Avir/FlySpire", false) ?? ContentFinder<Texture2D>.Get("UI/Commands/LaunchShip", false) ?? BaseContent.BadTex,
                    action = () => { }
                };
                fly.Disable(ModsConfig.OdysseyActive
                    ? "This cradle was built before the spire could fly. Deconstruct it and build it again (its cores come out whole)."
                    : "The spire flies only with Odyssey.");
                yield return fly;
            }
            if (parent.Faction != Faction.OfPlayer) yield break;
            var sink = new Command_Action
            {
                defaultLabel = "Sink a core (" + cores.Count + "/" + Props.maxCores + ")",
                defaultDesc = "Have a colonist carry a kindled spire core into the cradle. Up to " + Props.maxCores + ".",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Avir/SinkCore", false) ?? ContentFinder<Texture2D>.Get("Things/Item/Spire/SpireCore", false) ?? BaseContent.BadTex,
                action = ChooseCore
            };
            if (cores.Count >= Props.maxCores) sink.Disable("all nine cores are in");
            yield return sink;
            var bar = new Command_Toggle
            {
                defaultLabel = "Barrier",
                defaultDesc = "Raise the spire's barrier: a dome of radius " + BarrierRadius.ToString("0") + " that stops shots from outside, holds the air in space and keeps the storms of the stream off. It costs "
                              + (BarrierUpkeep * 10f).ToString("0.#") + " energy an hour to stand, and every blow is paid from its strength (" + barrierHp.ToString("0") + " / " + Barrier.ToString("0") + "), which the cores mend.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Avir/SpireBarrier", false) ?? FlowTex.Dome ?? BaseContent.BadTex,
                isActive = () => barrierOn,
                toggleAction = () => { barrierOn = !barrierOn; if (barrierOn) BarrierTick(); else DropBarrier(); }
            };
            if (ModsConfig.OdysseyActive)
                yield return new Command_Toggle
                {
                    defaultLabel = "Barrier in orbit",
                    defaultDesc = "In the void (orbit, space) the barrier rises by itself whenever the cores can hold it, and keeps the air in.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Avir/SpireBarrier", false) ?? FlowTex.Dome ?? BaseContent.BadTex,
                    isActive = () => autoOrbitBarrier,
                    toggleAction = () => autoOrbitBarrier = !autoOrbitBarrier
                };
            if (cores.Count == 0) bar.Disable("no spire core in the cradle");
            yield return bar;
            yield return new Command_Action
            {
                defaultLabel = "Barrier colour",
                defaultDesc = "Choose the colour of the spire's barrier: a wheel, a palette, hue and saturation - like the Avir's feathers.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Avir/BarrierColour", false) ?? FlowTex.Dome ?? BaseContent.BadTex,
                defaultIconColor = barrierColor,
                action = () => Find.WindowStack.Add(new Dialog_BarrierColor(this))
            };
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Dev: sink a kindled core",
                    action = () =>
                    {
                        Thing t = ThingMaker.MakeThing(parent.def.GetModExtension<SpireCradleExtension>()?.coreDef ?? ThingDef.Named("Avir_SpireCore"));
                        CompSpireCore c = t.TryGetComp<CompSpireCore>();
                        c.kindled = true; c.progress = CompSpireCore.KindleCost; c.power = 1.5f; c.capacity = 3000f; c.efficiency = 0.9f; c.ward = 1.5f; c.reach = 1.25f;
                        Sink(t);
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "Dev: mend the barrier",
                    action = () => barrierHp = Barrier
                };
            }
            if (cores.Count > 0)
                yield return new Command_Action
                {
                    defaultLabel = "Take a core out",
                    defaultDesc = "Lift a core out of the cradle; it keeps its energy.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Avir/TakeCore", false) ?? ContentFinder<Texture2D>.Get("Things/Item/Spire/SpireCore", false) ?? BaseContent.BadTex,
                    action = () =>
                    {
                        var opts = new List<FloatMenuOption>();
                        foreach (Thing t in cores.InnerListForReading.ToList())
                            opts.Add(new FloatMenuOption(t.LabelCap + " - " + t.TryGetComp<CompSpireCore>().stored.ToString("0") + " energy", () =>
                            {
                                IntVec3 at = parent.InteractionCell.IsValid ? parent.InteractionCell : parent.Position;
                                cores.TryDrop(t, at, parent.Map, ThingPlaceMode.Near, out Thing _);
                            }));
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                };
            if (DebugSettings.ShowDevGizmos)
                yield return new Command_Action { defaultLabel = "Dev: fill the spire", action = () => Give(Capacity) };
        }

        private void ChooseCore()
        {
            var opts = new List<FloatMenuOption>();
            foreach (Thing t in parent.Map.listerThings.ThingsOfDef(parent.def.GetModExtension<SpireCradleExtension>()?.coreDef ?? ThingDef.Named("Avir_SpireCore")).ToList())
            {
                CompSpireCore c = t.TryGetComp<CompSpireCore>();
                if (c == null || !c.kindled || t.IsForbidden(Faction.OfPlayer)) continue;
                opts.Add(new FloatMenuOption(t.LabelCap + " - power " + c.power.ToString("0.00"), () =>
                {
                    Pawn p = parent.Map.mapPawns.FreeColonistsSpawned
                        .Where(x => !x.Downed && x.CanReserveAndReach(t, PathEndMode.ClosestTouch, Danger.Deadly) && x.CanReach(parent, PathEndMode.Touch, Danger.Deadly))
                        .OrderBy(x => x.Position.DistanceToSquared(t.Position)).FirstOrDefault();
                    if (p == null) { Messages.Message("No colonist can carry it there.", parent, MessageTypeDefOf.RejectInput, false); return; }
                    Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Avir_SinkSpireCore"), t, parent);
                    job.count = 1;
                    p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }));
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("No kindled spire core on this map", null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        public override string CompInspectStringExtra()
        {
            var sb = new StringBuilder();
            sb.Append("Spire cores: ").Append(cores.Count).Append(" / ").Append(Props.maxCores);
            sb.Append("\nEnergy: ").Append(Stored.ToString("0")).Append(" / ").Append(Capacity.ToString("0"))
              .Append(" (the spire makes ").Append((Generation * 10f).ToString("0.#")).Append(" an hour").Append(InSpace ? ", in space x" + Props.orbitFactor.ToString("0") : "").Append(")");
            sb.Append("\nDomain radius ").Append(DomainRadius.ToString("0")).Append(" (what of it rises: the lift zone)");
            sb.Append("\nBarrier ").Append(barrierOn ? barrierHp.ToString("0") + " / " : "").Append(Barrier.ToString("0")).Append(barrierOn ? " (raised, radius " + BarrierRadius.ToString("0") + ")" : "").Append(", flight range ").Append(Range).Append(" tiles, efficiency ").Append(Efficiency.ToStringPercent());
            return sb.ToString();
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            DropBarrier();
        }

        /// <summary>The cores come out whole when the cradle is destroyed or taken apart - not when it merely
        /// leaves the map (the spire flying away takes them with it).</summary>
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (previousMap != null && cores.Count > 0) cores.TryDropAll(parent.Position, previousMap, ThingPlaceMode.Near);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref cores, "cores", this);
            Scribe_Values.Look(ref centre, "centre");
            Scribe_Values.Look(ref barrierOn, "barrierOn");
            Scribe_Values.Look(ref autoOrbitBarrier, "autoOrbitBarrier", true);
            Scribe_Values.Look(ref barrierHp, "barrierHp");
            Scribe_Values.Look(ref barrierColor, "barrierColor", new Color(0.78f, 0.62f, 1f));
            if (Scribe.mode == LoadSaveMode.PostLoadInit && cores == null) cores = new ThingOwner<Thing>(this, false);
        }
    }

    public class SpireCradleExtension : DefModExtension
    {
        public ThingDef coreDef;
    }

    /// <summary>One spire to a map.</summary>
    public class PlaceWorker_OneSpire : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            // Fix254: asked on every frame while the spire is being placed - only the spire itself, its blueprints and its
            // frames are looked at now, not every thing on the map.
            if (checkingDef is ThingDef td)
            {
                foreach (ThingDef d in new[] { td, td.blueprintDef, td.frameDef })
                {
                    if (d == null) continue;
                    List<Thing> list = map.listerThings.ThingsOfDef(d);
                    for (int i = 0; i < list.Count; i++)
                        if (list[i] != thingToIgnore && list[i] != thing) return "There can be only one spire on a map.";
                }
                return true;
            }
            foreach (Thing t in map.listerThings.AllThings)
            {
                if (t == thingToIgnore || t == thing) continue;
                BuildableDef d = t.def.entityDefToBuild ?? t.def;
                if (d == checkingDef) return "There can be only one spire on a map.";
            }
            return true;
        }
    }

    /// <summary>Carry a kindled core to the cradle and sink it.</summary>
    public class JobDriver_SinkSpireCore : JobDriver
    {
        private Thing Core => job.targetA.Thing;
        private ISpireCoreHolder Cradle => (job.targetB.Thing as ThingWithComps)?.AllComps.OfType<ISpireCoreHolder>().FirstOrDefault();

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOn(() => Cradle == null || !Cradle.CanSink(Core));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return Toils_General.Do(() =>
            {
                Thing c = pawn.carryTracker.CarriedThing;
                if (c == null || Cradle == null) return;
                pawn.carryTracker.innerContainer.Remove(c);
                Cradle.Sink(c);
            });
        }
    }

    /// <summary>The barrier's colour: the game's own colour picker (wheel, palette, hue and saturation),
    /// the same kind the Avir's feathers are coloured with.</summary>
    public class Dialog_BarrierColor : Dialog_ColorPickerBase
    {
        private readonly CompSpireCradle cradle;
        private static List<Color> palette;

        public Dialog_BarrierColor(CompSpireCradle cradle)
            : base(Widgets.ColorComponents.Hue | Widgets.ColorComponents.Sat | Widgets.ColorComponents.Value,
                   Widgets.ColorComponents.Hue | Widgets.ColorComponents.Sat | Widgets.ColorComponents.Value)
        {
            this.cradle = cradle;
            color = cradle.barrierColor;
            oldColor = color;
        }

        // the base size leaves a row a pixel short once the value field is shown
        public override Vector2 InitialSize => new Vector2(620f, 500f);

        protected override bool ShowDarklight => false;
        protected override Color DefaultColor => new Color(0.78f, 0.62f, 1f);
        protected override float ForcedColorValue => -1f;
        protected override bool ShowColorTemperatureBar => false;

        protected override List<Color> PickableColors
        {
            get
            {
                if (palette == null)
                {
                    palette = CompSpireCradle.BarrierColors.Select(b => b.c).ToList();
                    for (int h = 0; h < 18; h++)
                        foreach (float sat in new[] { 1f, 0.55f, 0.3f })
                            palette.Add(Color.HSVToRGB(h / 18f, sat, 1f));
                }
                return palette;
            }
        }

        protected override void SaveColor(Color c) => cradle.barrierColor = new Color(c.r, c.g, c.b, 1f);
    }
}
