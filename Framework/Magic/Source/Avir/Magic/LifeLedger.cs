using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>The ledger of Life in the editor (the author, Fix160: "I don't see how much the spell will fill and how
    /// much it spends at once in the present order - and that is the key mechanic"). The spell is walked in its own order,
    /// as at the cast: what each Withering takes goes into the flow, each Restoration pays from the flow first, then from
    /// the store; short of both, the giving is weakened by the share it got. Counted for one creature reached, wounded,
    /// so the numbers are the spell's own, not the place's.</summary>
    public static partial class LifeMagic
    {
        /// <summary>What a Withering of this verb takes from one living body, by the meeting (as Wither does).</summary>
        private static float TakeOne(RuneKind verb, RuneKind kind, float power, out string note)
        {
            note = null;
            float dmg;
            switch (verb)
            {
                case RuneKind.Release: dmg = 6f; break;
                case RuneKind.Beam: dmg = 7f; break;
                case RuneKind.Hold: dmg = 3f; break;
                case RuneKind.Growth: note = "from plants only"; return 0f;
                case RuneKind.Purge: note = "from poison only"; return 0f;
                case RuneKind.Lift: return 0f;
                case RuneKind.Step: note = "by the way dragged"; return 0f;
                default: dmg = 8f; break;
            }
            dmg *= power;
            switch (kind)
            {
                case RuneKind.Fire: note = "plus half of every burst after"; return dmg * 0.3f;
                case RuneKind.Water: note = "until they have lost 60% of their blood"; return Mathf.Min(72f, dmg * 1.44f);
                case RuneKind.Earth: note = "half of it sealed in emeralds"; return dmg * 0.5f;
                case RuneKind.Air: note = "only from the sick - nothing from the healthy"; return 0f;
            }
            return dmg;
        }

        /// <summary>What a Restoration of this verb costs for one wounded body, by the meeting (as Give does).</summary>
        private static float GiveOne(RuneKind verb, RuneKind kind, float power, float radius)
        {
            if (kind == RuneKind.Fire) return 4f;                                            // a smouldering heart
            if (kind == RuneKind.Air) return Mathf.PI * radius * radius * 0.5f;              // the fog, by its cells
            float per = 12f, regen = 0.25f;
            switch (verb)
            {
                case RuneKind.Release: per = 9f; regen = 0.15f; break;
                case RuneKind.Beam: per = 10f; regen = 0.2f; break;
                case RuneKind.Hold: per = 6f; regen = 0.4f; break;
                case RuneKind.Growth: per = 3f; regen = 0.15f; break;
                case RuneKind.Purge: per = 0f; regen = 0f; break;
                case RuneKind.Step: return 0f;
            }
            return per * power * 0.6f + regen * 4f;
        }

        private static string VerbName(RuneKind v) => v == (RuneKind)(-1) ? "bare" : v.ToString().ToLowerInvariant();

        public static string Ledger(SpellCircle root, Pawn caster)
        {
            if (root == null) return null;
            List<SpellCircle> order = MapComponent_SpellChain.Order(root);
            bool any = order.Any(c => IsLink(root, c) || (c.element != null && !c.Great && root.ParentOf(c) is SpellCircle b && b.Great && Is(b.element)));
            if (!any) return null;
            Hediff_ArcaneCore core = caster != null ? Mages.Core(caster) : null;
            float power = caster != null ? MagicConductors.PowerFactor(caster, core) : 1f;
            var sb = new StringBuilder();
            sb.AppendLine("Life, for one wounded creature reached (power " + power.ToString("0.##") + "), in the spell's order:");
            float flow = 0f, fromStore = 0f, worst = 1f;
            var stores = new Dictionary<RuneKind, float>();
            foreach (SpellCircle c in order)
            {
                SpellCircle box = c.Great ? c : root.ParentOf(c);
                if (box == null || !box.Great || !Is(box.element)) continue;
                RuneKind kind = PairOf(root, box);
                if (!stores.ContainsKey(kind)) stores[kind] = core?.Store(kind) ?? 0f;
                string store = StoreName(kind);
                void Spend(string what, float need)
                {
                    if (need <= 0f) return;
                    float a = Mathf.Min(flow, need); flow -= a;
                    float b = Mathf.Min(stores[kind], need - a); stores[kind] -= b; fromStore += b;
                    float share = (a + b) / need;
                    worst = Mathf.Min(worst, share);
                    sb.AppendLine("  " + what + ": spends " + need.ToString("0.#") + (a > 0f ? " (" + a.ToString("0.#") + " from what was just taken" : " (") + (b > 0f ? (a > 0f ? ", " : "") + b.ToString("0.#") + " from " + store : "") + ")"
                        + (share < 0.999f ? " - short, weakened to " + share.ToStringPercent() : ""));
                }
                if (IsLink(root, c))
                {
                    float r = Radius(c);
                    foreach (Act act in Read(c, out _))
                    {
                        if (act.trans) { sb.AppendLine("  " + (act.side == RuneKind.Drain ? "Withering" : "Restoration") + " on matter (Transmutation): by what lies there"); continue; }
                        if (act.side == RuneKind.Drain)
                        {
                            float t = TakeOne(act.verb, kind, power, out string note);
                            flow += t;
                            sb.AppendLine("  Withering, " + VerbName(act.verb) + ": takes " + t.ToString("0.#") + (note != null ? " (" + note + ")" : ""));
                        }
                        else Spend("Restoration, " + VerbName(act.verb), GiveOne(act.verb, kind, power, r));
                    }
                }
                else if (c.element != null) Spend("Life round " + c.element.label, 6f * power * 0.6f);
            }
            sb.Append("  In the end: " + (flow > 0.05f ? flow.ToString("0.#") + " goes into the store" : "nothing goes into the store")
                + (fromStore > 0.05f ? ", " + fromStore.ToString("0.#") + " taken from it" : "")
                + (worst < 0.999f ? "; the giving is weakened to " + worst.ToStringPercent() + " at its weakest" : "") + ".");
            if (core != null) sb.Append("\n  Now in the store: " + string.Join(", ", stores.Keys.Select(k => StoreName(k) + " " + core.Store(k).ToString("0"))) + ".");
            return sb.ToString();
        }
    }
}
