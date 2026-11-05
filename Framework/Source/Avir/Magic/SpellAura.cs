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
    /// Made for fire first - the other elements keep their own fields until they are put on it.</summary>
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

        /// <summary>The elements whose aura lives (fire for now).</summary>
        public static bool Lives(RuneDef el, ComboKind k) => k == ComboKind.None && el != null && el.kind == RuneKind.Fire;

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
            MapComponent_Auras.Aura old = c.all.FirstOrDefault(a => a.caster == caster && a.el == el && a.combo == k);
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

        /// <summary>The living aura round this pawn, if any.</summary>
        public static MapComponent_Auras.Aura Of(Pawn p) => p?.MapHeld?.GetComponent<MapComponent_Auras>()?.all.FirstOrDefault(a => a.caster == p);

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
            public float flash;   // when it last struck (real time), for the flare of the ring
            public float seed = Rand.Range(0f, 360f);
        }

        private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        /// <summary>The ring of the aura: its own picture by the element (or what two elements made), else the
        /// element's; whatever it is, tinted by its colour.</summary>
        private static Material Mat(Aura a)
        {
            string key = a.combo != ComboKind.None ? a.combo.ToString() : a.el?.kind.ToString() ?? "Fire";
            if (mats.TryGetValue(key, out Material m)) return m;
            string path = "Things/Mote/Avir/Magic/Aura_" + key;
            if (ContentFinder<Texture2D>.Get(path, false) != null) m = MaterialPool.MatFrom(path, ShaderDatabase.MoteGlow, Color.white);
            else
            {
                string parent = a.combo == ComboKind.Magma || a.combo == ComboKind.Haze ? "Fire" : a.combo == ComboKind.Storm || a.combo == ComboKind.Dust ? "Air" : a.combo == ComboKind.Bog ? "Earth" : a.combo == ComboKind.Steam ? "Water" : "Fire";
                m = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Aura_" + parent, ShaderDatabase.MoteGlow, SpellAura.ColorOf(a));
            }
            mats[key] = m;
            return m;
        }

        public override void MapComponentUpdate()
        {
            if (all.Count == 0 || Find.CurrentMap != map) return;
            float t = Time.realtimeSinceStartup;
            foreach (Aura a in all)
            {
                if (a.caster == null || !a.caster.Spawned) continue;
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
            all.RemoveAll(a => a.left <= 0 || a.caster == null || a.caster.Dead || a.caster.Downed || !a.caster.Spawned || a.caster.Map != map);
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Aura a = all[i];
                if (now % 30 == 0) Glow(a, 3);
                if (now < a.next) continue;
                a.next = now + a.interval;
                a.left--;
                Pulse(a);
            }
        }

        /// <summary>The fire that stays round the caster: embers on its edge.</summary>
        private void Glow(Aura a, int n)
        {
            Vector3 c = a.caster.DrawPos;
            for (int i = 0; i < n; i++)
            {
                float ang = Rand.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector3 p = c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * a.radius * Rand.Range(0.85f, 1f);
                if (p.ToIntVec3().InBounds(map)) FleckMaker.ThrowFireGlow(p, map, 0.6f);
            }
        }

        private void Pulse(Aura a)
        {
            Glow(a, 12);
            a.flash = Time.realtimeSinceStartup;
            bool keep = SpellAura.Pulsing; float keepR = SpellAura.PulseRadius;
            SpellAura.Pulsing = true; SpellAura.PulseRadius = a.radius;
            using (SpellAura.InField())
            try
            {
                using (SpellWards.Of(a.combo == ComboKind.None ? a.el?.kind : null, a.combo, false))   // a field round the caster, not a blow thrown: it meets wards as the ground does
                using (a.shaped ? SpellSculpt.Spare(a.aid) : null)
                    foreach (RitualSchema part in a.parts)
                    {
                        if (a.caster.Dead || !a.caster.Spawned) break;
                        SpellDash.CastPart(a.caster, part.Copy(), a.combo, a.el, new LocalTargetInfo(a.caster.Position), a.power * SpellAura.PulseShare);
                    }
            }
            catch (System.Exception e) { Log.ErrorOnce("[Avir] living aura: " + e, 0x5A17A0); a.left = 0; }
            finally { SpellAura.Pulsing = keep; SpellAura.PulseRadius = keepR; }
        }
    }
}
