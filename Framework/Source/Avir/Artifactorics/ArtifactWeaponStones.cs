using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Artifactorics
{
    // =====================================================================================
    //  STAGE 12c - WEAPON STONES AND AUTO-USE
    //
    //  Six stones that work only in a melee weapon: dash, blade whirl, mark, pull, venom blade,
    //  reflection. The other combat stones work in weapons too; peaceful ones only in armour.
    //  Any stone with canAuto can be switched to use itself in a fight (stone menu); it then
    //  works only while its artifact holds at least the owner's threshold (artifact tab).
    // =====================================================================================

    [DefOf]
    public static class ArtifactWeaponStoneDefOf
    {
        public static ArtifactAbilityDef ArtifactAbility_Dash;
        public static ArtifactAbilityDef ArtifactAbility_BladeWhirl;
        public static ArtifactAbilityDef ArtifactAbility_Mark;
        public static ArtifactAbilityDef ArtifactAbility_Pull;
        public static ArtifactAbilityDef ArtifactAbility_VenomBlade;
        public static ArtifactAbilityDef ArtifactAbility_Reflection;

        public static HediffDef Avir_StoneMark;
        public static HediffDef Avir_StoneVenomBlade;
        public static HediffDef Avir_StoneReflection;

        static ArtifactWeaponStoneDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactWeaponStoneDefOf)); }
    }

    /// <summary>A timed effect of a stone, with the power it was cast at.</summary>
    public class Hediff_StoneBuff : HediffWithComps
    {
        public int ticksLeft;
        public float power = 1f;
        public int casterId = -1;

        public override bool ShouldRemove => base.ShouldRemove || ticksLeft <= 0;

        public override string LabelInBrackets => (ticksLeft / 60f).ToString("0") + " s";

        public override void Tick()
        {
            base.Tick();
            ticksLeft--;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksLeft, "ticksLeft");
            Scribe_Values.Look(ref power, "power", 1f);
            Scribe_Values.Look(ref casterId, "casterId", -1);
        }

        public static Hediff_StoneBuff Give(Pawn p, HediffDef def, int ticks, float power, Pawn caster)
        {
            var h = p.health.hediffSet.GetFirstHediffOfDef(def) as Hediff_StoneBuff;
            if (h == null)
            {
                h = (Hediff_StoneBuff)HediffMaker.MakeHediff(def, p);
                p.health.AddHediff(h);
            }
            h.ticksLeft = Mathf.Max(h.ticksLeft, ticks);
            h.power = Mathf.Max(h.power, power);
            h.casterId = caster?.thingIDNumber ?? -1;
            return h;
        }
    }

    // ------------------------------------------------------------------ effects

    public class CompAbilityEffect_StoneDash : CompAbilityEffect_ArtifactStone
    {
        public static float Reach(Pawn p, ActiveStone a)
        {
            float power = a != null ? ArtifactAbilities.Power(p, a.state, a.stone) : 1f;
            return a == null ? 0f : ArtifactAbilities.Radius(a) * Mathf.Sqrt(Mathf.Max(0.25f, power));
        }

        public static IntVec3 Landing(Pawn caster, Pawn target)
        {
            Map map = caster.Map;
            IntVec3 best = IntVec3.Invalid; float bd = float.MaxValue;
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(target))
            {
                if (!c.InBounds(map) || !c.Standable(map) || c.GetFirstPawn(map) != null) continue;
                float d = c.DistanceToSquared(caster.Position);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!(target.Thing is Pawn t) || t.Dead) return false;
            float reach = Reach(parent.pawn, Source);
            if (t.Position.DistanceTo(parent.pawn.Position) > reach)
            {
                if (throwMessages) Messages.Message("Too far for this stone (" + reach.ToString("0") + " cells).", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (!Landing(parent.pawn, t).IsValid)
            {
                if (throwMessages) Messages.Message("No room next to the target.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            if (!(target.Thing is Pawn t)) return;
            IntVec3 land = Landing(caster, t);
            if (!land.IsValid) return;
            ArtifactMobility.Jump(caster, land, MoveFx.Leap);
            caster.jobs?.StopAll();
            ArtifactoricsSettingsDef s = ArtifactoricsSettingsDef.Main;
            ThingWithComps w = caster.equipment?.Primary;
            if (ArtifactWeapon.BestStrike(caster, w, out DamageDef def, out float dmg, out float pen))
                t.TakeDamage(new DamageInfo(def, dmg * s.dashStrikeFactor * power, pen, -1f, caster, null, w.def));
            Stun(t, caster, s.dashStunSeconds * power);
        }
    }

    public class CompAbilityEffect_StoneWhirl : CompAbilityEffect_ArtifactStone
    {
        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            ArtifactoricsSettingsDef s = ArtifactoricsSettingsDef.Main;
            ThingWithComps w = caster.equipment?.Primary;
            if (!ArtifactWeapon.BestStrike(caster, w, out DamageDef def, out float dmg, out float pen)) return;
            float r = ArtifactAbilities.Radius(a);
            Map map = caster.Map;
            FleckMaker.Static(caster.TrueCenter(), map, FleckDefOf.PsycastAreaEffect, r * 1.6f);
            foreach (Pawn v in GenRadial.RadialDistinctThingsAround(caster.Position, map, r, true).OfType<Pawn>().ToList())
            {
                if (v == caster || v.Dead || !v.HostileTo(caster)) continue;
                ArtifactWeapon.Extra(v, def, dmg * s.whirlStrikeFactor * power, pen, caster, w.def);
                if (v.Spawned) FleckMaker.ThrowMicroSparks(v.DrawPos, map);
            }
        }
    }

    public class CompAbilityEffect_StoneMark : CompAbilityEffect_ArtifactStone
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!(target.Thing is Pawn t) || t.Dead) return false;
            return base.Valid(target, throwMessages);
        }

        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            if (!(target.Thing is Pawn t)) return;
            Hediff_StoneBuff.Give(t, ArtifactWeaponStoneDefOf.Avir_StoneMark, Mathf.RoundToInt(ArtifactAbilities.Duration(a) * power * 60f), power, caster);
            FleckMaker.Static(t.TrueCenter(), t.Map, FleckDefOf.PsycastAreaEffect, 0.8f);
        }
    }

    public class CompAbilityEffect_StonePull : CompAbilityEffect_ArtifactStone
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!(target.Thing is Pawn t) || t.Dead) return false;
            if (!CompAbilityEffect_StoneDash.Landing(t, parent.pawn).IsValid)
            {
                if (throwMessages) Messages.Message("No room next to the bearer.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            if (!(target.Thing is Pawn t) || !t.Spawned) return;
            IntVec3 land = CompAbilityEffect_StoneDash.Landing(t, caster);
            if (!land.IsValid) return;
            FleckMaker.ThrowDustPuff(t.Position, t.Map, 1f);
            t.Position = land;
            t.Notify_Teleported(true, true);
            FleckMaker.ThrowDustPuff(land, t.Map, 1f);
            Stun(t, caster, ArtifactoricsSettingsDef.Main.pullStunSeconds * power);
        }
    }

    public class CompAbilityEffect_StoneSelfBuff : CompAbilityEffect_ArtifactStone
    {
        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            HediffDef def = Props.stoneAbility == ArtifactWeaponStoneDefOf.ArtifactAbility_VenomBlade
                ? ArtifactWeaponStoneDefOf.Avir_StoneVenomBlade : ArtifactWeaponStoneDefOf.Avir_StoneReflection;
            Hediff_StoneBuff.Give(caster, def, Mathf.RoundToInt(ArtifactAbilities.Duration(a) * power * 60f), power, caster);
            FleckMaker.Static(caster.TrueCenter(), caster.Map, FleckDefOf.PsycastAreaEffect, 1f);
        }
    }

    // ------------------------------------------------------------------ hooks

    /// <summary>Mark (more damage taken) and reflection (part of each hit turned back).</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_StoneBuffsPre
    {
        [HarmonyPriority(Priority.High + 5)]
        public static void Prefix(Pawn __instance, ref DamageInfo __0, ref bool __1)
        {
            if (__1 || __instance?.health?.hediffSet == null || __0.Def == null) return;
            ArtifactoricsSettingsDef s = ArtifactoricsSettingsDef.Main;
            if (s == null || !__0.Def.ExternalViolenceFor(__instance)) return;
            List<Hediff> hs = __instance.health.hediffSet.hediffs;
            for (int i = 0; i < hs.Count; i++)
            {
                if (!(hs[i] is Hediff_StoneBuff b)) continue;
                if (b.def == ArtifactWeaponStoneDefOf.Avir_StoneMark)
                    __0.SetAmount(__0.Amount * (1f + Mathf.Min(s.markBonusMax, s.markBonus * b.power)));
                else if (b.def == ArtifactWeaponStoneDefOf.Avir_StoneReflection && ArtifactWeapon.extraDepth == 0
                         && __0.Instigator is Pawn foe && foe != __instance && foe.Spawned && __instance.Spawned
                         && foe.Position.DistanceTo(__instance.Position) <= s.reflectRange)
                {
                    float back = __0.Amount * s.reflectShare;
                    __0.SetAmount(__0.Amount - back);
                    ArtifactWeapon.Extra(foe, __0.Def, back, __0.ArmorPenetrationInt, __instance);
                    FleckMaker.ThrowMicroSparks(__instance.DrawPos, __instance.Map);
                }
            }
        }
    }

    /// <summary>Venom blade: every blow of the weapon poisons.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_StoneVenomBladePost
    {
        public static void Postfix(Pawn __instance, DamageInfo __0)
        {
            if (__instance == null || __instance.Dead || !(__0.Instigator is Pawn a) || a.health?.hediffSet == null) return;
            if (!(a.health.hediffSet.GetFirstHediffOfDef(ArtifactWeaponStoneDefOf.Avir_StoneVenomBlade) is Hediff_StoneBuff b)) return;
            if (!ArtifactWeapon.IsBlow(__0, out _)) return;
            ArtifactoricsSettingsDef s = ArtifactoricsSettingsDef.Main;
            ArtifactWeapon.ApplyVenomRaw(__instance, s.venomBladeBuildup * b.power, s.venomBladeSlow * b.power, 0f, 0f, 7, b.power);
        }
    }

    // ------------------------------------------------------------------ auto-use

    /// <summary>Uses stones switched to auto in a fight, each by its own simple rule.</summary>
    public class MapComponent_StoneAutoUse : MapComponent
    {
        public MapComponent_StoneAutoUse(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 60 != 30) return;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                // any faction: the player's stones by their switch, faction pawns' stones are set to auto when generated
                if (p.Faction == null || p.Dead || p.Downed || p.InMentalState || p.abilities == null) continue;
                if (p.stances?.stunner != null && p.stances.stunner.Stunned) continue;
                if (!ArtifactCharge.InCombat(p) && !p.mindState.anyCloseHostilesRecently) continue;
                if (ArtifactWearCache.Worn(p).Count == 0) continue;
                float threshold = GameComponent_ArtifactCharge.Get?.StoneThreshold(p) ?? 0.25f;
                foreach (ActiveStone a in ArtifactAbilities.For(p))
                {
                    if (!a.active || !a.stone.autoUse || !a.stone.ability.canAuto || a.stone.ability.ability == null) continue;
                    if (a.state.charge < threshold) continue;
                    Ability ab = p.abilities.GetAbility(a.stone.ability.ability);
                    if (ab == null || ab.OnCooldown) continue;
                    LocalTargetInfo t = Choose(p, a);
                    if (!t.IsValid) continue;
                    if (!ValidFor(ab, t)) continue;
                    ab.Activate(t, t);
                    break;   // one stone per second
                }
            }
        }

        private static bool ValidFor(Ability ab, LocalTargetInfo t)
        {
            if (ab.comps == null) return true;
            foreach (AbilityComp c in ab.comps)
                if (c is CompAbilityEffect e && !e.Valid(t, false)) return false;
            return true;
        }

        private static IEnumerable<Pawn> Foes(Pawn p, float r)
        {
            foreach (Pawn e in GenRadial.RadialDistinctThingsAround(p.Position, p.Map, r, true).OfType<Pawn>())
                if (e != p && !e.Dead && !e.Downed && e.HostileTo(p)) yield return e;
        }

        private static bool Shooter(Pawn e) => e.equipment?.Primary != null && e.equipment.Primary.def.IsRangedWeapon;

        private static LocalTargetInfo Choose(Pawn p, ActiveStone a)
        {
            ArtifactAbilityDef d = a.stone.ability;
            float range = d.ability.verbProperties?.range ?? 10f;
            Pawn target = p.mindState.enemyTarget as Pawn;
            int adjacent = Foes(p, 1.5f).Count();

            if (d == ArtifactWeaponStoneDefOf.ArtifactAbility_Dash)
            {
                float reach = CompAbilityEffect_StoneDash.Reach(p, a);
                Pawn e = target != null && !target.Dead && target.Position.DistanceTo(p.Position) > 2.5f
                         && target.Position.DistanceTo(p.Position) <= reach ? target
                         : Foes(p, reach).Where(x => x.Position.DistanceTo(p.Position) > 2.5f).OrderBy(x => x.Position.DistanceToSquared(p.Position)).FirstOrDefault();
                return adjacent == 0 && e != null ? (LocalTargetInfo)e : LocalTargetInfo.Invalid;
            }
            if (d == ArtifactWeaponStoneDefOf.ArtifactAbility_BladeWhirl)
                return Foes(p, ArtifactAbilities.Radius(a)).Count() >= 2 ? (LocalTargetInfo)p : LocalTargetInfo.Invalid;
            if (d == ArtifactWeaponStoneDefOf.ArtifactAbility_Mark)
            {
                Pawn e = target ?? Foes(p, 2f).FirstOrDefault();
                return e != null && !e.Dead && e.Position.DistanceTo(p.Position) <= range
                       && !e.health.hediffSet.HasHediff(ArtifactWeaponStoneDefOf.Avir_StoneMark) ? (LocalTargetInfo)e : LocalTargetInfo.Invalid;
            }
            if (d == ArtifactWeaponStoneDefOf.ArtifactAbility_Pull)
            {
                Pawn e = Foes(p, range).Where(x => Shooter(x) && x.Position.DistanceTo(p.Position) > 3f)
                    .OrderBy(x => x.Position.DistanceToSquared(p.Position)).FirstOrDefault();
                return adjacent == 0 && e != null && GenSight.LineOfSight(p.Position, e.Position, p.Map) ? (LocalTargetInfo)e : LocalTargetInfo.Invalid;
            }
            if (d == ArtifactWeaponStoneDefOf.ArtifactAbility_VenomBlade)
                return adjacent > 0 && !p.health.hediffSet.HasHediff(ArtifactWeaponStoneDefOf.Avir_StoneVenomBlade) ? (LocalTargetInfo)p : LocalTargetInfo.Invalid;
            if (d == ArtifactWeaponStoneDefOf.ArtifactAbility_Reflection)
            {
                bool pressed = adjacent >= 2 || Find.TickManager.TicksGame - p.mindState.lastHarmTick < 90 && p.health.summaryHealth.SummaryHealthPercent < 0.8f;
                return pressed && !p.health.hediffSet.HasHediff(ArtifactWeaponStoneDefOf.Avir_StoneReflection) ? (LocalTargetInfo)p : LocalTargetInfo.Invalid;
            }
            if (d == ArtifactSpearDefOf.ArtifactAbility_BurningSpear)
            {
                // healing flame mends a hurt ally first; otherwise a visible enemy out of blade reach
                if (a.stone.energy != null && a.stone.energy.Heals)
                {
                    Pawn ally = p.Map.mapPawns.SpawnedPawnsInFaction(p.Faction)
                        .Where(x => x != p && !x.Dead && x.Position.DistanceTo(p.Position) <= range
                                    && x.health.summaryHealth.SummaryHealthPercent < 0.75f
                                    && GenSight.LineOfSight(p.Position, x.Position, p.Map))
                        .OrderBy(x => x.health.summaryHealth.SummaryHealthPercent).FirstOrDefault();
                    if (ally != null) return ally;
                }
                Pawn e = target != null && !target.Dead && !target.Downed && target.Position.DistanceTo(p.Position) <= range
                         && target.Position.DistanceTo(p.Position) > 2.5f && GenSight.LineOfSight(p.Position, target.Position, p.Map) ? target
                         : Foes(p, range).Where(x => x.Position.DistanceTo(p.Position) > 2.5f && GenSight.LineOfSight(p.Position, x.Position, p.Map))
                             .OrderBy(x => x.Position.DistanceToSquared(p.Position)).FirstOrDefault();
                return e != null ? (LocalTargetInfo)e : LocalTargetInfo.Invalid;
            }
            if (d == ArtifactAbilityDefOf.ArtifactAbility_Paralysis)
            {
                if (ArtifactParalysis.IsHolding(p)) return LocalTargetInfo.Invalid;
                Pawn e = Foes(p, range).Where(x => GenSight.LineOfSight(p.Position, x.Position, p.Map))
                    .OrderBy(x => x.Position.DistanceToSquared(p.Position)).FirstOrDefault();
                return e != null ? (LocalTargetInfo)e : LocalTargetInfo.Invalid;
            }
            if (d == ArtifactAbilityDefOf.ArtifactAbility_MassParalysis)
            {
                float r = ArtifactAbilities.Radius(a);
                List<Pawn> foes = Foes(p, range).ToList();
                Pawn best = null; int bestN = 2;
                foreach (Pawn e in foes)
                {
                    int n = foes.Count(x => x.Position.DistanceTo(e.Position) <= r);
                    if (n > bestN) { bestN = n; best = e; }
                }
                return best != null ? new LocalTargetInfo(best.Position) : LocalTargetInfo.Invalid;
            }
            if (d == ArtifactAbilityDefOf.ArtifactAbility_Push)
            {
                if (adjacent < 2) return LocalTargetInfo.Invalid;
                Pawn e = Foes(p, 1.5f).First();
                return new LocalTargetInfo(e.Position);
            }
            if (d == ArtifactAbilityDefOf.ArtifactAbility_Shroud || d == ArtifactAbilityDefOf.ArtifactAbility_Barrier)
            {
                int shooters = Foes(p, 30f).Count(Shooter);
                return shooters >= 3 && adjacent == 0 ? new LocalTargetInfo(p.Position) : LocalTargetInfo.Invalid;
            }
            return LocalTargetInfo.Invalid;
        }
    }
}
