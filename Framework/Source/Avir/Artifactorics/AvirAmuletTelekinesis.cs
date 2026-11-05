using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // AVIR TRADITIONAL AMULET - A'Shaign battle telekinesis.
    //
    // An A'Shaign amulet with any warrior path at battleTelekinesisNode (9). It deals NO damage (the
    // mod is played with Combat Extended); it only takes the fight out of an enemy's hands:
    //   - stop a projectile flying at the wearer          (vanilla Bullet/Projectile_Explosive, CE ProjectileCE)
    //   - wrest the weapon from the enemy the wearer runs at in melee
    //   - disarm a shooter aiming at the wearer
    //   - knock an enemy off its feet for a moment (it visibly lies on the ground)
    // Chance = base x wearer's psychic sensitivity x path strength (x volley), capped. The force is
    // physical: the target's psychic sensitivity does not matter (the deaf are not immune); a
    // knockdown is harder on big bodies (1 / body size, floor tkTargetFloor). Mechanoid weapons
    // are built in and cannot be wrested.
    // Every effect has a window on the wearer (one roll per window, hit or miss; the projectile
    // window starts only on a stop) and an immunity on a target it worked on, so an enemy of the
    // Avir never stays without a weapon for long. Weapons fall to the ground and can be picked up.
    // All numbers: Avir_AmuletSettings (tk*).
    // =====================================================================================

    [DefOf]
    public static class AvirAmuletTelekinesisDefOf
    {
        public static JobDef Avir_TelekineticKnockdown;
        static AvirAmuletTelekinesisDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(AvirAmuletTelekinesisDefOf)); }
    }

    public static class AmuletBattleTelekinesis
    {
        private static AmuletSettingsDef S => AmuletSettingsDef.Main;

        // wearer windows and target immunities (short-lived, not saved)
        private static readonly Dictionary<int, int> deflectReady = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> wrestReady = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> shooterReady = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> knockReady = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> disarmImmune = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> knockImmune = new Dictionary<int, int>();
        // one roll per projectile (a derived Impact may call the base one)
        private static readonly HashSet<int> rolled = new HashSet<int>();

        private static int Now => Find.TickManager?.TicksGame ?? 0;

        private static bool Ready(Dictionary<int, int> d, Thing t, int now) => !d.TryGetValue(t.thingIDNumber, out int r) || now >= r;

        /// <summary>Is battle telekinesis awake on this pawn; wearer factor (sensitivity x strength x volley).</summary>
        public static bool Active(Pawn p, out float wearerFactor, out float radius)
        {
            wearerFactor = 0f; radius = 0f;
            if (S == null || p == null || p.Dead || p.Downed || !p.Spawned) return false;
            CompAvirAmulet a = AmuletDevelopment.WornBy(p);
            if (a == null || !a.IsMulticlass || !AmuletDevelopment.WorksFor(a, p)) return false;
            AmuletCasteDef elder = AvirAmuletElderDefOf.Avir_Caste_Elder;
            int level = 0; float k = 0f;
            foreach (AmuletSubclassDef b in elder.Subclasses)
            {
                int l = AmuletDevelopment.Opened(a, b);
                if (l > level) level = l;
                if (l > 0) k = Mathf.Max(k, AmuletDevelopment.Strength(a, b, p));
            }
            if (level < S.battleTelekinesisNode) return false;
            if (!AvirTelekinesisUtility.TryGetRadius(p, out radius) || radius <= 0f) return false;
            float sens = p.GetStatValue(StatDefOf.PsychicSensitivity);
            wearerFactor = sens * k * (AmuletDevelopment.HasVolley(a) ? S.tkVolleyFactor : 1f);
            return wearerFactor > 0f;
        }

        /// <summary>Telekinesis is a physical force, not a psychic one: the target's mind does not
        /// matter, only its mass - the bigger the body, the harder it is to throw off its feet.</summary>
        public static float KnockdownFactor(Pawn t) => Mathf.Clamp(1f / Mathf.Max(0.01f, t.BodySize), S.tkTargetFloor, 1f);

        private static bool Roll(float baseChance, float wearerFactor, float targetFactor) =>
            Rand.Chance(Mathf.Min(S.tkChanceCap, baseChance * wearerFactor * targetFactor));

        // ---------------------------------------------------------------- timed (every 15 ticks, in a fight)

        public static void Tick(Pawn p, CompAvirAmulet a, Map map, int now)
        {
            if (!Active(p, out float wf, out float radius)) return;

            // the enemy the wearer runs at: wrest its weapon
            if (p.CurJob?.def == JobDefOf.AttackMelee && p.CurJob.targetA.Thing is Pawn t && Ready(wrestReady, p, now))
            {
                float d = p.Position.DistanceTo(t.Position);
                if (d > 1.5f && d <= radius && CanDisarm(p, t, now))
                {
                    wrestReady[p.thingIDNumber] = now + S.tkWrestCooldown;
                    if (Roll(S.tkWrestChance, wf, 1f)) Disarm(p, t, S.tkWrestImmunity, now);
                }
            }

            // a shooter aiming at the wearer
            if (Ready(shooterReady, p, now))
            {
                foreach (Pawn o in map.mapPawns.AllPawnsSpawned)
                {
                    if (!(o.stances?.curStance is Stance_Warmup w) || w.verb == null || w.verb.IsMeleeAttack) continue;
                    if (w.focusTarg.Thing != p || !AmuletElder.Hostile(p, o) || o.Position.DistanceTo(p.Position) > radius) continue;
                    if (!CanDisarm(p, o, now)) continue;
                    shooterReady[p.thingIDNumber] = now + S.tkDisarmShooterCooldown;
                    if (Roll(S.tkDisarmShooterChance, wf, 1f)) Disarm(p, o, S.tkDisarmShooterImmunity, now);
                    break;
                }
            }

            // knock the nearest enemy in reach off its feet
            if (Ready(knockReady, p, now))
            {
                Pawn n = AmuletElder.NearestHostile(p, radius);
                if (n != null && Ready(knockImmune, n, now) && n.jobs != null
                    && n.CurJobDef != AvirAmuletTelekinesisDefOf.Avir_TelekineticKnockdown
                    && GenSight.LineOfSight(p.Position, n.Position, map, true))
                {
                    knockReady[p.thingIDNumber] = now + S.tkKnockdownCooldown;
                    if (Roll(S.tkKnockdownChance, wf, KnockdownFactor(n))) Knockdown(p, n, now);
                }
            }
        }

        private static bool CanDisarm(Pawn p, Pawn t, int now) =>
            AmuletElder.Hostile(p, t) && !t.Downed && !t.RaceProps.IsMechanoid && t.equipment?.Primary != null
            && Ready(disarmImmune, t, now) && GenSight.LineOfSight(p.Position, t.Position, t.Map, true);

        /// <summary>The weapon is torn out and thrown a couple of cells towards the Avir.</summary>
        private static void Disarm(Pawn p, Pawn t, int immunity, int now)
        {
            ThingWithComps weapon = t.equipment.Primary;
            Map map = t.Map;
            IntVec3 drop = t.Position;
            Vector3 dir = (p.Position - t.Position).ToVector3();
            if (dir.sqrMagnitude > 0.01f)
            {
                IntVec3 c = t.Position + (dir.normalized * 2f).ToIntVec3();
                if (c.InBounds(map) && c.Standable(map)) drop = c;
            }
            if (!t.equipment.TryDropEquipment(weapon, out ThingWithComps dropped, drop, false)) return;
            disarmImmune[t.thingIDNumber] = now + immunity;
            FleckMaker.ThrowMicroSparks(t.DrawPos, map);
            MoteMaker.ThrowText(t.DrawPos, map, "Disarmed", new Color(0.85f, 0.75f, 1f));
            if (t.stances?.curStance is Stance_Warmup) t.stances.CancelBusyStanceSoft();
        }

        private static void Knockdown(Pawn p, Pawn t, int now)
        {
            knockImmune[t.thingIDNumber] = now + S.tkKnockdownImmunity;
            LayDown(t, S.tkKnockdownTicks);
            MoteMaker.ThrowText(t.DrawPos, t.Map, "Knocked down", new Color(0.85f, 0.75f, 1f));
        }

        /// <summary>Thrown off their feet: lies on the ground for a while, then gets up and goes back to what it was
        /// doing. Shared with the spells of the elements (a tremor, a burst of water, the earth mage coming up).</summary>
        public static void LayDown(Pawn t, int ticks)
        {
            if (t?.jobs == null || t.Dead || !t.Spawned || AvirAmuletTelekinesisDefOf.Avir_TelekineticKnockdown == null) return;
            Job job = JobMaker.MakeJob(AvirAmuletTelekinesisDefOf.Avir_TelekineticKnockdown);
            job.count = ticks;
            t.jobs.StartJob(job, JobCondition.InterruptForced, null, resumeCurJobAfterwards: true);
            FleckMaker.ThrowDustPuff(t.Position, t.Map, 1.2f);
        }

        // ---------------------------------------------------------------- projectiles

        /// <summary>A projectile about to hit a pawn: stop it in the air? (window starts on a stop)</summary>
        public static bool TryStop(Thing projectile, Thing hitThing)
        {
            if (!(hitThing is Pawn p) || projectile == null || !projectile.Spawned) return false;
            if (rolled.Count > 4000) rolled.Clear();
            if (!rolled.Add(projectile.thingIDNumber)) return false;
            int now = Now;
            if (!Ready(deflectReady, p, now) || !Active(p, out float wf, out _)) return false;
            if (!Roll(S.tkDeflectChance, wf, 1f)) return false;
            deflectReady[p.thingIDNumber] = now + S.tkDeflectCooldown;
            Map map = projectile.Map;
            FleckMaker.ThrowMicroSparks(projectile.DrawPos, map);
            MoteMaker.ThrowText(p.DrawPos, map, "Stopped", new Color(0.85f, 0.75f, 1f));
            projectile.Destroy();
            return true;
        }
    }

    /// <summary>Knocked down by telekinesis: lies on the ground for a moment, then gets up.</summary>
    public class JobDriver_AvirTelekineticKnockdown : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil lie = ToilMaker.MakeToil("AvirKnockdown");
            lie.initAction = () =>
            {
                pawn.pather?.StopDead();
                pawn.jobs.posture = PawnPosture.LayingOnGroundNormal;
            };
            lie.tickAction = () => pawn.jobs.posture = PawnPosture.LayingOnGroundNormal;
            lie.defaultCompleteMode = ToilCompleteMode.Delay;
            lie.defaultDuration = Mathf.Max(30, job.count > 0 ? job.count : 120);
            lie.handlingFacing = true;
            yield return lie;
        }
    }

    [HarmonyPatch(typeof(Bullet), "Impact")]
    public static class Patch_AmuletTelekinesis_Bullet
    {
        public static bool Prefix(Bullet __instance, Thing hitThing) => !AmuletBattleTelekinesis.TryStop(__instance, hitThing);
    }

    [HarmonyPatch(typeof(Projectile_Explosive), "Impact")]
    public static class Patch_AmuletTelekinesis_Explosive
    {
        public static bool Prefix(Projectile_Explosive __instance, Thing hitThing) => !AmuletBattleTelekinesis.TryStop(__instance, hitThing);
    }

    /// <summary>Combat Extended: ProjectileCE.Impact(Thing) and BulletCE.Impact(Thing), found by name.
    /// UNVERIFIED against a CE build (none at hand) - if the names differ it silently does nothing.</summary>
    [StaticConstructorOnStartup]
    public static class Patch_AmuletTelekinesis_CE
    {
        static Patch_AmuletTelekinesis_CE()
        {
            try
            {
                var harmony = new Harmony("Avir.AmuletTelekinesis.CE");
                HarmonyMethod prefix = new HarmonyMethod(typeof(Patch_AmuletTelekinesis_CE), nameof(Prefix));
                foreach (string name in new[] { "CombatExtended.ProjectileCE", "CombatExtended.BulletCE", "CombatExtended.ProjectileCE_Explosive" })
                {
                    Type t = AccessTools.TypeByName(name);
                    if (t == null) continue;
                    MethodInfo m = AccessTools.DeclaredMethod(t, "Impact", new[] { typeof(Thing) });
                    if (m != null && !m.IsAbstract) harmony.Patch(m, prefix: prefix);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] amulet telekinesis: Combat Extended projectiles not patched - " + e.Message);
            }
        }

        public static bool Prefix(object __instance, Thing __0) => !AmuletBattleTelekinesis.TryStop(__instance as Thing, __0);
    }
}
