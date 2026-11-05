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
    /// <summary>The healing turn (Магия-правила.md, the author's rule): a verb with a Thread right outward of it, in a
    /// circle that also holds Restoration, is tied to the mending - its blow on the living becomes healing. A Release so
    /// written is a burst of healing; a Lift lifts gently. Whom it reaches is the usual rule of the circle: everyone in it;
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

        /// <summary>Does this circle hold Restoration (what a Thread on a verb ties it to)?</summary>
        public static bool Restores(RitualSchema s) => s?.rings != null && s.rings.Any(g => g.runes.Any(x => x != null && x.kind == RuneKind.Restore));

        /// <summary>Does any verb of this circle turn to healing?</summary>
        public static bool Turns(RitualSchema s)
        {
            if (!Restores(s)) return false;
            foreach (RuneDef v in s.rings.SelectMany(g => g.runes).Where(x => x != null && x.role == RuneRole.Verb).Distinct())
            {
                SpellMods.Own(s, v.kind, out _, out _, out _, out int th);
                if (th > 0) return true;
            }
            return false;
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
