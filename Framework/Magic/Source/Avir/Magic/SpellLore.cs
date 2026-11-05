using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using RimWorld;
using Verse;

namespace Avir.Magic
{
    /// <summary>Hidden terms (Магия-правила.md, the author's rule): the codex does not tell everything at once. What a verb
    /// does, and what a sequence of runes comes to, is always told; but the price of a slipped hold is unknown until the
    /// first slip of that element, and what the Thread does after a Step, a Lift, a Shaping, a Reach or a pair of Barriers
    /// is unknown until it is first cast so. Found once, it is known for the whole game - and Arlie has a word for it.
    /// A setting turns the hiding off.</summary>
    public static class SpellLore
    {
        public static bool Hiding => AvirMod.Settings == null || AvirMod.Settings.magicHiddenTerms;

        public static bool Known(string key) => !Hiding || key == null || (GameComponent_SpellLore.Get?.known.Contains(key) ?? true);

        public static void Learn(string key)
        {
            var g = GameComponent_SpellLore.Get;
            if (g == null || key == null || !g.known.Add(key) || !Hiding) return;
            string line = Line(key);
            Messages.Message("Found: " + Name(key) + "." + (line != null ? "\nArlie: \"" + line + "\"" : ""), MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>The price of slipping, by the plaque's name ("Price: raging flame" and so on).</summary>
        public static string PriceKey(string plaque) => plaque.StartsWith("Price") ? "Price:" + plaque.Substring(plaque.IndexOf(':') + 1).Trim() : null;

        /// <summary>Which hidden thing a plaque tells of (null: never hidden).</summary>
        public static string KeyOfPlaque(string plaque)
        {
            if (plaque == null) return null;
            if (plaque.StartsWith("Price")) return PriceKey(plaque);
            switch (plaque)
            {
                case "Walls close in": return "Thread:Shape";
                case "Turned to its own": return "Thread:Aid";
                case "The step turned round": return "Thread:Step";
                case "Turned to mending": return "Thread:Heal";
                case "Drawn in": return "Thread:Implode";
                case "Mends and strikes own": case "Mends foes, strikes own": return "Thread:Heal";
            }
            return null;
        }

        private static string Name(string key)
        {
            switch (key)
            {
                case "Thread:Step": return "the Thread turns a Step round - the target is drawn to the caster";
                case "Thread:Lift": return "the Thread turns a Lift round - the body is pressed into the ground";
                case "Thread:Shape": return "the Thread turns a Shaping round - the walls close in";
                case "Thread:Reach": return "Reach and Thread - the spell leaps on to another place";
                case "Thread:Aid": return "two Barriers and a Thread - the blow goes to one's own (Selective Aid)";
                case "Thread:Heal": return "verb, Thread, Restoration - the blow mends instead of wounding";
                case "Thread:Implode": return "the Thread turns a Release round - the blast draws bodies in to its heart";
            }
            if (key.StartsWith("Path:") && System.Enum.TryParse(key.Substring(5), out ComboKind pk)) return "the secret path of " + Combos.Label(pk) + " - " + SpellPaths.Name(pk);
            return key.StartsWith("Price:") ? "the price of a slipped hold - " + key.Substring(6) : key;
        }

        private static string Line(string key)
        {
            switch (key)
            {
                case "Thread:Step": return "Oh, so you found it. The step goes the other way now. Do try not to pull a bear.";
                case "Thread:Lift": return "Down instead of up. The ground didn't ask for company, but it got some.";
                case "Thread:Shape": return "Walls that walk inward. Whoever is inside won't like the next part.";
                case "Thread:Reach": return "So now it hops. Like a flea. A very hot flea.";
                case "Thread:Aid": return "Two barriers and a thread, and the blow forgets your enemies. Rude to them, kind to yours.";
                case "Thread:Implode": return "Everyone into one heap. Tidy. Now drop something heavy on it.";
                case "Thread:Heal": return "You hit them and they got better. I've seen surgeons with worse manners. And worse results.";
            }
            if (key.StartsWith("Path:")) return "There it is. I told you it wanted to be something else. Write it down before you forget how you did it.";
            return key.StartsWith("Price:") ? "That's what a slipped hold costs. Now you know. Now you'll remember." : null;
        }

        /// <summary>A circle cast: what of the Thread it shows.</summary>
        public static void Cast(RitualSchema circle, bool aid)
        {
            if (circle == null) return;
            if (aid) Learn("Thread:Aid");
            if (SpellMods.Pulls(circle)) Learn("Thread:Step");
            if (SpellMods.Squeezes(circle)) Learn("Thread:Shape");
            SpellMods.Own(circle, RuneKind.Lift, out _, out _, out _, out int lt);
            if (SpellHeal.Turns(circle)) Learn("Thread:Heal");
            if (SpellHeal.Implodes(circle)) Learn("Thread:Implode");
            else if (lt > 0) Learn("Thread:Lift");
            if (SpellHops.Pairs(circle.rings.SelectMany(g => g.runes).Where(x => x != null).ToList()).Count > 0) Learn("Thread:Reach");
        }

        /// <summary>A hold slipped: the prices of the elements in it are known now.</summary>
        public static void Slipped(SpellCircle root)
        {
            if (root == null) return;
            foreach (SpellCircle c in WeaveSpell.Small(root))
            {
                if (c.element == null) continue;
                RuneKind k = c.element.kind;
                Learn(k == RuneKind.Fire ? "Price:raging flame" : k == RuneKind.Water ? "Price:chilled" : k == RuneKind.Earth ? "Price:stone-bound" : k == RuneKind.Air ? "Price:winded" : null);
            }
            foreach (ComboKind k in Combos.Pairs(root).Values.Distinct()) Learn("Price:" + Combos.Label(k));
        }

        /// <summary>The almanac's entry on the Thread, told only as far as it has been found.</summary>
        public static string ThreadEntry(string full)
        {
            if (!Hiding) return full;
            var parts = new List<string> { "The Thread. The Thread rune turns round what stands just inward of it." };
            if (Known("Thread:Step")) parts.Add("After a Step the caster's way becomes the target's: the one at the target is drawn to the caster.");
            if (Known("Thread:Lift")) parts.Add("After a Lift the body is not thrown up but pressed down, by its own weight.");
            if (Known("Thread:Shape")) parts.Add("After a Shaping the wall does not stand but closes on the target, a cell nearer at each step.");
            if (Known("Thread:Reach")) parts.Add("Right after a Reach it ties the spell on to a further place: what is outward of it leaps on.");
            if (Known("Thread:Aid")) parts.Add("Beside two Barriers on the outer edge it turns the blow to the caster's own.");
            if (Known("Thread:Implode")) parts.Add("After a Release the blast does not throw bodies out but draws them in to its heart.");
            if (Known("Thread:Heal")) parts.Add("Between a verb and Restoration it ties the verb to the mending: what would wound the living heals it.");
            parts.Add(parts.Count < 8 ? "What else it turns is found by casting." : "Anywhere else it ties nothing and does nothing.");
            return string.Join(" ", parts);
        }

        public const string Unknown = "Not yet known. Something here is not what it seems - cast it and see.";
    }

    public class GameComponent_SpellLore : GameComponent
    {
        public HashSet<string> known = new HashSet<string>();
        public static GameComponent_SpellLore Get => Current.Game?.GetComponent<GameComponent_SpellLore>();

        public GameComponent_SpellLore(Game g) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref known, "spellLore", LookMode.Value);
            if (known == null) known = new HashSet<string>();
        }
    }
}
