using System;
using System.Collections.Generic;
using System.Linq;
using Avir.Artifactorics;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>A ward: each element's own shield on one body (a dome is the same, over ground). It has a charge, shown
    /// as a bar like a shield's, and holds for a while. The rules (Магия-правила.md, part 1):
    /// another element's magic does not pass - the ward puts it out and spends charge on it; a blow stronger than what
    /// is left breaks it and comes through for the rest. Its own element (but fire's) passes, and the ward's charge is
    /// laid onto the blow, and the ward falls. Wards of fire, magma and steam: their wearer neither burns nor suffers
    /// their ground (pools, steam, flames on the body) and the ground does not spend them; a direct blow of their own
    /// element puts out both the ward and the spell. What only slows or hinders, a ward keeps off in its own element too.
    /// Plain blows: 25% of ranged and 15% of melee held, up to 75% and 50% by amplifying.</summary>
    public class Hediff_SpellWard : HediffWithComps
    {
        public float energy, energyMax = 1f, ranged = 0.25f, melee = 0.15f;

        /// <summary>The elements it is made of: one, or the two that met; none - the arcane ward, of no element.</summary>
        public RuneKind[] Parts
        {
            get
            {
                if (def == FireDefOf.Avir_FireWard) return new[] { RuneKind.Fire };
                if (def == WaterDefOf.Avir_WaterWard) return new[] { RuneKind.Water };
                if (def == EarthAirDefOf.Avir_EarthWard) return new[] { RuneKind.Earth };
                if (def == EarthAirDefOf.Avir_AirWard) return new[] { RuneKind.Air };
                foreach (ComboKind k in new[] { ComboKind.Steam, ComboKind.Storm, ComboKind.Magma, ComboKind.Haze, ComboKind.Bog, ComboKind.Dust })
                    if (def == ComboDefOf.Ward(k)) return Combos.Parts(k);
                RuneDef el = WardElement.Get(pawn);
                return el != null ? new[] { el.kind } : new RuneKind[0];
            }
        }

        /// <summary>Fire, magma, steam: wards that keep their wearer out of their own fire.</summary>
        public bool Fiery => def == FireDefOf.Avir_FireWard || def == ComboDefOf.Avir_MagmaWard || def == ComboDefOf.Avir_SteamWard;

        public override string LabelInBrackets => Mathf.CeilToInt(energy) + "/" + Mathf.CeilToInt(energyMax);

        public override string TipStringExtra =>
            base.TipStringExtra + "\nCharge " + Mathf.CeilToInt(energy) + " / " + Mathf.CeilToInt(energyMax)
            + "\nMagic of other elements: put out, at the cost of charge."
            + (Fiery ? "\nIts own fire: does not burn the wearer; a direct blow of it puts out both the ward and the spell." : "\nIts own element: passes, stronger by what is left of the ward - and the ward falls.")
            + "\nShots: " + ranged.ToStringPercent() + " held · melee: " + melee.ToStringPercent() + " held.";

        public override IEnumerable<Gizmo> GetGizmos()
        {
            if (pawn.Faction == Faction.OfPlayer && Find.Selector.SingleSelectedThing == pawn)
                yield return new Gizmo_ArtifactShield { title = def.LabelCap, cur = () => energy, max = () => energyMax, tip = () => TipStringExtra.Trim(), fill = def.defaultLabelColor };
        }

        public void Spend(float amount)
        {
            energy -= amount;
            if (energy <= 0.01f) Fall("Ward broken");
        }

        public void Fall(string why)
        {
            SpellWards.Note(def.defName + " on " + pawn.LabelShort + " fell: " + why + " (element at work " + SpellWards.element + "/" + SpellWards.combo + ", direct " + SpellWards.direct + ")");
            if (pawn.Spawned)
            {
                FleckMaker.ThrowLightningGlow(pawn.DrawPos, pawn.Map, 1.2f);
                FleckMaker.ThrowMicroSparks(pawn.DrawPos, pawn.Map);
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, why, 3f);
            }
            if (pawn.health.hediffSet.hediffs.Contains(this)) pawn.health.RemoveHediff(this);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref energy, "energy");
            Scribe_Values.Look(ref energyMax, "energyMax", 1f);
            Scribe_Values.Look(ref ranged, "ranged", 0.25f);
            Scribe_Values.Look(ref melee, "melee", 0.15f);
        }
    }

    public static class SpellWards
    {
        // ------------------------------------------------------------------ the element at work

        public static RuneKind? element;
        public static ComboKind combo;
        /// <summary>A blow aimed at someone (a circle striking, a bolt, a burst) - not the ground it leaves (pools, steam, a bog).</summary>
        public static bool direct;

        private sealed class Scope : IDisposable
        {
            private readonly RuneKind? e; private readonly ComboKind k; private readonly bool d;
            public Scope(RuneKind? e, ComboKind k, bool d) { this.e = e; this.k = k; this.d = d; }
            public void Dispose() { element = e; combo = k; direct = d; }
        }

        /// <summary>using (SpellWards.Of(...)) { ... } - the element at work inside, what was before restored after.</summary>
        public static IDisposable Of(RuneKind? el, ComboKind k, bool isDirect)
        {
            var s = new Scope(element, combo, direct);
            element = el; combo = k; direct = isDirect;
            return s;
        }

        public static bool AtWork => element.HasValue || combo != ComboKind.None;

        private static RuneKind[] Incoming => combo != ComboKind.None ? Combos.Parts(combo) : element.HasValue ? new[] { element.Value } : new RuneKind[0];

        public static Hediff_SpellWard On(Pawn p) => p?.health?.hediffSet?.hediffs.OfType<Hediff_SpellWard>().FirstOrDefault();

        // ------------------------------------------------------------------ laying a ward

        /// <summary>A ward on a body: its charge and its hold on plain blows grow with the spell's strength (amplifying).</summary>
        /// <summary>Dev mode: every ward decision written to the log.</summary>
        public static bool Trace;

        public static void Note(string s) { if (Trace) Log.Message("[Avir ward] " + s); }

        public static void Lay(Pawn p, HediffDef def, float power, int amplify, int ticks)
        {
            Note("lay " + def?.defName + " on " + p?.LabelShort + " (power " + power.ToString("0.00") + ", amplify " + amplify + ")" + (p == null || def == null ? " - nothing to lay" : p.Dead ? " - dead" : ""));
            if (p == null || def == null || p.Dead) return;
            // one ward at a time: a new one takes the place of the old
            foreach (Hediff_SpellWard old in p.health.hediffSet.hediffs.OfType<Hediff_SpellWard>().Where(h => h.def != def).ToList()) p.health.RemoveHediff(old);
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(def) ?? p.health.AddHediff(def);
            h.Severity = 1f;
            HediffComp_Disappears d = h.TryGetComp<HediffComp_Disappears>();
            if (d != null) d.ticksToDisappear = Mathf.Max(d.ticksToDisappear, ticks);
            if (h is Hediff_SpellWard w)
            {
                float s = Mathf.Max(0.3f, power) * (1f + 0.5f * amplify);
                w.energyMax = Mathf.Max(w.energyMax, 30f * s);
                w.energy = w.energyMax;
                // a plain ward: a quarter of the shots, a sixth of the blows; amplified up to three quarters and a half
                w.ranged = Mathf.Min(0.75f, 0.25f * s);
                w.melee = Mathf.Min(0.5f, 0.15f * s);
            }
        }

        /// <summary>The codex text of a ward: where it falls, what it holds, and its own nature.</summary>
        public static string Note(string own, bool fiery, string soft, string answer, int amplify)
        {
            float s = 1f + 0.5f * amplify;
            return "Friends at the target get a ward of " + own + " (the caster, if the Barrier is the last rune of the outer section). It has a charge and lasts for hours.\n"
                + "• Magic of other elements is put out at the cost of charge; a blow stronger than what is left breaks it and comes through.\n"
                + (fiery ? "• Its own fire does not burn the wearer and does not spend the ward; a direct blow of it puts out both the ward and the spell.\n"
                         : "• Its own element passes, stronger by the ward's charge, and the ward falls" + (soft != null ? " - but it keeps off " + soft : "") + ".\n")
                + "• Shots: " + Mathf.Min(0.75f, 0.25f * s).ToStringPercent() + " held, melee: " + Mathf.Min(0.5f, 0.15f * s).ToStringPercent() + " (at the spell's usual strength; Amplify raises it, up to 75% and 50%)."
                + (answer != null ? "\n• " + answer : "");
        }

        // ------------------------------------------------------------------ what it stops

        /// <summary>Does the ward keep off a magic effect that is not a blow (a state, a throw, a stun)? soft - it only slows
        /// or hinders (soaked, mired, dizzy, rooted, dusted, hazed): a ward keeps those off in its own element too.</summary>
        public static bool Stops(Pawn p, bool soft)
        {
            if (!AtWork) return false;
            Hediff_SpellWard w = On(p);
            if (w == null || w.energy <= 0f) return false;
            RuneKind[] mine = w.Parts, inc = Incoming;
            bool own = mine.Length > 0 && inc.Any(k => mine.Contains(k));
            bool other = mine.Length == 0 || inc.Any(k => !mine.Contains(k));
            if (own && w.Fiery && inc.Contains(RuneKind.Fire)) return true;   // its own fire: nothing of it touches the wearer
            if (own && !other) return soft;                                     // its own element: the soft is held, the hard passes
            w.Spend(soft ? 2f : 5f);                                            // another element: held, at a little cost
            return true;
        }

        /// <summary>A blow on a warded body. Returns true if nothing of it is left.</summary>
        internal static bool Take(Pawn p, ref DamageInfo dinfo)
        {
            Hediff_SpellWard w = On(p);
            if (w == null || dinfo.Def == null) return false;
            float amount = dinfo.Amount;
            if (amount <= 0f) return false;
            Note(p.LabelShort + " hit through " + w.def.defName + " (" + w.energy.ToString("0") + "/" + w.energyMax.ToString("0") + "): " + dinfo.Def.defName + " " + amount.ToString("0.#")
                + (AtWork ? ", magic " + element + "/" + combo + (direct ? " direct" : " ground") : ", plain"));
            if (!AtWork)
            {
                // a plain blow: a share of it held, at the cost of charge
                // flames on a ward of fire, magma or steam: nothing burns, nothing is spent (the ground of its own fire)
                if (w.Fiery && (dinfo.Def == DamageDefOf.Flame || dinfo.Def == DamageDefOf.Burn)) return true;
                if (!dinfo.Def.ExternalViolenceFor(p)) return false;
                bool far = dinfo.Def.isRanged || dinfo.Def.isExplosive;
                float held = amount * (far ? w.ranged : w.melee);
                if (held <= 0f) return false;
                held = Mathf.Min(held, w.energy);
                w.Spend(held);
                dinfo.SetAmount(amount - held);
                return amount - held <= 0.01f;
            }
            RuneKind[] mine = w.Parts, inc = Incoming;
            if (inc.Length == 0) return false;
            // a meeting is split between its two elements: each half meets the ward by its own rule
            float share = amount / inc.Length, left = 0f, laid = 0f;
            bool fall = false, quench = false;
            foreach (RuneKind k in inc)
            {
                bool own = mine.Contains(k);
                if (own && w.Fiery && k == RuneKind.Fire)
                {
                    // fire on a ward of fire: its ground does nothing; a direct blow puts out both
                    if (direct) quench = true;
                    continue;
                }
                if (own)
                {
                    // its own element passes, and the ward lies on the blow
                    if (direct) { laid = w.energy * 0.5f; fall = true; }
                    left += share;
                    continue;
                }
                // another element: put out at the cost of charge; too strong - the ward breaks, the rest comes through
                if (w.energy >= share) { w.energy -= share; continue; }
                left += share - w.energy;
                w.energy = 0f;
                fall = true;
            }
            if (quench) w.Fall("Ward and spell put out");
            else if (fall) w.Fall(laid > 0f ? "Ward burst" : "Ward broken");
            float now = left + laid;
            dinfo.SetAmount(now);
            return now <= 0.01f;
        }
    }

    /// <summary>A blast deals its blows over the ticks after it was set off: it keeps the element it was set off with.</summary>
    [HarmonyPatch(typeof(Explosion), nameof(Explosion.SpawnSetup))]
    public static class Patch_SpellWardsBlastBorn
    {
        internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Explosion, object[]> of = new System.Runtime.CompilerServices.ConditionalWeakTable<Explosion, object[]>();
        public static void Postfix(Explosion __instance)
        {
            if (SpellWards.AtWork) of.Add(__instance, new object[] { SpellWards.element, SpellWards.combo, SpellWards.direct });
        }
    }

    [HarmonyPatch(typeof(Explosion), "AffectCell")]
    public static class Patch_SpellWardsBlastCell
    {
        public static void Prefix(Explosion __instance, out IDisposable __state)
        {
            __state = Patch_SpellWardsBlastBorn.of.TryGetValue(__instance, out object[] e) ? SpellWards.Of((RuneKind?)e[0], (ComboKind)e[1], (bool)e[2]) : null;
        }
        public static void Finalizer(IDisposable __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_SpellWards
    {
        [HarmonyPriority(Priority.High)]
        public static bool Prefix(Pawn __instance, ref DamageInfo __0, ref bool __1)
        {
            if (__1 || !AvirMagic.On) return true;
            if (SpellWards.On(__instance) == null) return true;
            if (!SpellWards.Take(__instance, ref __0)) return true;
            __1 = true;
            return false;
        }
    }
}
