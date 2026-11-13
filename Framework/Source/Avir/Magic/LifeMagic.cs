using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;
using LudeonTK;
using HarmonyLib;

namespace Avir.Magic
{
    /// <summary>Life, the first great circle (Магия-правила.md 0.28). Its law - Symbiosis - is that it makes nothing: it
    /// only moves. Withering takes from one thing into the mage's store of life; the other verbs give that store away.
    /// With an empty store nothing is given at all: the key rune of Life is Withering. A store filled by one spell can be
    /// spent by another, whenever the player likes. The great circle's own sections are its pure effect, cast where the
    /// spell ends; round small circles it mends the friends the spell touches - from the store too.</summary>
    public static partial class LifeMagic
    {
        public static readonly Color Necrotic = new Color(0.66f, 0.82f, 0.55f), NecroticDark = new Color(0.22f, 0.3f, 0.2f, 0.7f);
        private static HediffDef H(string n) => DefDatabase<HediffDef>.GetNamedSilentFail(n);
        public static HediffDef Regen => H("Avir_LifeRegen");
        public static DamageDef WitherDmg => DefDatabase<DamageDef>.GetNamedSilentFail("Avir_Withering") ?? DamageDefOf.Burn;
        public static TerrainDef DeadGround => DefDatabase<TerrainDef>.GetNamedSilentFail("Avir_DeadGround");

        public static bool Is(RuneDef e) => e != null && e.kind == RuneKind.Life;
        public static IEnumerable<SpellCircle> Circles(SpellCircle root) => WeaveSpell.Greats(root).Where(g => Is(g.element));
        /// <summary>Is this circle of the queue one of Life: a great circle of Life (its own runes), or a small circle with
        /// no element inside one (a small circle of Life - the author's rule).</summary>
        public static bool IsLink(SpellCircle root, SpellCircle c)
        {
            if (c == null) return false;
            if (c.Great) return Is(c.element);
            if (c.element != null) return false;
            SpellCircle box = root?.ParentOf(c);
            return box != null && box.Great && Is(box.element);
        }
        /// <summary>Every circle of Life in a spell that acts on its own: the great ones' own runes and the small ones inside them.</summary>
        public static IEnumerable<SpellCircle> Links(SpellCircle root) => root == null ? Enumerable.Empty<SpellCircle>() : root.Working.Where(c => IsLink(root, c) && c.OwnRunes.Any());
        private static bool Friend(Pawn p, Pawn caster) => p != null && !p.Dead && !p.HostileTo(caster) && (p.Faction == caster.Faction || p.HostFaction == caster.Faction || p == caster);
        private static bool Living(Pawn p) => p != null && p.RaceProps != null && !p.RaceProps.IsMechanoid && p.RaceProps.IsFlesh;
        public static float Radius(SpellCircle g) => 2.5f + 1.5f * g.OwnRunes.Count(x => x.kind == RuneKind.Breadth);

        /// <summary>The two sides of Life (the author): Withering (death - takes) and Restoration (life - gives). Every
        /// verb and every beam of Life is tied to one of them - "Release + Withering", "Beam + Restoration"; a verb left
        /// untied does nothing, and Arlie says so. A bare Withering or Restoration acts at the point itself.</summary>
        public static bool Side(RuneKind k) => k == RuneKind.Drain || k == RuneKind.Restore;
        public static bool Verb(RuneKind k) => k == RuneKind.Release || k == RuneKind.Beam || k == RuneKind.Hold || k == RuneKind.Growth || k == RuneKind.Purge || k == RuneKind.Step || k == RuneKind.Lift;
        public static bool Knows(RuneKind k) => Side(k) || Verb(k);

        /// <summary>One act of a Life circle: a verb (or none) tied to its side; `hop` - the energy goes on at once
        /// along a Reach and a Thread written before it (the leap rule), to the nearest wounded one of the struck.</summary>
        public class Act { public RuneKind verb = (RuneKind)(-1); public RuneKind side; public bool hop; public int reach; public bool pin; public bool trans; }

        public static List<Act> Read(SpellCircle g, out List<RuneKind> untied)
        {
            var acts = new List<Act>(); untied = new List<RuneKind>();
            RuneKind? pending = null; bool thread = false, lastSide = false; int reach = 0;
            var runes = g.sections.Select((sec, i) => new { sec, i }).SelectMany(x => (x.sec.runes ?? new List<RuneDef>()).Where(r => r != null)
                .Select(r => new { r, outer = x.i == g.sections.Count - 1 })).ToList();
            bool shaped = SpellSculpt.Of(g);
            foreach (var x in runes)
            {
                RuneKind k = x.r.kind;
                if (shaped && x.outer && (k == RuneKind.Barrier || k == RuneKind.Thread)) continue;   // the shaping, not runes of its own
                if (k == RuneKind.Reach) { reach++; continue; }
                if (k == RuneKind.Thread) { thread = true; continue; }
                // Transmutation right after an act: that act works on matter - its side decides which way (the author)
                if (k == RuneKind.Transmute) { if (acts.Count > 0 && lastSide) acts[acts.Count - 1].trans = true; else untied.Add(k); lastSide = false; continue; }
                lastSide = false;
                if (Verb(k)) { if (pending != null) untied.Add(pending.Value); pending = k; continue; }
                if (Side(k))
                {
                    acts.Add(new Act { verb = pending ?? (RuneKind)(-1), side = k, hop = thread && reach > 0 && acts.Count > 0, reach = reach, pin = thread && reach == 0 });
                    lastSide = true;
                    pending = null; thread = false; reach = 0;
                }
            }
            if (pending != null) untied.Add(pending.Value);
            return acts;
        }

        /// <summary>Whom a Life circle reaches: as everywhere, by its outer edge - two Barriers spare one's own (it strikes the
        /// foes), two Barriers and a Thread turn it to one's own; unshaped, everyone living in reach, the caster too -
        /// Withering drinks friends as well, Restoration mends the foes as well.</summary>
        private static List<Pawn> Reached(Pawn caster, SpellCircle g, IEnumerable<Pawn> near)
        {
            bool shaped = SpellSculpt.Of(g), aid = SpellSculpt.AidOf(g);
            return near.Where(p => Living(p) && !p.Dead && (!shaped || (aid ? SpellSculpt.Own(p, caster) : !SpellSculpt.Own(p, caster)))).ToList();
        }

        private static IEnumerable<Pawn> Area(IntVec3 at, float r, Map map) => GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Pawn>();
        private static IEnumerable<Pawn> Line(Pawn caster, IntVec3 to, Map map, out List<IntVec3> cells)
        {
            cells = GenSight.BresenhamCellsBetween(caster.Position, to).Where(c => c.InBounds(map) && c != caster.Position).ToList();
            var l = cells; return cells.SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Distinct().ToList();
        }

        // ------------------------------------------------------------------ the great circle's own runes

        /// <summary>The energy just taken, not yet in the store: what a Restoration further on spends first; what is left
        /// goes into the store at the end. A Restoration that finds too little takes the rest from the store.</summary>
        private static float flow;
        private static readonly Dictionary<int, float> flows = new Dictionary<int, float>();
        /// <summary>One spell's energy lives through all its Life circles: taken in one, it is there for the next; what is
        /// left goes into the store when the spell is over. Kept for each caster, so two spells at once do not mix.</summary>
        public static void BeginSpell(Pawn caster) { if (caster != null) flows[caster.thingIDNumber] = 0f; }
        public static void EndSpell(Pawn caster)
        {
            if (caster == null || !flows.TryGetValue(caster.thingIDNumber, out float f)) return;
            flows.Remove(caster.thingIDNumber);
            Hediff_ArcaneCore core = Mages.Core(caster);
            RuneKind fk = flowKinds.TryGetValue(caster.thingIDNumber, out RuneKind k0) ? k0 : None;
            flowKinds.Remove(caster.thingIDNumber);
            if (core != null && f > 0f) core.AddTo(fk, f);
        }
        /// <summary>Which store the spell's leftover flow goes into: the meeting it was cast in.</summary>
        private static readonly Dictionary<int, RuneKind> flowKinds = new Dictionary<int, RuneKind>();
        /// <summary>The store a cast takes from and gives into: its meeting's, plain Life's outside one.</summary>
        public static RuneKind CurrentKind => pair;
        /// <summary>A circle of Life in its turn in the queue.</summary>
        public static void CastCircle(Pawn caster, SpellCircle root, SpellCircle g, IntVec3 at, float power, Map map)
        {
            flows.TryGetValue(caster.thingIDNumber, out flow);
            pair = PairOf(root, g.Great ? g : root?.ParentOf(g));
            aspect = AspectOf(root, g.Great ? g : root?.ParentOf(g));
            mix = MixOf(root, g.Great ? g : root?.ParentOf(g));
            flowKinds[caster.thingIDNumber] = pair;
            if (pair != None) GameComponent_Combos.First("life" + pair, caster);
            lightMeet = LightLife.Meets(root);
            try { Cast(caster, g, at, power, map); }
            finally { flows[caster.thingIDNumber] = flow; flow = 0f; pair = None; aspect = None; mix = LifeMix.None; lightMeet = false; }
        }

        // ------------------------------------------------------------------ the pairs: Life meeting a small element in its box

        public const RuneKind None = (RuneKind)(-1);
        /// <summary>The element Life meets in the circle being cast now (None - plain Life).</summary>
        private static RuneKind pair = None;

        /// <summary>A great circle of Life with small circles of an element inside takes that meeting's nature (the author's
        /// rule): fire - Wild Flame, water - Blood, earth - Emerald, air - Pestilence. A Closing anywhere in the box keeps
        /// them apart. Two different elements inside: the first in the queue decides (their own meetings are for later).</summary>
        public static RuneKind PairOf(SpellCircle root, SpellCircle box)
        {
            if (box == null || !box.Great || !Is(box.element)) return None;
            var inside = box.inner.Where(k => !k.Great).ToList();
            if (WeaveSpell.SealedGreat(box)) return None;   // a Closing in its first section keeps it apart (Fix214)
            SpellCircle first = inside.FirstOrDefault(k => k.element != null && (k.element.kind == RuneKind.Fire || k.element.kind == RuneKind.Water || k.element.kind == RuneKind.Earth || k.element.kind == RuneKind.Air));
            // the deep meeting needs two circles of the element (the author's ladder, Fix165); one is the wide meeting (AspectOf)
            if (first == null || inside.Count(k => k.element == first.element) < 2) return None;
            return first.element.kind;
        }

        /// <summary>The meeting being explained now (for the words of a Transmutation).</summary>
        public static RuneKind PairOfText = None;

        public static string PairText(RuneKind k)
        {
            switch (k)
            {
                case RuneKind.Fire: return "Wild Flame - living bombs: Withering sows a spark in the bodies it reaches; a carrier that touches another bursts in petals of flame, and the spark leaps on, weaker each time. Water does not put it out - in water the carrier steams and scalds. Restoration sows smouldering hearts that mend whom they touch.";
                case RuneKind.Water: return "Blood - the body is a pool, as water's pools are: Withering drinks part of the blood of the reached, Restoration gives it back. Release and Beam paralyse, Hold holds the blood still, Lift throws the body up, Lift with a Thread presses it down, Step drags it by its blood. The mage of Blood mends their own blood quickest of all, out of the store.";
                case RuneKind.Earth: return "Emerald - stone remembers life: Withering grows a crust of stone on the living, slower and harder as it thickens; full, a living statue that stands for days, hunger at a tenth. Half of what it takes is sealed in emeralds at the spot. Restoration draws on such stones, makes living ground; with Hold, a stone cocoon.";
                case RuneKind.Air: return "Pestilence - turned round: Withering takes the sicknesses of the reached onto the mage, who carries them as fuel (at mastery it draws out even cancer and the heaviest illnesses - those cannot be taken, only cured); Growth cast at the mage's own feet grows sicknesses on the mage, more of them the stronger it is; Restoration lays a fertile poison fog - the ground grows rich under it for three days, and whoever stands in it takes the poison in, which ends in a paralytic abasia. Growth swells the sicknesses it reaches.";
            }
            return null;
        }

        public static string PairName(RuneKind k) => k == RuneKind.Fire ? "Wild Flame" : k == RuneKind.Water ? "Blood" : k == RuneKind.Earth ? "Emerald" : k == RuneKind.Air ? "Pestilence" : null;
        /// <summary>The colour of the Life being cast now: its meeting's, else its aspect's, else plain necrotic.</summary>
        public static Color GateTint => pair != None ? PairColor(pair) : aspect != None ? AspectColor(aspect) : Necrotic;
        public static Color PairColor(RuneKind k) => k == RuneKind.Fire ? new Color(0.85f, 0.8f, 0.3f) : k == RuneKind.Water ? new Color(0.7f, 0.08f, 0.1f) : k == RuneKind.Earth ? new Color(0.2f, 0.7f, 0.4f) : k == RuneKind.Air ? new Color(0.75f, 0.78f, 0.3f) : Necrotic;

        public static void Cast(Pawn caster, SpellCircle g, IntVec3 at, float power, Map map)
        {
            Hediff_ArcaneCore core = Mages.Core(caster);
            if (core == null || map == null || !at.InBounds(map)) return;
            MagicSounds.Play("El:Life", at, map);   // Fix225
            if (SpellSculpt.Of(g)) power *= SpellSculpt.Keeps;
            if (pair == RuneKind.Air) core.pestMage = true;
            // an Aura with an Anchor in a circle of Life: a living aura - its acts pulse round the mage (the general rule of auras)
            if (!pulsing) RaiseAura(caster, g, power, map);
            if (!pulsing) LifeBarrier(caster, g, at, power, map);
            if (!pulsing && g.OwnRunes.Any(x => x.kind == RuneKind.Sense)) LifeSense(caster, g, at, power, map);
            if (pair == RuneKind.Water) core.bloodMage = true;
            float r = pulsing ? pulseRadius : Radius(g);
            IntVec3 last = at;
            bool gaveNothing = false;
            // Step + Anchor: a living gate grows where the spell lands (the author's plan; Fix195) - out of the store
            bool gate = !pulsing && g.OwnRunes.Any(x => x.kind == RuneKind.Step) && g.OwnRunes.Any(x => x.kind == RuneKind.Anchor);
            if (gate)
            {
                // Blood grows no gates (the author, Fix208); the others' gates glow in the colour of their Life
                if (pair == RuneKind.Water) { MoteMaker.ThrowText(at.ToVector3Shifted(), map, "Blood grows no gates", PairColor(pair), 3f); gate = false; }
                float fg = gate ? Pay(core, 15f) : 0f;
                if (gate && fg >= 0.99f) Building_LifeGate.Grow(caster, map, at, GateTint);
                else if (gate) MoteMaker.ThrowText(at.ToVector3Shifted(), map, "Not life enough to grow a gate", Necrotic, 3f);
            }
            foreach (Act act in Read(g, out _))
            {
                if (gate && act.verb == RuneKind.Step) continue;
                IntVec3 where = last;
                if (act.hop)
                {
                    // the leap: the energy goes on at once to the nearest wounded one reached, within the Reach
                    float range = 10f + 10f * act.reach;
                    Pawn to = Reached(caster, g, Area(last, range, map)).Where(Hurt).OrderBy(p => p.Position.DistanceToSquared(last)).FirstOrDefault();
                    if (to != null) { Leap(map, last, to.Position); where = to.Position; }
                }
                if (act.side == RuneKind.Drain)
                {
                    Take(caster, core, g, act, where, r, power, map);
                    if (act.verb != RuneKind.Beam && act.verb != RuneKind.Step)
                    {
                        MapComponent_SpellSprites.Put(map, "Life/Drain", where.ToVector3Shifted(), r * 2f, 60, -1f, -3f);
                        // the withered patch it leaves for a while (sheet 17b, Fix209)
                        MapComponent_SpellSprites.Put(map, "Life/DeadGround_" + (Rand.Bool ? "A" : "B"), where.ToVector3Shifted(), r * 2.1f, 900);
                    }
                }
                else
                {
                    bool gave = Give(caster, core, g, act, where, r, power, map);
                    gaveNothing |= !gave;
                    if (gave && act.verb != RuneKind.Beam && act.verb != RuneKind.Step) MapComponent_SpellSprites.Put(map, "Life/Mend", where.ToVector3Shifted(), r * 1.8f, 70);
                }
                last = where;
            }
            if (gaveNothing) MoteMaker.ThrowText(at.ToVector3Shifted(), map, "No life to give", Necrotic, 3f);
        }

        // ------------------------------------------------------------------ the Barrier of Life (Fix193)

        /// <summary>Bark and moss (the plan: "Оберег - кора"): a Barrier in a circle of Life lays a ward of bark - as everywhere,
        /// the last rune of the circle on the mage, anywhere else a dome of branches where the spell strikes (round the mage
        /// with an Aura). It holds by the rules of wards; every blow it stops feeds the store of life of the one who laid it.</summary>
        private static void LifeBarrier(Pawn caster, SpellCircle g, IntVec3 at, float power, Map map)
        {
            bool shaped = SpellSculpt.Of(g);
            var runes = g.sections.Select((sec, i) => new { sec, i }).SelectMany(x => (x.sec.runes ?? new List<RuneDef>()).Where(r => r != null).Select(r => new { r, outer = x.i == g.sections.Count - 1 }))
                .Where(x => !(shaped && x.outer && (x.r.kind == RuneKind.Barrier || x.r.kind == RuneKind.Thread))).Select(x => x.r).ToList();
            int barriers = runes.Count(x => x.kind == RuneKind.Barrier);
            if (barriers == 0) return;
            int amp = runes.Count(x => x.kind == RuneKind.Amplify);
            HediffDef ward = H("Avir_LifeWard");
            RuneDef life = DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Life");
            int ticks = Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power);
            if (runes.Last().kind == RuneKind.Barrier)
            {
                SpellWards.Lay(caster, ward, power, amp, ticks);
                if (caster.health.hediffSet.GetFirstHediffOfDef(ward) is Hediff_SpellWard w) w.feeds = caster;
            }
            else
            {
                bool aura = runes.Any(x => x.kind == RuneKind.Aura);
                var d = MapComponent_WardsDrawn.RaiseWard(map, aura ? caster.Position : at, Mathf.Max(1.4f, Radius(g) - 0.5f), life, ComboKind.None, caster, ticks, power, amp);
                if (d != null) { d.feeds = caster; if (ContentFinder<Texture2D>.Get("Things/Mote/Avir/Magic/Life_BranchDome", false) != null) d.tex = "Life_BranchDome"; if (aura) d.follow = caster; }
            }
        }

        /// <summary>The sense of Life (the author, Fix199: taming built on the sense, in plain life): the caster feels every
        /// living thing round them for a while, the unseen too; and the animals in reach feel the caster back - they grow
        /// less wild for a day (easier to tame and to train).</summary>
        private static void LifeSense(Pawn caster, SpellCircle g, IntVec3 at, float power, Map map)
        {
            int anchor = g.OwnRunes.Count(x => x.kind == RuneKind.Anchor), reach = g.OwnRunes.Count(x => x.kind == RuneKind.Reach);
            float radius = 20f + 8f * reach;
            SpellSense.Begin(caster, map, caster.Position, radius, power, RuneKind.Life, ComboKind.None, anchor);
            HediffDef kin = H("Avir_LifeKinship");
            foreach (Pawn a in map.mapPawns.AllPawnsSpawned.Where(a => a.RaceProps.Animal && !a.Dead && a.Position.InHorDistOf(at, Mathf.Max(6f, Radius(g) + 4f))).ToList())
            {
                if (kin != null) MagicFx.Give(a, kin, Mathf.RoundToInt(GenDate.TicksPerDay * power));
                MapComponent_SpellSprites.PutTinted(map, "Life/Heart", Necrotic, a.DrawPos, 0.9f, 60);
            }
        }

        /// <summary>A blow stopped by bark: half its weight goes into the store of plain Life of the one who laid it.</summary>
        public static void WardFeeds(Pawn feeds, float amount)
        {
            Hediff_ArcaneCore c = feeds != null ? Mages.Core(feeds) : null;
            if (c == null || amount <= 0f) return;
            c.AddTo(None, amount * 0.5f);
            if (feeds.Spawned && Rand.Chance(0.3f)) Wisp(feeds.DrawPos, feeds.Map);
        }

        // ------------------------------------------------------------------ the living aura of Life

        private static bool pulsing; private static float pulseRadius;

        private static void RaiseAura(Pawn caster, SpellCircle g, float power, Map map)
        {
            int anchor = g.OwnRunes.Count(x => x.kind == RuneKind.Anchor), breadth = g.OwnRunes.Count(x => x.kind == RuneKind.Breadth);
            if (anchor <= 0 || !g.OwnRunes.Any(x => x.kind == RuneKind.Aura)) return;
            var c = map.GetComponent<MapComponent_Auras>();
            if (c == null) return;
            c.all.RemoveAll(a => a.caster == caster && a.life != null);
            var au = new MapComponent_Auras.Aura
            {
                caster = caster, el = g.element ?? DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_Life"), combo = ComboKind.None, breadth = breadth,
                left = SpellAura.Strikes(anchor), power = power, interval = SpellAura.Interval(breadth), radius = SpellAura.Radius(breadth),
                life = g, lifePair = pair, lifeAspect = aspect, lifeMix = mix
            };
            au.next = Find.TickManager.TicksGame + au.interval;
            c.all.Add(au);
            Hediff_LivingAura.Show(caster);
        }

        /// <summary>One pulse of a living aura of Life: the circle's acts round the mage, with the aura's reach; its own
        /// energy for the pulse - what it takes, it may give in the same pulse, the rest into the store.</summary>
        public static void Pulse(Pawn caster, SpellCircle g, RuneKind withPair, float power, float radius, RuneKind withAspect = (RuneKind)(-1), LifeMix withMix = LifeMix.None)
        {
            if (caster?.Map == null) return;
            bool keepP = pulsing; float keepR = pulseRadius; RuneKind keepPair = pair, keepAspect = aspect; LifeMix keepMix = mix; float keepFlow = flow;
            // (Fix196) the aura keeps the whole meeting of its box - the wide and the mixed too, not only the deep one
            pulsing = true; pulseRadius = radius; pair = withPair; aspect = withAspect; mix = withMix; flow = 0f;
            MapComponent_SpellSprites.Put(caster.Map, "Life/Burst", caster.DrawPos, radius * 2f, 45);
            try
            {
                Hediff_ArcaneCore core = Mages.Core(caster);
                foreach (Act act in Read(g, out _).Where(x => x.verb != RuneKind.Beam && x.verb != RuneKind.Step).ToList())
                {
                    if (act.side == RuneKind.Drain) Take(caster, core, g, act, caster.Position, radius, power, caster.Map);
                    else Give(caster, core, g, act, caster.Position, radius, power, caster.Map);
                }
                if (core != null && flow > 0f) core.AddVita(flow);
            }
            finally { pulsing = keepP; pulseRadius = keepR; pair = keepPair; aspect = keepAspect; mix = keepMix; flow = keepFlow; }
        }

        private static void Leap(Map map, IntVec3 a, IntVec3 b)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt((b - a).LengthHorizontal), 2, 30);
            for (int i = 0; i <= n; i++) Wisp(Vector3.Lerp(a.ToVector3Shifted(), b.ToVector3Shifted(), i / (float)n), map);
        }

        /// <summary>Round small circles: the friends the spell touched are mended - out of the store.</summary>
        private static SpellCircle wrapOf;
        /// <summary>The shade of Life over small circles sealed from it (the author, Fix215): by the sides written in its own
        /// runes - Withering drinks whom they touched (into the store), Restoration mends them, both - first drinks, then
        /// mends; neither - no shade at all. Whom - by the small circle's outer edge (Barriers, Thread), as everywhere.</summary>
        public static void Shade(Pawn caster, SpellCircle g, IntVec3 at, float radius, float power, Map map, SpellCircle small)
        {
            bool drain = g.OwnRunes.Any(x => x.kind == RuneKind.Drain), give = g.OwnRunes.Any(x => x.kind == RuneKind.Restore);
            if (drain)
            {
                flows.TryGetValue(caster.thingIDNumber, out flow);
                try
                {
                    var who = Reached(caster, small ?? g, Area(at, radius, map)).ToList();
                    if (who.Count > 0) { flow += Wither(caster, who, 3f * power, map); MapComponent_SpellSprites.Put(map, "Life/Drain", at.ToVector3Shifted(), radius * 1.6f, 50, -1f, -3f); }
                }
                finally { if (flows.ContainsKey(caster.thingIDNumber)) flows[caster.thingIDNumber] = flow; flow = 0f; }
            }
            if (give) Wrap(caster, g, at, radius, power, map, small);
        }

        public static void Wrap(Pawn caster, SpellCircle g, IntVec3 at, float radius, float power, Map map, SpellCircle small = null)
        {
            wrapOf = small;
            flows.TryGetValue(caster.thingIDNumber, out flow);
            try { WrapNow(caster, g, at, radius, power, map); }
            finally { if (flows.ContainsKey(caster.thingIDNumber)) flows[caster.thingIDNumber] = flow; flow = 0f; wrapOf = null; }
        }

        private static void WrapNow(Pawn caster, SpellCircle g, IntVec3 at, float radius, float power, Map map)
        {
            Hediff_ArcaneCore core = Mages.Core(caster);
            if (core == null) return;
            // whom the small circle reached, by its own outer edge - no friend or foe chosen for it (the author's rule)
            var friends = Reached(caster, wrapOf ?? g, Area(at, radius, map)).ToList();
            float heal = 6f * power;
            float need = friends.Count(Hurt) * heal * 0.6f;
            if (need <= 0f) return;
            float f = Pay(core, need);
            if (f <= 0f) { MoteMaker.ThrowText(at.ToVector3Shifted(), map, "No life to give", Necrotic, 3f); return; }
            foreach (Pawn p in friends) { HealInjuries(p, heal * f); AddRegen(p, 0.1f * f); }
        }

        // ------------------------------------------------------------------ taking: Withering, the attack of Life

        private static void Take(Pawn caster, Hediff_ArcaneCore core, SpellCircle g, Act act, IntVec3 at, float r, float power, Map map)
        {
            if (act.trans) { flow += TransmuteTake(caster, g, act, at, r, power, map); return; }
            // held with Light, Withering drinks light - the living are not touched (the author, Fix265)
            if (lightMeet) { flow += LightLife.DrinkLight(caster, at, act.verb == RuneKind.Beam ? 1.5f : r, power, map); return; }
            float got = 0f;
            curVerb = act.verb;
            mixBeamHit = false;
            got += MixGround(at, act.verb == RuneKind.Beam ? 0f : r, map);   // Humus: the dead in reach go to the soil
            if (act.verb == RuneKind.Beam) { Line(caster, at, map, out var lc); AspectArea(lc, map); }
            else AspectArea(GenRadial.RadialCellsAround(at, act.verb == RuneKind.Release || act.verb == RuneKind.Hold ? r : Mathf.Max(1.5f, r - 1f), true), map);
            switch (act.verb)
            {
                case RuneKind.Release:   // a burst of withering
                    got += Wither(caster, Reached(caster, g, Area(at, r, map)), 6f * power, map) + Plants(at, r, map, true) + Corpses(at, r, map);
                    FleckMaker.Static(at, map, FleckDefOf.PsycastAreaEffect, r);
                    break;
                case RuneKind.Beam:      // a withering beam
                    MapComponent_SpellSprites.Vein(map, caster.DrawPos, at.ToVector3Shifted());
                    got += Wither(caster, Reached(caster, g, Line(caster, at, map, out var cells).Where(p => p != caster)), 7f * power, map);
                    foreach (IntVec3 c in cells) if (Rand.Chance(0.5f)) Wisp(c.ToVector3Shifted(), map);
                    break;
                case RuneKind.Hold:      // vines that drink while they hold
                    foreach (Pawn p in Reached(caster, g, Area(at, r, map)))
                    {
                        MagicFx.Give(p, H("Avir_Entangled"), Mathf.RoundToInt(360f * power));
                        // dead roots burst from the ground round the held one (sheet 17b, Fix209)
                        MapComponent_SpellSprites.Put(map, "Life/Roots_" + "ABC"[Rand.Range(0, 3)], p.DrawPos, 1.9f, Mathf.RoundToInt(360f * power));
                    }
                    got += Wither(caster, Reached(caster, g, Area(at, r, map)), 3f * power, map);
                    break;
                case RuneKind.Growth:    // from the green only: no creature is touched
                    got += Plants(at, r, map, false);
                    break;
                case RuneKind.Purge:     // poison and withering drawn out of the reached - into the store
                    foreach (Pawn p in Reached(caster, g, Area(at, r, map)))
                        foreach (Hediff h in p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.FoodPoisoning || h.def == HediffDefOf.ToxicBuildup).ToList())
                        { float d = Mathf.Min(h.Severity, 0.3f * power); h.Severity -= d; got += 20f * d; if (h.Severity <= 0.01f && p.health.hediffSet.hediffs.Contains(h)) p.health.RemoveHediff(h); }
                    break;
                case RuneKind.Lift:      // Blood: the body is a pool - thrown up, or pressed down; the wide meetings - their own (Fix166)
                    if (aspect != None) AspectLift(Reached(caster, g, Area(at, r, map)).ToList(), act.pin, power, map, at, r);
                    break;
                case RuneKind.Step:      // Blood only: the foe dragged to the mage by its own blood
                    if (pair == RuneKind.Water) got += BloodPull(caster, g, at, power, map, false);
                    break;
                default:                 // a bare Withering: the point itself, strong and narrow
                    float pr = Mathf.Max(1.5f, r - 1f);
                    got += Wither(caster, Reached(caster, g, Area(at, pr, map)), 8f * power, map) + Plants(at, pr, map, true) + Corpses(at, pr, map);
                    break;
            }
            float more = PairTake(caster, core, g, act, at, r, power, map, ref got);
            got += more;
            flow += got;
        }

        /// <summary>What each meeting adds to a Withering - and, for Emerald, where half of it goes.</summary>
        private static float PairTake(Pawn caster, Hediff_ArcaneCore core, SpellCircle g, Act act, IntVec3 at, float r, float power, Map map, ref float got)
            => PairTakeNow(caster, core, g, act, at, r, power, map, ref got);

        private static float Wither(Pawn caster, IEnumerable<Pawn> who, float dmg, Map map)
        {
            float got = 0f;
            foreach (Pawn p in who.ToList())
            {
                if (!Living(p) || p.Dead) continue;
                switch (pair)
                {
                    case RuneKind.Fire: Seed(p, caster, dmg, false, 3); got += dmg * 0.3f; break;          // a living bomb, not a burn
                    case RuneKind.Water: got += Suck(p, dmg); break;                                       // part of its blood
                    case RuneKind.Earth: Crust(p, dmg * 0.04f, curVerb == RuneKind.Growth ? Mathf.RoundToInt(GenDate.TicksPerHour * 8f * Mathf.Max(0.5f, dmg / 6f)) : 0); got += dmg * 0.5f; break;                  // stone grows on it
                    case RuneKind.Air: got += p == caster ? 0f : Pestilence.TakeFrom(caster, p, dmg) + Drink(p, dmg / 6f); break;   // its sicknesses taken onto the mage (Fix160), the rest drunk
                    default:
                        if (mix != LifeMix.None) { MixWither(caster, p, dmg); p.TakeDamage(new DamageInfo(WitherDmg, dmg * 0.6f, 2f, -1f, caster)); got += dmg; break; }
                        if (aspect != None) { AspectWither(caster, p, dmg); p.TakeDamage(new DamageInfo(WitherDmg, dmg * 0.6f, 2f, -1f, caster)); got += dmg; break; }
                        p.TakeDamage(new DamageInfo(WitherDmg, dmg, 2f, -1f, caster)); got += dmg;
                        Desiccation.Dry(p, 0.04f * dmg / 6f, false, caster);   // plain Life dries the flesh, slowly (Fix200)
                        break;
                }
                Wisp(p.DrawPos, map);
            }
            return got;
        }

        private static float Plants(IntVec3 at, float r, Map map, bool deadGround)
        {
            float got = 0f; var temp = MapComponent_TempTerrain.Of(map);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, r, true).Where(c => c.InBounds(map)).ToList())
            {
                Plant pl = c.GetPlant(map);
                if (pl == null || pl.def.plant == null || map.zoneManager.ZoneAt(c) is Zone_Growing) continue;
                got += (pl.def.plant.IsTree ? 6f : pl.def.plant.harvestYield > 0f ? 2f : 0.6f) * Mathf.Max(0.3f, pl.Growth);
                pl.Destroy();
                if (deadGround && DeadGround != null && map.terrainGrid.TerrainAt(c).fertility > 0f) temp?.Set(c, DeadGround, GenDate.TicksPerDay * 3);
                if (Rand.Chance(0.4f)) Wisp(c.ToVector3Shifted(), map);
            }
            return got;
        }

        private static float Corpses(IntVec3 at, float r, Map map)
        {
            float got = 0f;
            foreach (Corpse k in GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Corpse>().ToList())
            {
                CompRottable rot = k.GetComp<CompRottable>();
                if (k.InnerPawn != null && Living(k.InnerPawn) && rot != null && rot.Stage == RotStage.Fresh) { rot.RotProgress = 1e7f; got += 8f; Wisp(k.DrawPos, map); }
            }
            return got;
        }

        private static void Wisp(Vector3 v, Map map) { Color c = mix != LifeMix.None ? MixColor(mix) : aspect != None ? AspectColor(aspect) : PairColor(pair); FleckMaker.ThrowDustPuffThick(v, map, 1f, new Color(c.r, c.g, c.b, 0.55f)); }

        // ------------------------------------------------------------------ giving: Restoration, out of the flow, then the store

        /// <summary>Takes what is there of `need` - from the energy just taken first, then from the store; the share paid.</summary>
        private static IntVec3 payAt = IntVec3.Invalid;
        /// <summary>A giving already paid for elsewhere - by a shining flower that withers for it (Light and Life, Fix265).</summary>
        public static bool FreeGift;
        /// <summary>The circle being cast is held with Light (the Grove of Lights, Fix265).</summary>
        private static bool lightMeet;

        private static float Pay(Hediff_ArcaneCore core, float need)
        {
            if (need <= 0f || FreeGift) return 1f;
            float fromFlow = Mathf.Min(flow, need); flow -= fromFlow;
            float rest = need - fromFlow;
            // Emerald: the stones near the spot and on the mage give first
            if (pair == RuneKind.Earth && rest > 0f && payAt.IsValid) { float st = Emerald.Draw(core.pawn, payAt, rest); fromFlow += st; rest -= st; }
            float have = core.Store(pair);
            float fromStore = Mathf.Min(have, rest); core.SetStore(pair, have - fromStore);
            return Mathf.Clamp01((fromFlow + fromStore) / need);
        }

        private static bool Give(Pawn caster, Hediff_ArcaneCore core, SpellCircle g, Act act, IntVec3 at, float r, float power, Map map)
        {
            if (act.trans) return TransmuteGive(caster, core, g, act, at, r, power, map);
            // Wild Flame gives smouldering hearts, Pestilence gives its fertile poison - not plain mending
            if (pair == RuneKind.Fire || pair == RuneKind.Air) return GiveOwn(caster, core, g, act, at, r, power, map);
            List<Pawn> who; float per = 12f, regen = 0.25f;
            List<IntVec3> cells = null;
            switch (act.verb)
            {
                case RuneKind.Release: who = Reached(caster, g, Area(at, r, map)); per = 9f; regen = 0.15f; break;
                case RuneKind.Beam: who = Reached(caster, g, Line(caster, at, map, out cells)); per = 10f; regen = 0.2f; MapComponent_SpellSprites.Vein(map, caster.DrawPos, at.ToVector3Shifted()); break;
                case RuneKind.Hold: who = Reached(caster, g, Area(at, r, map)); per = 6f; regen = 0.4f; break;     // a cocoon: held still, mended deep
                case RuneKind.Growth: who = Reached(caster, g, Area(at, r, map)); per = 3f; regen = 0.15f; break;
                case RuneKind.Purge: who = Reached(caster, g, Area(at, r, map)); per = 0f; regen = 0f; break;
                case RuneKind.Lift when aspect != None:
                    AspectLift(Reached(caster, g, Area(at, r, map)).ToList(), act.pin, power, map, at, r);
                    return true;
                case RuneKind.Step:
                    if (pair != RuneKind.Water) return true;
                    payAt = at; float pulled = BloodPull(caster, g, at, power, map, true); payAt = IntVec3.Invalid; return pulled >= 0f;
                default: who = Reached(caster, g, Area(at, Mathf.Max(1.5f, r - 1f), map)); break;
            }
            var plants = act.verb == RuneKind.Growth ? GenRadial.RadialCellsAround(at, r, true).Where(c => c.InBounds(map)).Select(c => c.GetPlant(map)).Where(p => p != null && p.LifeStage == PlantLifeStage.Growing).ToList() : new List<Plant>();
            var bad = act.verb == RuneKind.Purge ? who.SelectMany(p => p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.FoodPoisoning || h.def == HediffDefOf.ToxicBuildup || h.def.defName == "Avir_Withered" && !(h is Hediff_Injury)).Select(h => new { p, h })).ToList() : null;
            float need = who.Sum(p => (Hurt(p) ? per * power * 0.6f : 0f) + regen * 4f) + plants.Count * 0.3f + (bad?.Count ?? 0) * 3f;
            if (need <= 0f) return true;
            payAt = at;
            float f = Pay(core, need);
            payAt = IntVec3.Invalid;
            if (f <= 0f) return false;
            PairGive(caster, act, who, at, r, power * f, map);
            if (aspect != None) AspectGive(who, act.verb, power * f, map, at, r);
            if (mix != LifeMix.None) MixGive(caster, core, who, act.verb, power * f, map, at, r);
            foreach (Pawn p in who)
            {
                if (per > 0f) HealInjuries(p, per * power * f);
                AddRegen(p, regen * power * f);
                if (act.verb == RuneKind.Hold) MagicFx.Give(p, H("Avir_Entangled"), Mathf.RoundToInt(300f * f));
                if (per > 0f) FleckMaker.ThrowMetaIcon(p.Position, map, FleckDefOf.HealingCross);
            }
            foreach (Plant pl in plants) pl.Growth = Mathf.Min(1f, pl.Growth + 0.15f * power * f);
            if (lightMeet && per > 0f) LightLife.Bloom(caster, who, at, r, power * f, map);   // the Grove of Lights: shining flowers round the healed
            if (bad != null) foreach (var x in bad) { x.h.Severity -= 0.3f * power * f; if (x.h.Severity <= 0.01f && x.p.health.hediffSet.hediffs.Contains(x.h)) x.p.health.RemoveHediff(x.h); }
            if (cells != null) foreach (IntVec3 c in cells) if (Rand.Chance(0.4f)) Wisp(c.ToVector3Shifted(), map);
            return true;
        }

        private static void PairGive(Pawn caster, Act act, List<Pawn> who, IntVec3 at, float r, float pw, Map map) => PairGiveNow(caster, act, who, at, r, pw, map);

        /// <summary>Blood and Step: drawn to the mage by its own blood (the author). Taken (a foe): every cell of the way
        /// costs it blood, and the store gets it; too far - it falls on the way, bled nearly white. Given (one's own):
        /// drawn gently, the mage paying from the store, the drawn mended a little. Returns what was taken, -1 if nothing
        /// could be paid for.</summary>
        private static float BloodPull(Pawn caster, SpellCircle g, IntVec3 at, float power, Map map, bool own)
        {
            Pawn v = Reached(caster, g, Area(at, 1.9f, map)).Where(p => p != caster && Living(p)).OrderBy(p => p.Position.DistanceToSquared(at)).FirstOrDefault();
            if (v == null) return 0f;
            IntVec3 near = CellFinder.StandableCellNear(caster.Position, map, 1.5f, c => c != caster.Position);
            if (!near.IsValid) return 0f;
            var path = GenSight.BresenhamCellsBetween(v.Position, near).Where(c => c.InBounds(map) && c.Standable(map)).ToList();
            if (path.Count == 0) return 0f;
            Hediff_ArcaneCore core = Mages.Core(caster);
            IntVec3 end = path[path.Count - 1];
            float got = 0f;
            if (own)
            {
                float f = Pay(core, path.Count * 1f);
                if (f <= 0f) return -1f;
                AddRegen(v, 0.2f * f);
            }
            else
            {
                // a foe pays every cell in blood: 3% blood loss a cell (less for the strong); past 60% it falls on the way
                float perCell = 0.03f / Mathf.Max(0.5f, power);
                Hediff bl = v.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss) ?? v.health.AddHediff(HediffDefOf.BloodLoss);
                int can = Mathf.FloorToInt(Mathf.Max(0f, 0.6f - bl.Severity) / perCell);
                if (can < path.Count) { end = path[Mathf.Clamp(can, 0, path.Count - 1)]; bl.Severity = 0.85f; }
                else bl.Severity += perCell * path.Count;
                got = 2f * Mathf.Min(can, path.Count);
                foreach (IntVec3 c in path.Take(Mathf.Min(can + 1, path.Count))) if (Rand.Chance(0.5f)) FilthMaker.TryMakeFilth(c, map, v.RaceProps.BloodDef ?? ThingDefOf.Filth_Blood);
            }
            foreach (IntVec3 c in path) if (Rand.Chance(0.4f)) Wisp(c.ToVector3Shifted(), map);
            v.pather?.StopDead();
            v.Position = end;
            v.Notify_Teleported(true, false);
            return got;
        }

        // ------------------------------------------------------------------ before the cast: is there anything to give from?

        /// <summary>Estimates a spell of Life before it is cast (the author's chain rule): enough from its Witherings for
        /// its Restorations - cast; too little but the store holds the rest - it takes from the store; nothing to wither
        /// here and nothing in the store while it must give - not let go at all (no Arcana spent).</summary>
        public static string Blocked(Pawn caster, RitualSchema s, LocalTargetInfo target)
        {
            if (s?.weave == null || caster?.Map == null) return null;
            Hediff_ArcaneCore core = Mages.Core(caster);
            if (core == null) return null;
            Shift(core);   // a spell just written in: the store left behind passes into its store before it is weighed
            var lifes = Links(s.weave).ToList();
            bool wraps = Circles(s.weave).Any(g => g.inner.Any(k => k.element != null));
            if (lifes.Count == 0 && !wraps) return null;
            // the author (Fix160): giving short of energy is weakened, not refused; a spell is held back only when
            // giving is all it does - no Withering, no element of its own - and there is nothing for it at all
            if (s.weave.All().Any(c => c.element != null && !Is(c.element))) return null;
            bool gives = lifes.Any(g => Read(g, out _).Any(a => a.side == RuneKind.Restore)) || wraps;
            if (!gives) return null;
            bool takes = lifes.Any(g => Read(g, out _).Any(a => a.side == RuneKind.Drain));
            if (Circles(s.weave).Select(g => PairOf(s.weave, g)).Distinct().Any(k => core.Store(k) > 0.5f)) return null;
            if (takes)
            {
                Map map = caster.Map; IntVec3 at = target.Cell;
                bool any = GenRadial.RadialCellsAround(at, 6f, true).Any(c => c.InBounds(map) && (c.GetPlant(map) != null || c.GetThingList(map).Any(t => (t is Pawn p && Living(p) && p != caster) || t is Corpse)));
                if (any) return null;
            }
            return "the store of life is empty and there is nothing here to wither - Life cannot give what it has not taken.";
        }

        // the author's three lines; one is chosen by the spell, so the same spell always says the same
        private static readonly string[] Quips =
        {
            "So, does it eat or does it get eaten?",
            "Life isn't a vegetarian, you know. Well, it could be, but not here.",
            "Oh, that's some good waste of logic there! Did you forget how life works? Go and read about it before you realise you're too are alive.",
        };
        private static string Quip(SpellCircle root) => Quips[(int)((uint)root.All().Sum(c => c.OwnRunes.Count() * 7 + (c.element?.shortHash ?? 0)) % Quips.Length)];

        /// <summary>Arlie on the grammar of Life: a verb or a beam not tied to Withering or Restoration.</summary>
        public static IEnumerable<KeyValuePair<string, string>> Warnings(SpellCircle root)
        {
            if (root == null) yield break;
            var untied = Links(root).SelectMany(g => { Read(g, out var u); return u; }).Distinct().ToList();
            if (untied.Count > 0)
                yield return new KeyValuePair<string, string>("Untied Verb of Life", "Arlie: \"" + Quip(root) + "\"\n"
                    + string.Join(", ", untied.Select(k => k.ToString())) + " here is not tied to Withering or Restoration, and does nothing. In Life every verb and every beam is written with one of the two: Release + Withering, Beam + Restoration.");
        }

        // ------------------------------------------------------------------ a lost hold: death strikes the mage, life overgrows the place

        public static void Slip(Pawn caster, SpellCircle root)
        {
            var lifes = Links(root).ToList();
            bool wraps = Circles(root).Any(g => g.inner.Any(k => k.element != null));
            if ((lifes.Count == 0 && !wraps) || caster == null || !caster.Spawned) return;
            bool death = lifes.Any(g => Read(g, out _).Any(a => a.side == RuneKind.Drain));
            bool life = wraps || lifes.Any(g => Read(g, out _).Any(a => a.side == RuneKind.Restore));
            Map map = caster.Map;
            if (death)
            {
                // the Withering turns on the one who held it: the mage dries, and the store spills away
                caster.TakeDamage(new DamageInfo(WitherDmg, 10f, 2f, -1f, caster));
                Hediff_ArcaneCore core = Mages.Core(caster);
                if (core != null) foreach (RuneKind k in Circles(root).Select(g => PairOf(root, g)).Distinct()) core.SetStore(k, core.Store(k) * 0.5f);
                for (int i = 0; i < 5; i++) Wisp(caster.DrawPos + new Vector3(Rand.Range(-1f, 1f), 0f, Rand.Range(-1f, 1f)), map);
                Messages.Message(caster.LabelShortCap + " lost the hold on a Withering: it drank from them.", caster, MessageTypeDefOf.NegativeEvent, false);
            }
            if (life)
            {
                // the Restoration breaks loose: the place overgrows - thick growth, vines on everyone, bark on the mage
                ThingDef grass = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_TallGrass") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Grass");
                foreach (IntVec3 c in GenRadial.RadialCellsAround(caster.Position, 4.5f, true).Where(c => c.InBounds(map)))
                {
                    Plant pl = c.GetPlant(map);
                    if (pl != null) pl.Growth = 1f;
                    else if (grass != null && Rand.Chance(0.5f) && c.GetEdifice(map) == null && map.terrainGrid.TerrainAt(c).fertility > 0f && c.GetFirstItem(map) == null)
                    { Plant np = (Plant)GenSpawn.Spawn(grass, c, map); np.Growth = Rand.Range(0.6f, 1f); }
                }
                foreach (Pawn p in Area(caster.Position, 4.5f, map).Where(Living).ToList()) MagicFx.Give(p, H("Avir_Entangled"), 360);
                HediffDef bark = H("Avir_LifeOverflow");
                if (bark != null) { Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(bark) ?? caster.health.AddHediff(bark); h.Severity = Mathf.Min(1f, h.Severity + 0.3f); }
                Messages.Message(caster.LabelShortCap + " lost the hold on a Restoration: the place overgrows.", caster, MessageTypeDefOf.NegativeEvent, false);
            }
        }

        // ------------------------------------------------------------------ helpers

        public static bool Hurt(Pawn p) => p.health.hediffSet.hediffs.Any(h => h is Hediff_Injury i && !i.IsPermanent()) || p.health.hediffSet.BleedRateTotal > 0f;

        public static void HealInjuries(Pawn p, float amount)
        {
            Desiccation.Undo(p, amount * 0.02f);   // magic healing turns the drying back (Fix200)
            float left = amount;
            foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => !i.IsPermanent()).OrderByDescending(i => i.Bleeding ? 1 : 0).ThenByDescending(i => i.Severity).ToList())
            {
                if (left <= 0f) break;
                float h = Mathf.Min(left, inj.Severity); inj.Heal(h); left -= h;
            }
        }

        /// <summary>The regrowth deepens with every mending (Avir_LifeRegen; like the Kharassian vapour, only quicker).</summary>
        public static void AddRegen(Pawn p, float amount)
        {
            if (Regen == null || amount <= 0f) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(Regen) ?? p.health.AddHediff(Regen);
            h.Severity = Mathf.Min(1.5f, (h.Severity <= 0.011f ? 0f : h.Severity) + amount);
        }

        // ------------------------------------------------------------------ the store spilling over, and its slow fading

        /// <summary>Overflow: what the store cannot hold breaks out round the mage - the grass bursts, every wound near
        /// closes a little (the foes' too), and bark and moss creep over the mage's hands.</summary>
        public static void Overflow(Pawn caster, float excess)
        {
            if (caster == null || !caster.Spawned || excess <= 0.5f) return;
            Map map = caster.Map;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(caster.Position, 3.5f, true).Where(c => c.InBounds(map)))
            {
                Plant pl = c.GetPlant(map);
                if (pl != null && pl.LifeStage == PlantLifeStage.Growing) pl.Growth = Mathf.Min(1f, pl.Growth + 0.02f * excess);
            }
            foreach (Pawn p in GenRadial.RadialDistinctThingsAround(caster.Position, map, 3.5f, true).OfType<Pawn>().Where(Living).ToList())
                HealInjuries(p, 0.5f * excess);
            HediffDef bark = H("Avir_LifeOverflow");
            if (bark != null)
            {
                Hediff h = caster.health.hediffSet.GetFirstHediffOfDef(bark) ?? caster.health.AddHediff(bark);
                h.Severity = Mathf.Min(1f, h.Severity + 0.02f * excess);
            }
            for (int i = 0; i < 6; i++) Wisp(caster.DrawPos + new Vector3(Rand.Range(-2f, 2f), 0f, Rand.Range(-2f, 2f)), map);
            MoteMaker.ThrowText(caster.DrawPos, map, "Overflow", Necrotic, 3f);
        }

        /// <summary>The store fades slowly, and what fades goes out of the mage: their own wounds close, the plants by
        /// them green, and above half the store their body fights sickness quicker (Avir_VitaFlow).</summary>
        public static void Fade(Hediff_ArcaneCore core, float hours)
        {
            Pawn p = core.pawn;
            HediffDef flowDef = H("Avir_VitaFlow");
            Hediff flow = flowDef != null ? p.health.hediffSet.GetFirstHediffOfDef(flowDef) : null;
            if (core.StoreAll <= 0.01f) { core.vita = 0f; core.stores.Clear(); if (flow != null) p.health.RemoveHediff(flow); return; }
            // every store fades by itself; what fades from all of them goes out of the mage together
            float gone = 0f, fill = 0f;
            foreach (RuneKind k in new[] { None }.Concat(core.stores.Keys.ToList()))
            {
                float v = core.Store(k), g0 = v * 0.02f * hours;
                core.SetStore(k, v - g0); gone += g0;
                fill = Mathf.Max(fill, core.Store(k) / Mathf.Max(1f, core.VitaMax));
            }
            // life not given anywhere grows on the one who holds it (the druids of the Grove, eating the chimeras): above
            // three quarters of a store, bark and moss creep over the mage
            if (fill > 0.75f)
            {
                HediffDef bark = H("Avir_LifeOverflow");
                if (bark != null) { Hediff h = p.health.hediffSet.GetFirstHediffOfDef(bark) ?? p.health.AddHediff(bark); h.Severity = Mathf.Min(1f, h.Severity + 0.16f * (fill - 0.75f) * hours); }
            }
            if (Living(p)) HealInjuries(p, gone * 0.5f);
            if (p.Spawned)
                foreach (IntVec3 c in GenRadial.RadialCellsAround(p.Position, 2.9f, true).Where(c => c.InBounds(p.Map)).InRandomOrder().Take(3))
                {
                    Plant pl = c.GetPlant(p.Map);
                    if (pl != null && pl.LifeStage == PlantLifeStage.Growing) pl.Growth = Mathf.Min(1f, pl.Growth + 0.01f * gone);
                }
            if (flowDef != null)
            {
                if (flow == null) flow = p.health.AddHediff(flowDef);
                flow.Severity = Mathf.Clamp(fill, 0.01f, 1f);
            }
        }
    }

    // ================================================================== the regrowth (after the Kharassian sahrumet, quicker)

    public class HediffCompProperties_LifeRegen : HediffCompProperties
    {
        public float daysPerLayer = 1.5f, daysPerOrgan = 15f, daysPerChronicCure = 5f, scarHealFractionPerDay = 0.35f;
        public float scarsFrom = 0.5f, saturatedFrom = 1f, fadePerDay = 0.25f;
        public HediffCompProperties_LifeRegen() { compClass = typeof(HediffComp_LifeRegen); }
    }

    /// <summary>What Life's mending leaves behind. Its depth grows with every mending and fades a quarter a day. Light, it
    /// quickens healing (its stages); deep (0.5+), it walks scars down to nothing; saturated (1+), it brings back what was
    /// lost - an outer part a layer at a time (the parent before its children), the organs all at once on a long clock,
    /// chronic ailments on a shorter one. The logic follows Kharassian.HediffComp_LimbRegrowth, kept here in Avir (the
    /// Kharassians will lean on Avir, not the other way round).</summary>
    public class HediffComp_LifeRegen : HediffComp
    {
        private float layerProgress, organProgress, chronicProgress;
        private int check = Rand.Range(0, 250);
        public HediffCompProperties_LifeRegen Props => (HediffCompProperties_LifeRegen)props;

        public override string CompTipStringExtra => parent.Severity >= Props.saturatedFrom
            ? "Next part: " + (layerProgress * 100f).ToString("F0") + "%\nOrgans: " + (organProgress * 100f).ToString("F0") + "%\nChronic ailments: " + (chronicProgress * 100f).ToString("F0") + "%"
            : parent.Severity >= Props.scarsFrom ? "Old scars are fading." : null;

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            base.CompPostTickInterval(ref severityAdjustment, delta);
            check -= delta;
            if (check > 0) return;
            int dt = 250 - check; check = 250;
            Pawn p = parent.pawn;
            if (p == null || p.Dead) return;
            float days = dt / (float)GenDate.TicksPerDay;
            severityAdjustment -= Props.fadePerDay * days;
            float sev = parent.Severity;
            if (sev >= Props.scarsFrom) MendScars(p, days * (sev >= Props.saturatedFrom ? 1.5f : 1f));
            if (sev < Props.saturatedFrom) return;
            layerProgress += days / Props.daysPerLayer;
            organProgress += days / Props.daysPerOrgan;
            chronicProgress += days / Props.daysPerChronicCure;
            if (layerProgress >= 1f)
            {
                var parts = Missing(p, x => x.depth != BodyPartDepth.Inside || x.def.IsSolid(x, p.health.hediffSet.hediffs));
                if (parts.Count > 0) { layerProgress = 0f; foreach (BodyPartRecord x in parts) RestoreBare(p, x); Note(p, "grown back " + string.Join(", ", parts.Select(x => x.Label))); }
                else layerProgress = 1f;
            }
            if (organProgress >= 1f)
            {
                var organs = Missing(p, x => x.depth == BodyPartDepth.Inside && !x.def.IsSolid(x, p.health.hediffSet.hediffs));
                if (organs.Count > 0) { organProgress = 0f; foreach (BodyPartRecord x in organs) RestoreBare(p, x); Note(p, "grown back the organs"); }
                else organProgress = 1f;
            }
            if (chronicProgress >= 1f)
            {
                var chronic = p.health.hediffSet.hediffs.Where(h => h.def.chronic).ToList();
                if (chronic.Count > 0) { chronicProgress = 0f; foreach (Hediff h in chronic) p.health.RemoveHediff(h); Note(p, "been rid of " + string.Join(", ", chronic.Select(h => h.LabelBase))); }
                else chronicProgress = 1f;
            }
        }

        private void MendScars(Pawn p, float days)
        {
            foreach (Hediff_Injury scar in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.IsPermanent() && i.Part != null).ToList())
            {
                scar.Severity -= scar.Part.def.GetMaxHealth(p) * Props.scarHealFractionPerDay * days;
                if (scar.Severity <= 0.001f && p.health.hediffSet.hediffs.Contains(scar)) p.health.RemoveHediff(scar);
            }
        }

        private static List<BodyPartRecord> Missing(Pawn p, System.Func<BodyPartRecord, bool> accept)
        {
            var res = new List<BodyPartRecord>();
            foreach (Hediff_MissingPart m in p.health.hediffSet.hediffs.OfType<Hediff_MissingPart>())
            {
                if (m.Part == null || !accept(m.Part) || res.Contains(m.Part)) continue;
                bool ancestorGone = false;
                for (BodyPartRecord a = m.Part.parent; a != null; a = a.parent) if (p.health.hediffSet.PartIsMissing(a)) { ancestorGone = true; break; }
                if (!ancestorGone) res.Add(m.Part);
            }
            return res;
        }

        private static void RestoreBare(Pawn p, BodyPartRecord part)
        {
            p.health.RestorePart(part);
            if (part.parts == null) return;
            foreach (BodyPartRecord child in part.parts)
            {
                if (p.health.hediffSet.PartIsMissing(child)) continue;
                var miss = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, p, child);
                miss.IsFresh = false;
                p.health.AddHediff(miss, child);
            }
        }

        private static void Note(Pawn p, string what)
        {
            if (PawnUtility.ShouldSendNotificationAbout(p)) Messages.Message(p.LabelShortCap + " has " + what + ": life's regrowth.", p, MessageTypeDefOf.PositiveEvent, false);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref layerProgress, "lifeLayer");
            Scribe_Values.Look(ref organProgress, "lifeOrgan");
            Scribe_Values.Look(ref chronicProgress, "lifeChronic");
        }
    }

    // ================================================================== the store on the screen

    public class Gizmo_Vita : Gizmo
    {
        public Hediff_ArcaneCore core;
        private static readonly Texture2D Fill = SolidColorMaterials.NewSolidColorTexture(new Color(0.55f, 0.72f, 0.46f));
        private static readonly Texture2D Empty = SolidColorMaterials.NewSolidColorTexture(new Color(0.08f, 0.1f, 0.08f));
        private static Texture2D art; private static bool looked;
        public Gizmo_Vita() { Order = -100.5f; }
        public override float GetWidth(float maxWidth) => 150f;

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            if (!looked) { looked = true; art = ContentFinder<Texture2D>.Get("UI/Magic/Scale_Life", false); }
            Rect r = new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f);
            Rect bar;
            if (art != null)
            {
                // the plaque has the Arcana plaque's layout: a title band above, a window for the bar below (13-87% across, 59-81% down)
                bar = new Rect(r.x + r.width * 0.13f, r.y + r.height * 0.59f, r.width * 0.74f, r.height * 0.22f);
                Widgets.FillableBar(bar, Mathf.Clamp01(core.vita / Mathf.Max(1f, core.VitaMax)), Fill, Empty, false);
                GUI.DrawTexture(r, art);
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = new Color(0.9f, 0.95f, 0.85f);
                Widgets.Label(new Rect(r.x, r.y + r.height * 0.33f, r.width, r.height * 0.2f), "Life");
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                Widgets.DrawWindowBackground(r);
                Rect inner = r.ContractedBy(5f);
                Text.Font = GameFont.Small;
                GUI.color = LifeMagic.Necrotic; Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), "Life"); GUI.color = Color.white;
                bar = new Rect(inner.x, inner.y + 28f, inner.width, inner.height - 28f);
                Widgets.FillableBar(bar, Mathf.Clamp01(core.vita / Mathf.Max(1f, core.VitaMax)), Fill, Empty, false);
            }
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(bar, Mathf.FloorToInt(core.vita) + " / " + Mathf.FloorToInt(core.VitaMax));
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(r, "The store of life: what Withering took, ready to be given. Life makes nothing - it only moves (Symbiosis): no spell of Life gives anything with the store empty. It fades by 2% an hour, and what fades mends the mage and greens the plants by them. Past the top it spills over (Overflow).");
            return new GizmoResult(GizmoState.Clear);
        }
    }


    // ================================================================== Emerald: life sealed in stone

    public class CompProperties_StoredLife : CompProperties
    {
        public float max = 200f;
        public CompProperties_StoredLife() { compClass = typeof(CompStoredLife); }
    }

    /// <summary>An emerald of life ("Masks": leaf - stone - an emerald full of life): the store of life in a thing. It is
    /// carried and given: a Restoration of the Emerald draws on the stones near the spot and on the mage first.</summary>
    public class CompStoredLife : ThingComp
    {
        public float life;
        public CompProperties_StoredLife Props => (CompProperties_StoredLife)props;
        public override string CompInspectStringExtra() => "Life held: " + life.ToString("0") + " / " + Props.max.ToString("0");
        public override string TransformLabel(string label) => label + " (" + life.ToString("0") + ")";
        public override void PostExposeData() { base.PostExposeData(); Scribe_Values.Look(ref life, "life"); }
    }

    public static class Emerald
    {
        public static ThingDef Def => DefDatabase<ThingDef>.GetNamedSilentFail("Avir_EmeraldOfLife");

        /// <summary>Seals life in an emerald at the spot - the one already lying there, if it has room, else a new one.</summary>
        public static void Charge(IntVec3 at, Map map, float amount)
        {
            if (Def == null || map == null) return;
            while (amount > 0.5f)
            {
                CompStoredLife c = GenRadial.RadialDistinctThingsAround(at, map, 2.9f, true).Where(t => t.def == Def).Select(t => t.TryGetComp<CompStoredLife>()).FirstOrDefault(x => x != null && x.life < x.Props.max);
                if (c == null) { Thing t = ThingMaker.MakeThing(Def); GenPlace.TryPlaceThing(t, at, map, ThingPlaceMode.Near); c = t.TryGetComp<CompStoredLife>(); if (c == null) return; }
                float put = Mathf.Min(amount, c.Props.max - c.life); c.life += put; amount -= put;
                FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 1f, new Color(0.2f, 0.7f, 0.4f, 0.6f));
            }
        }

        /// <summary>Draws life out of the emeralds on the mage and near the spot; a stone emptied crumbles.</summary>
        public static float Draw(Pawn mage, IntVec3 at, float need)
        {
            float got = 0f;
            var stones = new List<Thing>();
            if (mage?.inventory?.innerContainer != null) stones.AddRange(mage.inventory.innerContainer.Where(t => t.def == Def));
            if (mage?.Map != null && at.IsValid) stones.AddRange(GenRadial.RadialDistinctThingsAround(at, mage.Map, 4.9f, true).Where(t => t.def == Def));
            foreach (Thing t in stones)
            {
                if (got >= need) break;
                CompStoredLife c = t.TryGetComp<CompStoredLife>();
                if (c == null) continue;
                float take = Mathf.Min(c.life, need - got); c.life -= take; got += take;
                if (c.life <= 0.5f && !t.Destroyed) t.Destroy();
            }
            return got;
        }
    }

    // ================================================================== Pestilence: sickness carried as fuel

    /// <summary>A mage of Pestilence carries sicknesses (the author): real ones - but what they would grow by goes into
    /// the store of life instead, so they never come to anything; the body learns them at a quarter of the usual pace,
    /// and they do the mage no harm at all (their stages are not felt).</summary>
    public static class Pestilence
    {
        /// <summary>The carried sicknesses of every pawn (their hediffs' load ids), for the patch below.</summary>
        public static readonly HashSet<int> Held = new HashSet<int>();
        private static readonly Dictionary<int, float> lastSev = new Dictionary<int, float>(), lastImm = new Dictionary<int, float>();
        private static readonly HediffStage Nothing = new HediffStage();

        public static bool Sickness(Hediff h) => h != null && h.TryGetComp<HediffComp_Immunizable>() != null && h.def.isBad && !(h is Hediff_Injury);

        public static void Carry(Hediff_ArcaneCore core, Hediff h)
        {
            if (core == null || h == null) return;
            if (!core.carried.Contains(h.loadID)) core.carried.Add(h.loadID);
            Held.Add(h.loadID); lastSev[h.loadID] = h.Severity;
        }

        /// <summary>A sickness taken off one's own onto the mage - the healed is cured of it.</summary>
        public static void Gather(Pawn mage, Pawn from)
        {
            Hediff_ArcaneCore core = Mages.Core(mage);
            if (core == null) return;
            foreach (Hediff h in from.health.hediffSet.hediffs.Where(Sickness).ToList())
            {
                Hediff mine = mage.health.hediffSet.GetFirstHediffOfDef(h.def);
                if (mine == null) { mine = HediffMaker.MakeHediff(h.def, mage); mine.Severity = h.Severity; mage.health.AddHediff(mine); }
                else mine.Severity = Mathf.Max(mine.Severity, h.Severity);
                Carry(core, mine);
                from.health.RemoveHediff(h);
                Messages.Message(mage.LabelShortCap + " took " + h.LabelBase + " from " + from.LabelShort + ".", from, MessageTypeDefOf.PositiveEvent, false);
            }
        }

        /// <summary>The sicknesses a mage of Pestilence can grow on themselves (Fix160).</summary>
        private static readonly string[] Sowable = { "Flu", "Malaria", "SleepingSickness", "Plague", "LungRot", "Animal_Flu", "Animal_Plague" };
        /// <summary>The heaviest bodily illnesses - a master can cure them, not take them, only grow them on themselves.</summary>
        private static readonly string[] Heavy = { "Carcinoma", "Dementia", "Alzheimers", "Asthma", "HeartArteryBlockage", "BadBack", "Frail", "Cataract", "HearingLoss" };
        public static bool Master(Pawn mage) { Hediff_ArcaneCore c = Mages.Core(mage); return c != null && Arcane.Stage(c, RuneKind.Life, RuneKind.Air) >= 4; }

        /// <summary>Withering of Pestilence (the author, Fix160): the sicknesses of the reached are taken onto the mage and
        /// carried; at mastery the heaviest illnesses (cancer and the like) are cured outright - not taken, they cannot be.</summary>
        public static float TakeFrom(Pawn mage, Pawn from, float dmg)
        {
            if (from == null || from.Dead || Mages.Core(mage) == null) return 0f;
            float got = 0f;
            int had = from.health.hediffSet.hediffs.Count(Sickness);
            if (had > 0) { got += 15f * had; Gather(mage, from); }
            if (Master(mage))
                foreach (Hediff h in from.health.hediffSet.hediffs.Where(h => Heavy.Contains(h.def.defName)).ToList())
                {
                    got += 25f;
                    from.health.RemoveHediff(h);
                    Messages.Message(mage.LabelShortCap + " drew " + h.LabelBase + " out of " + from.LabelShort + ".", from, MessageTypeDefOf.PositiveEvent, false);
                }
            return got;
        }

        /// <summary>Growth of Pestilence at the mage's own feet (the author, Fix160): sicknesses grown on oneself, more of them
        /// the stronger the spell (one, and one more for each 0.4 of strength past 0.8); a master can grow the heaviest too.</summary>
        public static void Sow(Pawn mage, float power)
        {
            Hediff_ArcaneCore core = Mages.Core(mage);
            if (core == null || mage.Dead) return;
            int n = Mathf.Clamp(1 + Mathf.FloorToInt((power - 0.8f) / 0.4f), 1, 4);
            var pool = Sowable.Select(x => DefDatabase<HediffDef>.GetNamedSilentFail(x)).Where(d => d != null).ToList();
            if (Master(mage)) pool.AddRange(Heavy.Take(1).Select(x => DefDatabase<HediffDef>.GetNamedSilentFail(x)).Where(d => d != null));
            pool = pool.Where(d => mage.health.hediffSet.GetFirstHediffOfDef(d) == null && (d.defName.StartsWith("Animal_") == !mage.RaceProps.Humanlike)).InRandomOrder().ToList();
            var grown = new List<string>();
            foreach (HediffDef d in pool.Take(n))
            {
                Hediff h = HediffMaker.MakeHediff(d, mage);
                h.Severity = Mathf.Max(0.05f, d.initialSeverity);
                mage.health.AddHediff(h);
                Carry(core, h);
                grown.Add(h.LabelBase);
            }
            if (grown.Count > 0) Messages.Message(mage.LabelShortCap + " grew " + string.Join(", ", grown) + " in themselves - carried, as fuel.", mage, MessageTypeDefOf.NeutralEvent, false);
            else Messages.Message(mage.LabelShortCap + " carries every sickness they can grow already.", mage, MessageTypeDefOf.RejectInput, false);
        }

        /// <summary>A blow of a small circle of air in a box of Pestilence carries a sickness (Fix197): one of the sowable,
        /// lightly, if the struck has it not.</summary>
        public static void SowOn(Pawn p, Pawn by, float severity)
        {
            if (p == null || p.Dead || !Rand.Chance(0.5f)) return;
            HediffDef d = Sowable.Select(x => DefDatabase<HediffDef>.GetNamedSilentFail(x)).Where(x => x != null && (x.defName.StartsWith("Animal_") == !p.RaceProps.Humanlike) && p.health.hediffSet.GetFirstHediffOfDef(x) == null).RandomElementWithFallback();
            if (d == null) return;
            Hediff h = HediffMaker.MakeHediff(d, p); h.Severity = Mathf.Max(severity, d.initialSeverity);
            p.health.AddHediff(h);
            if (p.Spawned) MapComponent_SpellSprites.PutTinted(p.Map, "Life/PoisonFog", LifeMagic.PairColor(RuneKind.Air), p.DrawPos, 1f, 50);
        }

        /// <summary>A sickness grown on the mage on purpose: the flu, if nothing is carried yet.</summary>
        public static void GrowOn(Pawn mage)
        {
            Hediff_ArcaneCore core = Mages.Core(mage);
            if (core == null || Carried(mage) != null) return;
            HediffDef flu = DefDatabase<HediffDef>.GetNamedSilentFail("Flu");
            if (flu == null) return;
            Hediff h = mage.health.AddHediff(flu);
            h.Severity = 0.05f;
            Carry(core, h);
        }

        public static HediffDef Carried(Pawn mage)
        {
            Hediff_ArcaneCore core = Mages.Core(mage);
            return core == null ? null : mage.health.hediffSet.hediffs.FirstOrDefault(h => core.carried.Contains(h.loadID))?.def;
        }

        /// <summary>Every few seconds: what a carried sickness grew by goes into the store, and the body learns it at a quarter.</summary>
        public static void Tick(Hediff_ArcaneCore core)
        {
            if (core.carried.Count == 0) return;
            Pawn p = core.pawn;
            foreach (int id in core.carried.ToList())
            {
                Hediff h = p.health.hediffSet.hediffs.FirstOrDefault(x => x.loadID == id);
                if (h == null) { core.carried.Remove(id); Held.Remove(id); lastSev.Remove(id); lastImm.Remove(id); continue; }
                Held.Add(id);
                if (!lastSev.TryGetValue(id, out float ls)) ls = h.Severity;
                if (h.Severity > ls) { core.AddTo(RuneKind.Air, (h.Severity - ls) * 60f); h.Severity = ls; }
                lastSev[id] = h.Severity;
                ImmunityRecord rec = p.health.immunity.GetImmunityRecord(h.def);
                if (rec != null)
                {
                    if (lastImm.TryGetValue(id, out float li) && rec.immunity > li) rec.immunity = li + (rec.immunity - li) * 0.25f;
                    lastImm[id] = rec.immunity;
                }
            }
        }

        public static HediffStage NoStage => Nothing;
    }

    [HarmonyPatch(typeof(Hediff), "get_CurStage")]
    public static class Patch_PestilenceUnfelt
    {
        public static void Postfix(Hediff __instance, ref HediffStage __result)
        {
            if (Pestilence.Held.Count > 0 && __instance != null && Pestilence.Held.Contains(__instance.loadID)) __result = Pestilence.NoStage;
        }
    }

    // ================================================================== developer mode

    public static class LifeDev
    {
        private const string Cat = "Magic";

        [DebugAction(Cat, "Life: a store full / empty (Life, Wild Flame, Blood, Emerald, Pestilence)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Store(Pawn p)
        {
            Hediff_ArcaneCore c = Mages.Core(p); if (c == null) return;
            var opts = new[] { LifeMagic.None, RuneKind.Fire, RuneKind.Water, RuneKind.Earth, RuneKind.Air }.Select(k => new DebugMenuOption((LifeMagic.PairName(k) ?? "Life") + " - full / empty", DebugMenuOptionMode.Action,
                () => { c.SetStore(k, c.Store(k) > 1f ? 0f : c.VitaMax); Messages.Message(p.LabelShort + ": store of " + (LifeMagic.PairName(k) ?? "Life") + " " + c.Store(k).ToString("0") + " / " + c.VitaMax.ToString("0"), MessageTypeDefOf.NeutralEvent, false); })).ToList();
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        [DebugAction(Cat, "Life: price of a meeting (Wild Flame, Blood, Emerald, Pestilence)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Price(Pawn p)
        {
            var opts = new[] { RuneKind.Fire, RuneKind.Water, RuneKind.Earth, RuneKind.Air }
                .Select(k => new DebugMenuOption(LifeMagic.PairName(k) + " - " + LifeMagic.PairPrice(k), DebugMenuOptionMode.Action, () => LifeMagic.PayPair(k, p))).ToList();
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(opts));
        }

        [DebugAction(Cat, "Life: overflow now (+50 past the top)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Over(Pawn p) { Hediff_ArcaneCore c = Mages.Core(p); if (c == null) return; c.vita = c.VitaMax; c.AddVita(50f); }

        [DebugAction(Cat, "Life: carry the flu (Pestilence)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Flu(Pawn p) => Pestilence.GrowOn(p);

        [DebugAction(Cat, "Life: emerald with 100 life here", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Gem() => Emerald.Charge(Verse.UI.MouseCell(), Find.CurrentMap, 100f);

        [DebugAction(Cat, "Life: regrowth saturated (1.2)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Sat(Pawn p) => LifeMagic.AddRegen(p, 1.2f);

        [DebugAction(Cat, "Life: cut off a hand and scar (regrowth test)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Cut(Pawn p)
        {
            BodyPartRecord hand = p.health.hediffSet.GetNotMissingParts().FirstOrDefault(x => x.def.defName == "Hand");
            if (hand != null) { var m = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, p, hand); m.IsFresh = false; p.health.AddHediff(m, hand); }
        }

        [DebugAction(Cat, "Life: test spells into this caster's book", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Tests(Pawn p)
        {
            Hediff_ArcaneCore core = Mages.Core(p);
            if (core == null) return;
            RuneDef R(string n) => DefDatabase<RuneDef>.GetNamedSilentFail("Avir_Rune_" + n);
            RitualSchema Spell(string name, string life, params string[] smalls)
            {
                var root = new SpellCircle { binding = true };
                var g = new SpellCircle { element = R("Life") };
                foreach (string sec in life.Split('|'))
                {
                    var runes = sec.Split(',').Select(x => R(x.Trim())).Where(x => x != null).ToList();
                    if (runes.Count > 0) g.sections.Add(new RitualRing { runes = runes, slots = Mathf.Max(1, runes.Count) });
                }
                foreach (string c in smalls)
                {
                    string[] parts = c.Split(':');
                    var k = new SpellCircle { element = R(parts[0]) };
                    foreach (string sec in parts[1].Split('|')) { var runes = sec.Split(',').Select(x => R(x.Trim())).Where(x => x != null).ToList(); k.sections.Add(new RitualRing { runes = runes, slots = Mathf.Max(1, runes.Count) }); }
                    g.inner.Add(k);
                }
                root.inner.Add(g);
                return new RitualSchema { name = name, weave = root };
            }
            var list = new List<RitualSchema>
            {
                Spell("TEST Life: Withering at the point", "Drain"),
                Spell("TEST Life: burst of withering, foes only", "Release,Drain,Breadth|Barrier,Barrier"),
                Spell("TEST Life: withering beam (hits everyone on the line)", "Beam,Drain"),
                Spell("TEST Life: drink the foes, mend own (leap)", "Release,Drain,Breadth|Reach,Thread,Release,Restore"),
                Spell("TEST Life: burst of mending, own only", "Release,Restore,Breadth|Barrier,Barrier,Thread"),
                Spell("TEST Life: mending beam", "Beam,Restore"),
                Spell("TEST Life: cocoon", "Hold,Restore"),
                Spell("TEST Life: draw from the green only", "Growth,Drain,Breadth"),
                Spell("TEST Life: untied Release (Arlie)", "Release,Breadth"),
                Spell("TEST Life round fire (mends whom it touches)", "", "Fire:Release"),
                Spell("TEST Life box: beam drinks the foes, then a burst mends own", "", "None:Beam,Drain|Barrier,Barrier", "None:Release,Restore,Breadth|Barrier,Barrier,Thread"),
                Spell("TEST Life box: fire inside first, then Life's own runes", "Release,Drain,Breadth|Barrier,Barrier", "Fire:Release"),
                Spell("TEST Wild Flame: living bombs in a crowd (foes only)", "Release,Drain,Breadth|Barrier,Barrier", "Fire:Release", "Fire:Release"),
                Spell("TEST Wild Flame: bushes that burst", "Growth,Drain,Breadth", "Fire:Release", "Fire:Release"),
                Spell("TEST Wild Flame: set them all off", "Purge,Drain,Breadth", "Fire:Release", "Fire:Release"),
                Spell("TEST Wild Flame: smouldering hearts (own only)", "Release,Restore,Breadth|Barrier,Barrier,Thread", "Fire:Release", "Fire:Release"),
                Spell("TEST Blood: drink and paralyse", "Release,Drain,Breadth|Barrier,Barrier", "Water:Hold", "Water:Hold"),
                Spell("TEST Blood: hold the blood still", "Hold,Drain", "Water:Hold", "Water:Hold"),
                Spell("TEST Blood: throw up / press down", "Lift,Drain|Lift,Thread,Drain", "Water:Hold", "Water:Hold"),
                Spell("TEST Blood: drag a foe by its blood", "Step,Drain", "Water:Hold", "Water:Hold"),
                Spell("TEST Blood: give back the blood (own only)", "Release,Restore,Breadth|Barrier,Barrier,Thread", "Water:Hold", "Water:Hold"),
                Spell("TEST Emerald: stone wave", "Release,Drain,Breadth", "Earth:Hold", "Earth:Hold"),
                Spell("TEST Emerald: one to stone fast", "Hold,Drain,Amplify,Amplify", "Earth:Hold", "Earth:Hold"),
                Spell("TEST Emerald: shatter the crusts", "Purge,Drain,Breadth", "Earth:Hold", "Earth:Hold"),
                Spell("TEST Emerald: stone cocoon", "Hold,Restore", "Earth:Hold", "Earth:Hold"),
                Spell("TEST Pestilence: drink the sicknesses", "Release,Drain,Breadth", "Air:Release", "Air:Release"),
                Spell("TEST Pestilence: fertile poison fog", "Release,Restore,Breadth", "Air:Release", "Air:Release"),
                Spell("TEST Pestilence: lingering fog", "Hold,Restore", "Air:Release", "Air:Release"),
                Spell("TEST Life aura: withering round the mage, foes only", "Aura,Anchor,Breadth|Release,Drain|Barrier,Barrier"),
                Spell("TEST Life aura: mending round the mage, own only", "Aura,Anchor|Release,Restore|Barrier,Barrier,Thread"),
                Spell("TEST Emerald aura: stone circle", "Aura,Anchor|Release,Drain|Barrier,Barrier", "Earth:Hold", "Earth:Hold"),
                Spell("TEST Life: the dead into fertile earth", "Release,Drain,Transmute,Breadth"),
                Spell("TEST Life: quicken the ground", "Release,Restore,Transmute,Breadth"),
                Spell("TEST Emerald: plants into emerald sculptures", "Release,Drain,Transmute,Breadth", "Earth:Hold", "Earth:Hold"),
                Spell("TEST Emerald: sculptures back to life", "Release,Restore,Transmute,Breadth", "Earth:Hold", "Earth:Hold"),
                Spell("TEST Blood: stone into water (for good)", "Release,Restore,Transmute,Breadth", "Water:Hold", "Water:Hold"),
                Spell("TEST Blood: water into stone (for good)", "Release,Drain,Transmute,Breadth", "Water:Hold", "Water:Hold"),
                Spell("TEST Warmth: chill the crowd, put out the fire (everyone)", "Release,Drain,Breadth", "Fire:Release"),
                Spell("TEST Warmth: torpor - the held sleep", "Hold,Drain", "Fire:Release"),
                Spell("TEST Warmth: warm our own", "Release,Restore,Breadth|Barrier,Barrier,Thread", "Fire:Release"),
                Spell("TEST Sap: dry the foes", "Release,Drain,Breadth|Barrier,Barrier", "Water:Release"),
                Spell("TEST Sap: water cocoon", "Hold,Restore", "Water:Release"),
                Spell("TEST Frame: soften and fell", "Hold,Drain", "Earth:Release"),
                Spell("TEST Frame: harden our own, splint", "Hold,Restore", "Earth:Release"),
                Spell("TEST Breath: take the breath (foes)", "Beam,Drain|Barrier,Barrier", "Air:Release"),
                Spell("TEST Breath: air bubble (own)", "Hold,Restore|Barrier,Barrier,Thread", "Air:Release"),
            };
            foreach (RitualSchema s in list) core.Book.spells.Add(s);
            Messages.Message(list.Count + " test spells of Life written into " + p.LabelShort + "'s book.", MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
