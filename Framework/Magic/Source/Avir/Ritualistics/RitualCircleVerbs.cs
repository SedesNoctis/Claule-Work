using System.Collections.Generic;
using System.Linq;
using Avir.Artifactorics;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Avir.Ritualistics
{
    // =====================================================================================
    // The rest of the runes (slice 3): Reach, Release, Lift, Growth, Reforge, Barrier, Thread;
    // filling psyfocus, purging minds, drawing out the living; transmutation into current;
    // the Trap variants, the barrier dome, the endless vortex and self-detonation.
    // =====================================================================================
    public partial class CompRitualCircle
    {
        // ---------------------------------------------------------------- state

        /// <summary>Engraving: the ritual is cut into stone - it is not washed away, takes this many times longer, and is a little better.</summary>
        public const float EngraveWork = 5f, EngraveQuality = 0.1f;
        /// <summary>An Avir engraves with its essentium claws this many times faster.</summary>
        public const float ClawEngraving = 2.5f;
        /// <summary>Energy the endless vortex pours along each of its threads every rare tick, per step of its growth (1..10).</summary>
        public const float VortexYield = 2f;
        /// <summary>Chiselling an engraving off: this share of the work it took to cut.</summary>
        public const float ChiselShare = 0.5f;
        /// <summary>Composure of a building or artifact standing as the conduit.</summary>
        public const float ConduitComposure = 0.6f;

        /// <summary>The drawing on the floor is engraved (else chalk); the next drawing will be engraved.</summary>
        public bool engraved, engraveNext;
        /// <summary>The engraving is to be chiselled off (work, not a click); the work done so far.</summary>
        public bool chiselPending;
        public float chiselWork;

        /// <summary>Removal ordered: chalk is wiped away by whoever cleans, an engraving levelled by a constructor.</summary>
        public bool NeedsChisel => chiselPending && drawn != null;

        public float ChiselWorkNeeded =>
            drawn == null ? 1f
            : engraved ? (2500f + 1500f * drawn.AllRunes.Count() + 800f * drawn.components.Count(c => c.kind != ComponentKind.None))
                         * (1f + 0.25f * (int)drawn.size) * EngraveWork * ChiselShare
            : (150f + 60f * drawn.AllRunes.Count()) * (1f + 0.25f * (int)drawn.size);

        public void FinishChisel(Pawn by)
        {
            bool wasEngraved = engraved;
            chiselPending = false; chiselWork = 0f;
            Erase();
            if (by != null) Messages.Message(by.LabelShortCap + (wasEngraved ? " levelled the engraving away." : " wiped the chalk away."), parent, MessageTypeDefOf.NeutralEvent, false);
        }
        /// <summary>An engraving that runs by itself whenever it is needed, through a conduit at its centre.</summary>
        public bool selfSustain;
        /// <summary>A thing (building, battery, artifact) standing as the conduit of a self-sustaining engraving.</summary>
        public Thing conduit;

        /// <summary>Reforge: 0 quality, 1 heart metal (seal), 2 turn the armour side, 3 turn the weapon side, 4 imbue a Barrier core.</summary>
        public int reforgeMode;
        /// <summary>Ticks the dome has had no energy to hold itself.</summary>
        private int dryTicks;
        /// <summary>Things bound to the circle by thread runes, anywhere on the map.</summary>
        public List<Thing> threads = new List<Thing>();
        /// <summary>The endless vortex: 0 - none; grows while the chalk holds.</summary>
        public float vortex;

        private BarrierDome dome;
        private float domeHp;

        private StreamNet netCache;
        private int netCacheTick = -9999;

        /// <summary>The stream network this circle stands on (refreshed once a second).</summary>
        public StreamNet Net
        {
            get
            {
                int now = Find.TickManager.TicksGame;
                if (now - netCacheTick >= 60) { netCacheTick = now; netCache = MapComponent_StreamNetwork.Of(parent.Map)?.NetOf(parent); }
                return netCache;
            }
        }

        public int MaxThreads => Drawn ? 3 * Mathf.Max(Reading.thread, vortex > 0f ? 1 : 0) : 0;

        public IEnumerable<Thing> Threaded
        {
            get
            {
                if (!Drawn) yield break;
                if (threads.Count > 0 && (Reading.thread > 0 || vortex > 0f))
                    foreach (Thing t in threads) if (t != null && t.Spawned && t.Map == parent.Map) yield return t;
                // on a stream network: everything else on it, as if threaded
                StreamNet net = Net;
                if (net != null)
                    foreach (Thing n in net.nodes)
                        if (n != parent && n.Spawned && n.TryGetComp<CompRitualCircle>() == null && !threads.Contains(n)) yield return n;
            }
        }

        /// <summary>What a barrier spares: your people (but the performer, if the blast is in their hands) and your buildings.</summary>
        private List<Thing> Spared(Map map, Pawn except)
        {
            Faction own = parent.Faction ?? Faction.OfPlayer;
            var list = new List<Thing> { parent };
            foreach (Pawn q in map.mapPawns.AllPawnsSpawned) if (q != except && q.Faction == own) list.Add(q);
            foreach (Building b in map.listerBuildings.allBuildingsColonist) list.Add(b);
            return list;
        }

        /// <summary>Energy drawn out by a Drain step: it feeds the steps after it, or - when Drain is the last - is caught as crystals.</summary>
        private void Gain(RitualReading r, float amount)
        {
            if (amount <= 0f) return;
            if (verbIndex >= r.verbs.Count - 1)
            {
                crystalProgress += amount * Mathf.Clamp(drawQuality, 0.2f, 1.2f) * 0.5f;
                while (crystalProgress >= ChargePerCrystal)
                {
                    crystalProgress -= ChargePerCrystal;
                    GenPlace.TryPlaceThing(ThingMaker.MakeThing(PsychicDefOf.Avir_PsyCrystal), parent.Position, parent.Map, ThingPlaceMode.Near);
                }
            }
            else bank += amount;
        }

        // ---------------------------------------------------------------- Fill: psyfocus; Purge: minds

        /// <summary>Psycasters in reach: their psyfocus to full (100 energy a whole bar).</summary>
        private bool FillPsyfocus(RitualReading r)
        {
            foreach (Pawn q in PawnsInReach(r))
            {
                if (q.Faction != parent.Faction || q.psychicEntropy == null || !q.HasPsylink) continue;
                float need = 1f - q.psychicEntropy.CurrentPsyfocus;
                if (need <= 0.005f) continue;
                float give = Mathf.Min(need * 100f, bank);
                q.psychicEntropy.OffsetPsyfocusDirectly(give / 100f);
                bank -= give; spentTotal += give;
                if (bank <= 0.0001f) return false;
            }
            return true;
        }

        private static IEnumerable<HediffDef> MindStates()
        {
            if (HediffDefOf.PsychicHangover != null) yield return HediffDefOf.PsychicHangover;
            if (HediffDefOf.PsychicShock != null) yield return HediffDefOf.PsychicShock;
            if (HediffDefOf.DarkPsychicShock != null) yield return HediffDefOf.DarkPsychicShock;
            if (RitualDefOf.Avir_RitualBacklash != null) yield return RitualDefOf.Avir_RitualBacklash;
            HediffDef drained = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_FlowPressure");
            if (drained != null) yield return drained;
        }

        /// <summary>Purging takes psychic states off your people in reach: hangover, shock, the flow's backlash, neural heat.</summary>
        private bool PurgeMinds(RitualReading r)
        {
            foreach (Pawn q in PawnsInReach(r))
            {
                if (q.Faction != parent.Faction) continue;
                foreach (HediffDef d in MindStates())
                {
                    Hediff h = q.health.hediffSet.GetFirstHediffOfDef(d);
                    if (h == null) continue;
                    if (bank < PurgeCost) return false;
                    bank -= PurgeCost; spentTotal += PurgeCost;
                    q.health.RemoveHediff(h);
                    FleckMaker.ThrowLightningGlow(q.DrawPos, parent.Map, 0.8f);
                }
                if (q.psychicEntropy != null && q.psychicEntropy.EntropyValue > 0.5f)
                {
                    if (bank < PurgeCost) return false;
                    bank -= PurgeCost; spentTotal += PurgeCost;
                    q.psychicEntropy.RemoveAllEntropy();
                }
            }
            return true;
        }

        private IEnumerable<Pawn> PawnsInReach(RitualReading r)
        {
            float reach = Reach(r);
            IntVec3 at = Center;
            foreach (Pawn q in parent.Map.mapPawns.AllPawnsSpawned.ToList())
                if (!q.Dead && (q.Position - at).LengthHorizontal <= reach) yield return q;
        }

        // ---------------------------------------------------------------- component circles in the world

        /// <summary>The cell of component circle k: around the main circle, just outside it, each on its own cell.</summary>
        public IntVec3 ComponentCell(int k, RitualSchema s = null)
        {
            s = s ?? drawn ?? schema;
            int n = Mathf.Max(1, s?.components.Count ?? 1);
            float R = Radius * 0.96f * 1.22f + 0.4f;
            float a = -(-90f + 360f * k / n) * Mathf.Deg2Rad;
            IntVec3 c = parent.Position + new IntVec3(Mathf.RoundToInt(Mathf.Cos(a) * R), 0, Mathf.RoundToInt(Mathf.Sin(a) * R));
            // never inside the main circle, never on another component's cell
            for (int guard = 0; guard < 4 && (Inside(c) || Enumerable.Range(0, k).Any(j => ComponentCellRaw(j, s, n) == c)); guard++)
                c += new IntVec3(Mathf.RoundToInt(Mathf.Cos(a)), 0, Mathf.RoundToInt(Mathf.Sin(a)));
            return c;
        }

        private IntVec3 ComponentCellRaw(int k, RitualSchema s, int n)
        {
            float R = Radius * 0.96f * 1.22f + 0.4f;
            float a = -(-90f + 360f * k / n) * Mathf.Deg2Rad;
            return parent.Position + new IntVec3(Mathf.RoundToInt(Mathf.Cos(a) * R), 0, Mathf.RoundToInt(Mathf.Sin(a) * R));
        }

        /// <summary>The main circle's cells and the cells of the component circles of this kind.</summary>
        public IEnumerable<IntVec3> FuelCells(ComponentKind kind)
        {
            foreach (IntVec3 c in Cells) yield return c;
            RitualSchema s = drawn;
            if (s == null) yield break;
            for (int k = 0; k < s.components.Count; k++)
                if (s.components[k].kind == kind)
                {
                    IntVec3 c = ComponentCell(k);
                    if (c.InBounds(parent.Map)) yield return c;
                }
        }

        /// <summary>What lies on component circle k and belongs there (null - nothing).</summary>
        public Thing Holding(int k)
        {
            RitualSchema s = drawn;
            if (s == null || k >= s.components.Count || !parent.Spawned) return null;
            RitualComponent comp = s.components[k];
            IntVec3 c = ComponentCell(k);
            if (!c.InBounds(parent.Map)) return null;
            ThingDef want = comp.Expected;
            foreach (Thing t in c.GetThingList(parent.Map))
            {
                if (comp.kind == ComponentKind.Artifact ? t.TryGetComp<CompArtifact>()?.state != null : want != null && t.def == want) return t;
            }
            return null;
        }

        /// <summary>Crystal batteries laid in their component circles give their charge when crystals and feathers are gone.</summary>
        private void DrawBatteries()
        {
            RitualSchema s = drawn;
            if (s == null) return;
            for (int k = 0; k < s.components.Count; k++)
            {
                if (s.components[k].kind != ComponentKind.Battery) continue;
                Thing b = Holding(k);
                if (b == null) continue;
                float got = TakeFrom(b, CrystalEnergy);
                if (got <= 0f) continue;
                bank += got;
                FleckMaker.ThrowLightningGlow(b.DrawPos, parent.Map, 0.8f);
                return;
            }
            // last: the energy keepers of the stream network the circle stands on
            StreamNet net = Net;
            if (net == null) return;
            foreach (CompEnergyKeeper k in net.Keepers)
            {
                float got = k.Take(CrystalEnergy);
                if (got <= 0f) continue;
                bank += got;
                return;
            }
        }

        /// <summary>Artifacts laid in their component circles: each adds 15% to the ritual's strength, +5% for every step
        /// of its heart's quality, while it has charge - the ritual drinks it slowly.</summary>
        public float ArtifactBoost(int delta)
        {
            RitualSchema s = drawn;
            if (s == null) return 0f;
            float boost = 0f;
            for (int k = 0; k < s.components.Count; k++)
            {
                if (s.components[k].kind != ComponentKind.Artifact) continue;
                ArtifactState st = Holding(k)?.TryGetComp<CompArtifact>()?.state;
                if (st == null || st.charge <= 0.01f) continue;
                boost += 0.15f + 0.05f * (int)st.heartQuality;
                if (delta > 0) st.charge = Mathf.Max(0f, st.charge - 0.00002f * delta);
            }
            return boost;
        }

        // ---------------------------------------------------------------- dev mode: quick checks

        /// <summary>Dev mode: rituals never break.</summary>
        public static bool DevNoBacklash;

        private IEnumerable<Gizmo> DevGizmos()
        {
            Map map = parent.Map;
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(map);
            yield return new Command_Action
            {
                defaultLabel = "Dev: stream " + (ext?.devFlow >= 0f ? "(held " + ext.devFlow.ToString("0") + ")" : "(live)"),
                action = () =>
                {
                    var o = new List<FloatMenuOption> { new FloatMenuOption("live (release)", () => ext.devFlow = -1f) };
                    foreach (float f in new[] { 10000f, 35000f, 70000f, 150000f, 300000f, 600000f }.Select(v => v * MapComponent_ExternalFlow.Scale))
                    {
                        float ff = f;
                        o.Add(new FloatMenuOption(f.ToString("0") + "% - " + MapComponent_ExternalFlow.Tier(f), () => ext.devFlow = ff));
                    }
                    Find.WindowStack.Add(new FloatMenu(o));
                }
            };
            yield return new Command_Action { defaultLabel = "Dev: stream weather hour", action = () => FlowWeather.Hour(map, ext) };
            yield return new Command_Toggle { defaultLabel = "Dev: no backlash", isActive = () => DevNoBacklash, toggleAction = () => DevNoBacklash = !DevNoBacklash };
            if (Running)
            {
                yield return new Command_Action { defaultLabel = "Dev: +1000 energy", action = () => bank += 1000f };
                yield return new Command_Action { defaultLabel = "Dev: skip the gathering", action = () => stepTicks = Mathf.Max(stepTicks, ReleaseGather) };
            }
            if (Drawn && vortex <= 0f) yield return new Command_Action { defaultLabel = "Dev: start vortex", action = () => { vortex = 1f; } };
            if (vortex > 0f) yield return new Command_Action { defaultLabel = "Dev: vortex +3", action = () => vortex = Mathf.Min(10f, vortex + 3f) };
            if (selfSustain && conduit == null && Drawn)
                yield return new Command_Action
                {
                    defaultLabel = "Dev: wake the engraving",
                    action = () => { Thing c = FindConduit(); if (c == null) { Messages.Message("No conduit at the centre.", MessageTypeDefOf.RejectInput, false); return; } Begin(null, target, null, null, 0); conduit = c; }
                };
            yield return new Command_Action
            {
                defaultLabel = "Dev: effects",
                action = () =>
                {
                    Vector3 c = parent.DrawPos;
                    var o = new List<FloatMenuOption>
                    {
                        new FloatMenuOption("barrier ripple", () => RitualFx.Ripple(c, map, 3f)),
                        new FloatMenuOption("release blast (r 5)", () => RitualFx.Blast(c, map, 5f)),
                        new FloatMenuOption("lift wave (r 5)", () => RitualFx.Wave(c, map, 5f)),
                        new FloatMenuOption("stream motes", () => { for (int i = 0; i < 20; i++) RitualFx.Mote(c + Rand.InsideUnitCircleVec3 * 3f, map, Rand.Range(0.4f, 0.9f)); }),
                        new FloatMenuOption("dome for 30 s", () =>
                        {
                            DropDome();
                            dome = new BarrierDome { map = map, center = parent.Position, radius = 7f, hit = DomeHit };
                            BarrierDome.All.Add(dome);
                            domeHp = 150f;
                            devDomeUntil = Find.TickManager.TicksGame + 1800;
                        }),
                    };
                    Find.WindowStack.Add(new FloatMenu(o));
                }
            };
        }

        private int devDomeUntil = -1;

        // ---------------------------------------------------------------- self-sustaining engravings

        /// <summary>A thing of energy at the centre of the circle: an artifact, a crystal battery, an artifact building, a psychic pylon.</summary>
        public Thing FindConduit() => chosenThing != null && ConduitCandidates().Contains(chosenThing) ? chosenThing : ConduitCandidates().FirstOrDefault();

        /// <summary>Every thing of energy at the centre of the circle that could stand as its conduit.</summary>
        public IEnumerable<Thing> ConduitCandidates()
        {
            Map map = parent.Map;
            if (map == null) yield break;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(parent.Position, Mathf.Max(0.9f, Radius * 0.35f), true))
            {
                if (!c.InBounds(map)) continue;
                foreach (Thing t in c.GetThingList(map))
                    if (t != parent && !(t is Pawn) && (t.TryGetComp<CompArtifact>()?.state != null || t.TryGetComp<CompCrystalBattery>() != null
                                                        || t.TryGetComp<CompArtifactHost>() != null || t.TryGetComp<CompPsychicPylon>() != null))
                        yield return t;
            }
        }

        /// <summary>Why the ritual cannot begin for want of what it works on (null - everything lies where it should).
        /// A transmutation needs its material in the circle: with material circles, each one's amount (the cell and the main
        /// circle together); without any, a metal or jade lying in the main circle. It never begins - and never ends at once,
        /// with nothing to change - before that.</summary>
        public string MissingMaterial()
        {
            RitualSchema s = drawn;
            if (s == null || !parent.Spawned) return null;
            RitualReading r = Reading;
            if (!r.Has(RuneKind.Transmute) || r.Seal == null) return null;
            Map map = parent.Map;
            ThingDef target = r.Seal;
            List<Thing> lying = FuelCells(ComponentKind.Material).Distinct()
                .SelectMany(c => c.GetThingList(map)).Where(t => t.def.category == ThingCategory.Item).ToList();
            List<RitualComponent> mats = s.components
                .Where(c => c.kind == ComponentKind.Material && c.thing != null && c.thing != target && Transmutation.Allowed(c.thing)).ToList();
            if (mats.Count == 0)
            {
                bool any = lying.Any(t => t.def != target && Transmutation.Allowed(t.def) && Transmutation.Materials.Contains(t.def));
                return any ? null : "nothing to transmute lies in the circle - lay a metal or jade in it, or add a material circle";
            }
            // the same material may be named by several circles (a stack each): their amounts add up
            foreach (var g in mats.GroupBy(m => m.thing))
            {
                int need = g.Sum(m => Mathf.Max(1, m.amount)), got = lying.Where(t => t.def == g.Key).Sum(t => t.stackCount);
                if (got < need) return g.Key.label + " " + got + "/" + need + " lies in the circle - the haulers bring it to the material circles";
            }
            return null;
        }

        /// <summary>The conduit chosen in the ritual book: a colonist, or a thing of energy at the centre.</summary>
        public Pawn chosenPerformer;
        public Thing chosenThing;

        /// <summary>Begins with whatever the book chose; null - begun, else why not.</summary>
        public string BeginChosen()
        {
            if (chosenThing != null)
            {
                string why = CannotPerform(null) ?? CannotSustain(false);
                if (why != null) return why;
                if (!ConduitCandidates().Contains(chosenThing)) return "the chosen conduit is no longer at the centre";
                Begin(null, target, null, null, 0);
                conduit = chosenThing;
                return null;
            }
            if (chosenPerformer != null)
            {
                string why = CannotPerform(chosenPerformer);
                if (why != null) return why;
                if (chosenPerformer.Downed || chosenPerformer.Dead || !chosenPerformer.Spawned || chosenPerformer.Map != parent.Map) return chosenPerformer.LabelShort + " cannot come";
                Prepare(chosenPerformer);
                return null;
            }
            return "choose a conduit first";
        }

        /// <summary>Why this drawing cannot run through a thing as its conduit (null - it can). selfRunning: by itself,
        /// with nobody to start it - only an engraving.</summary>
        public string CannotSustain(bool selfRunning = true)
        {
            if (!Drawn) return "nothing drawn";
            if (selfRunning && !engraved) return "only an engraving sustains itself - chalk needs someone to begin it";
            RitualReading r = Reading;
            if (r.trap) return "a Trap holds by itself already";
            if (r.RawFlow || r.Has(RuneKind.Release) || r.Has(RuneKind.Reforge) || r.Has(RuneKind.Transfer) || r.Has(RuneKind.Blood) || r.Has(RuneKind.Shroud))
                return "raw stream, release, reforging, transfer and awakening need a living performer";
            if (r.verbs.Count == 0 && !r.Dome) return "no verb";
            return null;
        }

        /// <summary>Would the ritual have work now? (Checked before it wakes by itself.)</summary>
        private bool Needed(RitualReading r)
        {
            Map map = parent.Map;
            if (r.Dome) return GenHostility.AnyHostileActiveThreatToPlayer(map);
            float reach = Reach(r);
            IntVec3 at = Center;
            IEnumerable<Thing> near = GenRadial.RadialDistinctThingsAround(at, map, reach, true).Concat(Threaded);
            foreach (RuneDef v in r.verbs)
                switch (v.kind)
                {
                    case RuneKind.Fill:
                        if (ChargeTargets(r).Any(st => st.charge < 0.5f)) return true;
                        if (near.Any(t => t.TryGetComp<CompCrystalBattery>() is CompCrystalBattery b && b.Stored < CompCrystalBattery.Capacity * 0.5f)) return true;
                        break;
                    case RuneKind.Hold:
                    case RuneKind.Drain:
                        if (PawnsInReach(r).Any(q => q.HostileTo(Faction.OfPlayer) && !q.Downed)) return true;
                        if (v.kind == RuneKind.Drain && CompPsychicPylon.All.Any(py => py.parent.Map == map && py.charge > 50f && (py.parent.Position - at).LengthHorizontal <= reach)) return true;
                        break;
                    case RuneKind.Growth:
                        if (GenRadial.RadialCellsAround(at, reach, true).Any(c => c.InBounds(map) && c.GetPlant(map) is Plant pl && pl.LifeStage == PlantLifeStage.Growing)) return true;
                        break;
                    case RuneKind.Lift:
                        if (near.Any(t => t.def.category == ThingCategory.Item && t.def.EverHaulable)) return true;
                        break;
                    case RuneKind.Transmute:
                        return true;
                    case RuneKind.Purge:
                        if (PawnsInReach(r).Any(q => q.Faction == parent.Faction && MindStates().Any(d => q.health.hediffSet.HasHediff(d)))) return true;
                        break;
                }
            return false;
        }

        private void SelfSustainTick()
        {
            if (devDomeUntil > 0 && Find.TickManager.TicksGame > devDomeUntil && !Running) { DropDome(); devDomeUntil = -1; }
            if (performer != null) return;
            if (conduit != null)
            {
                if (CannotSustain(false) != null) { End(null); return; }
                if (!conduit.Spawned || conduit.Map != parent.Map) { End("its conduit is gone."); return; }
                if (!Step(250)) conduit = null;
                // the dome rests once no threat is left
                if (conduit != null && Reading.Dome && !GenHostility.AnyHostileActiveThreatToPlayer(parent.Map) && runTicks > 2500) End(null);
                return;
            }
            if (!selfSustain || CannotSustain() != null) return;
            if (!parent.IsHashIntervalTick(1250)) return;
            if (MissingMaterial() != null) return;   // it does not wake for nothing
            RitualReading r = Reading;
            if (!Needed(r)) return;
            Thing c = FindConduit();
            if (c == null) return;
            Begin(null, target, null, null, 0);
            conduit = c;
        }

        // ---------------------------------------------------------------- Fill + Focus: experience out of the flow

        /// <summary>Experience each vessel snatched in this ritual.</summary>
        private readonly Dictionary<object, float> grabbed = new Dictionary<object, float>();

        /// <summary>Experience a vessel can snatch in one ritual: grows with focus and the map's flow.</summary>
        public static float GrabCap(RitualReading r, float flowRatio) => 400f * r.focus * Mathf.Max(0.25f, flowRatio);

        /// <summary>Filling with focus: every artifact and amulet in reach (worn or loose) snatches experience
        /// out of the external flow - ten experience for a unit of energy; it pulls the flow up as it goes.</summary>
        private bool GrabXp(RitualReading r)
        {
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(parent.Map);
            float ratio = ext == null ? 0f : ext.Total / MapComponent_ExternalFlow.Calm;
            float cap = GrabCap(r, ratio);
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(Center, parent.Map, Reach(r), true).Concat(Threaded).ToList())
                foreach (object v in Vessels(t))
                {
                    grabbed.TryGetValue(v, out float got);
                    if (got >= cap - 0.5f) continue;
                    float want = Mathf.Min(cap - got, 50f);
                    float cost = want / 10f;
                    if (bank < cost) return false;
                    bank -= cost; spentTotal += cost;
                    SetXp(v, Xp(v) + want);
                    grabbed[v] = got + want;
                    if (ext != null) ext.deeds += want * 5f;
                    if (Rand.Chance(0.2f)) FleckMaker.ThrowLightningGlow(t.DrawPos, parent.Map, 0.6f);
                    return false;   // a handful at a time
                }
            return true;
        }

        // ---------------------------------------------------------------- Drain: the living and the threaded

        /// <summary>Foes and strangers in reach tire as their strength is drawn out; at the end they collapse.</summary>
        private bool DrainLiving(RitualReading r, int delta)
        {
            bool any = false;
            foreach (Pawn q in PawnsInReach(r))
            {
                if (q.Faction == parent.Faction || q.Downed || !q.RaceProps.IsFlesh || held.Contains(q)) continue;
                Need_Rest rest = q.needs?.rest;
                if (rest == null || rest.CurLevel <= 0.01f) continue;
                if (PsychicShelter.Shielded(q, true)) continue;
                any = true;
                float take = 0.0006f * delta * Mathf.Max(0.2f, r.Power(drawQuality));
                rest.CurLevel = Mathf.Max(0f, rest.CurLevel - take);
                Gain(r, take * 120f * Mathf.Max(0.2f, q.GetStatValue(StatDefOf.PsychicSensitivity)));
                if (rest.CurLevel <= 0.01f) Collapse(q, q.LabelShortCap + " collapses: the ritual drew all its strength out.");
                if (q.IsHashIntervalTick(90, delta)) FleckMaker.ThrowLightningGlow(q.DrawPos, parent.Map, 0.5f);
            }
            return any;
        }

        public static void Collapse(Pawn q, string msg)
        {
            HediffDef d = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_StrengthDrained");
            if (d != null && !q.health.hediffSet.HasHediff(d)) q.health.AddHediff(d);
            if (msg != null) Messages.Message(msg, q, MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>Threaded batteries, artifact buildings and electric batteries give their charge.</summary>
        private bool DrainThreads(RitualReading r, int delta)
        {
            bool any = false;
            float per = 0.15f * delta;
            foreach (Thing t in Threaded)
            {
                float got = TakeFrom(t, per);
                if (got <= 0f) continue;
                any = true;
                Gain(r, got);
            }
            return any;
        }

        /// <summary>Energy taken from a thing (ritual units); 0 when it has none.</summary>
        public static float TakeFrom(Thing t, float want)
        {
            CompEnergyKeeper k = t.TryGetComp<CompEnergyKeeper>();
            if (k != null) return k.Take(want);
            CompCrystalBattery b = t.TryGetComp<CompCrystalBattery>();
            if (b != null && b.Stored > 0.01f)
            {
                float g = Mathf.Min(want, b.Stored);
                b.stored = b.Stored - g;
                return g;
            }
            CompArtifactHost h = t.TryGetComp<CompArtifactHost>();
            if (h != null)
                foreach (ArtifactState st in h.slots)
                {
                    if (st == null || st.charge <= 0.01f) continue;
                    float cap = Mathf.Max(1f, ArtifactCharge.Capacity(st, null));
                    float g = Mathf.Min(want, st.charge * cap);
                    st.charge -= g / cap;
                    return g;
                }
            CompPowerBattery pb = t.TryGetComp<CompPowerBattery>();
            if (pb != null && pb.StoredEnergy > 1f)
            {
                float wd = Mathf.Min(want * WdPerUnit, pb.StoredEnergy);
                pb.DrawPower(wd);
                return wd / WdPerUnit;
            }
            CompPsychicPylon py = t.TryGetComp<CompPsychicPylon>();
            if (py != null && py.charge > 1f) return py.Take(want);
            ArtifactState art = t.TryGetComp<CompArtifact>()?.state;
            if (art != null && art.charge > 0.01f)
            {
                float cap = Mathf.Max(1f, ArtifactCharge.Capacity(art, null));
                float g = Mathf.Min(want, art.charge * cap);
                art.charge -= g / cap;
                return g;
            }
            return 0f;
        }

        /// <summary>Energy given to a thing (ritual units); returns what it took.</summary>
        public static float GiveTo(Thing t, float amount)
        {
            // a cracked core hearth drinks the stream into its cracks until they close
            Avir.Spire.CompCoreHearth hearth = t.TryGetComp<Avir.Spire.CompCoreHearth>();
            if (hearth != null && hearth.cracked) return hearth.Mend(amount);
            CompEnergyKeeper k = t.TryGetComp<CompEnergyKeeper>();
            if (k != null) return k.Give(amount);
            CompCrystalBattery b = t.TryGetComp<CompCrystalBattery>();
            if (b != null && !b.IsFull)
            {
                float g = Mathf.Min(amount, CompCrystalBattery.Capacity - b.Stored);
                b.stored = b.Stored + g;
                return g;
            }
            CompArtifactHost h = t.TryGetComp<CompArtifactHost>();
            if (h != null)
                foreach (ArtifactState st in h.slots)
                {
                    if (st == null || st.charge >= 0.999f) continue;
                    float cap = Mathf.Max(1f, ArtifactCharge.Capacity(st, null));
                    float g = Mathf.Min(amount, (1f - st.charge) * cap);
                    st.charge += g / cap;
                    return g;
                }
            CompPowerBattery pb = t.TryGetComp<CompPowerBattery>();
            if (pb != null && pb.AmountCanAccept > 0.5f)
            {
                float wd = Mathf.Min(amount * WdPerUnit, pb.AmountCanAccept);
                pb.AddEnergy(wd);
                return wd / WdPerUnit;
            }
            return 0f;
        }

        // ---------------------------------------------------------------- Transmute into current

        /// <summary>One unit of ritual energy is a hundred watts for an hour.</summary>
        public const float WdPerUnit = 100f / 24f;

        /// <summary>Electric batteries on the threaded grids and, with breadth, in reach are charged.</summary>
        private bool DoCurrent(RitualReading r, int delta)
        {
            var batteries = new HashSet<CompPowerBattery>();
            foreach (Thing t in Threaded)
            {
                PowerNet net = t.TryGetComp<CompPower>()?.PowerNet;
                if (net != null) foreach (CompPowerBattery pb in net.batteryComps) batteries.Add(pb);
            }
            if (r.breadth > 0)
                foreach (Thing t in GenRadial.RadialDistinctThingsAround(Center, parent.Map, Reach(r), true))
                    if (t.TryGetComp<CompPowerBattery>() is CompPowerBattery pb) batteries.Add(pb);
            var open = batteries.Where(b => b.AmountCanAccept > 0.5f).ToList();
            if (open.Count == 0) return r.anchor == 0;
            float give = Mathf.Min(bank, 0.1f * delta * r.Power(drawQuality));
            if (give <= 0.0001f) return false;
            float each = give / open.Count;
            foreach (CompPowerBattery pb in open)
            {
                float wd = Mathf.Min(each * WdPerUnit, pb.AmountCanAccept);
                pb.AddEnergy(wd);
                bank -= wd / WdPerUnit; spentTotal += wd / WdPerUnit;
            }
            if (parent.IsHashIntervalTick(120, delta)) FleckMaker.ThrowLightningGlow(open.RandomElement().parent.DrawPos, parent.Map, 0.6f);
            return false;
        }

        // ---------------------------------------------------------------- Release

        /// <summary>Ticks a release gathers before it goes off.</summary>
        private const int ReleaseGather = 900;

        private bool DoRelease(RitualReading r)
        {
            if (stepTicks < ReleaseGather) return false;
            Map map = parent.Map;
            float e = bank;
            bank = 0f; spentTotal += e;
            IntVec3 at = Center;
            if (e < 20f)
            {
                FleckMaker.ThrowLightningGlow(at.ToVector3Shifted(), map, 1f);
                End("the release fizzled - too little energy.");
                return true;
            }
            // the stream pours into any release: the stormier the map, the wider and harder it goes off
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(map);
            float ratio = ext == null ? 0f : ext.Total / MapComponent_ExternalFlow.Calm;
            float surge = 1f + 0.15f * ratio;
            float radius = Mathf.Clamp(1.9f + Mathf.Sqrt(e * surge) / 4f, 1.9f, 4.9f + 2f * (int)Size + 2f * r.breadth + Mathf.Min(14f, ratio * 0.6f));
            int dmg = Mathf.Clamp(Mathf.RoundToInt(12f + e * surge / 10f), 12, Mathf.RoundToInt(150f + 450f * Mathf.Clamp01(ratio / 12f)));
            bool inHand = at == parent.Position;
            Pawn p = performer;
            if (inHand && r.barrier == 0 && r.safety > 0 && p != null)
            {
                // the safety rune throws the performer clear before it goes off
                IntVec3 out1 = CellFinder.RandomClosewalkCellNear(parent.Position, map, Mathf.CeilToInt(radius) + 3, c => (c - parent.Position).LengthHorizontal > radius);
                p.Position = out1;
                p.Notify_Teleported(true, false);
            }
            List<Thing> ignored = r.barrier > 0 ? Spared(map, null) : new List<Thing> { parent };
            if (r.SelfDetonation)
            {
                radius *= 1.3f; dmg = Mathf.RoundToInt(dmg * 1.5f);
                ignored = null;
                RitualPresetDef self = DefDatabase<RitualPresetDef>.GetNamedSilentFail("Avir_Ritual_SelfDetonation");
                if (self != null) RitualDiscoveries.Learn(self);
            }
            if (r.Throw) { RitualFx.Wave(at.ToVector3Shifted(), map, radius); Throw(at, radius, e, ignored); }
            else { RitualFx.Blast(at.ToVector3Shifted(), map, radius); GenExplosion.DoExplosion(at, map, radius, DamageDefOf.Bomb, null, dmg, ignoredThings: ignored); }
            if (r.SelfDetonation)
            {
                drawn = null; drawQuality = 0f;
                End("the release went off in " + (p?.LabelShort ?? "its conduit") + "'s hands.");
                return true;
            }
            if (r.anchor == 0 && verbIndex >= r.verbs.Count - 1) End("released.");
            return true;
        }

        /// <summary>Lift with Release: a wave that throws everyone back from the point and stuns them.</summary>
        private void Throw(IntVec3 at, float radius, float e, List<Thing> ignored)
        {
            Map map = parent.Map;
            float push = Mathf.Clamp(e / 60f, 2f, 8f);
            FleckMaker.ThrowLightningGlow(at.ToVector3Shifted(), map, radius);
            foreach (Pawn q in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (q.Dead || (ignored != null && ignored.Contains(q))) continue;
                float d = (q.Position - at).LengthHorizontal;
                if (d > radius) continue;
                Vector3 dir = (q.Position - at).ToVector3();
                if (dir.sqrMagnitude < 0.01f) dir = Rand.InsideUnitCircleVec3;
                dir.Normalize();
                int steps = Mathf.RoundToInt(push * (1f - 0.5f * d / radius));
                IntVec3 to = q.Position;
                for (int k = 1; k <= steps; k++)
                {
                    IntVec3 c = (q.Position.ToVector3Shifted() + dir * k).ToIntVec3();
                    if (!c.InBounds(map) || !c.Standable(map)) break;
                    to = c;
                }
                if (to != q.Position)
                {
                    q.Position = to;
                    q.Notify_Teleported(false, false);
                }
                q.stances?.stunner?.StunFor(150, null, false, true);
                q.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Mathf.Clamp(e / 40f, 2f, 25f)));
                FleckMaker.ThrowDustPuff(q.Position, map, 1.2f);
            }
        }

        // ---------------------------------------------------------------- Lift

        private bool DoLift(RitualReading r, int delta)
        {
            if (stepTicks % 60 >= delta && stepTicks > delta) return false;
            Map map = parent.Map;
            float reach = Reach(r);
            IntVec3 at = Center;
            float power = Mathf.Max(0.3f, r.Power(drawQuality));
            bool did = false;
            // snow and filth swept away
            int sweep = Mathf.RoundToInt(6 * power);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, reach, true))
            {
                if (sweep <= 0) break;
                if (!c.InBounds(map)) continue;
                if (map.snowGrid.GetDepth(c) > 0.01f)
                {
                    if (bank < 0.5f) return false;
                    map.snowGrid.SetDepth(c, 0f);
                    bank -= 0.5f; spentTotal += 0.5f; sweep--; did = true;
                }
                Thing filth = c.GetThingList(map).FirstOrDefault(t => t is Filth);
                if (filth != null)
                {
                    if (bank < 0.5f) return false;
                    filth.Destroy();
                    bank -= 0.5f; spentTotal += 0.5f; sweep--; did = true;
                }
            }
            // loose things to the best storage for them
            float budget = Mathf.Min(15f * power, bank / 0.1f);
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(at, map, reach, true).ToList())
            {
                if (budget <= 0.5f) break;
                if (t.def.category != ThingCategory.Item || !t.def.EverHaulable || !t.Spawned || t.IsForbidden(Faction.OfPlayer)
                    || map.reservationManager.IsReservedByAnyoneOf(t, Faction.OfPlayer) || map.physicalInteractionReservationManager.IsReserved(t)
                    || RitualHold.Held(t)) continue;
                StoragePriority cur = StoreUtility.CurrentStoragePriorityOf(t);
                if (!StoreUtility.TryFindBestBetterStoreCellFor(t, null, map, cur, Faction.OfPlayer, out IntVec3 dest, true)) continue;
                float mass = Mathf.Max(0.001f, t.GetStatValue(StatDefOf.Mass));
                int n = Mathf.Min(t.stackCount, Mathf.FloorToInt(budget / mass));
                if (n <= 0) continue;
                IntVec3 from = t.Position;
                Thing moving = n >= t.stackCount ? t : t.SplitOff(n);
                if (moving == t) t.DeSpawn();
                if (!GenPlace.TryPlaceThing(moving, dest, map, ThingPlaceMode.Direct) && !GenPlace.TryPlaceThing(moving, dest, map, ThingPlaceMode.Near))
                {
                    GenPlace.TryPlaceThing(moving, from, map, ThingPlaceMode.Near);
                    continue;
                }
                budget -= n * mass;
                bank -= n * mass * 0.1f; spentTotal += n * mass * 0.1f;
                did = true;
                FleckMaker.ThrowDustPuff(from, map, 0.8f);
                FleckMaker.ThrowLightningGlow(dest.ToVector3Shifted(), map, 0.5f);
            }
            return !did && r.anchor == 0;
        }

        // ---------------------------------------------------------------- Growth

        private int lastSoilTick = -99999;

        private bool DoGrowth(RitualReading r, int delta)
        {
            if (stepTicks % 120 >= delta && stepTicks > delta) return false;
            Map map = parent.Map;
            float reach = Reach(r);
            IntVec3 at = Center;
            float power = Mathf.Max(0.3f, r.Power(drawQuality));
            bool did = false;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, reach, true))
            {
                if (!c.InBounds(map)) continue;
                Plant plant = c.GetPlant(map);
                if (plant == null || plant.LifeStage != PlantLifeStage.Growing || plant.Growth >= 0.999f) continue;
                if (bank < 0.2f) return false;
                plant.Growth = Mathf.Min(1f, plant.Growth + 0.02f * power);
                bank -= 0.2f; spentTotal += 0.2f; did = true;
                if (Rand.Chance(0.05f)) FleckMaker.ThrowLightningGlow(c.ToVector3Shifted(), map, 0.4f);
            }
            foreach (Pawn q in PawnsInReach(r))
            {
                if (q.Faction != parent.Faction && q.HostFaction != parent.Faction) continue;
                foreach (Hediff_Injury inj in q.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(h => !h.IsPermanent()).ToList())
                {
                    if (bank < 0.5f) return false;
                    inj.Heal(0.6f * power);
                    bank -= 0.5f; spentTotal += 0.5f; did = true;
                }
            }
            // anchored: the soil itself grows richer, a cell at a time
            if (r.anchor > 0 && Find.TickManager.TicksGame - lastSoilTick > 2500 && bank >= 30f)
            {
                IntVec3 soil = GenRadial.RadialCellsAround(at, reach, true).Where(c => c.InBounds(map) && c.GetTerrain(map) == TerrainDefOf.Soil).RandomElementWithFallback(IntVec3.Invalid);
                if (soil.IsValid)
                {
                    map.terrainGrid.SetTerrain(soil, TerrainDefOf.SoilRich);
                    bank -= 30f; spentTotal += 30f; did = true;
                    lastSoilTick = Find.TickManager.TicksGame;
                }
            }
            return !did && r.anchor == 0;
        }

        // ---------------------------------------------------------------- Reforge

        /// <summary>The artifact laid in the circle.</summary>
        public Thing ReforgeTarget =>
            reforgeThing != null && ReforgeCandidates().Contains(reforgeThing) ? reforgeThing : ReforgeCandidates().FirstOrDefault(t => t != chosenThing);

        /// <summary>The artifact the player marked for reforging (null - the first one lying in the circle that is not the conduit).</summary>
        public Thing reforgeThing;

        public IEnumerable<Thing> ReforgeCandidates() =>
            parent.Map == null ? Enumerable.Empty<Thing>() : Cells.SelectMany(c => c.GetThingList(parent.Map))
                .Where(t => t.TryGetComp<CompArtifact>()?.state != null || CompStriderBow.Of(t)?.bonded != null);

        /// <summary>Energy it takes to break a bonded weapon's bond.</summary>
        public const float UnbondCost = 1000f;

        /// <summary>Marks a thing on the map: what holds the ritual, or what is reforged.</summary>
        private void PickOnMap(System.Func<Thing, bool> can, System.Action<Thing> set, string ask)
        {
            Messages.Message(ask, MessageTypeDefOf.SilentInput, false);
            var tp = new TargetingParameters { canTargetBuildings = true, canTargetItems = true, canTargetPawns = false, canTargetLocations = false,
                                               mapObjectTargetsMustBeAutoAttackable = false, validator = ti => ti.Thing != null && can(ti.Thing) };
            Find.Targeter.BeginTargeting(tp, ti => { if (ti.Thing != null && can(ti.Thing)) { set(ti.Thing); SoundDefOf.Click.PlayOneShotOnCamera(); } });
        }

        private IEnumerable<Gizmo> ChoiceGizmos()
        {
            if (!Drawn || Running) yield break;
            if (ConduitCandidates().Any())
                yield return new Command_Action
                {
                    defaultLabel = chosenThing != null ? "Conduit: " + chosenThing.LabelShortCap : "Choose conduit...",
                    defaultDesc = "Click the artifact, crystal battery, artifact building or pylon at the centre that will hold the ritual. "
                                  + "Clicking this again while one is chosen clears the choice. A colonist chosen in the ritual book clears it too.",
                    icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Focus")?.Glow ?? BaseContent.BadTex,
                    action = () =>
                    {
                        if (chosenThing != null) { chosenThing = null; return; }
                        PickOnMap(t => ConduitCandidates().Contains(t), t => { chosenThing = t; chosenPerformer = null; if (reforgeThing == t) reforgeThing = null; },
                                  "Click the thing at the centre that will hold the ritual.");
                    }
                };
            if ((Reading.Has(RuneKind.Reforge) || Reading.Has(RuneKind.Infuse)) && ReforgeCandidates().Any())
                yield return new Command_Action
                {
                    defaultLabel = reforgeThing != null ? "Artifact: " + reforgeThing.LabelShortCap : "Choose the artifact...",
                    defaultDesc = "Click the artifact in the circle that the ritual will reforge or infuse. Without a choice it takes the first artifact in the circle that is not the conduit. "
                                  + "Clicking this again while one is chosen clears the choice.",
                    icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Reforge")?.Ink ?? BaseContent.BadTex,
                    action = () =>
                    {
                        if (reforgeThing != null) { reforgeThing = null; return; }
                        PickOnMap(t => ReforgeCandidates().Contains(t) && t != chosenThing, t => reforgeThing = t, "Click the artifact to reforge or infuse.");
                    }
                };
        }

        // ---------------------------------------------------------------- Infuse

        /// <summary>Experience given to the infused artifact by this run (the cap counts from it).</summary>
        private float infusedXp;
        private int infusedPointsAtStart = -1;

        /// <summary>Energy into the laid artifact's experience, about one for one, a little at a time; done when the
        /// artifact has gained its point (two in a large circle) or nothing is laid there.</summary>
        private bool DoInfuse(RitualReading r, int delta)
        {
            Thing t = ReforgeTarget;
            ArtifactState st = t?.TryGetComp<CompArtifact>()?.state;
            if (st == null || !st.HasHeart) { End("nothing to infuse: lay an artifact with a heart in the circle."); return true; }
            if (infusedPointsAtStart < 0) infusedPointsAtStart = st.PointsEarned;
            int cap = Size == RitualCircleSize.Large ? 2 : 1;
            if (st.PointsEarned - infusedPointsAtStart >= cap) { Finish(t); return true; }
            if (stepTicks % 60 >= delta && stepTicks > delta) return false;
            float take = Mathf.Min(bank, 20f);
            if (take < 1f) return false;   // waits for energy
            bank -= take; spentTotal += take;
            float xp = take * Mathf.Max(0.3f, r.Power(drawQuality));
            st.AddXp(xp);
            infusedXp += xp;
            if (Rand.Chance(0.2f)) FleckMaker.ThrowLightningGlow(t.DrawPos, parent.Map, 0.6f);
            if (st.PointsEarned - infusedPointsAtStart >= cap) { Finish(t); return true; }
            return false;
        }

        private void Finish(Thing t)
        {
            Messages.Message(t.LabelShortCap + " is infused: +" + infusedXp.ToString("0") + " experience.", t, MessageTypeDefOf.PositiveEvent, false);
            infusedXp = 0f; infusedPointsAtStart = -1;
            ArtifactBonusCache.ClearAll();
        }

        public static float ReforgeCost(ArtifactState st, int mode)
        {
            switch (mode)
            {
                case 0: return 500f * ((int)st.heartQuality + 1);
                case 1: return 1500f;
                default: return 1000f;
            }
        }

        /// <summary>Experience a reforged spiral scoops out of the map's flow: more with focus and amplification, more the stormier the map.</summary>
        public float ReforgeScoop(RitualReading r)
        {
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(parent.Map);
            float ratio = ext == null ? 0f : ext.Total / MapComponent_ExternalFlow.Calm;
            return 1500f * (1f + r.focus + 0.5f * r.amplify) * Mathf.Max(0.25f, ratio) * Mathf.Max(0.3f, drawQuality);
        }

        public static ArtifactMetalDef MetalOf(ThingDef seal) =>
            seal == null ? null : DefDatabase<ArtifactMetalDef>.AllDefsListForReading.FirstOrDefault(m => m.material == seal);

        /// <summary>Why this reforging cannot be done on this artifact (null - it can).</summary>
        public string CannotReforge(ArtifactState st, int mode, RitualReading r)
        {
            if (st == null) return "lay an artifact in the circle";
            switch (mode)
            {
                case 0: return st.heartQuality >= QualityCategory.Legendary ? "its heart is already legendary" : null;
                case 1:
                    ArtifactMetalDef m = MetalOf(r.Seal);
                    if (m == null) return "a seal of the new metal must lie in a component circle";
                    return m == st.heartMetal ? "its heart is already " + m.label : null;
                case 2: return null;
            }
            return null;
        }

        public static string ReforgeLabel(int mode, RitualReading r)
        {
            switch (mode)
            {
                case 0: return "Heart one quality step up";
                case 1: return "Heart into " + (MetalOf(r.Seal)?.label ?? "the sealed metal");
                default: return "Reforge the spiral: every cell closes, every point returns, and it scoops experience out of the stream";
            }
        }

        private bool DoReforge(RitualReading r)
        {
            Thing t = ReforgeTarget;
            // a bonded Strider bow: the reforging breaks the bond, and the bow will take a new archer
            CompStriderBow bow = CompStriderBow.Of(t);
            if (bow?.bonded != null)
            {
                if (bank < UnbondCost) return false;
                bank -= UnbondCost; spentTotal += UnbondCost;
                string was = bow.bonded.LabelShort;
                bow.Unbond();
                GenExplosion.DoExplosion(t.Position, parent.Map, 1.9f, DamageDefOf.Stun, null, 1, ignoredThings: new List<Thing> { t, parent });
                Messages.Message(t.LabelShortCap + " is reforged: its bond with " + was + " is broken, and it will take a new archer.", t, MessageTypeDefOf.PositiveEvent, false);
                End("the bond is broken.");
                return true;
            }
            ArtifactState st = t?.TryGetComp<CompArtifact>()?.state;
            string why = CannotReforge(st, reforgeMode, r);
            if (why != null) { End("cannot reforge: " + why + "."); return true; }
            float cost = ReforgeCost(st, reforgeMode);
            if (bank < cost) return false;
            bank -= cost; spentTotal += cost;
            switch (reforgeMode)
            {
                case 0: st.heartQuality = (QualityCategory)((int)st.heartQuality + 1); break;
                case 1: st.heartMetal = MetalOf(r.Seal); break;
                case 2:
                    // almost a reset - but nothing is lost, even burnt points come back, and the flow pours more in
                    st.opened.Clear();
                    st.dormantArms.Clear();
                    st.burntPoints = 0;
                    float scoop = ReforgeScoop(r);
                    st.xpEarned += scoop;
                    MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(parent.Map);
                    if (ext != null) ext.deeds += scoop * 5f;
                    break;
            }
            ArtifactBonusCache.ClearAll();
            GenExplosion.DoExplosion(t.Position, parent.Map, 1.9f, DamageDefOf.Stun, null, 1, ignoredThings: new List<Thing> { t, parent });
            Messages.Message(t.LabelShortCap + " is reforged: " + ReforgeLabel(reforgeMode, r).ToLower() + ".", t, MessageTypeDefOf.PositiveEvent, false);
            End("the artifact is reforged.");
            return true;
        }

        // ---------------------------------------------------------------- Barrier dome

        private bool DomeTick(RitualReading r, MapComponent_ExternalFlow ext, int delta)
        {
            if (runTicks >= AnchorTicks) { End("the anchor's day is over - the dome fades."); return false; }
            float radius = r.DomeRadius;
            float maxHp = 150f * Mathf.Max(0.3f, r.Power(drawQuality));
            if (dome == null)
            {
                dome = new BarrierDome { map = parent.Map, center = Center, radius = radius, hit = DomeHit };
                BarrierDome.All.Add(dome);
                domeHp = Mathf.Min(maxHp, bank);
                bank -= domeHp;
            }
            // the dome holds itself on the energy, and mends from it
            float upkeep = 0.004f * radius * delta;
            if (bank >= upkeep) { bank -= upkeep; spentTotal += upkeep; dryTicks = 0; }
            else if ((dryTicks += delta) > 600) { End("the dome ran dry and faded."); return false; }
            if (domeHp < maxHp && bank > 1f)
            {
                float mend = Mathf.Min(maxHp - domeHp, Mathf.Min(bank, 0.5f * delta));
                domeHp += mend; bank -= mend; spentTotal += mend;
            }
            // a barrier pulls the map's flow by itself
            if (ext != null) ext.deeds += 15f * radius * delta / 60f;
            return true;
        }

        private bool DomeHit(Projectile pr)
        {
            float dmg = Mathf.Max(5f, pr.DamageAmount);
            domeHp -= dmg;
            if (domeHp > 0f) return true;
            // shattered: this shot still stops, the dome is gone
            Messages.Message("The barrier dome shattered.", parent, MessageTypeDefOf.NegativeEvent, false);
            End("the dome shattered.");
            return true;
        }

        private void DropDome()
        {
            if (dome == null) return;
            BarrierDome.All.Remove(dome);
            dome = null;
        }

        // ---------------------------------------------------------------- the pulsing Trap

        public const float PulseRange = 18f;
        public const int PulseEvery = 3;   // rare ticks between pulses (about 12 seconds)

        /// <summary>
        /// The pulsing Trap turns its prisoner into a turret: while foes are in reach, every few seconds it tears a tenth
        /// of the prisoner's strength out and throws it at them as a wave. At nine tenths the prisoner is dropped,
        /// drained and spent; the cage opens.
        /// </summary>
        private void PulseTick(Pawn q)
        {
            if (pulseCooldown > 0) { pulseCooldown--; return; }
            Map map = parent.Map;
            Faction own = parent.Faction ?? Faction.OfPlayer;
            List<Pawn> foes = map.attackTargetsCache.TargetsHostileToFaction(own).Select(t => t.Thing as Pawn)
                .Where(f => f != null && f.Spawned && !f.Dead && !f.Downed && !held.Contains(f) && (f.Position - parent.Position).LengthHorizontal <= PulseRange).ToList();
            if (foes.Count == 0) return;   // nothing to strike: the prisoner's strength is kept
            pulseCooldown = PulseEvery;
            pulsesTaken.TryGetValue(q, out int n);
            float sens = Mathf.Max(0.2f, q.GetStatValue(StatDefOf.PsychicSensitivity));
            float power = (6f + 4f * sens) * Mathf.Max(0.3f, drawQuality);
            RitualFx.Wave(parent.DrawPos, map, PulseRange * 0.5f);
            foreach (Pawn f in foes)
            {
                if (f.RaceProps.IsMechanoid) f.TakeDamage(new DamageInfo(DamageDefOf.EMP, power * 2f, 0f, -1f, q));
                else
                {
                    f.TakeDamage(new DamageInfo(DamageDefOf.Burn, power, 0f, -1f, q));
                    f.stances?.stunner?.StunFor(Mathf.RoundToInt(60f + 12f * power), q, false, true);
                }
                RitualFx.Ripple(f.DrawPos, map, 1.2f);
            }
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(map);
            if (ext != null) ext.deeds += 300f * power;
            n++;
            pulsesTaken[q] = n;
            if (q.needs?.rest != null) q.needs.rest.CurLevel = Mathf.Max(0f, q.needs.rest.CurLevel - 0.1f);
            if (n >= 9)
            {
                if (q.needs?.rest != null) q.needs.rest.CurLevel = 0f;
                Collapse(q, null);
                Release(q, q.LabelShortCap + " is spent: nine tenths of its strength thrown at its own side, it is dropped, drained, and the Trap opens.");
            }
        }

        // ---------------------------------------------------------------- the endless vortex

        private void StartVortex()
        {
            vortex = 1f;
            Pawn p = performer;
            End("the stream found nothing to fill and nothing to close it: an endless vortex.");
            Arlia.Say("spinning");
            Find.LetterStack.ReceiveLetter("Endless vortex",
                "The ritual at " + parent.LabelShort + " was meant to fill, but nothing could be filled and nothing closed the circle. The stream of the map pours in and has nowhere to go: "
                + "it spins, and grows. It lifts dust and things, it calls the storm, it draws the strength of everyone who comes near.\n\n"
                + "It is also a pump and a source: the map's stream keeps rising while it spins, and whatever is bound to the circle by threads - crystal batteries, artifact buildings, electric batteries - is filled from it, the more the larger it has grown.\n\n"
                + "It holds as long as the drawing does: wash it away, draw another ritual over it, or erase it.",
                LetterDefOf.ThreatBig, parent);
            RitualPresetDef v = DefDatabase<RitualPresetDef>.GetNamedSilentFail("Avir_Ritual_EndlessVortex");
            if (v != null) RitualDiscoveries.Learn(v);
            p?.jobs?.EndCurrentJob(JobCondition.InterruptForced);
        }

        private void VortexTick()
        {
            if (vortex <= 0f) return;
            Map map = parent.Map;
            if (!Drawn) { vortex = 0f; Messages.Message("The endless vortex is broken.", parent, MessageTypeDefOf.PositiveEvent, false); return; }
            vortex = Mathf.Min(10f, vortex + 0.02f);
            float reach = Radius + 2f + vortex;
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(map);
            if (ext != null) ext.deeds += 3000f * vortex;   // a pump: the map's stream rises
            // a source: whatever is threaded to the vortex is filled from it
            foreach (Thing t in Threaded.ToList()) GiveTo(t, VortexYield * vortex);
            for (int i = 0; i < 2; i++) RitualFx.Mote(parent.DrawPos + Rand.InsideUnitCircleVec3 * reach, map, Rand.Range(0.4f, 0.9f));
            // dust, and things lifted and thrown about
            for (int i = 0; i < 3; i++) FleckMaker.ThrowDustPuff(parent.Position + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(reach))], map, 1.5f);
            Thing loose = GenRadial.RadialDistinctThingsAround(parent.Position, map, reach, true)
                .Where(t => t.def.category == ThingCategory.Item && t.def.EverHaulable && t.Spawned).RandomElementWithFallback();
            if (loose != null && Rand.Chance(0.5f) && CellFinder.TryFindRandomCellNear(parent.Position, map, Mathf.CeilToInt(reach + 3f), c => c.Standable(map), out IntVec3 to))
            {
                loose.DeSpawn();
                GenPlace.TryPlaceThing(loose, to, map, ThingPlaceMode.Near);
            }
            // whoever comes near is drawn on
            foreach (Pawn q in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (q.Dead || (q.Position - parent.Position).LengthHorizontal > reach || q.needs?.rest == null) continue;
                q.needs.rest.CurLevel = Mathf.Max(0f, q.needs.rest.CurLevel - 0.01f * vortex);
            }
            // it calls the storm
            if (vortex >= 3f && Rand.Chance(0.15f + 0.03f * vortex)
                && CellFinder.TryFindRandomCellNear(parent.Position, map, Mathf.CeilToInt(reach + 6f), c => !c.Roofed(map) && (c - parent.Position).LengthHorizontal > Radius + 1f, out IntVec3 strike))
                map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(map, strike));
            if (vortex >= 5f && !(map.weatherManager.curWeather?.defName ?? "").Contains("Thunderstorm"))
            {
                WeatherDef storm = DefDatabase<WeatherDef>.GetNamedSilentFail("RainyThunderstorm");
                if (storm != null) map.weatherManager.TransitionTo(storm);
            }
        }

        /// <summary>Scuffs the chalk: whatever was drawn - and a vortex - is gone.</summary>
        public void Erase()
        {
            if (Running) End("the chalk is erased.");
            drawn = null; drawQuality = 0f; armed = false; vortex = 0f; engraved = false; selfSustain = false; chiselPending = false; chiselWork = 0f;
            ReleaseAll("the chalk is erased");
            Messages.Message("The ritual circle is erased.", parent, MessageTypeDefOf.NeutralEvent, false);
        }

        // ---------------------------------------------------------------- Trap variants

        /// <summary>What happens to a prisoner of a Trap variant, each rare tick.</summary>
        private void TrapVariantTick(Pawn q, RitualReading r)
        {
            MapComponent_ExternalFlow ext = MapComponent_ExternalFlow.Of(parent.Map);
            switch (r.trapKind)
            {
                case TrapKind.Incomplete:
                    // fed by the map's flow while there is any; the cage draws a little of it
                    if (ext != null && ext.Total > 1f) { Sustain(q); ext.deeds += 500f; }
                    break;
                case TrapKind.Feeding:
                    // you pay along the thread
                    float paid = 0f;
                    foreach (Thing t in Threaded) { paid += TakeFrom(t, 1f - paid); if (paid >= 0.999f) break; }
                    if (paid >= 0.999f) Sustain(q);
                    break;
                case TrapKind.Pulse:
                    PulseTick(q);
                    break;
                case TrapKind.Milking:
                    Need_Rest rest = q.needs?.rest;
                    if (rest == null) break;
                    float take = r.focus > 0 ? 0.004f : 0.012f;
                    rest.CurLevel = Mathf.Max(0f, rest.CurLevel - take);
                    if (r.focus > 0 && ext != null) ext.deeds += 800f;
                    float energy = 1.5f * Mathf.Max(0.2f, q.GetStatValue(StatDefOf.PsychicSensitivity)) * Mathf.Max(0.2f, drawQuality);
                    foreach (Thing t in Threaded) { energy -= GiveTo(t, energy); if (energy <= 0.001f) break; }
                    if (rest.CurLevel <= 0.01f && r.focus == 0)
                    {
                        Collapse(q, null);
                        Release(q, q.LabelShortCap + " is milked dry and collapses: the Trap opens - it can be taken.");
                    }
                    break;
            }
        }

        private static void Sustain(Pawn q)
        {
            if (q.needs?.food != null) q.needs.food.CurLevel = Mathf.Max(q.needs.food.CurLevel, q.needs.food.MaxLevel * 0.5f);
            if (q.needs?.rest != null) q.needs.rest.CurLevel = Mathf.Max(q.needs.rest.CurLevel, 0.5f);
        }

        // ---------------------------------------------------------------- gizmos, choosing

        private IEnumerable<Gizmo> MoreGizmos()
        {
            foreach (Gizmo g in ChoiceGizmos()) yield return g;
            if (Drawn && (Reading.thread > 0 || vortex > 0f))
            {
                var bind = new Command_Action
                {
                    defaultLabel = "Threads (" + threads.Count(t => t != null && t.Spawned) + "/" + MaxThreads + ")...",
                    defaultDesc = "Bind things anywhere on the map to the circle by thin grey threads: crystal batteries, artifact buildings, psychic pylons, electric batteries and grids. "
                                  + "A filling ritual reaches them; drawing out takes from them; a Trap feeds or milks along them; transmutation into current charges their grids. Right-click ends.",
                    icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Thread")?.Ink ?? BaseContent.BadTex,
                    action = BindThread
                };
                if (threads.Count(t => t != null && t.Spawned) >= MaxThreads) bind.Disable("every thread is bound - cut them to bind others");
                yield return bind;
                if (threads.Count > 0)
                    yield return new Command_Action
                    {
                        defaultLabel = "Cut threads",
                        defaultDesc = "Cut every thread of this circle.",
                        icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Thread")?.Glow ?? BaseContent.BadTex,
                        action = () => threads.Clear()
                    };
            }
            if (Drawn && engraved)
            {
                string why = CannotSustain();
                var tog = new Command_Toggle
                {
                    defaultLabel = "Self-sustaining",
                    defaultDesc = "The engraving wakes by itself whenever there is work for it - artifacts or batteries to fill, foes to hold or drain, plants to grow, a threat for the dome - "
                                  + "through a conduit at its centre: an artifact, a crystal battery, an artifact building or a psychic pylon. Energy comes from the conduit, crystals and feathers in the circle, threads, colonists standing in it, and focus runes. "
                                  + "A thing holds the stream with " + ConduitComposure.ToStringPercent() + " composure; a backlash burns it.\n\nConduit now: " + (FindConduit()?.LabelShort ?? "none"),
                    icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Anchor")?.Ink ?? BaseContent.BadTex,
                    isActive = () => selfSustain,
                    toggleAction = () => { selfSustain = !selfSustain; if (!selfSustain && conduit != null) End("no longer self-sustaining."); }
                };
                if (why != null) tog.Disable(why);
                yield return tog;
            }
            if (drawn != null)
                yield return new Command_Toggle
                {
                    defaultLabel = engraved ? (vortex > 0f ? "Level the engraving (vortex)" : "Level the engraving") : vortex > 0f ? "Break the vortex" : "Erase the chalk",
                    defaultDesc = (engraved
                                      ? "An engraving cannot be broken or attacked - only levelled away, like rough ground: a constructor's work, about half of what it took to cut."
                                      : "Chalk is wiped away like any dirt: whoever cleans does it, quickly." + (vortex > 0f ? " Whoever comes near the vortex is drained, mind." : ""))
                                  + " Click again to call it off; the work done is kept."
                                  + (chiselPending ? "\n\nProgress: " + (chiselWork / ChiselWorkNeeded).ToStringPercent() : ""),
                    icon = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Purge")?.Ink ?? BaseContent.BadTex,
                    isActive = () => chiselPending,
                    toggleAction = () => chiselPending = !chiselPending
                };
        }

        public static bool Threadable(Thing t) =>
            t != null && (t.TryGetComp<CompCrystalBattery>() != null || t.TryGetComp<CompEnergyKeeper>() != null || t.TryGetComp<CompArtifactHost>() != null || t.TryGetComp<CompPsychicPylon>() != null
                          || t.TryGetComp<CompPower>() != null);

        private void BindThread()
        {
            var tp = new TargetingParameters { canTargetBuildings = true, canTargetItems = true, canTargetPawns = false, canTargetLocations = false, mapObjectTargetsMustBeAutoAttackable = false, validator = ti => Threadable(ti.Thing) };
            Find.Targeter.BeginTargeting(tp, ti =>
            {
                if (!Threadable(ti.Thing) || threads.Contains(ti.Thing)) return;
                threads.RemoveAll(x => x == null || !x.Spawned);
                threads.Add(ti.Thing);
                if (threads.Count < MaxThreads) BindThread();
            }, null, null, null);
        }

        /// <summary>After the performer is chosen: the point a reach rune carries it to, then what Transfer or Reforge needs.</summary>
        private void Prepare(Pawn p)
        {
            string lack = MissingMaterial();
            if (lack != null) { Messages.Message("Cannot begin: " + lack + ".", parent, MessageTypeDefOf.RejectInput, false); return; }
            RitualReading r = Reading;
            System.Action<IntVec3> next = at =>
            {
                if (r.Has(RuneKind.Transfer)) TransferMenu(p, at);
                else if (r.Has(RuneKind.Reforge)) ReforgeMenu(p, at);
                else if (IsPath(r)) PathMenu(p, at);
                else if (r.Has(RuneKind.Blood) || r.Has(RuneKind.Shroud)) AwakenMenu(p, at);
                else Order(p, at, null, null, 0);
            };
            if (r.reach == 0 && !(r.Dome && r.reach > 0)) { next(IntVec3.Invalid); return; }
            float range = r.ReachRange;
            Messages.Message("Choose the point the ritual reaches - anywhere on the map.", MessageTypeDefOf.SilentInput, false);
            var tp = new TargetingParameters { canTargetLocations = true, canTargetPawns = true, canTargetBuildings = true, canTargetItems = true };
            Find.Targeter.BeginTargeting(tp, ti => next(ti.Cell), null,
                ti => ti.Cell.InBounds(parent.Map) && (ti.Cell - parent.Position).LengthHorizontal <= range,
                null, null, null, true, null,
                ti =>
                {
                    if (range < GenRadial.MaxRadialPatternRadius) GenDraw.DrawRadiusRing(parent.Position, range);
                    if (ti.IsValid && Reach(r) < GenRadial.MaxRadialPatternRadius) GenDraw.DrawRadiusRing(ti.Cell, r.Dome ? Mathf.Min(r.DomeRadius, GenRadial.MaxRadialPatternRadius - 1f) : Reach(r));
                });
        }

        private void ReforgeMenu(Pawn p, IntVec3 at)
        {
            RitualReading r = Reading;
            Thing t = ReforgeTarget;
            ArtifactState st = t?.TryGetComp<CompArtifact>()?.state;
            var opts = new List<FloatMenuOption>();
            CompStriderBow bow = CompStriderBow.Of(t);
            if (bow?.bonded != null)
                opts.Add(new FloatMenuOption("Break the bond of " + t.LabelShort + " with " + bow.bonded.LabelShort + " (" + UnbondCost.ToString("0") + " energy)",
                    () => Order(p, at, null, null, 0)));
            else if (st == null) opts.Add(new FloatMenuOption("Lay an artifact in the circle first", null));
            else
                for (int m = 0; m <= 2; m++)
                {
                    int mm = m;
                    string why = CannotReforge(st, m, r);
                    string label = ReforgeLabel(m, r) + " (" + ReforgeCost(st, m).ToString("0") + " energy)" + (why != null ? " - " + why : "");
                    opts.Add(new FloatMenuOption(label, why != null ? null : (System.Action)(() => Order(p, at, null, null, mm))));
                }
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // ---------------------------------------------------------------- drawing and saving

        private static Material netMark;
        private static Material NetMarkMat => netMark ?? (netMark = MaterialPool.MatFrom(RitualTex.Ring, ShaderDatabase.Transparent, new Color(0.8f, 0.7f, 1f, 0.55f)));

        private static readonly Material ThreadMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.75f, 0.75f, 0.78f, 0.45f));

        private void DrawMore()
        {
            Vector3 c = parent.DrawPos;
            c.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            // the threads to the network and to threaded things show only while the circle is selected
            // the threads the player bound are drawn as threads; what the stream network joins is only marked with a
            // faint ring where it stands - dozens of lines fanning out over the whole base said nothing and hid the map
            if (Find.Selector.IsSelected(parent))
            foreach (Thing t in Threaded)
            {
                Vector3 b = t.DrawPos; b.y = c.y;
                if (!threads.Contains(t))
                {
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(b, Quaternion.identity, new Vector3(1.3f, 1f, 1.3f)), NetMarkMat, 0);
                    continue;
                }
                if (FlowTex.Thread == null) { GenDraw.DrawLineBetween(c, b, ThreadMat, 0.06f); continue; }
                DrawThread(c, b);
            }
            if (dome != null)
            {
                Vector3 d = dome.center.ToVector3Shifted(); d.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                float pulse = 0.55f + 0.12f * Mathf.Sin(Find.TickManager.TicksGame / 30f) + 0.3f * (1f - Mathf.Clamp01(domeHp / 150f));
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(d, Quaternion.identity, new Vector3(dome.radius * 2f, 1f, dome.radius * 2f)),
                    MaterialPool.MatFrom(FlowTex.Dome ?? RitualTex.Ring, ShaderDatabase.Transparent, new Color(1f, 1f, 1f, Mathf.Clamp01(pulse))), 0);
            }
            if (vortex > 0f)
            {
                Vector3 v = parent.DrawPos; v.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                float size = (Radius + 2f + vortex) * 2f;
                float turn = Find.TickManager.TicksGame * 2f % 360f;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(v, Quaternion.AngleAxis(turn, Vector3.up), new Vector3(size, 1f, size)),
                    FlowTex.Vortex != null ? MaterialPool.MatFrom(FlowTex.Vortex, ShaderDatabase.Transparent, new Color(1f, 1f, 1f, Mathf.Clamp01(0.45f + 0.05f * vortex)))
                                           : MaterialPool.MatFrom(RitualOrnament.CoreGlow, ShaderDatabase.Transparent, new Color(0.7f, 0.5f, 1f, 0.35f)), 0);
            }
        }

        /// <summary>The thread texture laid in pieces of a few cells, so it keeps its grain over any length.</summary>
        private static void DrawThread(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.1f) return;
            const float piece = 4f;
            Quaternion rot = Quaternion.LookRotation(d) * Quaternion.Euler(0f, 90f, 0f);
            Material m = MaterialPool.MatFrom(FlowTex.Thread, ShaderDatabase.MoteGlow, new Color(1f, 1f, 1f, 0.8f));
            int n = Mathf.CeilToInt(len / piece);
            float step = len / n;
            for (int i = 0; i < n; i++)
            {
                Vector3 mid = a + d.normalized * (step * (i + 0.5f));
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(mid, rot, new Vector3(step, 1f, 0.25f)), m, 0);
            }
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            if (!Drawn) return;
            RitualReading r = Reading;
            // what was chosen: the conduit (gold) and the artifact to reforge (violet)
            if (chosenThing != null && chosenThing.Spawned) GenDraw.DrawLineBetween(parent.DrawPos, chosenThing.DrawPos, SimpleColor.Yellow);
            if (reforgeThing != null && reforgeThing.Spawned) GenDraw.DrawLineBetween(parent.DrawPos, reforgeThing.DrawPos, SimpleColor.Magenta);
            if (Running && r.reach > 0 && target.IsValid)
            {
                GenDraw.DrawLineBetween(parent.DrawPos, target.ToVector3Shifted(), SimpleColor.White);
                float rr = r.Dome ? r.DomeRadius : Reach(r);
                if (rr < GenRadial.MaxRadialPatternRadius) GenDraw.DrawRadiusRing(target, rr);
            }
        }

        private void ExposeMore()
        {
            Scribe_Values.Look(ref target, "target", IntVec3.Invalid);
            Scribe_References.Look(ref chosenPerformer, "chosenPerformer");
            Scribe_References.Look(ref chosenThing, "chosenThing");
            Scribe_References.Look(ref reforgeThing, "reforgeThing");
            Scribe_Values.Look(ref stepTicks, "stepTicks");
            Scribe_Values.Look(ref reforgeMode, "reforgeMode");
            Scribe_Values.Look(ref vortex, "vortex");
            Scribe_Values.Look(ref domeHp, "domeHp");
            Scribe_Values.Look(ref lastSoilTick, "lastSoilTick", -99999);
            ExposeAwakening();
            Scribe_Collections.Look(ref threads, "threads", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (threads == null) threads = new List<Thing>();
                threads.RemoveAll(x => x == null);
            }
        }
    }

    /// <summary>A barrier dome: shots from outside stop on its edge. Raised by a ritual (barrier + anchor) or a Spire segment.</summary>
    public class BarrierDome
    {
        public static readonly List<BarrierDome> All = new List<BarrierDome>();
        public Map map;
        public IntVec3 center;
        public float radius;
        /// <summary>The spire's barrier is a ship's shield too: no vacuum under it.</summary>
        public bool sealsVacuum;
        /// <summary>Domes of one barrier (the spire and its pylons): a shot inside any of them is inside all.</summary>
        public object group;
        public bool InGroup(Vector3 v) => group == null ? Inside(v) : All.Any(o => o.group == group && o.map == map && o.Inside(v));
        /// <summary>Takes the hit; false - the dome let it through.</summary>
        public System.Func<Projectile, bool> hit;

        public static bool Covers(Map map, IntVec3 c) => All.Any(d => d.map == map && (c - d.center).LengthHorizontalSquared <= d.radius * d.radius);

        public static bool Seals(Map map, IntVec3 c)
        {
            for (int i = 0; i < All.Count; i++)
            {
                BarrierDome d = All[i];
                if (d.sealsVacuum && d.map == map && (c - d.center).LengthHorizontalSquared <= d.radius * d.radius) return true;
            }
            return false;
        }

        public bool Inside(Vector3 v) => (new Vector3(v.x, 0f, v.z) - center.ToVector3Shifted()).WithY(0f).sqrMagnitude <= radius * radius;
    }

    [HarmonyLib.HarmonyPatch(typeof(Projectile), "CheckForFreeInterceptBetween")]
    public static class Patch_BarrierDome
    {
        public static void Postfix(Projectile __instance, Vector3 lastExactPos, Vector3 newExactPos, ref bool __result)
        {
            if (__result || BarrierDome.All.Count == 0 || !__instance.Spawned) return;
            Map map = __instance.Map;
            Thing launcher = __instance.Launcher;
            foreach (BarrierDome d in BarrierDome.All.ToList())
            {
                if (d.map != map || d.InGroup(lastExactPos) || !d.Inside(newExactPos)) continue;
                // only shots from outside, and not your own
                if (launcher != null && launcher.Faction == Faction.OfPlayer) continue;
                if (d.hit != null && !d.hit(__instance)) continue;
                RitualFx.Ripple(newExactPos, map, 2.5f);
                FleckMaker.ThrowMicroSparks(newExactPos, map);
                __instance.Destroy();
                __result = true;
                return;
            }
        }
    }
}
