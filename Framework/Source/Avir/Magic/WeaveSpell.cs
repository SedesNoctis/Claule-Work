using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    // =====================================================================================
    // A spell written as circles round circles. Every small circle (fire, water, earth, air) is cast on its own, as
    // its runes say; circles of the same element strengthen each other (resonance); fire and water together scald
    // (steam). A great circle wraps all within it: Life heals the friends the spell touches; the other great
    // elements are not made yet. Its own sections, with no small circles inside, are its pure effect.
    // =====================================================================================
    public static class WeaveSpell
    {
        public static List<SpellCircle> Small(SpellCircle root) => root.Working.Where(c => !c.Great).ToList();
        public static List<SpellCircle> Greats(SpellCircle root) => root.Working.Where(c => c.Great).ToList();

        public static int Runes(SpellCircle root) => root.Working.Sum(c => c.OwnRunes.Count() + (c.element != null ? 1 : 0));

        /// <summary>The Reach that lengthens the cast: a Reach paired with a Thread leaps instead.</summary>
        public static int Reach(SpellCircle root) => root.Working.Sum(c => SpellHops.CastReach(c));

        /// <summary>What width and length cost over a plain rune (Fix243, the author). Any Breadth covers more ground, so it
        /// costs 1 Arcana more (2 in all). A Breadth or a Reach bound to a Beam - written outward of the Beam, before the next
        /// verb or form - costs 2 more (3 in all): the beam strikes, or heals, along all its length and width. A Reach bound
        /// to anything else only carries the cast further and costs as a plain rune. An empty beam (no verb before it) pays
        /// for its Reach, not for its Breadth (Fix244).</summary>
        public const float BreadthPrice = 1f, BeamRunePrice = 2f;
        public static float BeamPrice(List<RuneDef> runes)
        {
            float extra = 0f;
            bool onBeam = false, verbSeen = false, empty = false;
            foreach (RuneDef x in runes)
            {
                if (x == null) continue;
                if (x.role == RuneRole.Verb) verbSeen = true;
                if (x.role == RuneRole.Verb || x.role == RuneRole.Form) { onBeam = x.kind == RuneKind.Beam; if (onBeam) empty = !verbSeen; continue; }
                // an empty beam (no verb before it) lets nothing out on its way: its Breadth is not paid for (Fix244)
                if (x.kind == RuneKind.Breadth) extra += onBeam ? (empty ? 0f : BeamRunePrice) : BreadthPrice;
                else if (x.kind == RuneKind.Reach && onBeam) extra += BeamRunePrice;
            }
            return extra;
        }

        public static float Cost(SpellCircle root)
        {
            float cost = 0f;
            foreach (SpellCircle c in root.Working) cost += (2f + c.OwnRunes.Count() + (c.element != null ? 1 : 0) + BeamPrice(c.OwnRunes.ToList())) * (c.Great ? 1.5f : 1f);
            int els = root.Working.Where(c => c.element != null).Select(c => c.element).Distinct().Count();
            return cost + 3f * Mathf.Max(0, els - 1);
        }

        public static int CastTicks(bool sorcerer, SpellCircle root)
        {
            int n = root.Working.Count();
            int breadth = root.Working.Sum(c => c.OwnRunes.Count(r => r.kind == RuneKind.Breadth));
            return Mathf.RoundToInt((sorcerer ? 60f : 150f) * (1f + 0.25f * (n - 1)) * (1f + 0.05f * Reach(root)) * (1f + 0.08f * breadth) * (Heavy(root) ? AvirMod.Settings.earthCastFactor : 1f));
        }

        /// <summary>Earth is heavy: a spell with earth in it, or magma, or bog, is slower to cast (dust rides the air and is not).</summary>
        public static bool Heavy(SpellCircle root)
        {
            var pairs = Combos.Pairs(root);
            return Small(root).Any(c => EarthMagic.Is(c.element) && (!pairs.TryGetValue(c, out ComboKind k) || k == ComboKind.Magma || k == ComboKind.Bog));
        }

        /// <summary>What it asks of composure: the hardest circle, and a little more for every other one.</summary>
        public static float Risk(SpellCircle root)
        {
            var all = root.Working.ToList();
            float max = all.Max(c => RitualEvaluator.Read(c.AsSchema()).Risk(1f, 1f, 0f));
            return max + 0.08f * (all.Count - 1);
        }

        public static int Resonance(SpellCircle root, RuneDef el) => Small(root).Count(c => c.element == el);

        /// <summary>Circles of one element strengthen each other, less and less (Магия-правила.md): +25% for the 2nd to
        /// the 5th, +10% for each past that, no more than x2.5 - many circles are a stronger spell, not an endless one.</summary>
        public static float ResonanceFactor(int n) => Mathf.Min(2.5f, 1f + 0.25f * Mathf.Clamp(n - 1, 0, 4) + 0.1f * Mathf.Max(0, n - 5));

        public static bool Steam(SpellCircle root) => Combos.Pairs(root).ContainsValue(ComboKind.Steam);

        /// <summary>Water and air together: a storm - the wind's chained strike turns to lightning.</summary>
        public static bool Storm(SpellCircle root) => Combos.Pairs(root).ContainsValue(ComboKind.Storm);

        /// <summary>A Life circle in words: its acts in their order (a verb tied to Withering or Restoration).</summary>
        public static string LifeText(SpellCircle g)
        {
            var bits = new List<string>();
            foreach (LifeMagic.Act a in LifeMagic.Read(g, out _))
            {
                bool take = a.side == RuneKind.Drain;
                string t;
                switch (a.verb)
                {
                    case RuneKind.Release: t = take ? "a burst of withering" : "a burst of mending"; break;
                    case RuneKind.Beam: t = take ? "a withering beam" : "a mending beam"; break;
                    case RuneKind.Hold: t = take ? "vines that drink while they hold" : "a cocoon: held still and mended deep"; break;
                    case RuneKind.Growth: t = take ? "life drawn from the green only" : "growth: the plants quicken, the regrowth deepens"; break;
                    case RuneKind.Purge: t = take ? "poison drawn out into the store" : "poison and withering cleansed"; break;
                    case RuneKind.Lift: t = a.pin ? "the body pressed down by its blood (Blood only)" : "the body thrown up by its blood (Blood only)"; break;
                    case RuneKind.Step: t = take ? "a foe dragged to the mage by its own blood (Blood only)" : "one's own drawn gently to the mage (Blood only)"; break;
                    default: t = take ? "Withering at the point" : "mending at the point"; break;
                }
                if (a.trans) t = take ? TransTake(LifeMagic.PairOfText) : TransGive(LifeMagic.PairOfText);
                if (a.hop) t += " - leaping on to the nearest wounded";
                bits.Add(t + (take ? " (into the store)" : " (from the store)"));
            }
            string own = bits.Count > 0 ? string.Join(", then ", bits) : null;
            if (own != null && g.OwnRunes.Any(x => x.kind == RuneKind.Aura) && g.OwnRunes.Any(x => x.kind == RuneKind.Anchor))
                own += " - and as a living aura round the mage, pulse after pulse (beams and steps excepted)";
            if (g.inner.Count > 0) return "and Life round it: " + (own != null ? own + "; and " : "") + "every friend the spell touches is mended from the store";
            return "Life: " + (own ?? "nothing tied yet - write Withering or Restoration");
        }

        private static string TransTake(RuneKind p) => p == RuneKind.Water ? "water turned to stone, for good" : p == RuneKind.Earth ? "plants turned to emerald sculptures of themselves" : p == RuneKind.Fire ? "ash taken into the store" : p == RuneKind.Air ? "rot taken into the store, the ground richer for it" : "the dead turned to fertile earth";
        private static string TransGive(RuneKind p) => p == RuneKind.Water ? "stone turned to water, for good" : p == RuneKind.Earth ? "emerald sculptures let go - plants again" : "the ground quickened - more fertile for ten days";

        /// <summary>A Closing in the first (innermost) section of a great circle parts it from the small circles inside it (the
        /// author, Fix213-214): they work as their own elements, and their meetings are their own. A Closing inside the small
        /// circles parts only them from one another; one in the great circle's outermost section parts great from great.</summary>
        public static bool SealedGreat(SpellCircle box) => box != null && box.Great && box.element != null && box.sections.Count > 0
                                                           && (box.sections[0].runes ?? new List<RuneDef>()).Any(x => x != null && x.kind == RuneKind.Closing);

        /// <summary>The great circle a small one stands in, if that one is sealed from it.</summary>
        public static SpellCircle SealedBox(SpellCircle root, SpellCircle c)
        {
            if (c == null || c.Great || c.element == null) return null;
            for (SpellCircle b = root?.ParentOf(c); b != null && !b.binding; b = root.ParentOf(b)) if (b.Great) return SealedGreat(b) ? b : null;
            return null;
        }

        /// <summary>A small circle sealed from its great circle is cast by its own elements (the author, Fix214): the mage's skill in
        /// the great element does not lift it - the strength is the small elements' own; the great one only adds its shade.</summary>
        public static float SealedPower(Pawn caster, SpellCircle root, SpellCircle c, ComboKind met)
        {
            if (SealedBox(root, c) == null) return 1f;
            Hediff_ArcaneCore core = Mages.Core(caster);
            if (core == null) return 1f;
            float whole = MageClass.PowerFactor(core, new RitualSchema { weave = root });
            float own = MageClass.PowerFactorOf(core, met != ComboKind.None ? Combos.Parts(met) : new[] { c.element.kind });
            return whole > 0.01f ? own / whole : 1f;
        }

        public static bool Wraps(SpellCircle root, RuneKind k) => Greats(root).Any(c => c.element?.kind == k && c.inner.Count > 0);

        // ------------------------------------------------------------------------------ the effect, in words

        public static SpellEffect Explain(SpellCircle root)
        {
            var e = new SpellEffect();
            var smalls = Small(root);
            var greats = Greats(root);
            if (root.Working.All(c => c.element == null)) { e.main = "An empty circle. Write an element into its centre: fire, water, earth or air."; return e; }
            var parts = new List<string>();
            // one after another, from the heart outward: each strikes where the one before it ended
            var order = MapComponent_SpellChain.Order(root);
            var pairs = Combos.Pairs(root);
            var seconds2 = SecondOrder.Of(root);
            for (int k = 0; k < order.Count; k++)
            {
                SpellCircle c = order[k];
                if (LightMagic.IsLink(root, c))
                {
                    parts.Add((order.Count > 1 ? (k + 1) + ". " : "") + (k > 0 ? "then: " : "") + (c.Great ? "" : "a small circle of ") + LightMagic.Text(c));
                    continue;
                }
                if (LifeMagic.IsLink(root, c))
                {
                    LifeMagic.PairOfText = LifeMagic.PairOf(root, c.Great ? c : root.ParentOf(c));
                    string pn = LifeMagic.PairName(LifeMagic.PairOfText) ?? LifeMagic.AspectName(LifeMagic.AspectOf(root, c.Great ? c : root.ParentOf(c))) ?? LifeMagic.MixName(LifeMagic.MixOf(root, c.Great ? c : root.ParentOf(c)));
                    parts.Add((order.Count > 1 ? (k + 1) + ". " : "") + (k > 0 ? "then: " : "") + (c.Great ? "" : "a small circle of ") + (pn != null ? pn + " - " : "") + LifeText(c));
                    continue;
                }
                bool met = pairs.TryGetValue(c, out ComboKind ck);
                SpellEffect one = met ? ComboText.Explain(ck, c.AsSchema()) : SpellText.ExplainSchema(c.AsSchema());
                string where = "";
                if (k > 0)
                {
                    RitualReading prev = RitualEvaluator.Read(order[k - 1].AsSchema());
                    bool step = prev.verbs.Any(v => v.kind == RuneKind.Step), aura = prev.verbs.Any(v => v.kind == RuneKind.Aura);
                    where = step && !SpellMods.Pulls(order[k - 1].AsSchema()) ? "then, once the caster is there, round them: " : step || aura ? "then, by the caster: " : "then: ";
                }
                // a circle in a meeting of the second order is named by what the three made (Ash, Obsidian...)
                string nameOf = met ? Combos.Label(ck).CapitalizeFirst() : c.element.LabelCap.ToString();
                if (seconds2.TryGetValue(c, out SecondKind sy)) nameOf = SecondOrder.Label(sy).CapitalizeFirst() + " - " + Combos.Label(SecondOrder.PairOf(sy)) + " meeting " + SecondOrder.Third(sy).ToString().ToLower();
                parts.Add((order.Count > 1 ? (k + 1) + ". " : "") + where + nameOf + " (" + c.element.label + ")" + " - " + one.main.TrimEnd('.'));
                // the order of its own runes, numbered by the circle when there are several
                one.extras.RemoveAll(x => x.Key == "In turn");
                string turns = SpellText.Turns(c.AsSchema());
                if (turns != null) e.extras.Add(new KeyValuePair<string, string>(order.Count > 1 ? "In turn (" + (k + 1) + ")" : "In turn",
                    turns));
                foreach (var x in one.extras) if (!e.extras.Any(y => y.Key == x.Key)) e.extras.Add(x);
            }
            foreach (SpellCircle g in greats)
            {
                if (g.element == null) { parts.Add("a great circle with no element yet"); continue; }
                if (g.element.kind == RuneKind.Life) { if (g.inner.Count > 0) parts.Add("Life round its circles: every friend they touch is mended from the store as each strikes"); }
                else if (g.element.kind == RuneKind.Light)
                {
                    if (g.inner.Any(x => x.element != null))
                        parts.Add(LightMagic.Sealed(g) ? "Light sealed round its elements: they work as themselves, and every reflector echoes them"
                                  : LightMagic.Zenith(g) ? "Zenith - Light meeting Fire, energy: its Fire says what is done at the point - Restoration heals friends hard and at once, Growth gives strength, each Hold lays a seal on every friend (it flares and heals once when they are hurt), Purge puts fire out on friends; Release and Beam set white flame on foes - it clings to anything, machines too, water does not put it out, and the one burning is a walking reflector"
                                  : LightMagic.Glint(g) ? "Glint - Light meeting Air: every cast leaves a mirage at the point and one more; a mirage is a reflector and a way - the mage, drafted, steps out of the one nearest where it is sent, and slips into the farthest from its foes when struck close; eight at most. Its Air: Release - a flash on the wind at the point, Beam - along the line; foes are thrown back, fall and are dazzled"
                                  : LightMagic.Mirror(g) ? "Mirror - Light meeting Water: the mage casts only through shining water - any on the map that sees the point - or from itself with a flask of it; every water its light touches shines for sixteen hours; Release in the Water lays new water where it strikes, a Beam a pool at its end"
                                  : LightMagic.Crystal(g) ? "Crystal - Light meeting Earth: the reflectors are crystals, the forms grow crystals, the other circles inside work as themselves and echo"
                                  : "Light round its elements: they work as they are, and every reflector echoes them (a Closing in Light's first section keeps them apart from Light - their strength is then their own)");
                }
                else parts.Add(g.element.LabelCap + (g.inner.Count > 0 ? " round it" : "") + " (not made yet)");
            }
            e.main = string.Join(order.Count > 1 ? "\n" : "; ", parts) + ".";
            if (order.Count > 1) e.extras.Insert(0, new KeyValuePair<string, string>("In order", "The circles act one after another, from the heart outward: those round the heart first, " + (MapComponent_SpellChain.Widdershins(root) ? "counter-clockwise from the top (Darkness and Space turn the spell the other way)" : "clockwise from the top") + ", then those inside the great circles. Each waits until the one before it has ended, and strikes where it ended."));
            foreach (var g in smalls.GroupBy(c => c.element).Where(g => g.Key != null && g.Count() > 1))
                e.extras.Insert(0, new KeyValuePair<string, string>(g.Key.LabelCap + " ×" + g.Count() + ": resonance", "Circles of the same element strengthen each other: +" + (25 * (g.Count() - 1)) + "% to each."));
            foreach (ComboKind ck in pairs.Values.Distinct()) e.extras.Insert(0, new KeyValuePair<string, string>(Combos.Label(ck).CapitalizeFirst() + ": two elements met", ComboText.Short(ck)));
            if (LightLife.Meets(root))
                e.extras.Insert(0, new KeyValuePair<string, string>("Grove of Lights - Light and Life", "Held one in the other, Life is paid in light: Withering drinks light - reflectors, shining water, lamps, the open sky - not the living, and leaves a blind patch; Restoration grows shining flowers of this land round the healed (living reflectors, they do not age); the reflectors are spirit lights that drift to your people and mend them; the reflectors echo Life too - out of a flower the flower pays (it withers), out of the rest the store. A lost hold breaks it all into wild growth."));
            if (LightMagic.Any(root))
            {
                e.extras.Insert(0, new KeyValuePair<string, string>("Price of Light: Defocus", LightMagic.DefocusText));
                e.extras.Insert(0, new KeyValuePair<string, string>("Reflection", LightMagic.ReflectionText));
                var echoes = LightMagic.EchoCircles(root);
                string what = string.Join(", ", echoes.Select(c => LightMagic.IsLink(root, c) ? "Light" + (c.Great ? "" : " (small)") : (Combos.Pairs(root).TryGetValue(c, out ComboKind ek) ? Combos.Label(ek) : c.element.label)));
                e.extras.Insert(0, new KeyValuePair<string, string>("Echoes: " + echoes.Count, echoes.Count == 0 ? "Nothing here echoes." :
                    "What the reflectors send back, each from where it stands: " + what + ". A full echo takes " + echoes.Count + " reflector" + (echoes.Count > 1 ? "s" : "") + "; a cast raises about as many new ones (fewer in a Crystal). Everything inside a great circle of Light echoes - its small circles first, clockwise, then Light's own runes; the Earth that went into a Crystal is the Crystal itself."));
                if (LightMagic.AnyCrystal(root))
                    e.extras.Insert(0, new KeyValuePair<string, string>("Crystal", "Light meeting Earth. The first circle of Earth inside goes into the Light and makes it Crystal; the other circles stay themselves (Earth, Fire - magma...) and echo. The reflectors are crystals: sturdier, not burning, standing against blasts; each echo keeps three quarters of the cast before, and a crystal's facets may send its circle twice. Fewer of them rise with a cast, but the forms grow them: a wall - a wall of crystals, a cell between; a beam or a release - unstable crystals that answer once and burst into shards; a hold - a cage of crystals round whom it holds. Earth's longer reach stays, its stone underfoot does not help. When crystals break, their shards cut whoever stands close. A crystal that loses focus breaks alone - the rest hold. A Barrier lays a ward of Crystal: it sends spells back and stops shards."));
                if (LightMagic.Circles(root).Any(LightMagic.Sealed))
                    e.extras.Insert(0, new KeyValuePair<string, string>("Sealed in Light", "A Closing in the first section of a great circle of Light keeps the great element pure: the circles of elements inside it work as themselves - fire and earth make magma - and every reflector echoes them too, from where it stands: their lines and walls start at the reflector, a ward on the way may stop them. A Step or an Aura belongs to the mage's own body and does not echo; a Barrier echoes as a dome only."));
            }
            if (Greats(root).Any(g => SealedGreat(g) && g.inner.Any(k => !k.Great && k.element != null)))
                e.extras.Insert(0, new KeyValuePair<string, string>("Sealed Great Circle", "A Closing in a great circle's first section parts it from the small circles inside: they work as their own elements, their meetings are their own, and their strength is the mage's skill in those elements alone - the great element only adds its shade (Life mends whom they touch, Light echoes them)."));
            if (Wraps(root, RuneKind.Life)) e.extras.Insert(0, new KeyValuePair<string, string>("Life Wraps", "Friends within the spell's reach are mended as it strikes - out of the store of life."));
            foreach (RuneKind lk in LifeMagic.Circles(root).Select(g => LifeMagic.PairOf(root, g)).Where(k => k != LifeMagic.None).Distinct())
                e.extras.Insert(0, new KeyValuePair<string, string>(LifeMagic.PairName(lk), LifeMagic.PairText(lk)));
            foreach (PartialKind pk in Partials.InSpell(root).Distinct())
                e.extras.Insert(0, new KeyValuePair<string, string>(pk.ToString(), Partials.Text(pk)));
            foreach (LifeMix lm in LifeMagic.Circles(root).Select(g => LifeMagic.MixOf(root, g)).Where(k => k != LifeMix.None).Distinct())
                e.extras.Insert(0, new KeyValuePair<string, string>(lm.ToString(), LifeMagic.MixText(lm)));
            foreach (RuneKind la in LifeMagic.Circles(root).Select(g => LifeMagic.AspectOf(root, g)).Where(k => k != LifeMagic.None).Distinct())
                e.extras.Insert(0, new KeyValuePair<string, string>(LifeMagic.AspectName(la), LifeMagic.AspectText(la)));
            if (LifeMagic.Links(root).Any(g => g.OwnRunes.Any(x => x.kind == RuneKind.Step) && g.OwnRunes.Any(x => x.kind == RuneKind.Anchor)))
                e.extras.Insert(0, new KeyValuePair<string, string>("Living Gate", "Step and Anchor in a circle of Life: a gate of roots grows where the spell lands (15 of the store). Two gates or more - a colonist can go through them (a command on the colonist); a gate can draw a friend (mending, light) or a foe (spent) through to itself. The druid's store pays for each passage; with it empty, the walker pays - weakened and sleepy. A gate lives on those who go through it: unused, it dries and dies in about a week."));
            if (LifeMagic.Circles(root).Any())
            {
                e.extras.Insert(0, new KeyValuePair<string, string>("Symbiosis", "Life makes nothing; it only moves. Withering fills the store of life, the other verbs give it away - with the store empty they give nothing."));
                if (!LifeMagic.Links(root).Any(g => g.OwnRunes.Any(x => x.kind == RuneKind.Drain))) e.extras.Insert(0, new KeyValuePair<string, string>("Giving from the Store", "No Withering here: this spell only spends the store of life a former spell filled."));
            }
            // the price chip last, once
            var price = e.extras.Where(x => x.Key.StartsWith("Price")).ToList();
            e.extras.RemoveAll(x => x.Key.StartsWith("Price"));
            if (price.Count > 0) e.extras.Add(price[0]);
            return e;
        }

        // ------------------------------------------------------------------------------ the cast

        public static void Resolve(Pawn caster, RitualSchema s, LocalTargetInfo target, float power)
        {
            SpellCircle root = s.weave;
            Map map = caster.Map;
            // one circle after another, from the heart outward - each where the one before it ended
            MapComponent_SpellChain.Start(caster, root, target, power);
        }

        /// <summary>What the circles leave together, once the last of them has struck: steam, Life round them.</summary>
        public static void AfterCircles(Pawn caster, SpellCircle root, LocalTargetInfo target, float power, Map map)
        {
            float radius = 2f + 1.5f * root.Working.Sum(c => c.OwnRunes.Count(x => x.kind == RuneKind.Breadth));
            if (false)
            {
                foreach (Pawn q in GenRadial.RadialDistinctThingsAround(target.Cell, map, radius, true).OfType<Pawn>().ToList())
                    if (MagicFx.Foe(q, caster)) q.TakeDamage(new DamageInfo(DamageDefOf.Burn, 5f * power, 0f, -1f, caster));
                for (int i = 0; i < 6; i++) FleckMaker.ThrowSmoke(target.Cell.ToVector3Shifted() + new Vector3(Rand.Range(-radius, radius), 0f, Rand.Range(-radius, radius)), map, 2f);
            }
            // Life (LifeMagic): its own runes where the spell ended; round small circles it mends whom the spell touched -
            // both out of the store of life, never for nothing
            // Life acts in the queue now (MapComponent_SpellChain.Walk), each circle of it in its turn
        }

        /// <summary>A lost hold: every element's price - fire's raging flame among them.</summary>
        public static void Slip(Pawn caster, SpellCircle root)
        {
            SpellLore.Slipped(root);   // the price is known now
            // a small element in a meeting of Life pays as the meeting, not as itself (the author's rule)
            var plain = Small(root).Where(c => LifeMagic.PairOfCircle(root, c) == LifeMagic.None).ToList();
            if (plain.Any(c => FireMagic.Is(c.element))) FireMagic.Raging(caster);
            if (plain.Any(c => WaterMagic.Is(c.element))) WaterMagic.Chilled(caster);
            if (plain.Any(c => EarthMagic.Is(c.element))) EarthMagic.Petrified(caster);
            foreach (ComboKind k in Combos.Pairs(root).Values.Distinct()) ComboMagic.Price(k, caster);
            foreach (RuneKind lp in Small(root).Select(c => LifeMagic.PairOfCircle(root, c)).Where(k => k != LifeMagic.None).Distinct()) LifeMagic.PayPair(lp, caster);
            LifeMagic.Slip(caster, root);
            if (plain.Any(c => AirMagic.Is(c.element))) AirMagic.Winded(caster);
        }

        /// <summary>A spell that takes the mage under the ground: it can come up anywhere on the map.</summary>
        public static bool Burrows(SpellCircle root) =>
            Small(root).Any(c => EarthMagic.Is(c.element) && c.OwnRunes.Any(x => x.kind == RuneKind.Step) && !SpellMods.Pulls(c.AsSchema()));
    }
}
