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
                LifeMix lm = LifeMagic.MixOf(root, c);
                return deep != LifeMagic.None ? LifeMagic.PairName(deep)?.Replace(" ", "") : wide != LifeMagic.None ? LifeMagic.AspectName(wide) : lm != LifeMix.None ? lm.ToString() : null;
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

        /// <summary>Every meeting rune there is (the author's sheets), by name - for the sign editor (Fix192).</summary>
        public static List<string> All
        {
            get
            {
                if (all == null)
                    all = ContentFinder<Texture2D>.GetAllInFolder("UI/Ritualistics/Runes/Glow").Where(t => t != null && t.name.StartsWith("Combo_")).Select(t => t.name.Substring(6)).Distinct().OrderBy(n => n).ToList();
                return all;
            }
        }
        private static List<string> all;

        /// <summary>A meeting's own colour, by its name: the pair's, the second order's, Life's, a partial's.</summary>
        public static Color ColorOf(string name)
        {
            if (name == null) return Color.white;
            if (System.Enum.TryParse(name, out SecondKind y) && y != SecondKind.None) return SecondOrder.ColorOf(y);
            if (System.Enum.TryParse(name, out ComboKind k) && k != ComboKind.None) return Combos.ColorOf(k);
            if (name == "Thunder") return Partials.ThunderColor;
            if (name == "Whirl" || name == "Veil") return Partials.SandColor;
            foreach (RuneKind e in new[] { RuneKind.Fire, RuneKind.Water, RuneKind.Earth, RuneKind.Air })
            {
                if (LifeMagic.PairName(e)?.Replace(" ", "") == name) return LifeMagic.PairColor(e);
                if (LifeMagic.AspectName(e) == name) return LifeMagic.AspectColor(e);
            }
            if (System.Enum.TryParse(name, out LifeMix lmx) && lmx != LifeMix.None) return LifeMagic.MixColor(lmx);
            return Color.white;
        }

        /// <summary>A spell's own meeting, by name (for the default sign and its colour).</summary>
        public static string NameForSpell(RitualSchema s)
        {
            if (s?.weave == null) return null;
            SpellCircle root = s.weave;
            foreach (SecondKind y in SecondOrder.Of(root).Values.Distinct()) if (Get(y.ToString(), false) != null) return y.ToString();
            foreach (SpellCircle g in LifeMagic.Circles(root)) { string n = NameOf(root, g); if (Get(n, false) != null) return n; }
            foreach (var kv in Combos.Pairs(root, false))
            {
                string n = NameOf(root, kv.Key);
                if (Get(n, false) != null) return n;
            }
            return null;
        }

        private static readonly Dictionary<RitualSchema, KeyValuePair<int, string>> nameCache = new Dictionary<RitualSchema, KeyValuePair<int, string>>();
        public static string NameForSpellCached(RitualSchema s)
        {
            if (s == null) return null;
            int now = Time.frameCount;
            if (nameCache.TryGetValue(s, out var kv) && now - kv.Key < 60) return kv.Value;
            string n = NameForSpell(s);
            nameCache[s] = new KeyValuePair<int, string>(now, n);
            return n;
        }

        /// <summary>The meeting's sign drawn as the elements' are: its rune in its own colour (glowing on the dark, inked on
        /// the parchment) - not black.</summary>
        public static void DrawSign(Rect r, string name, bool onDark = false)
        {
            Texture2D glow = Get(name, false), ink = Get(name, true);
            if (glow == null) return;
            Color keep = GUI.color, c = ColorOf(name);
            bool light = onDark || FlowStyle.Pick(ink, glow) == glow;
            GUI.color = light || ink == null ? Color.Lerp(c, Color.white, 0.15f) : SpellGlyph.Ink(c);
            GUI.DrawTexture(r, light || ink == null ? glow : ink, ScaleMode.ScaleToFit);
            GUI.color = keep;
        }

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
