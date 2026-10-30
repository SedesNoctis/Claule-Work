using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Avir.Ritualistics
{
    /// <summary>One circle of a spell. Its element stands in the centre; its sections run outward, runes in each.
    /// A circle of a great element (Life, Light, Time, Space, Darkness) may hold other circles inside it - it wraps
    /// them whatever their size - and keeps its own sections round them. A small element (fire, water, earth, air)
    /// holds only runes. "Small" and "great" are the rank of the element, never the size of the circle.</summary>
    public class SpellCircle : IExposable
    {
        public RuneDef element;
        public List<RitualRing> sections = new List<RitualRing>();
        public List<SpellCircle> inner = new List<SpellCircle>();

        public const int MaxInner = 5, MaxWraps = 5, MaxSectionsSmall = 4, MaxSectionsGreat = 3, MaxSlots = 6;
        /// <summary>The whole spell at most: five great circles and twenty-five small ones, however they are nested.</summary>
        public const int MaxGreat = 5, MaxSmall = 25;

        public int GreatCount => All().Count(c => c.Great);
        public int SmallCount => All().Count(c => !c.Great);

        public static bool GreatElement(RuneDef e) => e != null && (e.kind == RuneKind.Life || e.kind == RuneKind.Light || e.kind == RuneKind.Time
                                                                  || e.kind == RuneKind.Space || e.kind == RuneKind.Darkness);
        public bool Great => GreatElement(element) || inner.Count > 0;
        public int MaxSections => Great ? MaxSectionsGreat : MaxSectionsSmall;

        public IEnumerable<RuneDef> OwnRunes => sections.SelectMany(r => r.runes).Where(r => r != null);

        /// <summary>Every circle of the spell, this one first, then inward.</summary>
        public IEnumerable<SpellCircle> All()
        {
            yield return this;
            foreach (SpellCircle c in inner)
                foreach (SpellCircle d in c.All()) yield return d;
        }

        public SpellCircle Copy()
        {
            var c = new SpellCircle { element = element };
            foreach (RitualRing r in sections) c.sections.Add(r.Copy());
            foreach (SpellCircle i in inner) c.inner.Add(i.Copy());
            return c;
        }

        /// <summary>The circle read alone, as a one-circle ritual: its element and its runes.</summary>
        public RitualSchema AsSchema()
        {
            var s = new RitualSchema { name = element?.label ?? "circle", size = RitualCircleSize.Small };
            s.rings.Add(new RitualRing { runes = element != null ? new List<RuneDef> { element } : new List<RuneDef>(), slots = 1 });
            foreach (RitualRing r in sections) s.rings.Add(r.Copy());
            return s;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref element, "element");
            List<string> packed = null;
            if (Scribe.mode == LoadSaveMode.Saving)
                packed = sections.Select(r => r.Slots + "|" + string.Join(",", r.runes.Where(x => x != null).Select(x => x.defName))).ToList();
            Scribe_Collections.Look(ref packed, "sections", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                sections = new List<RitualRing>();
                if (packed != null)
                    foreach (string raw in packed)
                    {
                        string line = raw ?? "";
                        int slots = 0, bar = line.IndexOf('|');
                        if (bar >= 0) { int.TryParse(line.Substring(0, bar), out slots); line = line.Substring(bar + 1); }
                        var ring = new RitualRing { slots = slots };
                        foreach (string n in line.Split(',')) { RuneDef d = DefDatabase<RuneDef>.GetNamedSilentFail(n); if (d != null) ring.runes.Add(d); }
                        sections.Add(ring);
                    }
            }
            Scribe_Collections.Look(ref inner, "inner", LookMode.Deep);
            if (inner == null) inner = new List<SpellCircle>();
            if (sections == null) sections = new List<RitualRing>();
        }

        /// <summary>A spell written the old way (rings) as one circle: the first element is its centre.</summary>
        public static SpellCircle FromRings(RitualSchema s)
        {
            var c = new SpellCircle();
            foreach (RitualRing r in s.rings)
            {
                var rr = new RitualRing { slots = r.slots };
                foreach (RuneDef d in r.runes)
                {
                    if (d == null) continue;
                    if (d.role == RuneRole.Element && c.element == null) { c.element = d; continue; }
                    rr.runes.Add(d);
                }
                if (rr.runes.Count > 0 || r.runes.Count == 0) { rr.slots = Mathf.Max(rr.runes.Count, 1); c.sections.Add(rr); }
            }
            if (c.sections.Count == 0) c.sections.Add(new RitualRing { slots = 2 });
            return c;
        }
    }

    /// <summary>A spell's sign, made by the player: a background (diamond, shield, circle or none) with its plate and
    /// rim, a main glyph, and a small second glyph in the corner - each in a colour of its own.</summary>
    public class SpellIcon : IExposable
    {
        public enum Shape { None, Diamond, Shield, Circle }
        public Shape shape = Shape.Circle;
        public RuneDef main, second;
        public Color plate = new Color(0.12f, 0.08f, 0.14f), rim = new Color(0.95f, 0.86f, 0.6f), glyph = Color.white, glyph2 = new Color(0.95f, 0.86f, 0.6f);

        public SpellIcon Copy() => new SpellIcon { shape = shape, main = main, second = second, plate = plate, rim = rim, glyph = glyph, glyph2 = glyph2 };

        public void ExposeData()
        {
            Scribe_Values.Look(ref shape, "shape", Shape.Circle);
            Scribe_Defs.Look(ref main, "main");
            Scribe_Defs.Look(ref second, "second");
            Scribe_Values.Look(ref plate, "plate");
            Scribe_Values.Look(ref rim, "rim");
            Scribe_Values.Look(ref glyph, "glyph", Color.white);
            Scribe_Values.Look(ref glyph2, "glyph2");
        }
    }
}
