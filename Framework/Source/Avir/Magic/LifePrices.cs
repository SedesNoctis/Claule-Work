using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The price of slipping inside a great circle of Life (the author's rule): the great circle changes the
    /// price of the small circles in it to its own colour - the earth in an Emerald box does not leave the caster
    /// stone-bound, it grows emerald on them. And Life has two prices of its own, one for each side: a lost Withering
    /// drinks from the caster, a lost Restoration overgrows the place.</summary>
    public static partial class LifeMagic
    {
        /// <summary>The meeting a small circle of an element stands in: it is the element its Life box met (else None).</summary>
        public static RuneKind PairOfCircle(SpellCircle root, SpellCircle c)
        {
            if (root == null || c == null || c.Great || c.element == null) return None;
            SpellCircle box = root.ParentOf(c);
            if (box == null || !box.Great || !Is(box.element)) return None;
            RuneKind k = PairOf(root, box);
            return k == c.element.kind ? k : None;
        }

        public static string PairPrice(RuneKind k) =>
            k == RuneKind.Fire ? "wild seed" : k == RuneKind.Water ? "bled" : k == RuneKind.Earth ? "emerald-bound" : k == RuneKind.Air ? "sickened" : null;

        private static string PairPriceText(RuneKind k) =>
            k == RuneKind.Fire ? "If the hold slips, the Wild Flame sows itself in the caster: a spark in their own body, ready to burst at the first touch of another."
          : k == RuneKind.Water ? "If the hold slips, the blood goes the wrong way: the caster bleeds out a quarter of what they can lose."
          : k == RuneKind.Earth ? "If the hold slips, the emerald grows on the caster instead: a crust over a third of the body, stiff and slow until it falls away."
          : "If the hold slips, the fog turns on its maker: the caster's legs give out for a couple of hours (no poison - even a mage of Pestilence feels it).";

        private static string ElementPrice(RuneKind k) =>
            k == RuneKind.Fire ? "Price: raging flame" : k == RuneKind.Water ? "Price: chilled" : k == RuneKind.Earth ? "Price: stone-bound" : k == RuneKind.Air ? "Price: winded" : null;

        public const string WitherPrice = "Price: withering", OvergrowPrice = "Price: overgrowth";

        /// <summary>The prices as the codex shows them: a small element in a meeting of Life pays as the meeting, and
        /// the two sides of Life have their own.</summary>
        public static void Prices(SpellCircle root, List<KeyValuePair<string, string>> extras)
        {
            if (root == null || extras == null || !Circles(root).Any()) return;
            var small = WeaveSpell.Small(root).Where(c => c.element != null).ToList();
            foreach (RuneKind k in small.Select(c => PairOfCircle(root, c)).Where(k => k != None).Distinct())
            {
                // the plain price goes when every circle of that element pays as the meeting
                if (!small.Any(c => c.element.kind == k && PairOfCircle(root, c) == None)) extras.RemoveAll(kv => kv.Key == ElementPrice(k));
                string name = "Price: " + PairPrice(k);
                if (!extras.Any(kv => kv.Key == name)) extras.Add(new KeyValuePair<string, string>(name, PairPriceText(k)));
            }
            Sides(root, out bool death, out bool life);
            if (death && !extras.Any(kv => kv.Key == WitherPrice))
                extras.Add(new KeyValuePair<string, string>(WitherPrice, "If the hold slips on a Withering, it drinks from the one who held it: a withering wound, and half the store of life spills away."));
            if (life && !extras.Any(kv => kv.Key == OvergrowPrice))
                extras.Add(new KeyValuePair<string, string>(OvergrowPrice, "If the hold slips on a Restoration, life breaks loose: the place round the caster overgrows, vines catch everyone there, bark grows on the caster."));
        }

        private static void Sides(SpellCircle root, out bool death, out bool life)
        {
            var lifes = Links(root).ToList();
            death = lifes.Any(g => Read(g, out _).Any(a => a.side == RuneKind.Drain));
            life = Circles(root).Any(g => g.inner.Any(k => k.element != null)) || lifes.Any(g => Read(g, out _).Any(a => a.side == RuneKind.Restore));
        }

        /// <summary>A lost hold on a meeting of Life: its own price, in place of its element's.</summary>
        public static void PayPair(RuneKind k, Pawn caster)
        {
            if (caster == null || caster.Dead) return;
            switch (k)
            {
                case RuneKind.Fire:
                    Seed(caster, null, 1f, false, 2);
                    Messages.Message(caster.LabelShortCap + " lost the hold on the Wild Flame: a spark took root in them.", caster, MessageTypeDefOf.NegativeEvent, false);
                    break;
                case RuneKind.Water:
                    HealthUtility.AdjustSeverity(caster, HediffDefOf.BloodLoss, 0.25f);
                    Messages.Message(caster.LabelShortCap + " lost the hold on the Blood: it ran out of them.", caster, MessageTypeDefOf.NegativeEvent, false);
                    break;
                case RuneKind.Earth:
                    Crust(caster, 0.35f);
                    Messages.Message(caster.LabelShortCap + " lost the hold on the Emerald: it grows on them now.", caster, MessageTypeDefOf.NegativeEvent, false);
                    break;
                case RuneKind.Air:
                    HediffDef ab = H("Avir_Abasia");
                    if (ab != null) MagicFx.Give(caster, ab, 5000);
                    Messages.Message(caster.LabelShortCap + " lost the hold on the Pestilence: their legs gave out.", caster, MessageTypeDefOf.NegativeEvent, false);
                    break;
            }
        }

        /// <summary>The prices learnt by a lost hold (hidden terms).</summary>
        public static IEnumerable<string> PriceKeys(SpellCircle root)
        {
            if (root == null || !Circles(root).Any()) yield break;
            foreach (RuneKind k in WeaveSpell.Small(root).Select(c => PairOfCircle(root, c)).Where(k => k != None).Distinct()) yield return "Price:" + PairPrice(k);
            Sides(root, out bool death, out bool life);
            if (death) yield return "Price:withering";
            if (life) yield return "Price:overgrowth";
        }
    }
}
