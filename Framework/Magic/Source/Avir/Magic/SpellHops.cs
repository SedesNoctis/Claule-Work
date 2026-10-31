using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>Reach turned by the Thread (Магия-правила.md, "Дальность + Нить"; the author's rule): Reach written right
    /// before a Thread does not lengthen the cast - it ties the spell on to a further place. What is written outward of the
    /// pair does not strike where the spell struck: it leaps on from there to another place the caster names, within the
    /// spell's reach of the point it leaps from. Pair after pair, the spell runs on from place to place - a chain of held
    /// ground. Reach alone still only lengthens the cast; a Thread alone does what it does elsewhere.</summary>
    public static class SpellHops
    {
        private static bool Acts(RuneDef x) => x.role == RuneRole.Verb || x.role == RuneRole.Form;

        /// <summary>The places of a pair (Reach, then Thread at once - across a section's edge too) in a run of runes.</summary>
        public static List<int> Pairs(List<RuneDef> seq)
        {
            var at = new List<int>();
            for (int i = 0; i + 1 < seq.Count; i++)
                if (seq[i] != null && seq[i + 1] != null && seq[i].kind == RuneKind.Reach && seq[i + 1].kind == RuneKind.Thread) { at.Add(i); i++; }
            return at;
        }

        private static List<RuneDef> Seq(RitualSchema s) => s?.rings.SelectMany(g => g.runes).Where(x => x != null).ToList() ?? new List<RuneDef>();
        private static List<RuneDef> Seq(SpellCircle c) => c?.sections == null ? new List<RuneDef>() : c.sections.SelectMany(g => g.runes).Where(x => x != null).ToList();

        /// <summary>How many leaps a circle makes.</summary>
        public static int Count(SpellCircle c) => Pairs(Seq(c)).Count;

        /// <summary>How many places past the first a spell asks for: one for every pair in every circle.</summary>
        public static int Count(RitualSchema spell) =>
            spell?.weave != null ? spell.weave.Working.Sum(Count) : Pairs(Seq(spell)).Count;

        /// <summary>The Reach runes of a circle that lengthen the cast (those in a pair do not).</summary>
        public static int CastReach(SpellCircle c) => c.OwnRunes.Count(r => r.kind == RuneKind.Reach) - Count(c);

        /// <summary>Is the Thread at this place of a run the second half of a pair (it ties nothing inward of it)?</summary>
        public static bool PairThread(List<RuneDef> seq, int i) => i > 0 && seq[i] != null && seq[i].kind == RuneKind.Thread && seq[i - 1] != null && seq[i - 1].kind == RuneKind.Reach;

        /// <summary>Cuts a circle at its pairs: it keeps what is inward of the first; each part after a pair is returned in
        /// order, with the element and the circle-wide modifiers, to strike at the next place named.</summary>
        public static List<RitualSchema> Cut(ref RitualSchema s)
        {
            if (s?.rings == null || s.rings.Count < 2) return null;
            var places = new List<KeyValuePair<int, int>>();
            for (int g = 1; g < s.rings.Count; g++)
                for (int i = 0; i < s.rings[g].runes.Count; i++)
                    if (s.rings[g].runes[i] != null) places.Add(new KeyValuePair<int, int>(g, i));
            RitualSchema src = s;
            RuneDef At(KeyValuePair<int, int> k) => src.rings[k.Key].runes[k.Value];
            List<RuneDef> seq = places.Select(At).ToList();
            List<int> pairs = Pairs(seq);
            if (pairs.Count == 0) return null;
            int firstAct = seq.FindIndex(Acts);
            var wide = seq.Take(firstAct < 0 ? 0 : firstAct).Where(x => x.kind != RuneKind.Barrier && x.kind != RuneKind.Reach).ToList();
            var parts = new List<RitualSchema>();
            for (int p = 0; p < pairs.Count; p++)
            {
                int from = pairs[p] + 2, to = p + 1 < pairs.Count ? pairs[p + 1] : seq.Count;
                var one = new RitualSchema { name = s.name, size = s.size };
                one.rings.Add(new RitualRing { runes = s.rings[0].runes.ToList(), slots = 1 });
                one.rings.Add(new RitualRing { runes = wide.Concat(seq.Skip(from).Take(Mathf.Max(0, to - from))).ToList() });
                parts.Add(one);
            }
            // what stays: everything inward of the first pair
            RitualSchema before = s.Copy();
            foreach (var k in places.Skip(pairs[0]).Reverse()) before.rings[k.Key].runes.RemoveAt(k.Value);
            before.rings.RemoveAll(g => g != before.rings[0] && g.runes.Count == 0);
            s = before;
            return parts;
        }

        // ------------------------------------------------------------------ the places named while aiming

        private static readonly Dictionary<int, List<IntVec3>> pending = new Dictionary<int, List<IntVec3>>();

        public static void Set(Pawn mage, List<IntVec3> places)
        {
            if (mage == null) return;
            if (places == null || places.Count == 0) pending.Remove(mage.thingIDNumber); else pending[mage.thingIDNumber] = places.ToList();
        }

        /// <summary>The places this caster named for the spell now cast (taken once).</summary>
        public static List<IntVec3> Take(Pawn mage)
        {
            if (mage == null || !pending.TryGetValue(mage.thingIDNumber, out var l)) return new List<IntVec3>();
            pending.Remove(mage.thingIDNumber);
            return l;
        }
    }
}
