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
    /// <summary>What a ward is, on a body or over ground (a zone dome): a charge, its elements, its hold on plain blows.</summary>
    public interface IWard
    {
        float Energy { get; set; }
        float EnergyMax { get; }
        float Ranged { get; }
        float Melee { get; }
        RuneKind[] Parts { get; }
        bool Fiery { get; }
        string Name { get; }
        void Spend(float amount);
        void Fall(string why);
    }

    public class Hediff_SpellWard : HediffWithComps, IWard
    {
        public float energy, energyMax = 1f, ranged = 0.25f, melee = 0.15f;

        public float Energy { get => energy; set => energy = value; }
        public float EnergyMax => energyMax;
        public float Ranged => ranged;
        public float Melee => melee;
        public string Name => def.label + " on " + pawn?.LabelShort;

        /// <summary>The meeting it is made of, if any.</summary>
        public ComboKind Combo
        {
            get
            {
                foreach (ComboKind k in new[] { ComboKind.Steam, ComboKind.Storm, ComboKind.Magma, ComboKind.Haze, ComboKind.Bog, ComboKind.Dust })
                    if (def == ComboDefOf.Ward(k)) return k;
                return ComboKind.None;
            }
        }

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

        internal static RuneKind[] Incoming => combo != ComboKind.None ? Combos.Parts(combo) : element.HasValue ? new[] { element.Value } : new RuneKind[0];

        public static Hediff_SpellWard On(Pawn p) => p?.health?.hediffSet?.hediffs.OfType<Hediff_SpellWard>().FirstOrDefault();

        /// <summary>What wards a pawn: its own ward, else the zone dome it stands in.</summary>
        public static IWard Of(Pawn p)
        {
            IWard w = On(p);
            if (w != null) return w;
            return p != null && p.Spawned ? MapComponent_WardsDrawn.Over(p.Map, p.Position) : null;
        }

        /// <summary>The layers of ward round a pawn, outside first: the dome it stands in, then its own ward. A blow meets
        /// the dome, and what the dome lets through meets the ward on the body.</summary>
        public static IEnumerable<IWard> Layers(Pawn p)
        {
            if (p == null) yield break;
            if (p.Spawned) { IWard d = MapComponent_WardsDrawn.Over(p.Map, p.Position); if (d != null && d.Energy > 0f) yield return d; }
            IWard own = On(p);
            if (own != null) yield return own;
        }

        // ------------------------------------------------------------------ a spell meeting a ward whole

        /// <summary>A spell (its elements) meets a ward or a dome whole - a bolt flying in, a spell landing inside a dome.
        /// Another element: put out at the cost of charge (12 a power) - and fire and water meeting there leave steam -
        /// or, too strong, it breaks the ward and goes on. Its own fire on a fiery ward: both go out. Its own element (not
        /// fire): it goes on, stronger by what was left of the ward, and the ward falls. True - the spell is put out.</summary>
        public static bool Meets(IWard d, RuneKind[] inc, float power, Map map, IntVec3 at, out float boost)
        {
            boost = 1f;
            if (d == null || inc == null || inc.Length == 0) return false;
            RuneKind[] mine = d.Parts;
            bool overlap = mine.Length > 0 && inc.Any(k => mine.Contains(k));
            bool allOwn = mine.Length > 0 && inc.All(k => mine.Contains(k));
            Note(d.Name + " meets a spell of " + string.Join("+", inc) + " (power " + power.ToString("0.00") + ", charge " + d.Energy.ToString("0") + ")");
            if (d.Fiery && overlap && inc.Contains(RuneKind.Fire)) { d.Fall("Ward and spell put out"); Puff(map, at, 0.5f); return true; }
            if (allOwn) { boost = 1f + 0.5f * Mathf.Clamp01(d.Energy / Mathf.Max(1f, d.EnergyMax)); d.Fall("Ward burst"); return false; }
            float need = 12f * power;
            if (d.Energy >= need)
            {
                d.Spend(need);
                // fire and water meeting in it: the spell goes up as steam (harmless)
                bool steam = (mine.Contains(RuneKind.Fire) && inc.Contains(RuneKind.Water)) || (mine.Contains(RuneKind.Water) && inc.Contains(RuneKind.Fire));
                if (steam) ComboMagic.Smoke(map, FireMagic.Point(at, map, 2.5f), 0.7f);
                Puff(map, at, 1f);
                if (map != null) MoteMaker.ThrowText(at.ToVector3Shifted(), map, steam ? "Put out - steam" : "Spell put out", 3f);
                return true;
            }
            d.Fall("Ward broken");
            return false;
        }

        private static void Puff(Map map, IntVec3 at, float s)
        {
            if (map == null || !at.InBounds(map)) return;
            FleckMaker.ThrowLightningGlow(at.ToVector3Shifted(), map, 1.2f * s + 0.4f);
            FleckMaker.ThrowMicroSparks(at.ToVector3Shifted(), map);
        }

        /// <summary>A spell landing at a point inside a zone dome meets the dome first. True - it is put out there.</summary>
        public static bool DomeTakes(Map map, IntVec3 at, RuneDef el, ComboKind k, ref float power)
        {
            MapComponent_WardsDrawn.Dome d = MapComponent_WardsDrawn.Over(map, at);
            if (d == null) return false;
            RuneKind[] inc = k != ComboKind.None ? Combos.Parts(k) : el != null ? new[] { el.kind } : new RuneKind[0];
            bool out_ = Meets(d, inc, power, map, at, out float boost);
            power *= boost;
            return out_;
        }

        // ------------------------------------------------------------------ the barrier in its turn

        private static bool Acts(RuneDef x) => x.role == RuneRole.Verb || x.role == RuneRole.Form;

        /// <summary>Does the circle strike anything? A circle of barriers only lays a ward: it meets no dome as a blow.</summary>
        public static bool Strikes(RitualSchema s) => s?.rings != null && s.rings.SelectMany(g => g.runes).Any(x => x != null && Acts(x));

        /// <summary>Casts a circle with its barriers in their turn (Магия-правила.md, 1.1): what is written before a Barrier
        /// acts, then the ward is laid, then what comes after. The Barrier as the very last rune, after something that acts,
        /// lays the ward on the caster; anywhere else - a dome over the ground where the spell strikes (round the caster,
        /// with an aura). Amplify runes right after a Barrier strengthen its ward.</summary>
        public static void InOrder(Pawn caster, RitualSchema s, ComboKind k, RuneDef el, LocalTargetInfo target, float power, Action<RitualSchema> cast)
        {
            List<RuneDef> seq = s.rings.SelectMany(g => g.runes).Where(x => x != null).ToList();
            if (!seq.Any(x => x.kind == RuneKind.Barrier)) { cast(s); return; }
            var elements = seq.Where(x => x.role == RuneRole.Element).ToList();
            int firstAct = seq.FindIndex(Acts);
            var wide = seq.Take(firstAct < 0 ? seq.Count : firstAct).Where(x => x.role != RuneRole.Element && x.kind != RuneKind.Barrier).ToList();
            var seg = new List<RuneDef>();
            bool acted = false;
            for (int i = 0; i < seq.Count; i++)
            {
                RuneDef x = seq[i];
                if (x.role == RuneRole.Element) continue;
                if (x.kind == RuneKind.Barrier)
                {
                    if (seg.Any(Acts)) { cast(Part(s, elements, wide, seg)); acted = true; }
                    int amp = 0;
                    while (i + 1 < seq.Count && seq[i + 1].kind == RuneKind.Amplify) { amp++; i++; }
                    bool onCaster = i == seq.Count - 1 && acted;
                    LayBarrier(caster, el, k, onCaster, target, s, power, amp);
                    seg = new List<RuneDef>();
                    continue;
                }
                if (firstAct < 0 || i < firstAct) continue;   // the circle-wide modifiers: in every part already
                seg.Add(x);
            }
            if (seg.Any(Acts)) cast(Part(s, elements, wide, seg));
        }

        private static RitualSchema Part(RitualSchema s, List<RuneDef> elements, List<RuneDef> wide, List<RuneDef> seg)
        {
            var p = new RitualSchema { name = s.name, size = s.size };
            p.rings.Add(new RitualRing { runes = elements.ToList(), slots = 1 });
            p.rings.Add(new RitualRing { runes = wide.Concat(seg).ToList() });
            return p;
        }

        public static HediffDef WardDef(RuneDef el, ComboKind k)
        {
            if (k != ComboKind.None) return ComboDefOf.Ward(k);
            switch (el?.kind)
            {
                case RuneKind.Fire: return FireDefOf.Avir_FireWard;
                case RuneKind.Water: return WaterDefOf.Avir_WaterWard;
                case RuneKind.Earth: return EarthAirDefOf.Avir_EarthWard;
                case RuneKind.Air: return EarthAirDefOf.Avir_AirWard;
                default: return MagicSpellDefOf.Avir_ArcaneWard;
            }
        }

        /// <summary>A Barrier's ward: on the caster, or a dome over the ground.</summary>
        public static void LayBarrier(Pawn caster, RuneDef el, ComboKind k, bool onCaster, LocalTargetInfo target, RitualSchema s, float power, int amplify)
        {
            if (caster == null || !caster.Spawned) return;
            int ticks = Mathf.RoundToInt(GenDate.TicksPerHour * 4f * power);
            Note(caster.LabelShort + ": Barrier of " + (k != ComboKind.None ? k.ToString() : el?.kind.ToString()) + (onCaster ? " on the caster" : " - a dome") + ", amplify " + amplify);
            if (onCaster)
            {
                Lay(caster, WardDef(el, k), power, amplify, ticks);
                if (k == ComboKind.None && el?.kind == RuneKind.Fire && caster.GetAttachment(ThingDefOf.Fire) is Fire f) f.Destroy();
                return;
            }
            RitualReading r = RitualEvaluator.Read(s);
            IntVec3 at = r.verbs.Any(v => v.kind == RuneKind.Aura) ? caster.Position : target.Cell;
            float radius = Mathf.Max(1.4f, FireMagic.PointRadius(r) - 0.5f);
            MapComponent_WardsDrawn.RaiseWard(caster.Map, at, radius, el, k, caster, ticks, power, amplify);
        }

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
            return "A ward of " + own + ", laid in its turn: as the very last rune of the circle - on the caster; anywhere else - a dome over the ground where the spell strikes (round the caster, with an aura), guarding whoever stands inside and putting out spells that land in it. It has a charge and lasts for hours.\n"
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
            foreach (IWard w in Layers(p).ToList()) if (StopsBy(w, soft)) return true;
            return false;
        }

        private static bool StopsBy(IWard w, bool soft)
        {
            if (w == null || w.Energy <= 0f) return false;
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
            // layer by layer: the dome over the ground, then the ward on the body
            foreach (IWard w in Layers(p).ToList()) if (TakeBy(w, p, ref dinfo)) return true;
            return false;
        }

        private static bool TakeBy(IWard w, Pawn p, ref DamageInfo dinfo)
        {
            if (w == null || dinfo.Def == null) return false;
            float amount = dinfo.Amount;
            if (amount <= 0f) return false;
            Note(p.LabelShort + " hit through " + w.Name + " (" + w.Energy.ToString("0") + "/" + w.EnergyMax.ToString("0") + "): " + dinfo.Def.defName + " " + amount.ToString("0.#")
                + (AtWork ? ", magic " + element + "/" + combo + (direct ? " direct" : " ground") : ", plain"));
            if (!AtWork)
            {
                // a plain blow: a share of it held, at the cost of charge
                // flames on a ward of fire, magma or steam: nothing burns, nothing is spent (the ground of its own fire)
                if (w.Fiery && (dinfo.Def == DamageDefOf.Flame || dinfo.Def == DamageDefOf.Burn)) return true;
                if (!dinfo.Def.ExternalViolenceFor(p)) return false;
                bool far = dinfo.Def.isRanged || dinfo.Def.isExplosive;
                float held = amount * (far ? w.Ranged : w.Melee);
                if (held <= 0f) return false;
                held = Mathf.Min(held, w.Energy);
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
                    if (direct) { laid = w.Energy * 0.5f; fall = true; }
                    left += share;
                    continue;
                }
                // another element: put out at the cost of charge; too strong - the ward breaks, the rest comes through
                if (w.Energy >= share) { w.Energy -= share; continue; }
                left += share - w.Energy;
                w.Energy = 0f;
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
        public static void Postfix(IDisposable __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_SpellWards
    {
        [HarmonyPriority(Priority.High)]
        public static bool Prefix(Pawn __instance, ref DamageInfo __0, ref bool __1)
        {
            if (__1 || !AvirMagic.On) return true;
            if (SpellWards.Of(__instance) == null) return true;
            if (!SpellWards.Take(__instance, ref __0)) return true;
            __1 = true;
            return false;
        }
    }

    /// <summary>Whoever stands in a zone dome sees it on their bar: its charge, over the ward of their own if they have one.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_DomeGizmo
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            if (!AvirMagic.On || !__instance.Spawned || __instance.Faction != Faction.OfPlayer || Find.Selector.SingleSelectedThing != __instance) yield break;
            MapComponent_WardsDrawn.Dome d = MapComponent_WardsDrawn.Over(__instance.Map, __instance.Position);
            if (d == null) yield break;
            Color c = d.combo != ComboKind.None ? Combos.ColorOf(d.combo) : d.el != null ? d.el.color : new Color(0.6f, 0.6f, 0.9f);
            yield return new Gizmo_ArtifactShield
            {
                title = d.Name.CapitalizeFirst(), cur = () => d.energy, max = () => d.energyMax, fill = c,
                tip = () => d.Name.CapitalizeFirst() + " - the outer layer: a blow meets the dome first, and what it lets through meets the ward on the body."
                    + "\nShots: " + d.ranged.ToStringPercent() + " held · melee: " + d.melee.ToStringPercent() + " held."
            };
        }
    }

    /// <summary>Magic's blows go through armour by their own measure (Магия-правила.md): blunt by element - water and bog,
    /// earth and magma, air and the rest - and cutting ones alike; heat by the game's own rules. With Combat Extended the
    /// measures are its own (MPa, mm), set apart in the settings. Deeper by Amplify on the verb and a great circle only
    /// (SpellDepth), up to the cap in the settings; a blow under 10 goes in shallower.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_SpellPenetration
    {
        private static readonly System.Reflection.FieldInfo Pen = AccessTools.Field(typeof(DamageInfo), "armorPenetrationInt");
        private static bool? ce;

        [HarmonyPriority(Priority.First)]
        public static void Prefix(Pawn __instance, ref DamageInfo __0)
        {
            if (!AvirMagic.On || !SpellWards.AtWork || Pen == null) return;
            DamageDef d = __0.Def;
            if (d?.armorCategory == null || __0.ArmorPenetrationInt > 0f) return;
            bool sharp = d.armorCategory == DamageArmorCategoryDefOf.Sharp, blunt = d.armorCategory.defName == "Blunt";
            if (!sharp && !blunt) return;   // heat and the rest: by the game's own rules
            if (ce == null) ce = ModsConfig.IsActive("CETeam.CombatExtended");
            AvirSettings o = AvirMod.Settings;
            RuneKind[] inc = SpellWards.Incoming;
            bool water = inc.Contains(RuneKind.Water) && !inc.Contains(RuneKind.Fire) && !inc.Contains(RuneKind.Air);
            bool earth = inc.Contains(RuneKind.Earth) && !water && !inc.Contains(RuneKind.Air);
            float pen = sharp ? (ce.Value ? o.magicPenSharpCE : o.magicPenSharp)
                : water ? (ce.Value ? o.magicPenWaterCE : o.magicPenWater)
                : earth ? (ce.Value ? o.magicPenEarthCE : o.magicPenEarth)
                : (ce.Value ? o.magicPenAirCE : o.magicPenAir);
            // deeper only by the craft of the blow (Amplify on its verbs, a great circle), never by piling power on;
            // a slight blow (a tick of a pool, a scratch) goes in shallower
            pen *= SpellDepth.Factor * SpellDepth.At(__instance.Position) * Mathf.Clamp(__0.Amount / 10f, 0.5f, 1f);
            if (pen <= 0f) return;
            SpellWards.Note("penetration " + d.defName + " " + __0.Amount.ToString("0.#") + ": " + pen.ToString("0.##") + (ce.Value ? (sharp ? " mm" : " MPa") : " (share)"));
            object box = __0;
            Pen.SetValue(box, pen);
            __0 = (DamageInfo)box;
        }
    }
}
