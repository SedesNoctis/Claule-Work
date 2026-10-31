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
                return;
            }
            if (anchor <= 0) return;   // no anchor and no aura of this spell to feed: it strikes once, as an aura always did
            if (old != null) c.all.Remove(old);
            var a = new MapComponent_Auras.Aura
            {
                caster = caster, el = el, combo = k, spell = Spell, breadth = breadth, left = Strikes(anchor), power = power,
                interval = Interval(breadth), radius = Radius(breadth), shaped = SpellSculpt.Active
            };
            a.parts.Add(part.Copy());
            a.next = now + a.interval;
            c.all.Add(a);
        }
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
            public bool shaped;
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
            bool keep = SpellAura.Pulsing; float keepR = SpellAura.PulseRadius;
            SpellAura.Pulsing = true; SpellAura.PulseRadius = a.radius;
            try
            {
                using (SpellWards.Of(a.combo == ComboKind.None ? a.el?.kind : null, a.combo, true))
                using (a.shaped ? SpellSculpt.Spare() : null)
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
