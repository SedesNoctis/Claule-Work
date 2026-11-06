using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;

namespace Avir.Magic
{
    /// <summary>A deep meeting of Life colours its own small circles (the author, Fix197: "каждой"): the blows of a small
    /// circle of the element inside a box of Wild Flame, Blood, Emerald or Pestilence come out as that meeting's own -
    ///   Emerald: the struck part turns to stone (its own wound - no medicine for it, it passes by itself; the part works
    ///     worse by how deep it went, it is never torn off; it gives armour, and under CE it cracks easier from blunt blows);
    ///   Wild Flame: the flame sows a spark in the struck, and burns at half;
    ///   Blood: the blow cuts, and the blood it spills is drunk into the store;
    ///   Pestilence: the blow carries a sickness, at half strength.</summary>
    public static class LifePaint
    {
        [ThreadStatic] private static RuneKind painting;
        [ThreadStatic] private static Pawn by;
        public static bool On => by != null;
        public static RuneKind Kind => painting;

        private sealed class Scope : IDisposable { private readonly RuneKind k0; private readonly Pawn b0; public Scope(RuneKind k, Pawn b) { k0 = painting; b0 = by; painting = k; by = b; } public void Dispose() { painting = k0; by = b0; } }
        public static IDisposable Paint(RuneKind deep, Pawn caster) => new Scope(deep, caster);

        public static HediffDef Petrified => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_PetrifiedPart");

        /// <summary>A part turned to stone: deeper the stronger the blow; never torn off.</summary>
        public static void Petrify(Pawn p, BodyPartRecord part, float amount, Pawn caster)
        {
            HediffDef d = Petrified;
            if (d == null || p == null || p.Dead) return;
            part = part ?? p.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside).Where(x => x.def.hitPoints > 0).RandomElementByWeightWithFallback(x => x.coverageAbs);
            if (part == null) return;
            Hediff h = p.health.hediffSet.hediffs.FirstOrDefault(x => x.def == d && x.Part == part);
            if (h == null) { h = HediffMaker.MakeHediff(d, p, part); h.Severity = 0.01f; p.health.AddHediff(h, part); }
            h.Severity = Mathf.Min(0.95f, h.Severity + amount / Mathf.Max(8f, part.def.GetMaxHealth(p)));
            if (p.Spawned) MapComponent_SpellSprites.PutTinted(p.Map, "Life/StoneCrust", LifeMagic.PairColor(RuneKind.Earth), p.DrawPos, 0.9f, 50);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_LifePaint
    {
        public static bool Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            if (!LifePaint.On || !(__instance is Pawn p) || p.Dead || !p.RaceProps.IsFlesh || dinfo.Def == null || !dinfo.Def.harmsHealth) return true;
            Pawn caster = dinfo.Instigator as Pawn;
            if (caster == null || dinfo.Amount <= 0f) return true;
            switch (LifePaint.Kind)
            {
                case RuneKind.Earth:
                    LifePaint.Petrify(p, dinfo.HitPart, dinfo.Amount, caster);
                    return false;   // stone instead of the blow
                case RuneKind.Fire:
                    LifeMagic.Seed(p, caster, dinfo.Amount / 6f, false, 3);
                    dinfo.SetAmount(dinfo.Amount * 0.5f);
                    return true;
                case RuneKind.Water:
                    dinfo = new DamageInfo(DamageDefOf.Cut, dinfo.Amount, dinfo.ArmorPenetrationInt, dinfo.Angle, caster, dinfo.HitPart);
                    Mages.Core(caster)?.AddTo(RuneKind.Water, LifeMagic.Suck(p, dinfo.Amount * 0.5f));
                    return true;
                case RuneKind.Air:
                    Pestilence.SowOn(p, caster, 0.1f);
                    dinfo.SetAmount(dinfo.Amount * 0.5f);
                    return true;
            }
            return true;
        }
    }

    /// <summary>Stone does not lie down (the author, Fix197): a body under a thick crust of Emerald, or a living statue,
    /// is not downed by its stone - it stands where it was caught, held still. Only what would down it anyway (no
    /// consciousness, the pain) still does.</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.ShouldBeDowned))]
    public static class Patch_StatueStands
    {
        private static HediffDef crust;
        public static void Postfix(Pawn ___pawn, ref bool __result)
        {
            if (!__result || ___pawn?.health?.hediffSet == null) return;
            crust = crust ?? DefDatabase<HediffDef>.GetNamedSilentFail("Avir_StoneCrust");
            Hediff h = crust != null ? ___pawn.health.hediffSet.GetFirstHediffOfDef(crust) : null;
            if (h == null || h.Severity < 0.4f) return;
            if (!___pawn.health.capacities.CanBeAwake || ___pawn.health.hediffSet.PainTotal >= ___pawn.GetStatValue(StatDefOf.PainShockThreshold)) return;
            __result = false;
            if (___pawn.Spawned) { ___pawn.stances?.stunner?.StunFor(300, null, false, false); ___pawn.pather?.StopDead(); }
        }
    }
}
