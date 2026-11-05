using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Avir.Artifactorics
{
    public class CompProperties_ArtifactStoneEffect : CompProperties_AbilityEffect
    {
        /// <summary>Which ability stone this castable ability belongs to.</summary>
        public ArtifactAbilityDef stoneAbility;
    }

    /// <summary>
    /// Base of every stone ability: finds the working stone, checks and spends its charge,
    /// applies the effect with the stone's power, then sets the cooldown (metal-adjusted).
    /// </summary>
    public abstract class CompAbilityEffect_ArtifactStone : CompAbilityEffect
    {
        public new CompProperties_ArtifactStoneEffect Props => (CompProperties_ArtifactStoneEffect)props;

        protected ActiveStone Source => ArtifactAbilities.Source(parent.pawn, Props.stoneAbility);

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            ActiveStone a = Source;
            if (a == null)
            {
                if (throwMessages) Messages.Message("No working stone for this ability.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            float share = ArtifactAbilities.Cost(a, Props.stoneAbility.chargeCost) / ArtifactCharge.Capacity(a.state, parent.pawn);
            if (a.state.charge < share)
            {
                if (throwMessages) Messages.Message("The artifact has too little charge.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn p = parent.pawn;
            ActiveStone a = Source;
            if (a == null || p?.Map == null) return;
            if (!ArtifactAbilities.Spend(p, a, ArtifactAbilities.Cost(a, Props.stoneAbility.chargeCost))) return;
            float power = ArtifactAbilities.Power(p, a.state, a.stone);
            DoEffect(p, a, power, target);
            int cd = ArtifactAbilities.Cooldown(a);
            if (cd > 0) parent.StartCooldown(cd);
        }

        protected abstract void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target);

        protected static void Stun(Pawn victim, Thing by, float seconds)
        {
            if (victim?.stances?.stunner == null || victim.Dead || !victim.Spawned) return;
            victim.stances.stunner.StunFor(Mathf.RoundToInt(seconds * 60f), by, false, true);
        }
    }

    // ------------------------------------------------------------------ paralysis (held)

    public class CompAbilityEffect_ArtifactParalysis : CompAbilityEffect_ArtifactStone
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!(target.Thing is Pawn t) || t.Dead) return false;
            if (ArtifactParalysis.IsHolding(parent.pawn))
            {
                if (throwMessages) Messages.Message("Already holding someone.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            if (target.Thing is Pawn t) ArtifactParalysis.StartHold(caster, t, a, power);
        }
    }

    /// <summary>A held paralysis: lasts until released, the target dies or leaves, the caster
    /// falls, or the artifact runs dry. Costs charge every second it is held. The caster may move.</summary>
    public static class ArtifactParalysis
    {
        private class Hold
        {
            public Pawn caster, target;
            public ActiveStone stone;
            public float power;
        }

        private static readonly List<Hold> holds = new List<Hold>();

        public static bool IsHolding(Pawn p) => holds.Any(h => h.caster == p);

        public static void StartHold(Pawn caster, Pawn target, ActiveStone a, float power)
        {
            holds.RemoveAll(h => h.caster == caster);
            holds.Add(new Hold { caster = caster, target = target, stone = a, power = power });
            if (!caster.health.hediffSet.HasHediff(ArtifactAbilityHediffDefOf.Avir_ArtifactParalysisHold))
                caster.health.AddHediff(ArtifactAbilityHediffDefOf.Avir_ArtifactParalysisHold);
            Stun(target, caster);
        }

        public static void Release(Pawn caster)
        {
            holds.RemoveAll(h => h.caster == caster);
            Hediff h = caster?.health?.hediffSet?.GetFirstHediffOfDef(ArtifactAbilityHediffDefOf.Avir_ArtifactParalysisHold);
            if (h != null) caster.health.RemoveHediff(h);
        }

        private static void Stun(Pawn target, Pawn caster)
        {
            if (target?.stances?.stunner == null || target.Dead || !target.Spawned) return;
            target.stances.stunner.StunFor(45, caster, false, true);
        }

        public static void TickHolds(Map map)
        {
            for (int i = holds.Count - 1; i >= 0; i--)
            {
                Hold h = holds[i];
                if (h.caster.Map != map) continue;
                bool end = h.caster.Dead || h.caster.Downed || !h.caster.Spawned || h.target == null || h.target.Dead
                           || !h.target.Spawned || h.target.Map != h.caster.Map
                           || ArtifactAbilities.Source(h.caster, ArtifactAbilityDefOf.ArtifactAbility_Paralysis) == null;
                if (!end)
                {
                    float cost = ArtifactAbilities.Cost(h.stone, h.stone.stone.ability.chargePerSecond) * 0.5f / Mathf.Max(0.25f, h.power);
                    end = !ArtifactAbilities.Spend(h.caster, h.stone, cost);
                }
                if (end) { Release(h.caster); continue; }
                Stun(h.target, h.caster);
                if (Find.TickManager.TicksGame % 120 == 0) FleckMaker.ThrowMicroSparks(h.target.DrawPos, map);
            }
        }
    }

    [DefOf]
    public static class ArtifactAbilityHediffDefOf
    {
        public static HediffDef Avir_ArtifactParalysisHold;
        public static HediffDef Avir_ArtifactStanched;
        public static ThingDef Avir_ArtifactBarrier_4Cells;

        static ArtifactAbilityHediffDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactAbilityHediffDefOf)); }
    }

    /// <summary>On the caster while a paralysis is held: carries the Release button.</summary>
    public class Hediff_ParalysisHold : HediffWithComps
    {
        public override bool ShouldRemove => base.ShouldRemove || !ArtifactParalysis.IsHolding(pawn);

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos()) yield return g;
            yield return new Command_Action
            {
                defaultLabel = "Release paralysis",
                defaultDesc = "Let go of the held target.",
                icon = ArtifactAbilityDefOf.ArtifactAbility_Paralysis.Icon,
                action = () => ArtifactParalysis.Release(pawn)
            };
        }
    }

    // ------------------------------------------------------------------ mass paralysis

    public class CompAbilityEffect_ArtifactMassParalysis : CompAbilityEffect_ArtifactStone
    {
        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            Map map = caster.Map;
            float r = ArtifactAbilities.Radius(a);
            var victims = map.mapPawns.AllPawnsSpawned
                .Where(p => !p.Dead && p.HostileTo(caster) && p.Position.DistanceTo(target.Cell) <= r).ToList();
            FleckMaker.Static(target.Cell.ToVector3Shifted(), map, FleckDefOf.PsycastAreaEffect, r);
            if (victims.Count == 0) return;
            float seconds = ArtifactAbilities.Duration(a) * power / Mathf.Sqrt(victims.Count);
            foreach (Pawn v in victims) Stun(v, caster, seconds);
        }
    }

    // ------------------------------------------------------------------ teleport

    public class CompAbilityEffect_ArtifactTeleport : CompAbilityEffect_ArtifactStone
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!ArtifactMobility.SafeLanding(target.Cell, parent.pawn.Map, parent.pawn))
            {
                if (throwMessages) Messages.Message("Cannot teleport there.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            float reach = parent.def.verbProperties.range * Mathf.Sqrt(Mathf.Max(0.25f, Source != null ? ArtifactAbilities.Power(parent.pawn, Source.state, Source.stone) : 1f));
            if (target.Cell.DistanceTo(parent.pawn.Position) > reach)
            {
                if (throwMessages) Messages.Message("Too far for this stone (" + reach.ToString("0") + " cells).", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            ArtifactMobility.Jump(caster, target.Cell);
            caster.jobs?.StopAll();
        }
    }

    /// <summary>Teleport stone, automatic use: only at peace, only short trips inside the home area
    /// to a workbench, a bed or a table, and only onto a safe cell next to it.</summary>
    public static class ArtifactTeleport
    {
        public static void TryAuto(Pawn p, ActiveStone a)
        {
            if (!p.pather.Moving || ArtifactCharge.InCombat(p) || p.Drafted) return;
            Ability ab = p.abilities?.GetAbility(a.stone.ability.ability);
            if (ab == null || ab.OnCooldown) return;
            Thing dest = p.pather.Destination.Thing;
            if (dest == null || !(dest is Building_WorkTable || dest is Building_Bed || dest.def.surfaceType == SurfaceType.Eat)) return;
            Map map = p.Map;
            if (!map.areaManager.Home[p.Position] || !map.areaManager.Home[dest.Position]) return;
            float dist = p.Position.DistanceTo(dest.Position);
            ArtifactoricsSettingsDef s = ArtifactoricsSettingsDef.Main;
            if (dist < s.autoTeleportMinDistance) return;
            float reach = ab.def.verbProperties.range * Mathf.Sqrt(Mathf.Max(0.25f, ArtifactAbilities.Power(p, a.state, a.stone)));
            if (dist > reach) return;
            IntVec3 land = IntVec3.Invalid;
            IntVec3 aim = dest.def.hasInteractionCell ? dest.InteractionCell : dest.Position;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(aim, 2f, true))
            {
                if (!map.areaManager.Home[c] || !ArtifactMobility.SafeLanding(c, map, p)) continue;
                land = c;
                break;
            }
            if (!land.IsValid) return;
            if (!ArtifactAbilities.Spend(p, a, ArtifactAbilities.Cost(a, s.autoTeleportCost))) return;
            LocalTargetInfo d = p.pather.Destination;
            ArtifactMobility.Jump(p, land);
            ab.StartCooldown(Mathf.RoundToInt(s.autoTeleportCooldownSeconds * 60f));
            if (d.IsValid && p.CurJob != null) p.pather.StartPath(d, PathEndMode.Touch);
        }
    }

    // ------------------------------------------------------------------ Architect's blueprint (passive)

    /// <summary>Repairs player buildings anywhere on the pawn's map. While the pawn is busy
    /// repairing: fast, up to 15 buildings. In the background (switch in the artifact menu): slow,
    /// up to 5 buildings.</summary>
    public static class ArtifactArchitect
    {
        public static void Tick(Pawn p, ActiveStone a, int ticks)
        {
            ArtifactoricsSettingsDef s = ArtifactoricsSettingsDef.Main;
            Map map = p.Map;
            bool active = p.CurJobDef == JobDefOf.Repair;
            if (!active && !a.stone.backgroundOn) return;
            List<Thing> damaged = map.listerBuildingsRepairable.RepairableBuildings(Faction.OfPlayer);
            if (damaged.NullOrEmpty()) return;
            float power = ArtifactAbilities.Power(p, a.state, a.stone);
            int count = active ? s.architectActiveBuildings : s.architectBackgroundBuildings;
            float hpPerSec = (active ? s.architectActiveHpPerSecond : s.architectBackgroundHpPerSecond) * power;
            float seconds = ticks / 60f;
            float cost = active
                ? s.architectActiveCostPer2s * seconds / 2f
                : 0f;
            var targets = damaged.Where(b => b.Spawned && b.HitPoints < b.MaxHitPoints)
                .OrderBy(b => b.Position.DistanceToSquared(p.Position)).Take(count).ToList();
            if (targets.Count == 0) return;
            if (!active) cost = s.architectBackgroundCostPerBuildingPer10s * targets.Count * seconds / 10f;
            if (!ArtifactAbilities.Spend(p, a, ArtifactAbilities.Cost(a, cost))) return;
            foreach (Thing b in targets)
            {
                int add = Mathf.Max(1, Mathf.RoundToInt(hpPerSec * seconds));
                b.HitPoints = Mathf.Min(b.MaxHitPoints, b.HitPoints + add);
                if (b.HitPoints >= b.MaxHitPoints) map.listerBuildingsRepairable.Notify_BuildingRepaired((Building)b);
                if (Rand.Chance(0.3f)) FleckMaker.ThrowMicroSparks(b.DrawPos, map);
            }
        }
    }

    // ------------------------------------------------------------------ barrier

    public class CompAbilityEffect_ArtifactBarrier : CompAbilityEffect_ArtifactStone
    {
        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            ArtifactoricsSettingsDef s = ArtifactoricsSettingsDef.Main;
            float r = ArtifactAbilities.Radius(a) * Mathf.Sqrt(power);
            int ri = Mathf.Clamp(Mathf.RoundToInt(r), 3, 9);
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_ArtifactBarrier_" + ri + "Cells") ?? ArtifactAbilityHediffDefOf.Avir_ArtifactBarrier_4Cells;
            Thing barrier = ThingMaker.MakeThing(def);
            CompProjectileInterceptor ic = barrier.TryGetComp<CompProjectileInterceptor>();
            int hp = Mathf.RoundToInt(s.barrierHitPoints * power);
            if (ic != null)
            {
                ic.maxHitPointsOverride = hp;
                ic.currentHitPoints = hp;
            }
            CompArtifactBarrier life = barrier.TryGetComp<CompArtifactBarrier>();
            if (life != null) life.ticksLeft = Mathf.RoundToInt(ArtifactAbilities.Duration(a) * 60f);
            GenSpawn.Spawn(barrier, target.Cell, caster.Map);
            FleckMaker.Static(target.Cell.ToVector3Shifted(), caster.Map, FleckDefOf.PsycastAreaEffect, ri);
        }
    }

    public class CompProperties_ArtifactBarrier : CompProperties
    {
        public CompProperties_ArtifactBarrier() { compClass = typeof(CompArtifactBarrier); }
    }

    /// <summary>The barrier's own lifetime, set per cast.</summary>
    public class CompArtifactBarrier : ThingComp
    {
        public int ticksLeft = 2400;

        public override void CompTick()
        {
            base.CompTick();
            if (--ticksLeft <= 0 && parent.Spawned) parent.Destroy();
        }

        public override string CompInspectStringExtra() => "Fades in " + ticksLeft.ToStringTicksToPeriod() + ".";

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksLeft, "ticksLeft", 2400);
        }
    }

    // ------------------------------------------------------------------ push

    public class CompAbilityEffect_ArtifactPush : CompAbilityEffect_ArtifactStone
    {
        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            Map map = caster.Map;
            Vector3 dir = (target.Cell - caster.Position).ToVector3();
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            float reach = ArtifactAbilities.Radius(a);
            int knock = Mathf.Clamp(Mathf.RoundToInt(2f + power), 2, 5);
            float stun = ArtifactAbilities.Duration(a);
            foreach (Pawn v in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (v == caster || v.Dead || !v.HostileTo(caster)) continue;
                Vector3 to = (v.Position - caster.Position).ToVector3();
                float d = to.magnitude;
                if (d > reach || d < 0.5f) continue;
                if (Vector3.Angle(dir, to) > 35f) continue;
                IntVec3 land = v.Position;
                Vector3 step = to.normalized;
                for (int i = 1; i <= knock; i++)
                {
                    IntVec3 c = (v.Position.ToVector3Shifted() + step * i).ToIntVec3();
                    if (!c.InBounds(map) || !c.Standable(map) || c.GetFirstPawn(map) != null) break;
                    land = c;
                }
                if (land != v.Position)
                {
                    v.Position = land;
                    v.Notify_Teleported(true, true);
                }
                Stun(v, caster, stun);
                FleckMaker.ThrowDustPuff(v.Position, map, 1f);
            }
            FleckMaker.Static(caster.TrueCenter() + dir, map, FleckDefOf.PsycastAreaEffect, 1.5f);
        }
    }

    // ------------------------------------------------------------------ shroud

    public class CompAbilityEffect_ArtifactShroud : CompAbilityEffect_ArtifactStone
    {
        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            Map map = caster.Map;
            float r = ArtifactAbilities.Radius(a);
            int amount = Mathf.RoundToInt(ArtifactoricsSettingsDef.Main.shroudGasPerCell * Mathf.Max(0.5f, power)
                                          * (a.stone.metal?.stoneDuration ?? 1f));
            foreach (IntVec3 c in GenRadial.RadialCellsAround(target.Cell, r, true))
            {
                if (!c.InBounds(map) || c.Filled(map)) continue;
                GasUtility.AddGas(c, map, GasType.BlindSmoke, amount);
            }
        }
    }

    // ------------------------------------------------------------------ stanch

    public class CompAbilityEffect_ArtifactStanch : CompAbilityEffect_ArtifactStone
    {
        protected override void DoEffect(Pawn caster, ActiveStone a, float power, LocalTargetInfo target)
        {
            Map map = caster.Map;
            float r = ArtifactAbilities.Radius(a) * Mathf.Sqrt(Mathf.Max(0.25f, power));
            int ticks = Mathf.RoundToInt(ArtifactAbilities.Duration(a) * power * 60f);
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (p.Dead || p.Faction != caster.Faction || p.Position.DistanceTo(caster.Position) > r) continue;
                if (p.health.hediffSet.GetFirstHediffOfDef(ArtifactAbilityHediffDefOf.Avir_ArtifactStanched) is Hediff_ArtifactTimed h)
                    h.ticksLeft = Mathf.Max(h.ticksLeft, ticks);
                else
                {
                    var n = (Hediff_ArtifactTimed)HediffMaker.MakeHediff(ArtifactAbilityHediffDefOf.Avir_ArtifactStanched, p);
                    n.ticksLeft = ticks;
                    p.health.AddHediff(n);
                }
                FleckMaker.ThrowMicroSparks(p.DrawPos, map);
            }
            FleckMaker.Static(caster.TrueCenter(), map, FleckDefOf.PsycastAreaEffect, r);
        }
    }
}
