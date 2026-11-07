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
            T("Turned to its own", "Selective Aid",
                "Selective Aid. Two Barriers and a Thread on the outermost section of a circle turn Selective Protection round: the blow goes to the caster's own people - and to the caster - and passes the foes by. It is a quarter weaker for its care. Made for mending, warding and strengthening one's own from the circle; written on a blast, it is the caster's own business."),
            T("Second meeting: (.+)", m => "Meeting of the Second Order",
                "Meeting of the Second Order. Two circles meet first, as any pair does; the small circle right after them meets what they made, and the three become one thing. The chain is read in order from its start, and the order decides: steam meeting earth is not the bog meeting fire. All three circles work as the pair - its whole grammar of runes - and the second meeting lays its own nature over every part, as strong again by a quarter. After the three, a circle of an element already in the meeting strengthens it by a quarter more, and does not start a meeting of its own; a Closing written first in its first section, right at the element, parts it from the meeting, and it may then meet the circle after it."),
            T("Transmutation", "Transmutation",
                "Transmutation. In a circle that meets no one - or written with a Closing right after it - the element works on matter where the spell lands: fire smelts and melts and dries, water washes and puts out, earth cuts and smooths, air clears and blows away; a meeting does the work of both. With a Release it changes the ground under the foes for a while; with an Aura it leaves a short trail. What it does in a meeting with nothing after it is not written here."),
            T("Wards the foes", "Warded Foes",
                "Warded Foes. A Barrier wards whoever its circle reaches: under Selective Aid one's own, under Selective Protection the foes. A spell that wards the foes it was meant to strike has been built backwards - unless it was meant to."),
            T("Mends and strikes own|Mends foes, strikes own", "Crossed Purposes",
                "Crossed Purposes. One spell, two minds: it heals and wounds the same people, or heals the foes while it strikes one's own. Each circle chooses whom it reaches by its own outer edge; the healing turn and the blow follow those choices, not the caster's wishes."),
            T("Symbiosis", "Symbiosis",
                "Symbiosis - the law of Life. Life makes nothing; it only moves what lives from one thing into another. Withering - the attack of Life - takes from the grass, the living and the fresh dead into the mage's store of life; Restoration gives that store away. Each verb is tied to one of the two. Life not given anywhere grows on the one who holds it: a store kept above three quarters covers the mage in bark and moss. With the store empty they give nothing at all. One spell may fill the store and another spend it, whenever the mage likes. The store fades slowly, and what fades mends the mage; past its top it spills over (Overflow)."),
            T("Life Wraps", "Life Wraps",
                "Life Wraps. A great circle of Life drawn round small circles mends every friend the spell touches as it strikes - out of the store of life, like any giving of Life."),
            T("Giving from the Store", "Giving from the Store",
                "Giving from the Store. A spell of Life with no Withering in it takes nothing: it spends what an earlier spell put into the store of life. Cast a Withering first, or carry the store full."),
            T("Untied Verb of Life", "Untied Verb of Life",
                "Untied Verb of Life. Life has two sides: Withering, which takes, and Restoration, which gives. Every verb and every beam of Life is written with one of them - Release + Withering is a burst that drinks, Beam + Restoration a beam that mends. A verb with neither does nothing at all. Whom it reaches is the circle's rule, as everywhere: two Barriers on its outer edge strike only the foes, two Barriers and a Thread only one's own; unshaped, Withering drinks friends too and Restoration mends the foes too."),
            T("Wild Flame", "Wild Flame",
                "Wild Flame - Life meeting Fire: living bombs. Withering sows a spark in the bodies its circle reaches; the spark sets its carrier alight, and the carrier runs in panic to the others nearest. A carrier that touches another bursts in petals of green-gold flame, and the spark leaps on into the one touched, weaker each time. Water does not put it out: in water the carrier steams, and the steam scalds. With Release a whole crowd is sown; with Beam, a fuse along a line; with Hold the carrier is rooted - a mine; with Growth the bushes are sown and burst when walked into, and the sparks already sown are fed - each feeding (three at most) makes the spark half again stronger, one leap longer, its burst wider; with Purge every spark bursts at once; with Step the mage carries one into the crowd. Restoration sows smouldering hearts whose bursts mend. Unshaped, the sparks take root in one's own as well, and the bombs run to them."),
            T("Blood", "Blood",
                "Blood - Life meeting Water. The body is a pool, as water's pools are. Withering drinks part of the blood of those its circle reaches; Restoration gives it back, and with Hold the blood clots and the bleeding stops. Release and Beam paralyse; Hold holds the blood still - no limb answers; Lift throws the body up, Lift with a Thread presses it to the ground; Step drags it to the mage by its own blood, and a body too far falls on the way. Spilled blood is drunk into the store. The mage of Blood mends their own blood quickest of all, out of the store."),
            T("Emerald", "Emerald",
                "Emerald - Life meeting Earth. Stone remembers life: leaf, stone, an emerald. Withering grows a crust of green stone on the living - slower and harder as it thickens; the body slows, hardens and quiets - hunger, speech, hands, legs - and takes the stone's colour; full, a living statue that stands (or lies, as it was caught) for days without falling, its hunger at a tenth, until the stone lets go. Half of what it takes is sealed in emeralds at the spot (all of it, drawn from the green with Growth). Hold turns one to stone fast; Purge shatters the crusts - shards strike round them. Restoration draws on the emeralds first, leaves living ground, and with Hold wraps the reached in a stone cocoon."),
            T("Pestilence", "Pestilence",
                "Pestilence - Life meeting Air, turned round: here Withering mends and Restoration harms. Withering drinks the sicknesses and poisons of those its circle reaches into the store. Restoration lays a fertile poison fog: the ground grows rich under it for three days, and the poison is the place's own - whoever stands in it takes it in fast, it keeps growing slower after they leave, and it ends in a paralytic abasia for days. The fog does not know friend from foe. Growth swells the sicknesses it reaches - grow them, then drink them; Purge lifts the fog."),
            T("A hunch", "A Hunch",
                "A Hunch. Some things the almanac does not tell; it only marks that something is there. Cast it and see."),
            T("Secret path: (.+)", m => "Secret Path",
                "Secret Path. A meeting turned in on itself: with a Transmutation in the outermost section of each of its two circles, it gives up what it usually is and becomes one thing only. Each meeting has its own. They are found by casting, not by reading."),
            T("Drawn in", "Implosion",
                "Implosion. A Thread written on a Release turns the blast inward: it strikes as it would, but whoever it would throw back is pulled in toward its heart instead, and never past it. The old masters used it to gather a crowd for the circle that came after."),
            T("Healing thread", "Healing Thread",
                "Healing Thread. A Beam, a Thread written right after it, and Restoration right after the Thread: the line itself is tied to the mending, and whoever it passes through has their wounds closed, the worst first. Whom it reaches is the circle's rule, as for any blow. What the beam carries is its own: a tied verb heals, a plain one strikes. Spread wider with Breadth, it is thinner."),
            T("Beam on a route", "Beam on a Route",
                "Beam on a Route. Beam, Anchor and Thread written one after another (and no Restoration after the Thread): a streaming beam that walks. After the target, mark where it goes on: the point it strikes moves along the marks at its pace - about 2.5 cells a second, slower for every Anchor past the first, quicker with Amplify - and strikes once a second all the way. Without a Step: two marks, swept from the caster. Every Step in that circle is one more mark and makes it a chain: the beam reaches a mark and goes on from it, not from the caster - up to nine marks, each reaching a tenth less far than the one before. The Steps carry no one here. The caster stands while it lasts; break the stream off to move."),
            T("Streaming beam", "Streaming Beam",
                "Streaming Beam. An Anchor written on a Beam - after it, not beside the element - holds it: the beam does not strike once and go, but stays on its target and strikes along the line again every second, four times for each Anchor, at half the strength of the cast. A stream breaks when its target falls, slips out of sight or out of reach, or when the caster is struck down. A healing thread so held is a stream of healing."),
            T("Turned to mending", "Healing Turn",
                "Healing Turn. A verb, a Thread written right after it and Restoration right after the Thread: the three together tie that verb to the mending. The bond is the verb's alone - the other verbs of the circle strike as they are written, and the next circle is a circle of its own. Mind the outer edge: Selective Aid on such a circle sends its untied blows to one's own too. The blow lands as it was written - as wide, as strong - but on living flesh it closes wounds instead of opening them, the worst first. A release so written is a burst of healing; a lift raises gently and sets down; pressing and throwing do nothing. The turned blow starts no fire, breaks no wall, stuns no one. A machine has nothing to mend and takes the blow as it is. Who is reached follows the circle: all; foes only under Selective Protection; one's own only under Selective Aid."),
            T("Echo", "Echo",
                "Echo. Under the strict rule of modifiers, a modifier beside the element works on the whole circle, and one after a verb or a form works on that one only, up to the next. A verb written again later is its own act with its own modifiers - and it hears an echo of the copy before it: half of all that copy came to, its echo included - so echoes build up, each copy a little more than the last. What is less than a whole rune still tells: as strength for Amplify, as width for Breadth. Reach is how far the spell is cast and is never echoed."),
            T("Strikes everyone", "Open Blow",
                "Open Blow. A spell's blow knows no sides: whoever stands in it is struck - friend, beast, the caster at their own feet. Only a ward keeps them, or Selective Protection written on the circle."),
            T("Wear of repeats", "Tautology",
                "Tautology. A verb written twice in one section is the same act said again, and it tires: the second acts at half, the third at a quarter, the rest not at all - yet each still draws its Arcana. In another section the same verb is a new act and does not wear. The old masters spread their verbs; they did not pile them."),
            T("Heavy", "Earthen Slowness",
                "Earthen Slowness. Earth does not hurry. A circle of earth - and of what earth makes with others, magma and bog - is written into the world slower than any other: the cast takes longer. Dust rides the air and is spared it."),
            T("Unwieldy", "Unwieldy Stone",
                "Unwieldy Stone. Earth is not quick to turn. A spell with earth in it - earth alone, magma, bog - strikes the place it was aimed at, not the one who stood there: whoever walks away before the cast is spared. Earth and bog make up for it in reach, as wide as by one Breadth; magma does not. Earth that meets the air (dust) rides the wind and follows its target like any other."),
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
                "Confluence. Two neighbouring circles of elements that can meet become a third: fire and water - steam, fire and earth - magma, and so on. The same element again before them only strengthens the pair: fire, fire and earth is a stronger magma. A Closing parts two circles: in a circle's first section, right at its element, from the one before; in its outermost section, from the one after. Each circle keeps its own verbs, but works as what they made."),
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
                "Ward. The Barrier rune guards. Alone in a circle, it lays a ward on whoever it is aimed at - yourself or another - or a dome on the ground. Last in a circle, after something that acts, it lays a ward on the caster - after the blow, not before it. Anywhere else, a dome over the place the spell strikes, guarding whoever stands inside. In a circle with an Aura the dome is raised round the caster and goes with them wherever they walk - a moving dome; those who keep beside them stay under it."),
            T("Warms friends", "Hearth Warmth", "Hearth Warmth. Held fire is a hearth as much as a weapon: those of the caster's side who stand by it bear the cold far better for a day."),
            T("Ground that takes no fire", "Burnt Ground", "Burnt Ground. Ground purged by fire has nothing left to burn: for some hours flames that reach it die."),
            T("Burns infection", "Cleansing Fire", "Cleansing Fire. Fire turned to mending burns back infection and what lives in a wound."),
            T("Better cautery", "Cautery", "Cautery. Growth in a mending of fire: the seared wounds hold better."),
            T("Stores heat|Keeps the water|Keeps the weight|Keeps the breath", "Drawn Store", "Drawn Store. What the Drawing-out rune takes from the target the caster keeps in the body for a while, and the next spell of that element draws on it - stronger."),
            T("Stands longer", "Lasting Stone", "Lasting Stone. Holding and anchors keep shaped stone standing longer."),
            T("Feeds fire", "Fanned Flames", "Fanned Flames. A gust feeds the fires it passes over - careful near your own walls."),
            T("Own Blast", "Own Blast", "Own Blast. Magic does not know its maker. A blow that falls where the caster stands - an aura, a release after a step, a spell aimed at one's own feet - strikes the caster like anyone else. A ward laid by the same circle comes after the blow, too late for it; and a ward of the same fire is put out by a direct blow of it - though not by an aura, which meets wards as the ground does. What keeps the caster is Selective Protection."),
            T("The Thread", "The Thread",
                "The Thread. The Thread rune turns round what stands just inward of it. After a Step the caster's way becomes the target's: the one at the target is drawn to the caster. After a Lift the body is not thrown up but pressed down, by its own weight. After a Shaping the wall does not stand but closes: a ring round the target, a cell nearer at each step, until the stone meets. Right after a Reach the pair ties the spell on to a further place: what is written outward of it leaps from where the spell struck to another place the caster names, within the spell's reach - pair after pair, a chain of held ground; that Reach lengthens nothing. Anywhere else it ties nothing and does nothing."),
            T("Living aura", "Living Aura",
                "Living Aura. An Aura anchored does not strike once and fade: it stays round the caster and lets the circle's verbs out again every few seconds, wherever the caster goes. What is written between the Aura and its first verb tunes the aura: each Anchor - three strikes more; each Breadth - a cell wider, and a half again as slow; Amplify - stronger strikes. Each strike is half the cast; a blast within it is half as wide, and bursts at the caster's feet. It strikes whoever stands in it, the caster too - Selective Protection keeps the caster's people. A further circle of the same element in the same spell, with an Aura of its own, widens the first rather than raising a second. Small circles never hold it wider than eight cells. An aura seeks the living, not the ground: its blasts spare the plants and set no grass alight on purpose, its fire spreads from body to body - only where the struck stand may the ground catch after them. A ward of the aura's own element it goes round, untouched and unspent; a ward of another element it breaks on, at the cost of the ward's charge."),
            T("Walls close in", "Closing Ring",
                "Closing Ring. Shaping turned round by the Thread: a ring of stone rises round the target and steps inward, a cell at a time. Whoever stands where the next ring rises is shoved inside; whoever is too big to shove is ground against the stone. At the last step the stone meets and crushes whoever is caught - by their size and weight, through any plate. A wall the shut-in break leaves a gap on that side for every ring after. Holding and Anchors slow the closing."),
        };

        /// <summary>Names the plaques of a reading by the almanac, once.</summary>
        public static void Apply(SpellEffect e)
        {
            if (e == null || e.termed) return;
            e.termed = true;
            for (int i = 0; i < e.extras.Count; i++)
            {
                var kv = e.extras[i];
                // hidden until found in play (SpellLore)
                string hk = SpellLore.KeyOfPlaque(kv.Key);
                if (hk != null && !SpellLore.Known(hk) && !Prefs.DevMode) { e.extras[i] = new KeyValuePair<string, string>("Something hidden", SpellLore.Unknown); continue; }
                foreach (Term t in Terms)
                {
                    Match m = t.match.Match(kv.Key);
                    if (!m.Success) continue;
                    string here = kv.Value;
                    string entry = t.entry;
                    if (kv.Key == "The Thread") entry = SpellLore.ThreadEntry(entry);   // only what has been found of it
                    if (here != null && here.StartsWith(entry)) break;   // named already (a copy of a reading)
                    string body = here.NullOrEmpty() || here == entry ? entry : entry + "\n\nHere: " + here;
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
            var aids = s.weave != null ? new HashSet<RitualSchema>() : null;
            if (s.weave != null) { circles = circles.ToList(); int ix = 0; foreach (SpellCircle k in s.weave.Working) { if (SpellSculpt.AidOf(k)) aids.Add(((List<RitualSchema>)circles)[ix]); ix++; } }
            bool any = false, hiddenAny = false;
            var said = new List<string>();
            foreach (RitualSchema c in circles)
            {
                if (!c.rings.SelectMany(g => g.runes).Any(x => x != null && x.kind == RuneKind.Thread)) continue;
                any = true;
                bool hidden = false;
                if (SpellMods.Pulls(c) && !SpellLore.Known("Thread:Step")) hidden = true;
                else if (SpellMods.Pulls(c)) said.Add("Outward of a Step: the step turns round - whoever stands at the target is drawn to the caster.");
                if (SpellMods.Squeezes(c) && !SpellLore.Known("Thread:Shape")) hidden = true;
                else if (SpellMods.Squeezes(c)) said.Add("Outward of a Shaping: the wall does not stand - a ring of stone rises round the target and closes a cell at a time; whoever is caught in the middle is crushed.");
                SpellMods.Own(c, RuneKind.Lift, out _, out _, out _, out int lt);
                int leaps = SpellHops.Pairs(c.rings.SelectMany(g => g.runes).Where(x => x != null).ToList()).Count;
                if (leaps > 0 && !SpellLore.Known("Thread:Reach")) hidden = true;
                else if (leaps > 0) said.Add("Right after a Reach (" + leaps + "x): the pair does not lengthen the cast - what is written outward of it leaps on from where the spell struck to another place you name, within the spell's reach of it.");
                if (lt > 0 && !SpellLore.Known("Thread:Lift")) hidden = true;
                else if (lt > 0) said.Add("Outward of a Lift: the lift turns round - the body is pressed into the ground by its own weight instead of thrown up.");
                if (aids != null && aids.Contains(c))
                {
                    if (!SpellLore.Known("Thread:Aid")) hidden = true;
                    else said.Add("Beside two Barriers on the outer edge: the blow goes to the caster's own (Selective Aid).");
                }
                if (hidden) hiddenAny = true;
            }
            if (!any) return null;
            if (hiddenAny) said.Add(SpellLore.Unknown);
            return said.Count > 0 ? string.Join("\n", said.Distinct())
                : "Not outward of a Step, a Lift or a Shaping here: it ties nothing and does nothing. Write it right after one of them to turn it round.";
        }
    }
}
