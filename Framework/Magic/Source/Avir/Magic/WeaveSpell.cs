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

        public static int Reach(SpellCircle root) => root.Working.Sum(c => c.OwnRunes.Count(r => r.kind == RuneKind.Reach));

        public static float Cost(SpellCircle root)
        {
            float cost = 0f;
            foreach (SpellCircle c in root.Working) cost += (2f + c.OwnRunes.Count() + (c.element != null ? 1 : 0)) * (c.Great ? 1.5f : 1f);
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
            for (int k = 0; k < order.Count; k++)
            {
                SpellCircle c = order[k];
                bool met = pairs.TryGetValue(c, out ComboKind ck);
                SpellEffect one = met ? ComboText.Explain(ck, c.AsSchema()) : SpellText.ExplainSchema(c.AsSchema());
                string where = "";
                if (k > 0)
                {
                    RitualReading prev = RitualEvaluator.Read(order[k - 1].AsSchema());
                    bool step = prev.verbs.Any(v => v.kind == RuneKind.Step), aura = prev.verbs.Any(v => v.kind == RuneKind.Aura);
                    where = step && !SpellMods.Pulls(order[k - 1].AsSchema()) ? "then, once the caster is there, round them: " : step || aura ? "then, by the caster: " : "then: ";
                }
                parts.Add((order.Count > 1 ? (k + 1) + ". " : "") + where + (met ? Combos.Label(ck).CapitalizeFirst() + " (" + c.element.label + ")" : c.element.LabelCap) + " - " + one.main.TrimEnd('.'));
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
                if (g.element.kind == RuneKind.Life) parts.Add(g.inner.Count > 0 ? "and Life round it: every friend the spell touches is healed" : "Life alone: it heals friends at the point");
                else parts.Add(g.element.LabelCap + (g.inner.Count > 0 ? " round it" : "") + " (not made yet)");
            }
            e.main = string.Join(order.Count > 1 ? "\n" : "; ", parts) + ".";
            if (order.Count > 1) e.extras.Insert(0, new KeyValuePair<string, string>("In order", "The circles act one after another, from the heart outward: those round the heart first, " + (MapComponent_SpellChain.Widdershins(root) ? "counter-clockwise from the top (Darkness and Space turn the spell the other way)" : "clockwise from the top") + ", then those inside the great circles. Each waits until the one before it has ended, and strikes where it ended."));
            foreach (var g in smalls.GroupBy(c => c.element).Where(g => g.Key != null && g.Count() > 1))
                e.extras.Insert(0, new KeyValuePair<string, string>(g.Key.LabelCap + " ×" + g.Count() + ": resonance", "Circles of the same element strengthen each other: +" + (25 * (g.Count() - 1)) + "% to each."));
            foreach (ComboKind ck in pairs.Values.Distinct()) e.extras.Insert(0, new KeyValuePair<string, string>(Combos.Label(ck).CapitalizeFirst() + ": two elements met", ComboText.Short(ck)));
            if (Wraps(root, RuneKind.Life)) e.extras.Insert(0, new KeyValuePair<string, string>("Life wraps", "Friends within the spell's reach are healed as it strikes."));
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
            foreach (SpellCircle g in Greats(root))
            {
                if (g.element?.kind != RuneKind.Life) continue;
                float heal = (6f + 3f * g.OwnRunes.Count(x => x.kind == RuneKind.Restore || x.kind == RuneKind.Growth)) * power;
                foreach (Pawn q in GenRadial.RadialDistinctThingsAround(target.Cell, map, radius + (g.inner.Count > 0 ? 1f : 0f), true).OfType<Pawn>().ToList())
                {
                    if (q.Dead || q.HostileTo(caster)) continue;
                    float left = heal;
                    foreach (Hediff_Injury inj in q.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => i.CanHealNaturally() || i.CanHealFromTending()).ToList())
                    {
                        if (left <= 0f) break;
                        float h = Mathf.Min(left, inj.Severity); inj.Heal(h); left -= h;
                    }
                    FleckMaker.ThrowMetaIcon(q.Position, map, FleckDefOf.HealingCross);
                }
            }
        }

        /// <summary>A lost hold: every element's price - fire's raging flame among them.</summary>
        public static void Slip(Pawn caster, SpellCircle root)
        {
            if (Small(root).Any(c => FireMagic.Is(c.element))) FireMagic.Raging(caster);
            if (Small(root).Any(c => WaterMagic.Is(c.element))) WaterMagic.Chilled(caster);
            if (Small(root).Any(c => EarthMagic.Is(c.element))) EarthMagic.Petrified(caster);
            foreach (ComboKind k in Combos.Pairs(root).Values.Distinct()) ComboMagic.Price(k, caster);
            if (Small(root).Any(c => AirMagic.Is(c.element))) AirMagic.Winded(caster);
        }

        /// <summary>A spell that takes the mage under the ground: it can come up anywhere on the map.</summary>
        public static bool Burrows(SpellCircle root) =>
            Small(root).Any(c => EarthMagic.Is(c.element) && c.OwnRunes.Any(x => x.kind == RuneKind.Step) && !SpellMods.Pulls(c.AsSchema()));
    }
}
