using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The runes of the meetings (the author's sheets, Fix183): Combo_&lt;Name&gt; in the runes' Glow and Ink folders -
    /// a meeting's own glyph shown in place of its element's: in the editor's centre of a circle that met, on the seal
    /// of a great circle of Life with its meeting, and as a spell's sign when none was made. A glyph not drawn yet - the
    /// element's own stays.</summary>
    public static class MeetingGlyphs
    {
        private static readonly Dictionary<string, Texture2D> glow = new Dictionary<string, Texture2D>(), ink = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string name, bool asInk)
        {
            if (name == null) return null;
            var d = asInk ? ink : glow;
            if (!d.TryGetValue(name, out Texture2D t)) d[name] = t = ContentFinder<Texture2D>.Get("UI/Ritualistics/Runes/" + (asInk ? "Ink" : "Glow") + "/Combo_" + name, false);
            return t;
        }

        /// <summary>The meeting a circle stands in, by name: second order first, then the pair; a great circle of Life -
        /// its meeting (deep or wide).</summary>
        public static string NameOf(SpellCircle root, SpellCircle c)
        {
            if (root == null || c == null) return null;
            if (c.Great && LifeMagic.Is(c.element))
            {
                RuneKind deep = LifeMagic.PairOf(root, c), wide = LifeMagic.AspectOf(root, c);
                return deep != LifeMagic.None ? LifeMagic.PairName(deep)?.Replace(" ", "") : wide != LifeMagic.None ? LifeMagic.AspectName(wide) : null;
            }
            if (c.element == null) return null;
            if (SecondOrder.Of(root).TryGetValue(c, out SecondKind y) && y != SecondKind.None) return y.ToString();
            if (Combos.Pairs(root, false).TryGetValue(c, out ComboKind k) && k != ComboKind.None)
            {
                // a partial meeting shows its own rune (Thunder for dust with a Focus beside Release)
                foreach (RuneKind v in Partials.FocusedVerbs(new RitualSchema { rings = c.sections })) { PartialKind pk = Partials.Of(k, v); if (pk != PartialKind.None && Get(pk.ToString(), false) != null) return pk.ToString(); }
                return k.ToString();
            }
            return null;
        }

        public static Texture2D For(SpellCircle root, SpellCircle c, bool asInk) => Get(NameOf(root, c), asInk);

        private static readonly Dictionary<RitualSchema, KeyValuePair<int, Texture2D>> cache = new Dictionary<RitualSchema, KeyValuePair<int, Texture2D>>();
        /// <summary>The same, looked up again at most once a second (a gizmo is drawn every frame).</summary>
        public static Texture2D ForSpellCached(RitualSchema s)
        {
            if (s == null) return null;
            int now = Time.frameCount;
            if (cache.TryGetValue(s, out var kv) && now - kv.Key < 60) return kv.Value;
            Texture2D t = ForSpell(s, false);
            cache[s] = new KeyValuePair<int, Texture2D>(now, t);
            return t;
        }

        /// <summary>A spell's own meeting glyph, for its sign when the player made none: the strongest meeting in it.</summary>
        public static Texture2D ForSpell(RitualSchema s, bool asInk)
        {
            if (s?.weave == null) return null;
            SpellCircle root = s.weave;
            foreach (SecondKind y in SecondOrder.Of(root).Values.Distinct()) { Texture2D t = Get(y.ToString(), asInk); if (t != null) return t; }
            foreach (SpellCircle g in LifeMagic.Circles(root)) { Texture2D t = For(root, g, asInk); if (t != null) return t; }
            foreach (ComboKind k in Combos.Pairs(root, false).Values.Distinct()) { Texture2D t = Get(k.ToString(), asInk); if (t != null) return t; }
            return null;
        }
    }
}
