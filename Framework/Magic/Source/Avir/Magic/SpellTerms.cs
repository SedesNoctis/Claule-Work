using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avir.Ritualistics;
using Verse;

namespace Avir.Magic
{
    /// <summary>The codex's plaques named as a magic almanac names them: a term, a short entry in the manner of a
    /// student's handbook, then what it comes to in this spell. One place for the names, so they read alike everywhere.</summary>
    public static class SpellTerms
    {
        private class Term
        {
            public Regex match;
            public System.Func<Match, string> name;
            public string entry;
        }

        private static Term T(string pattern, System.Func<Match, string> name, string entry) => new Term { match = new Regex("^" + pattern + "$"), name = name, entry = entry };
        private static Term T(string pattern, string name, string entry) => T(pattern, m => name, entry);

        private static readonly List<Term> Terms = new List<Term>
        {
            T("Spares its own - in part", "Selective Protection (partial)",
                "Selective Protection. Two Barriers on the outermost section of a circle bend its blow round the caster's people - and round the caster - so that only foes are struck. The blow pays for its care: it is a quarter weaker. Each circle must be bent on its own; a circle left plain strikes everyone in its reach."),
            T("Spares its own", "Selective Protection",
                "Selective Protection. Two Barriers on the outermost section of a circle bend its blow round the caster's people - and round the caster - so that only foes are struck. The blow pays for its care: it is a quarter weaker. Each circle must be bent on its own; a circle left plain strikes everyone in its reach."),
            T("Strikes everyone", "Open Blow",
                "Open Blow. A spell's blow knows no sides: whoever stands in it is struck - friend, beast, the caster at their own feet. Only a ward keeps them, or Selective Protection written on the circle."),
            T("Wear of repeats", "Tautology",
                "Tautology. A verb written twice in one section is the same act said again, and it tires: the second acts at half, the third at a quarter, the rest not at all - yet each still draws its Arcana. In another section the same verb is a new act and does not wear. The old masters spread their verbs; they did not pile them."),
            T("Heavy", "Earthen Slowness",
                "Earthen Slowness. Earth does not hurry. A circle of earth - and of what earth makes with others, magma and bog - is written into the world slower than any other: the cast takes longer. Dust rides the air and is spared it."),
            T("Stone at hand", "Stone at Hand",
                "Stone at Hand. Earth's spells take up the stone they find where they strike - rubble, chunks, lumps of basalt, the mire for a bog - and throw it into the work. Stronger, cheaper, and deeper through armour; and the stone so used is gone."),
            T("Momentum", "Air's Momentum",
                "Air's Momentum. The wind, once raised, is slow to lie down. A spell of the air cast soon after another comes quicker and cheaper, and quicker again, while the caster keeps it moving."),
            T("Closed", "Sealed Circle",
                "Sealed Circle. The Closing rune shuts a circle on itself: its element stays pure and does not meet its neighbours in the chain - fire stays fire beside water."),
            T("Focus", "Focusing",
                "Focusing. The Focus rune draws the map's stream into its circle. On a calm map it gives little; under a strong stream, a great deal. It makes a spell stronger, not deeper through armour."),
            T("Ring", "Ring Wall",
                "Ring Wall. Shaping that strikes at the caster's own feet - after a step, from an aura, or aimed at themselves - raises its wall as a ring round them, and whoever stands inside is shut in with the caster."),
            T("Many targets", "Scattered Strikes",
                "Scattered Strikes. A storm is not aimed at one: besides its target it seeks the nearest foes round it, and on wet ground leaps further."),
            T("In turn( \\((\\d+)\\))?", m => "Order of Acts" + (m.Groups[2].Success ? " (" + m.Groups[2].Value + ")" : ""),
                "Order of Acts. The runes of a circle act one after another, as they are written from the centre outward."),
            T("In order", "Order of Circles",
                "Order of Circles. The circles of a spell act one after another, from the heart outward; each strikes where the one before it ended."),
            T("By their place", "Rule of Placement",
                "Rule of Placement. A modifier works on the rune just inward of it; written beside the element, on the whole circle."),
            T("(.+): two elements met", m => "Confluence: " + m.Groups[1].Value,
                "Confluence. Two neighbouring circles of elements that can meet become a third: fire and water - steam, fire and earth - magma, and so on. Each circle keeps its own verbs, but works as what they made."),
            T("(.+): inherited", m => "Inheritance: " + m.Groups[1].Value,
                "Inheritance. What two elements make keeps the habits of both its parents: the ground it feeds on, how it is cast, what a slipped hold brings."),
            T("(.+) x(\\d+)", m => "Repetition: " + m.Groups[1].Value + " ×" + m.Groups[2].Value,
                "Repetition. A verb written more than once happens more than once, in turn. Within one section it wears (see Tautology); in different sections each is whole."),
            T("Wider", "Breadth", "Breadth. The Breadth rune widens a spell's reach on the ground - a wider point, a broader line - and the blow, spread wider, goes less deep at its edges. Each makes the cast a little longer."),
            T("Further", "Reach", "Reach. The Reach rune lets a spell be cast further, and the cast takes a little longer for it."),
            T("Stronger|Heavier", "Amplification", "Amplification. The Amplify rune strengthens the verb just inward of it, and drives its blow deeper through armour - each rune less than the one before."),
            T("Explosion", "Amplified Burst", "Amplified Burst. A release of fire, amplified, bursts as a blast of flame."),
            T("Further back", "Amplified Throw", "Amplified Throw. Amplified, the burst throws foes further."),
            T("Several forms", "Multiple Forms", "Multiple Forms. Each form delivers the spell on its own: it happens once for each."),
            T("Bodies thrown|The fall", "The Fall", "The Fall. Whoever is thrown up hangs out of reach, then comes down. A fall is the body's own weight against the ground: plate does not soften it, and the heavier the gear, the harder the landing."),
            T("Price: (.+)", m => "Price of Slipping: " + m.Groups[1].Value,
                "Price of Slipping. Every element takes its toll from a caster who loses the hold: what the spell would have done, it does to them."),
            T("Ward of (.+)", m => "Ward of " + m.Groups[1].Value.CapitalizeFirst(),
                "Ward. The Barrier rune guards: last in a circle, after something that acts, it lays a ward on the caster; anywhere else, a dome over the place the spell strikes, guarding whoever stands inside."),
            T("Warms friends", "Hearth Warmth", "Hearth Warmth. Held fire is a hearth as much as a weapon: those of the caster's side who stand by it bear the cold far better for a day."),
            T("Ground that takes no fire", "Burnt Ground", "Burnt Ground. Ground purged by fire has nothing left to burn: for some hours flames that reach it die."),
            T("Burns infection", "Cleansing Fire", "Cleansing Fire. Fire turned to mending burns back infection and what lives in a wound."),
            T("Better cautery", "Cautery", "Cautery. Growth in a mending of fire: the seared wounds hold better."),
            T("Stores heat|Keeps the water|Keeps the weight|Keeps the breath", "Drawn Store", "Drawn Store. What the Drawing-out rune takes from the target the caster keeps in the body for a while, and the next spell of that element draws on it - stronger."),
            T("Stands longer", "Lasting Stone", "Lasting Stone. Holding and anchors keep shaped stone standing longer."),
            T("Feeds fire", "Fanned Flames", "Fanned Flames. A gust feeds the fires it passes over - careful near your own walls."),
            T("The Thread", "The Thread",
                "The Thread. The Thread rune turns a Step round: written right outward of a Step, it makes the caster's way the target's - the one at the target is drawn to the caster, as the caster would have gone to them. Anywhere else it ties nothing and does nothing."),
        };

        /// <summary>Names the plaques of a reading by the almanac, once.</summary>
        public static void Apply(SpellEffect e)
        {
            if (e == null || e.termed) return;
            e.termed = true;
            for (int i = 0; i < e.extras.Count; i++)
            {
                var kv = e.extras[i];
                foreach (Term t in Terms)
                {
                    Match m = t.match.Match(kv.Key);
                    if (!m.Success) continue;
                    string here = kv.Value;
                    if (here != null && here.StartsWith(t.entry)) break;   // named already (a copy of a reading)
                    string body = here.NullOrEmpty() || here == t.entry ? t.entry : t.entry + "\n\nHere: " + here;
                    e.extras[i] = new KeyValuePair<string, string>(t.name(m), body);
                    break;
                }
            }
            Merge(e);
        }

        private const string HereMark = "\n\nHere: ";

        private static string HereOf(string v)
        {
            if (v == null) return "";
            int i = v.IndexOf(HereMark);
            return i < 0 ? "" : v.Substring(i + HereMark.Length);
        }

        private static string EntryOf(string v)
        {
            if (v == null) return "";
            int i = v.IndexOf(HereMark);
            return i < 0 ? v : v.Substring(0, i);
        }

        /// <summary>One plaque for one term: the order of acts of every circle under one, each confluence once, and what
        /// a confluence inherits read inside it rather than beside it.</summary>
        private static void Merge(SpellEffect e)
        {
            var outList = new List<KeyValuePair<string, string>>();
            var at = new Dictionary<string, int>();
            var inherit = new List<KeyValuePair<string, string>>();
            foreach (var kv in e.extras)
            {
                string key = kv.Key, here = HereOf(kv.Value);
                Match n = Regex.Match(key, "^Order of Acts \\((\\d+)\\)$");
                if (n.Success) { key = "Order of Acts"; if (here.Length > 0) here = "Circle " + n.Groups[1].Value + ": " + here; }
                if (key.StartsWith("Inheritance: ")) { inherit.Add(new KeyValuePair<string, string>(key.Substring(13), here)); continue; }
                if (at.TryGetValue(key, out int i))
                {
                    var old = outList[i];
                    string oh = HereOf(old.Value);
                    if (here.Length > 0 && !oh.Contains(here))
                        outList[i] = new KeyValuePair<string, string>(key, EntryOf(old.Value) + HereMark + (oh.Length > 0 ? oh + "\n" : "") + here);
                    continue;
                }
                at[key] = outList.Count;
                outList.Add(new KeyValuePair<string, string>(key, here.Length > 0 ? EntryOf(kv.Value) + HereMark + here : kv.Value));
            }
            foreach (var ih in inherit)
            {
                string key = "Confluence: " + ih.Key;
                if (at.TryGetValue(key, out int i))
                {
                    var old = outList[i];
                    outList[i] = new KeyValuePair<string, string>(key, old.Value + "\n\nFrom its parents it keeps: " + ih.Value);
                }
                else { at[key] = outList.Count; outList.Add(new KeyValuePair<string, string>(key, "Confluence. What two elements make keeps the habits of both its parents." + HereMark + ih.Value)); }
            }
            e.extras.Clear();
            e.extras.AddRange(outList);
        }

        /// <summary>What the Thread does in this spell, for whoever reads it.</summary>
        public static string ThreadHere(RitualSchema s)
        {
            if (s == null) return null;
            IEnumerable<RitualSchema> circles = s.weave != null ? s.weave.Working.Select(c => c.AsSchema()) : new[] { s };
            bool any = false, pulls = false;
            foreach (RitualSchema c in circles)
            {
                if (!c.rings.SelectMany(g => g.runes).Any(x => x != null && x.kind == RuneKind.Thread)) continue;
                any = true;
                if (SpellMods.Pulls(c)) pulls = true;
            }
            if (!any) return null;
            return pulls ? "Written outward of a Step: the step turns round - whoever stands at the target is drawn to the caster."
                         : "Not outward of a Step here: it ties nothing and does nothing. Write it right after a Step to draw the target to you.";
        }
    }
}
