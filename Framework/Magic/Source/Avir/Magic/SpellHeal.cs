using System;
using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Magic
{
    /// <summary>The healing turn (Магия-правила.md, the author's rule): a verb, a Thread right outward of it and Restoration
    /// right outward of the Thread - "Release + Thread + Restore" - tie that one verb to the mending: its blow on the living
    /// becomes healing. The bond is local: other verbs of the circle strike as written, and another circle of the same
    /// spell is its own (Release + two Barriers in the next circle is a plain blast on foes). A Release so written is a
    /// burst of healing; a Lift lifts gently. Whom it reaches is the usual rule of the circle: everyone in it;
    /// with two Barriers on the outer edge, only foes (Selective Protection); with two Barriers and a Thread, only the
    /// caster's own (Selective Aid). A blow so turned harms nothing: no wound, no fire, no knock, no ill state; a machine
    /// has no life to mend and takes it as a blow. Hidden until first cast (SpellLore, "Thread:Heal").</summary>
    public static class SpellHeal
    {
        /// <summary>Set for the verb being applied (SpellMods.Verb); cleared at the start and end of every part.</summary>
        public static bool Now { get => now && at == GenTicks.TicksGame; set { now = value; at = GenTicks.TicksGame; } }
        private static bool now;
        private static int at = -1, depth;
        public static bool Active => Now || depth > 0;

        private sealed class Scope : IDisposable { public void Dispose() { if (depth > 0) depth--; } }
        public static IDisposable In(bool on) { if (!on) return null; depth++; return new Scope(); }

        /// <summary>The verbs of this circle tied to the mending: each followed (its own modifiers between allowed) by a
        /// Thread, and that Thread at once by Restoration.</summary>
        public static HashSet<RuneDef> Bound(RitualSchema s)
        {
            var bound = new HashSet<RuneDef>();
            if (s?.rings == null) return bound;
            List<RuneDef> seq = s.rings.SelectMany(g => g.runes).Where(x => x != null).ToList();
            RuneDef verb = null;
            for (int i = 0; i < seq.Count; i++)
            {
                RuneDef x = seq[i];
                if (x.role == RuneRole.Form && x.kind == RuneKind.Restore && verb != null && i > 0 && seq[i - 1].kind == RuneKind.Thread) bound.Add(verb);
                if (x.role == RuneRole.Verb) verb = x; else if (x.role == RuneRole.Form) verb = null;
            }
            return bound;
        }

        /// <summary>Is this Restoration only the knot of a healing turn (right after "verb + Thread")?</summary>
        public static bool Knot(RitualSchema s) => Bound(s).Count > 0;

        /// <summary>Does any verb of this circle (or of any circle of the spell) turn to healing?</summary>
        public static bool Turns(RitualSchema s) => s?.weave != null ? s.weave.Working.Any(k => Bound(k.AsSchema()).Count > 0) : Bound(s).Count > 0;

        /// <summary>What a Restoration form carries, like any form: the verbs of its circle - the tied ones heal, the rest
        /// strike as written (the author: "Release + Thread + Restore + Release" - the second Release strikes; with Selective
        /// Aid on the circle it strikes the caster's own - a badly built spell is the player's to answer for). Growth and
        /// Purge, when not tied, are Restoration's own helpers (deeper sealing, burnt infection) and are not cast. Nothing
        /// when another form carries the verbs already (they would strike twice).</summary>
        public static List<RuneDef> Carried(RitualSchema s, List<RuneDef> forms, List<RuneDef> verbs)
        {
            if (forms.Any(f => f.kind != RuneKind.Restore)) return new List<RuneDef>();
            HashSet<RuneDef> b = Bound(s);
            return verbs.Where(v => b.Contains(v) || (v.kind != RuneKind.Growth && v.kind != RuneKind.Purge)).ToList();
        }

        public static bool Living(Pawn p) => p != null && !p.Dead && p.RaceProps.IsFlesh;

        /// <summary>Is this body under a turned blow just now - to be spared every harm of it?</summary>
        public static bool Spares(Pawn p) => Active && SpellWards.AtWork && Living(p);

        /// <summary>A blow turned to healing: its strength goes into the wounds, the worst first.</summary>
        public static void Mend(Pawn p, float amount)
        {
            if (!Living(p) || amount <= 0f) return;
            float left = amount * 0.7f;
            foreach (Hediff_Injury inj in p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(i => !i.IsPermanent()).OrderByDescending(i => i.Severity).ToList())
            {
                if (left <= 0.01f) break;
                float h = Mathf.Min(left, inj.Severity);
                inj.Heal(h);
                left -= h;
            }
            if (p.Spawned) FleckMaker.ThrowMetaIcon(p.Position, p.Map, FleckDefOf.HealingCross);
        }
    }

    /// <summary>A turned blow on a living body heals instead of wounding.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_SpellHealTurn
    {
        [HarmonyPriority(Priority.First + 1)]
        public static bool Prefix(Pawn __instance, ref DamageInfo __0, ref bool __1)
        {
            if (__1 || !AvirMagic.On || !SpellHeal.Active || !SpellWards.AtWork || !SpellHeal.Living(__instance)) return true;
            SpellHeal.Mend(__instance, __0.Amount);
            __1 = true;
            return false;
        }
    }

    /// <summary>A turned blow harms no thing: walls, items and plants are left as they were.</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_SpellHealSparesThings
    {
        public static bool Prefix(Thing __instance, ref DamageWorker.DamageResult __result)
        {
            if (!SpellHeal.Active || !SpellWards.AtWork || __instance is Pawn) return true;
            __result = new DamageWorker.DamageResult();
            return false;
        }
    }

    /// <summary>A turned blow stuns no one.</summary>
    [HarmonyPatch(typeof(StunHandler), nameof(StunHandler.StunFor))]
    public static class Patch_SpellHealNoStun
    {
        public static bool Prefix(StunHandler __instance) => !(__instance.parent is Pawn p && SpellHeal.Spares(p));
    }

    /// <summary>A turned blow leaves no ill state on the body (burns, chill, sickness of the spell).</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Patch_SpellHealNoIll
    {
        public static bool Prefix(Pawn ___pawn, Hediff hediff) => hediff == null || !hediff.def.isBad || hediff is Hediff_Injury || !SpellHeal.Spares(___pawn);
    }

    /// <summary>A turned blow sets nothing alight.</summary>
    [HarmonyPatch(typeof(FireUtility), nameof(FireUtility.TryStartFireIn))]
    public static class Patch_SpellHealNoFire
    {
        public static bool Prefix(ref bool __result)
        {
            if (!SpellHeal.Active || !SpellWards.AtWork) return true;
            __result = false;
            return false;
        }
    }
}
