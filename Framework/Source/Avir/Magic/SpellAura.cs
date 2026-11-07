using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>The living aura (Магия-правила.md, "Аура"): an Aura with an Anchor does not strike once and fade - it
    /// stays round the caster and lets its circle's verbs out again and again, wherever the caster goes. What is written
    /// between the Aura and the first verb tunes the aura itself:
    ///   Anchor - how many times it strikes again (three for each);
    ///   Breadth - how far it reaches; but an aura widens hard: a cell for each, never past the edge a small circle can
    ///             hold, and each widening makes it strike more rarely (half as rarely again);
    ///   Amplify - the strength of each strike (as on any form).
    /// What is written after a verb works on that verb as usual. Each strike is half the strength of the cast; a blast
    /// within an aura is half as wide. The aura strikes everyone in it, the caster too - only a ward or Selective
    /// Protection on the circle keeps them, the same rule as for any blow. A circle of the same element further on in the
    /// same spell, with an Aura of its own, does not raise a second aura: it widens the first (and slows it) and adds its
    /// verbs to its strikes. It ends when its strikes are spent, or when the caster falls, dies or leaves the map; a new
    /// living aura of the same element raised by another spell takes the old one's place.
    /// Made for fire first; since Fix105 every element and every meeting of two lives so (with an Anchor) - without an
    /// Anchor each keeps its old field (water's tide, earth's and air's, the meetings' fields).</summary>
    public static class SpellAura
    {
        public const float PulseShare = 0.5f;
        public const int BaseInterval = 180;
        /// <summary>The widest a living aura of small circles reaches - the map is for the great circles.</summary>
        public const float SmallMax = 8f;
        public const float BaseRadius = 3.9f;
        public const int PerAnchor = 3;

        private static int field;
        /// <summary>An aura's blow is being dealt now (its first strike or any after): a ward of its own element lets it
        /// pass round the wearer untouched and unspent; a ward of another element takes it on its charge.</summary>
        public static bool Field => field > 0;
        private sealed class FieldScope : System.IDisposable { public void Dispose() { if (field > 0) field--; } }
        public static System.IDisposable InField(bool on = true) { if (!on) return null; field++; return new FieldScope(); }

        /// <summary>A strike of a living aura is being dealt now: it must not raise another.</summary>
        public static bool Pulsing;
        public static float PulseRadius;
        /// <summary>The spell being cast (its weave), so the circles of one spell feed one aura.</summary>
        public static object Spell;

        /// <summary>The elements whose aura lives: all of them, and every meeting of two.</summary>
        public static bool Lives(RuneDef el, ComboKind k) => k != ComboKind.None || el != null;

        /// <summary>A part's aura in the living way (pulsing now, or raising one): its radius; false - the old field.</summary>
        public static bool Living(RitualSchema part, RuneDef el, ComboKind k, out float radius)
        {
            radius = PulseRadius;
            if (Pulsing) return true;
            if (!Raises(part, el, k, out int b, out _)) return false;
            radius = Radius(b);
            return true;
        }

        public static float Radius(int breadth) => Mathf.Min(SmallMax, BaseRadius + breadth);
        public static int Interval(int breadth) => Mathf.RoundToInt(BaseInterval * Mathf.Pow(1.5f, breadth));
        public static int Strikes(int anchor) => PerAnchor * anchor;

        /// <summary>The radius an aura of fire strikes now: a living aura's own while it pulses; otherwise, a lone
        /// aura - as wide as before, a little over a point.</summary>
        public static float RadiusFor(RitualReading rf, float fallback) => Pulsing ? PulseRadius : fallback;

        /// <summary>Does this part raise a living aura (an Aura with an Anchor, and no Step - a strike does not step)?</summary>
        public static bool Raises(RitualSchema part, RuneDef el, ComboKind k, out int breadth, out int anchor) =>
            Shaped(part, el, k, out breadth, out anchor) && anchor > 0;

        /// <summary>Is this part an aura that could live (of an element that has it, with a verb, no Step)?</summary>
        public static bool Shaped(RitualSchema part, RuneDef el, ComboKind k, out int breadth, out int anchor)
        {
            breadth = anchor = 0;
            if (!Lives(el, k) || part?.rings == null) return false;
            var runes = part.rings.SelectMany(g => g.runes).Where(x => x != null).ToList();
            if (!runes.Any(x => x.kind == RuneKind.Aura) || runes.Any(x => x.kind == RuneKind.Step)) return false;
            if (!runes.Any(x => x.role == RuneRole.Verb)) return false;
            SpellMods.Own(part, RuneKind.Aura, out _, out breadth, out anchor, out _);
            return true;
        }

        /// <summary>Called as a part is cast: raises the living aura, or feeds the one this spell already raised.</summary>
        public static void Note(Pawn caster, RitualSchema part, ComboKind k, RuneDef el, float power)
        {
            if (Pulsing || caster?.Map == null || !Shaped(part, el, k, out int breadth, out int anchor)) return;
            var c = caster.Map.GetComponent<MapComponent_Auras>();
            if (c == null) return;
            int now = Find.TickManager.TicksGame;
            MapComponent_Auras.Aura old = c.all.FirstOrDefault(a => !a.beam && a.caster == caster && a.el == el && a.combo == k);
            if (old != null && old.spell != null && old.spell == Spell)
            {
                // a further circle of the same spell: the aura widens - and strikes more rarely for it
                old.breadth += breadth;
                old.left += Strikes(anchor);
                old.parts.Add(part.Copy());
                old.power = Mathf.Max(old.power, power);
                old.interval = Interval(old.breadth);
                old.radius = Radius(old.breadth);
                if (caster.Spawned) MoteMaker.ThrowText(caster.DrawPos, caster.Map, "Aura widened", 2f);
                Hediff_LivingAura.Show(caster);
                return;
            }
            if (anchor <= 0) return;   // no anchor and no aura of this spell to feed: it strikes once, as an aura always did
            if (old != null) c.all.Remove(old);
            var a = new MapComponent_Auras.Aura
            {
                caster = caster, el = el, combo = k, spell = Spell, breadth = breadth, left = Strikes(anchor), power = power,
                interval = Interval(breadth), radius = Radius(breadth), shaped = SpellSculpt.Active, aid = SpellSculpt.Allies
            };
            a.parts.Add(part.Copy());
            a.next = now + a.interval;
            c.all.Add(a);
            Hediff_LivingAura.Show(caster);
        }

        /// <summary>The streaming beam (the author's concept): a Beam with an Anchor of its own is not one stroke - it holds
        /// on its target and strikes along the line again every second, four times for each Anchor, each at half strength;
        /// it breaks when the target falls, goes out of sight or out of reach, or the caster falls or steps off the spot -
        /// a stream is held standing (the author). A healing thread so written is a stream of healing.</summary>
        public const int BeamInterval = 60, BeamPerAnchor = 4;

        public static void NoteBeam(Pawn caster, RitualSchema part, ComboKind k, RuneDef el, float power, LocalTargetInfo target)
        {
            if (Pulsing || caster?.Map == null || part?.rings == null || !target.IsValid) return;
            var runes = part.rings.SelectMany(g => g.runes).Where(x => x != null).ToList();
            SpellRoute.Pending route = SpellRoute.IsRoute(runes) ? SpellRoute.Take(caster) : null;
            if (!runes.Any(x => x.kind == RuneKind.Beam) || runes.Any(x => x.kind == RuneKind.Aura || x.kind == RuneKind.Step && route == null)) return;
            SpellMods.Own(part, RuneKind.Beam, out _, out _, out int anchor, out _);
            // the Anchor must be the beam's own (written after it), not the whole circle's
            SpellMods.Own(part, (RuneKind)(-1), out _, out _, out int wide, out _);   // no rune of that kind: the circle-wide only
            anchor -= wide;
            if (anchor <= 0) return;
            var c = caster.Map.GetComponent<MapComponent_Auras>();
            if (c == null) return;
            c.all.RemoveAll(a => a.beam && a.caster == caster);
            var b = new MapComponent_Auras.Aura
            {
                caster = caster, el = el, combo = k, spell = Spell, beam = true, target = target, left = BeamPerAnchor * anchor,
                power = power, interval = BeamInterval, shaped = SpellSculpt.Active, aid = SpellSculpt.Allies, stand = caster.Position
            };
            // the caster stands and holds it (the author): a wait as long as the stream, right after the cast
            Verse.AI.Job hold = JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture);
            hold.expiryInterval = b.left * b.interval + 30;
            caster.jobs?.jobQueue?.EnqueueFirst(hold);
            if (route != null)
            {
                // the route: the strike walks it at its pace - 2.5 cells a second, slower for every Anchor past the first,
                // quicker with Amplify; it strikes once a second all the way, and the caster stands till it is walked
                SpellMods.Own(part, RuneKind.Beam, out int amp, out _, out _, out _);
                b.route = route.marks; b.chain = route.chain;
                b.speed = 2.5f * Mathf.Pow(0.75f, Mathf.Max(0, anchor - 1)) * (1f + 0.2f * amp);
                b.left = Mathf.CeilToInt(SpellRoute.Length(b.route) / b.speed) + 1;
                b.target = new LocalTargetInfo(b.route[0]);
                hold.expiryInterval = b.left * b.interval + 30;
            }
            b.parts.Add(part.Copy());
            b.next = Find.TickManager.TicksGame + b.interval;
            c.all.Add(b);
            if (caster.Spawned) MoteMaker.ThrowText(caster.DrawPos, caster.Map, "Stream", 2f);
        }

        public static bool BeamHolds(MapComponent_Auras.Aura a)
        {
            if (!a.beam) return true;
            if (a.caster.Position != a.stand) return false;
            if (a.route != null) return true;   // a route is walked to its end (a wall on the way only stops a stroke)
            LocalTargetInfo t = a.target;
            if (t.HasThing && (t.Thing.Destroyed || !t.Thing.Spawned || t.Thing.Map != a.caster.Map || (t.Thing is Pawn tp && tp.Dead))) return false;
            IntVec3 at = t.Cell;
            if (!at.InBounds(a.caster.Map) || a.caster.Position.DistanceTo(at) > 40f) return false;
            return GenSight.LineOfSight(a.caster.Position, at, a.caster.Map, true);
        }

        /// <summary>The living aura round this pawn, if any.</summary>
        public static MapComponent_Auras.Aura Of(Pawn p) => p?.MapHeld?.GetComponent<MapComponent_Auras>()?.all.FirstOrDefault(a => a.caster == p && !a.beam);

        public static Color ColorOf(MapComponent_Auras.Aura a) =>
            a == null ? Color.white : a.combo != ComboKind.None ? Combos.ColorOf(a.combo) : a.el != null ? a.el.color : Color.white;
    }

    /// <summary>The living aura on the caster's health tab, in its element's colour: how many strikes it has left and
    /// how often it strikes. It goes when the aura goes.</summary>
    public class Hediff_LivingAura : HediffWithComps
    {
        private static HediffDef def_;
        public static HediffDef Def => def_ ?? (def_ = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_LivingAura"));

        public static void Show(Pawn p)
        {
            if (Def == null || p?.health == null) return;
            if (p.health.hediffSet.GetFirstHediffOfDef(Def) == null) p.health.AddHediff(Def);
        }

        private MapComponent_Auras.Aura Aura => SpellAura.Of(pawn);

        public override string Label
        {
            get
            {
                MapComponent_Auras.Aura a = Aura;
                string of = a == null ? "" : a.combo != ComboKind.None ? Combos.Label(a.combo) : a.el?.label;
                return of.NullOrEmpty() ? base.Label : "living aura of " + of;
            }
        }

        public override string LabelInBrackets
        {
            get
            {
                MapComponent_Auras.Aura a = Aura;
                if (a == null) return null;
                return a.left + " strikes left · every " + (a.interval / 60f).ToString("0.#") + " s";
            }
        }

        public override Color LabelColor => Aura != null ? SpellAura.ColorOf(Aura) : base.LabelColor;

        public override string TipStringExtra
        {
            get
            {
                MapComponent_Auras.Aura a = Aura;
                if (a == null) return base.TipStringExtra;
                return "Radius " + a.radius.ToString("0.#") + " · next strike in " + Mathf.Max(0f, (a.next - Find.TickManager.TicksGame) / 60f).ToString("0.#") + " s · each strike at "
                    + SpellAura.PulseShare.ToStringPercent() + " of the cast" + (a.aid ? " · only the caster's own (Selective Aid)" : a.shaped ? " · spares the caster's own" : " · strikes everyone in it, the caster too");
            }
        }

        public override bool ShouldRemove => Aura == null;
    }

    /// <summary>The living auras on a map. They are not kept in a save: an aura lasts seconds, not days.</summary>
    public class MapComponent_Auras : MapComponent
    {
        public class Aura
        {
            public Pawn caster;
            public RuneDef el;
            public ComboKind combo;
            public object spell;
            public List<RitualSchema> parts = new List<RitualSchema>();
            public int breadth, left, interval, next;
            public float power, radius;
            public bool shaped, aid;
            /// <summary>A living aura of Life: its circle (its acts pulse round the mage by Life's own grammar).</summary>
            public SpellCircle life; public RuneKind lifePair = (RuneKind)(-1), lifeAspect = (RuneKind)(-1); public LifeMix lifeMix;   // the box's meeting, all of it (Fix196)
            /// <summary>A streaming beam: it holds on its target instead of standing round the caster.</summary>
            public bool beam;
            public LocalTargetInfo target;
            public IntVec3 stand;   // a stream is held standing: the caster stepping off it lets it go
            // a beam on a route (Fix234): its marks, chained or swept from the caster, how far along it is, how fast it goes
            public List<IntVec3> route; public bool chain; public float along, speed; public int leg;
            public float flash;   // when it last struck (real time), for the flare of the ring
            public float seed = Rand.Range(0f, 360f);
        }

        private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        /// <summary>The ring of the aura: its own picture by the element (or what two elements made), else the
        /// element's; whatever it is, tinted by its colour.</summary>
        private static Material Mat(Aura a)
        {
            string key = a.combo != ComboKind.None ? a.combo.ToString() : a.el?.kind.ToString() ?? "Fire";
            // a second-order meeting: its own picture when drawn (Aura_Inferno...), else its pair's
            if (a.spell is SpellCircle root)
            {
                SecondKind y = SecondOrder.Of(root).Values.FirstOrDefault(v => SecondOrder.PairOf(v) == a.combo);
                if (y != SecondKind.None && ContentFinder<Texture2D>.Get("Things/Mote/Avir/Magic/Aura_" + y, false) != null) key = y.ToString();
            }
            if (mats.TryGetValue(key, out Material m)) return m;
            string path = "Things/Mote/Avir/Magic/Aura_" + key;
            // the rings are painted with their own light and alpha (Fix119): drawn as they are, not as added glow -
            // under glow the dark stones of earth and the mud of the bog would vanish
            if (ContentFinder<Texture2D>.Get(path, false) != null) m = MaterialPool.MatFrom(path, ShaderDatabase.Transparent, Color.white);
            else if (ContentFinder<Texture2D>.Get("Things/Mote/Avir/Magic/Aura_Base", false) != null)
                m = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Aura_Base", ShaderDatabase.Transparent, SpellAura.ColorOf(a));
            else
            {
                string parent = a.combo == ComboKind.Magma || a.combo == ComboKind.Haze ? "Fire" : a.combo == ComboKind.Storm || a.combo == ComboKind.Dust ? "Air" : a.combo == ComboKind.Bog ? "Earth" : a.combo == ComboKind.Steam ? "Water" : "Fire";
                m = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Aura_" + parent, ShaderDatabase.Transparent, SpellAura.ColorOf(a));
            }
            mats[key] = m;
            return m;
        }

        public override void MapComponentUpdate()
        {
            if (SpellRoute.Deferred != null && Find.CurrentMap == map) { System.Action d = SpellRoute.Deferred; SpellRoute.Deferred = null; d(); }   // Fix234
            if (all.Count == 0 || Find.CurrentMap != map) return;
            float t = Time.realtimeSinceStartup;
            foreach (Aura a in all)
            {
                if (a.caster == null || !a.caster.Spawned || a.beam) continue;
                Vector3 pos = a.caster.DrawPos;
                pos.y = AltitudeLayer.MoteLow.AltitudeFor();
                // breathing gently; it flares as it strikes
                float flare = Mathf.Clamp01(1f - (t - a.flash) / 0.6f);
                float size = (a.radius * 2f + 1f) * (1f + 0.02f * Mathf.Sin(t * 2.2f) + 0.08f * flare);
                var block = new MaterialPropertyBlock();
                Color c = Color.white; c.a = 0.55f + 0.1f * Mathf.Sin(t * 2.2f) + 0.45f * flare;
                block.SetColor(ShaderPropertyIDs.Color, c);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.AngleAxis((t * 9f + a.seed) % 360f, Vector3.up), new Vector3(size, 1f, size)), Mat(a), 0, null, 0, block);
            }
        }

        public readonly List<Aura> all = new List<Aura>();

        public MapComponent_Auras(Map map) : base(map) { }



        public override void MapComponentTick()
        {
            if (all.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            all.RemoveAll(a => a.left <= 0 || a.caster == null || a.caster.Dead || a.caster.Downed || !a.caster.Spawned || a.caster.Map != map || !SpellAura.BeamHolds(a));
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Aura a = all[i];
                if (now % 30 == 0 && !a.beam) Glow(a, 3);
                if (a.route != null)
                {
                    a.along += a.speed / 60f;
                    Vector3 pt = SpellRoute.PointAt(a.route, a.along, out a.leg);
                    a.target = new LocalTargetInfo(pt.ToIntVec3());
                    if (now % 6 == 0)
                    {
                        Color col = SpellAura.ColorOf(a); col.a = 0.6f;
                        FleckMaker.ThrowDustPuffThick(pt, map, 0.8f, col);
                        Vector3 from = a.chain ? a.route[a.leg].ToVector3Shifted() : a.caster.DrawPos;
                        if (now % 12 == 0) MapComponent_SpellSprites.Bolt(map, from, pt, 10);
                    }
                }
                if (now < a.next) continue;
                a.next = now + a.interval;
                a.left--;
                Pulse(a);
            }
        }

        /// <summary>What stays round the caster shows on its edge: embers of fire (and of magma, of haze); spray, dust,
        /// wind of the others, in their colour.</summary>
        private void Glow(Aura a, int n)
        {
            Vector3 c = a.caster.DrawPos;
            bool fiery = a.combo == ComboKind.Magma || a.combo == ComboKind.Haze || (a.combo == ComboKind.None && a.el?.kind == RuneKind.Fire);
            Color col = SpellAura.ColorOf(a); col.a = 0.5f;
            for (int i = 0; i < n; i++)
            {
                float ang = Rand.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector3 p = c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * a.radius * Rand.Range(0.85f, 1f);
                if (!p.ToIntVec3().InBounds(map)) continue;
                if (fiery) FleckMaker.ThrowFireGlow(p, map, 0.6f);
                else FleckMaker.ThrowDustPuffThick(p, map, 0.7f, col);
            }
        }

        private void Pulse(Aura a)
        {
            if (!a.beam) Glow(a, 12);
            a.flash = Time.realtimeSinceStartup;
            bool keep = SpellAura.Pulsing; float keepR = SpellAura.PulseRadius;
            SpellAura.Pulsing = true; SpellAura.PulseRadius = a.radius;
            using (SpellAura.InField(!a.beam))   // a stream is a blow thrown, not a field
            try
            {
                using (SpellWards.Of(a.combo == ComboKind.None ? a.el?.kind : null, a.combo, a.beam))   // a field round the caster, not a blow thrown: it meets wards as the ground does
                if (a.life != null) LifeMagic.Pulse(a.caster, a.life, a.lifePair, a.power * SpellAura.PulseShare, a.radius, a.lifeAspect, a.lifeMix);
                else
                using (a.shaped ? SpellSculpt.Spare(a.aid) : null)
                    foreach (RitualSchema part in a.parts)
                    {
                        if (a.caster.Dead || !a.caster.Spawned) break;
                    {
                        // a chained route strikes from the mark it last passed, not from the caster (Fix234)
                        IntVec3 keepFrom = SpellOrigin.From;
                        if (a.route != null && a.chain && a.leg > 0) SpellOrigin.From = a.route[a.leg];
                        try { SpellDash.CastPart(a.caster, part.Copy(), a.combo, a.el, a.beam ? a.target : new LocalTargetInfo(a.caster.Position), a.power * SpellAura.PulseShare); }
                        finally { SpellOrigin.From = keepFrom; }
                    }
                    }
            }
            catch (System.Exception e) { Log.ErrorOnce("[Avir] living aura: " + e, 0x5A17A0); a.left = 0; }
            finally { SpellAura.Pulsing = keep; SpellAura.PulseRadius = keepR; }
        }
    }
}
