using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// One pending link in a Purify chain. Not a Def - a plain queued action, owned by
    /// GameComponent_AvirPurifyChains. IExposable so a chain in progress across a save/load
    /// doesn't just vanish or throw - worst case it fires a step late or not at all, which is a
    /// far smaller problem than crashing on load.
    ///
    /// currentCenter is the center of the MOST RECENT ring - the next step picks a random point
    /// on THAT ring's own border as its new center, so the chain walks outward step by step
    /// instead of looping back on the same spot.
    /// </summary>
    public class AvirPurifyChainStep : IExposable
    {
        public Map map;
        public IntVec3 currentCenter;
        public Pawn instigator;
        public int stepsRemaining;
        public int fireAtAbsTick;

        public void ExposeData()
        {
            Scribe_References.Look(ref map, "map");
            Scribe_Values.Look(ref currentCenter, "currentCenter");
            Scribe_References.Look(ref instigator, "instigator");
            Scribe_Values.Look(ref stepsRemaining, "stepsRemaining");
            Scribe_Values.Look(ref fireAtAbsTick, "fireAtAbsTick");
        }
    }

    /// <summary>
    /// Drives Purify's chain reaction.
    ///
    /// Every detonation, initial cast or chained repeat, is the SAME full pattern:
    ///   1. One real explosion (Flame damage) filling a small "5x5" disc at that pattern's center.
    ///   2. A ring of SEPARATE small point-explosions (also Flame damage) spaced around a circle
    ///      at "18x18" radius around that same center - a genuine ring of real, damaging blasts,
    ///      not one big filled disc, so the gap between center and ring is never touched and
    ///      stays normal flammable ground fire can spread through from both sides.
    ///
    /// What changes between steps is WHERE the pattern happens: the initial cast centers on the
    /// cast target. Every chained repeat after that picks a random point on the PREVIOUS step's
    /// own ring border and detonates the whole pattern fresh there - walking the explosion
    /// outward step by step (never back over already-burnt ground), for stepsRemaining repeats,
    /// 60 ticks apart, driven by the caster's current Ecstatic Flames stack count at cast time.
    ///
    /// Every blast point also lays down (boosted-flammability) oil filth before igniting, so fire
    /// has real fuel to catch and keep spreading on instead of dying instantly on ground the
    /// explosion just cleared.
    /// </summary>
    public class GameComponent_AvirPurifyChains : GameComponent
    {
        public const int ChainStepDelayTicks = 60;

        private const float CenterExplosionRadius = 2.5f; // "5x5"
        private const float RingRadius = 9f; // "18x18"
        private const float RingPointExplosionRadius = 1.25f; // each individual ring blast
        private const float RingPointSpacing = 2f; // cells between ring blast centers

        private const float ChanceToStartFire = 0.7f; // matches Avir_Combustion's initialIgniteChance
        private const float FireSize = 1.0f;
        private const float SoakSeverity = 0.7f; // matches Avir_Combustion's own soakSeverity

        public List<AvirPurifyChainStep> pendingSteps = new List<AvirPurifyChainStep>();

        public GameComponent_AvirPurifyChains(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pendingSteps, "avirPurifyChainSteps", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && pendingSteps == null)
            {
                pendingSteps = new List<AvirPurifyChainStep>();
            }
        }

        /// <summary>Fires the very first pattern immediately (via CompAbilityEffect_Purify
        /// calling DetonatePattern directly) - this only queues the CHAINED repeats after it.</summary>
        public void QueueChainRepeats(Map map, IntVec3 originCell, Pawn instigator, int additionalRepeats)
        {
            if (additionalRepeats <= 0)
            {
                return;
            }

            pendingSteps.Add(new AvirPurifyChainStep
            {
                map = map,
                currentCenter = originCell,
                instigator = instigator,
                stepsRemaining = additionalRepeats,
                fireAtAbsTick = Find.TickManager.TicksGame + ChainStepDelayTicks,
            });
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (pendingSteps.Count == 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;

            for (int i = pendingSteps.Count - 1; i >= 0; i--)
            {
                AvirPurifyChainStep step = pendingSteps[i];

                if (step?.map == null || !Find.Maps.Contains(step.map))
                {
                    // Map no longer exists (abandoned/left) - drop this chain silently.
                    pendingSteps.RemoveAt(i);
                    continue;
                }

                if (now < step.fireAtAbsTick)
                {
                    continue;
                }

                pendingSteps.RemoveAt(i);

                // Pick a random point on the PREVIOUS ring's own border and detonate the whole
                // pattern fresh there - this is the new center for whatever step comes next.
                List<IntVec3> previousRingPoints = GetRingPoints(step.map, step.currentCenter);
                if (previousRingPoints.Count == 0)
                {
                    continue; // nothing in bounds to walk to - chain dies out here.
                }

                IntVec3 nextCenter = previousRingPoints[Rand.Range(0, previousRingPoints.Count)];
                DetonatePattern(step.map, nextCenter, step.instigator);

                int remaining = step.stepsRemaining - 1;
                if (remaining > 0)
                {
                    pendingSteps.Add(new AvirPurifyChainStep
                    {
                        map = step.map,
                        currentCenter = nextCenter,
                        instigator = step.instigator,
                        stepsRemaining = remaining,
                        fireAtAbsTick = now + ChainStepDelayTicks,
                    });
                }
            }
        }

        /// <summary>The full pattern - center explosion, then the ring of point-explosions
        /// around it. Used for both the initial cast and every chained repeat, just at a
        /// different center each time.</summary>
        public static void DetonatePattern(Map map, IntVec3 center, Pawn instigator)
        {
            DetonatePoint(map, center, CenterExplosionRadius, instigator);

            List<IntVec3> ringPoints = GetRingPoints(map, center);
            for (int i = 0; i < ringPoints.Count; i++)
            {
                DetonatePoint(map, ringPoints[i], RingPointExplosionRadius, instigator);
            }
        }

        /// <summary>Computes the ring border points around "center" without detonating anything -
        /// used both to actually detonate the ring and to pick where the NEXT chain step walks to.</summary>
        private static List<IntVec3> GetRingPoints(Map map, IntVec3 center)
        {
            List<IntVec3> points = new List<IntVec3>();
            if (map == null)
            {
                return points;
            }

            float circumference = 2f * Mathf.PI * RingRadius;
            int pointCount = Mathf.Max(8, Mathf.RoundToInt(circumference / RingPointSpacing));

            for (int i = 0; i < pointCount; i++)
            {
                float angle = (2f * Mathf.PI * i) / pointCount;
                int dx = Mathf.RoundToInt(RingRadius * Mathf.Cos(angle));
                int dz = Mathf.RoundToInt(RingRadius * Mathf.Sin(angle));
                IntVec3 point = center + new IntVec3(dx, 0, dz);

                if (point.InBounds(map))
                {
                    points.Add(point);
                }
            }

            return points;
        }

        /// <summary>One real explosion (Flame damage only, no Bomb) at a single point, with fuel
        /// laid down first and fire forced afterward so it reliably catches and spreads.</summary>
        private static void DetonatePoint(Map map, IntVec3 cell, float radius, Pawn instigator)
        {
            // Fuel FIRST - the ground needs something to burn before the blast hits it, not after.
            foreach (IntVec3 c in GenRadial.RadialCellsAround(cell, radius, true))
            {
                if (c.InBounds(map))
                {
                    AvirOilFuelUtility.SpawnFuelAt(map, c);
                }
            }

            GenExplosion.DoExplosion(
                cell,
                map,
                radius,
                DamageDefOf.Flame,
                instigator,
                damAmount: -1,
                armorPenetration: -1,
                explosionSound: null,
                chanceToStartFire: ChanceToStartFire);

            // DoExplosion's own chanceToStartFire is not reliable here - force it directly rather
            // than trusting that side effect. With real fuel already down, the fire this creates
            // should actually persist and spread on its own via vanilla's normal fire mechanics.
            ForceIgniteArea(map, cell, radius, FireSize, instigator);
        }

        /// <summary>Force-spawns (or refreshes) a Fire thing on every in-bounds cell within
        /// radius, bypassing FireUtility.TryStartFireIn's normal flammability checks. Every fire
        /// tile created/refreshed here also soaks its own 9x9 in Prometheum, per fire tile.</summary>
        private static void ForceIgniteArea(Map map, IntVec3 center, float radius, float fireSize, Thing instigator)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                Fire existing = null;
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Fire f)
                    {
                        existing = f;
                        break;
                    }
                }

                if (existing != null)
                {
                    existing.fireSize = Mathf.Max(existing.fireSize, fireSize);
                }
                else
                {
                    Fire fire = (Fire)ThingMaker.MakeThing(ThingDefOf.Fire);
                    fire.fireSize = fireSize;
                    GenSpawn.Spawn(fire, cell, map);
                }

                AvirPrometheumSoakUtility.SoakAroundFireTile(map, cell, SoakSeverity);
            }
        }
    }
}
