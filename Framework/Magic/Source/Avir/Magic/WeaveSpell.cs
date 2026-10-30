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
            return Mathf.RoundToInt((sorcerer ? 60f : 150f) * (1f + 0.25f * (n - 1)) * (1f + 0.05f * Reach(root)));
        }

        /// <summary>What it asks of composure: the hardest circle, and a little more for every other one.</summary>
        public static float Risk(SpellCircle root)
        {
            var all = root.Working.ToList();
            float max = all.Max(c => RitualEvaluator.Read(c.AsSchema()).Risk(1f, 1f, 0f));
            return max + 0.08f * (all.Count - 1);
        }

        public static int Resonance(SpellCircle root, RuneDef el) => Small(root).Count(c => c.element == el);

        public static bool Steam(SpellCircle root)
        {
            var els = Small(root).Select(c => c.element?.kind).ToList();
            return els.Contains(RuneKind.Fire) && els.Contains(RuneKind.Water);
        }

        public static bool Wraps(SpellCircle root, RuneKind k) => Greats(root).Any(c => c.element?.kind == k && c.inner.Count > 0);

        // ------------------------------------------------------------------------------ the effect, in words

        public static SpellEffect Explain(SpellCircle root)
        {
            var e = new SpellEffect();
            var smalls = Small(root);
            var greats = Greats(root);
            if (root.Working.All(c => c.element == null)) { e.main = "An empty circle. Write an element into its centre: fire for now - the others come later."; return e; }
            var parts = new List<string>();
            foreach (var g in smalls.GroupBy(c => c.element))
            {
                if (g.Key == null) continue;
                if (!FireMagic.Is(g.Key) && !WaterMagic.Is(g.Key)) { parts.Add(g.Key.LabelCap + " (not made yet)"); continue; }
                SpellEffect one = SpellText.ExplainSchema(g.First().AsSchema());
                parts.Add((g.Count() > 1 ? g.Count() + " circles of " + g.Key.label + ": " : "") + one.main.TrimEnd('.'));
                foreach (var x in one.extras) if (!e.extras.Any(y => y.Key == x.Key)) e.extras.Add(x);
            }
            foreach (SpellCircle g in greats)
            {
                if (g.element == null) { parts.Add("a great circle with no element yet"); continue; }
                if (g.element.kind == RuneKind.Life) parts.Add(g.inner.Count > 0 ? "and Life round it: every friend the spell touches is healed" : "Life alone: it heals friends at the point");
                else parts.Add(g.element.LabelCap + (g.inner.Count > 0 ? " round it" : "") + " (not made yet)");
            }
            e.main = string.Join("; ", parts) + ".";
            foreach (var g in smalls.GroupBy(c => c.element).Where(g => g.Key != null && g.Count() > 1))
                e.extras.Insert(0, new KeyValuePair<string, string>(g.Key.LabelCap + " ×" + g.Count() + ": resonance", "Circles of the same element strengthen each other: +" + (25 * (g.Count() - 1)) + "% to each."));
            if (Steam(root)) e.extras.Insert(0, new KeyValuePair<string, string>("Steam: fire + water", "Where fire and water strike together, scalding steam bursts at the point."));
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
            foreach (SpellCircle c in Small(root))
            {
                if (c.element == null) continue;
                float p = power * (1f + 0.25f * (Resonance(root, c.element) - 1));
                RitualSchema one = c.AsSchema();
                RitualReading r = RitualEvaluator.Read(one);
                if (FireMagic.Is(c.element)) FireMagic.Cast(caster, one, r, target, p);
                else if (WaterMagic.Is(c.element)) WaterMagic.Cast(caster, one, r, target, p);
            }
            float radius = 2f + 1.5f * root.Working.Sum(c => c.OwnRunes.Count(x => x.kind == RuneKind.Breadth));
            if (Steam(root))
            {
                foreach (Pawn q in GenRadial.RadialDistinctThingsAround(target.Cell, map, radius, true).OfType<Pawn>().ToList())
                    if (q.HostileTo(caster)) q.TakeDamage(new DamageInfo(DamageDefOf.Burn, 5f * power, 0f, -1f, caster));
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
        }
    }
}
