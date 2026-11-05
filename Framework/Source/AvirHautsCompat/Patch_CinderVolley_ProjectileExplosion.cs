using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirHautsCompat
{
    /// <summary>
    /// Makes a Cinder-Drawn pawn's Combat Extended shots detonate a secondary Prometheum
    /// explosion on impact. This assembly does NOT reference CombatExtended.dll - CE's types are
    /// found by name via reflection, and the patch is only installed if they're actually present
    /// (see TryInstall, called from AvirHautsCompatHarmony's static constructor). If CE isn't
    /// active, this class does nothing at all.
    ///
    /// UNVERIFIED AGAINST CE SOURCE - I don't have CombatExtended's decompiled source in front of
    /// me, only its public XML (which confirms CombatExtended.ProjectileCE and
    /// CombatExtended.ProjectilePropertiesCE are real types, and that PrometheumFlame /
    /// FilthPrometheum are real, always-available CE defs - see Damages_LocalInjury.xml and
    /// Filth_Various.xml). What I could NOT verify:
    ///   - That the method to patch is named exactly "Impact" and takes a Thing as its first/only
    ///     required parameter (this mirrors vanilla Projectile.Impact(Thing), which CE almost
    ///     certainly overrides the same way, but "almost certainly" isn't "confirmed").
    ///   - The exact field/property name CE uses for "who fired this" - tried both "launcher"
    ///     (field) and "Launcher" (property), matching vanilla Projectile's own naming.
    /// If TryInstall logs a warning on startup instead of silently succeeding, open
    /// CombatExtended.ProjectileCE in ILSpy/dnSpy and check both of those against the log message
    /// - it'll tell you exactly which lookup failed.
    /// </summary>
    public static class Patch_CinderVolley_ProjectileExplosion
    {
        private const string CEProjectileTypeName = "CombatExtended.ProjectileCE";
        private const string ImpactMethodName = "Impact";

        // How much of the 3x3 actually catches fire per shot, independent of GenExplosion's own
        // chanceToStartFire. Tune this down if an hour of Cinder Volley turns into a firestorm
        // faster than intended - it's the easiest knob here.
        private const float ManualIgniteChancePerCell = 0.5f;

        private static HediffDef cinderDrawnDef;
        private static DamageDef prometheumFlameDef;
        private static ThingDef filthPrometheumDef;

        private static FieldInfo launcherField;
        private static PropertyInfo launcherProperty;

        public static void TryInstall(Harmony harmony)
        {
            try
            {
                if (!ModsConfig.IsActive("CETeam.CombatExtended"))
                {
                    return; // CE not active - nothing to patch, nothing to grant either (see Patch_Cinderfletched_PostProcess).
                }

                Type projectileType = GenTypes.GetTypeInAnyAssembly(CEProjectileTypeName);
                if (projectileType == null)
                {
                    Log.Warning("[AvirHautsCompat] CE is active but " + CEProjectileTypeName + " wasn't found - Cinder Volley's explosion won't fire. CE may have renamed this type.");
                    return;
                }

                MethodInfo impactMethod = AccessTools.Method(projectileType, ImpactMethodName);
                if (impactMethod == null)
                {
                    Log.Warning("[AvirHautsCompat] CE is active but " + CEProjectileTypeName + "." + ImpactMethodName + "(...) wasn't found (or is ambiguous) - Cinder Volley's explosion won't fire. Check the real method name/signature.");
                    return;
                }

                launcherField = AccessTools.Field(projectileType, "launcher");
                launcherProperty = launcherField != null ? null : AccessTools.Property(projectileType, "Launcher");
                if (launcherField == null && launcherProperty == null)
                {
                    Log.Warning("[AvirHautsCompat] CE is active but couldn't find a 'launcher' field or 'Launcher' property on " + CEProjectileTypeName + " - Cinder Volley's explosion won't fire.");
                    return;
                }

                cinderDrawnDef = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_CinderDrawn");
                prometheumFlameDef = DefDatabase<DamageDef>.GetNamedSilentFail("PrometheumFlame");
                filthPrometheumDef = DefDatabase<ThingDef>.GetNamedSilentFail("FilthPrometheum");

                harmony.Patch(impactMethod, prefix: new HarmonyMethod(typeof(Patch_CinderVolley_ProjectileExplosion), nameof(Prefix)));
            }
            catch (Exception e)
            {
                Log.Warning("[AvirHautsCompat] Cinder Volley CE patch install failed: " + e);
            }
        }

        // Prefix, not postfix: by the time Impact() returns, CE (like vanilla) has likely already
        // destroyed the projectile Thing, which can leave Position/Map unreliable. Capturing
        // everything before the original method runs avoids that entirely, and we're not
        // replacing CE's own impact behavior - just adding a side effect - so there's no need to
        // block the original call.
        public static void Prefix(object __instance)
        {
            try
            {
                if (cinderDrawnDef == null || prometheumFlameDef == null) return;

                Thing projectileThing = __instance as Thing;
                if (projectileThing == null || !projectileThing.Spawned) return;

                Pawn shooter = GetLauncherPawn(__instance);
                if (shooter == null || shooter.health?.hediffSet == null) return;
                if (!shooter.health.hediffSet.HasHediff(cinderDrawnDef)) return;

                IntVec3 position = projectileThing.Position;
                Map map = projectileThing.Map;
                if (map == null) return;

                TriggerExplosion(position, map, shooter);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirHautsCompat] Cinder Volley explosion trigger failed: " + e, 0x4A51B8);
            }
        }

        private static Pawn GetLauncherPawn(object projectileInstance)
        {
            object launcherValue = launcherField != null
                ? launcherField.GetValue(projectileInstance)
                : launcherProperty?.GetValue(projectileInstance);

            return launcherValue as Pawn;
        }

        private static void TriggerExplosion(IntVec3 position, Map map, Pawn instigator)
        {
            // Radius 1.5 gives full 3x3 coverage (radius 1.0 clips the diagonals).
            const float radius = 1.5f;

            GenExplosion.DoExplosion(
                center: position,
                map: map,
                radius: radius,
                damType: prometheumFlameDef,
                instigator: instigator,
                damAmount: -1,
                armorPenetration: -1f,
                explosionSound: null,
                weapon: null,
                projectile: null,
                intendedTarget: null,
                postExplosionSpawnThingDef: filthPrometheumDef,
                postExplosionSpawnChance: filthPrometheumDef != null ? 0.5f : 0f,
                postExplosionSpawnThingCount: 1,
                postExplosionGasType: null,
                applyDamageToExplosionCellsNeighbors: false,
                preExplosionSpawnThingDef: null,
                preExplosionSpawnChance: 0f,
                preExplosionSpawnThingCount: 1,
                chanceToStartFire: 0.5f,
                damageFalloff: false);

            // Belt-and-suspenders fire spread, independent of whatever GenExplosion's own
            // chanceToStartFire actually does internally - guarantees "prometheum fire spreading"
            // reads as fire spreading, not just a damage-only blast.
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(position, radius, true))
            {
                if (!cell.InBounds(map)) continue;
                if (!Rand.Chance(ManualIgniteChancePerCell)) continue;
                FireUtility.TryStartFireIn(cell, map, Rand.Range(0.4f, 1.0f), instigator);
            }
        }
    }
}
