using System.Collections.Generic;
using System.Linq;
using Avir;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir.Traits
{
    public class CompProperties_Combustion : CompProperties_AbilityEffect
    {
        public float arcaneHungerCost = 0.15f;
        public float radius = 2.5f;
        public float initialIgniteChance = 0.7f;
        public ThingDef spreaderThingDef;

        public float soakRadius = 4.5f;
        public float soakSeverity = 0.7f;

        public CompProperties_Combustion()
        {
            this.compClass = typeof(CompAbilityEffect_Combustion);
        }
    }

    public class CompAbilityEffect_Combustion : CompAbilityEffect
    {
        private static HashSet<ThingDef> bloodFilthDefs;
        private static ThingDef primaryBloodFilthDef;

        private static DamageDef explosionDamageDef;
        private static HediffDef prometheumSoakedDef;
        private static bool defsResolved;

        private static void EnsureDefsResolved()
        {
            if (defsResolved) return;
            defsResolved = true;

            bloodFilthDefs = new HashSet<ThingDef>();
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef def = allDefs[i];
                if (def.category == ThingCategory.Filth && def.defName.ToLowerInvariant().Contains("blood"))
                {
                    bloodFilthDefs.Add(def);
                    if (primaryBloodFilthDef == null) primaryBloodFilthDef = def;
                }
            }

            BoostBloodFlammability();

            string[] explosionCandidates = { "Bomb", "Flame", "PrometheumFlame" };
            foreach (string name in explosionCandidates)
            {
                explosionDamageDef = DefDatabase<DamageDef>.GetNamedSilentFail(name);
                if (explosionDamageDef != null) break;
            }

            prometheumSoakedDef = DefDatabase<HediffDef>.GetNamedSilentFail("PrometheumSoaked");

            // One-time diagnostic dump - always logged (not WarningOnce-gated), so it's easy to
            // find and confirms exactly what got resolved without needing to trigger a failure
            // path first.
            Log.Message("[Avir] Combustion defs resolved - blood filth defs found: "
                + (bloodFilthDefs.Count == 0 ? "NONE" : string.Join(", ", bloodFilthDefs.Select(d => d.defName)))
                + " | primary: " + (primaryBloodFilthDef?.defName ?? "none")
                + " | explosion damage: " + (explosionDamageDef?.defName ?? "none")
                + " | PrometheumSoaked: " + (prometheumSoakedDef != null ? "found" : "not found (CE inactive or renamed)"));

            if (bloodFilthDefs.Count == 0)
                Log.WarningOnce("[Avir] Found no filth-category ThingDef with 'blood' in its defName - Combustion's blood-present check and spreading will never trigger.", 0x4A51C0);
            if (explosionDamageDef == null)
                Log.WarningOnce("[Avir] Couldn't find any explosion DamageDef candidate - Combustion's initial explosion will do no damage (ignition still works).", 0x4A51BF);
        }

        // THE ACTUAL FIRE FIX: blood filth almost certainly has 0 Flammability by default in
        // vanilla (it's inert debris, not fuel) - meaning FireUtility.TryStartFireIn was likely
        // succeeding technically and then extinguishing instantly with nothing to burn, invisible
        // to the player. This directly boosts the Flammability stat on every blood filth def we
        // found, once, at mod load. Global effect: blood anywhere becomes more flammable near any
        // fire from now on, not just during a Combustion cast - intentional, not a side effect to
        // work around.
        private static void BoostBloodFlammability()
        {
            const float minFlammability = 1.5f;

            StatDef flammabilityStat = DefDatabase<StatDef>.GetNamedSilentFail("Flammability");
            if (flammabilityStat == null)
            {
                Log.WarningOnce("[Avir] StatDef 'Flammability' not found - can't boost blood's flammability, fire will likely still fail to catch.", 0x4A51C2);
                return;
            }

            foreach (ThingDef def in bloodFilthDefs)
            {
                if (def.statBases == null) def.statBases = new List<StatModifier>();

                StatModifier existing = def.statBases.Find(s => s.stat == flammabilityStat);
                if (existing != null)
                {
                    if (existing.value < minFlammability) existing.value = minFlammability;
                }
                else
                {
                    def.statBases.Add(new StatModifier { stat = flammabilityStat, value = minFlammability });
                }
            }

            Log.Message("[Avir] Boosted Flammability to at least " + minFlammability + " on: "
                + string.Join(", ", bloodFilthDefs.Select(d => d.defName)));
        }

        public new CompProperties_Combustion Props => (CompProperties_Combustion)this.props;

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (!base.Valid(target, throwMessages)) return false;

            EnsureDefsResolved();

            Pawn caster = this.parent.pawn;
            Gene_ArcaneHunger hunger = Gene_ArcaneHunger.Of(caster);
            if (hunger == null || !hunger.Active || hunger.Value < this.Props.arcaneHungerCost)
            {
                if (throwMessages)
                {
                    Messages.Message(
                        caster.LabelShortCap + " doesn't have enough arcane hunger to cast this.",
                        caster, MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }

            if (bloodFilthDefs.Count == 0) return true;

            Map map = caster.Map;
            IntVec3 cell = target.Cell;
            if (map == null || !cell.IsValid || !cell.InBounds(map)) return false;

            if (!CellHasBlood(cell, map))
            {
                if (throwMessages)
                {
                    Messages.Message("No blood here to ignite.", new TargetInfo(cell, map), MessageTypeDefOf.RejectInput, false);
                }
                return false;
            }

            return true;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            EnsureDefsResolved();

            Pawn caster = this.parent.pawn;
            Map map = caster.Map;
            if (map == null) return;

            IntVec3 center = target.Cell;
            if (!center.IsValid || !center.InBounds(map)) return;

            Gene_ArcaneHunger hunger = Gene_ArcaneHunger.Of(caster);
            if (hunger != null) hunger.Value = Mathf.Clamp01(hunger.Value - this.Props.arcaneHungerCost);

            if (explosionDamageDef != null)
            {
                GenExplosion.DoExplosion(
                    center: center,
                    map: map,
                    radius: this.Props.radius,
                    damType: explosionDamageDef,
                    instigator: caster,
                    damAmount: -1,
                    armorPenetration: -1f,
                    explosionSound: null,
                    weapon: null,
                    projectile: null,
                    intendedTarget: null,
                    postExplosionSpawnThingDef: null,
                    postExplosionSpawnChance: 0f,
                    postExplosionSpawnThingCount: 1,
                    postExplosionGasType: null,
                    applyDamageToExplosionCellsNeighbors: false,
                    preExplosionSpawnThingDef: null,
                    preExplosionSpawnChance: 0f,
                    preExplosionSpawnThingCount: 1,
                    chanceToStartFire: 0.3f,
                    damageFalloff: false);
            }

            List<IntVec3> ignitedInitial = new List<IntVec3>();
            int bloodyCellsFound = 0;
            int fireAttempts = 0;
            int fireSuccesses = 0;

            if (primaryBloodFilthDef != null)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, this.Props.radius, true))
                {
                    if (!cell.InBounds(map)) continue;

                    FilthMaker.TryMakeFilth(cell, map, primaryBloodFilthDef, 1);

                    if (!CellHasBlood(cell, map)) continue;
                    bloodyCellsFound++;

                    if (Rand.Chance(this.Props.initialIgniteChance))
                    {
                        fireAttempts++;
                        bool started = FireUtility.TryStartFireIn(cell, map, Rand.Range(0.4f, 0.9f), caster);
                        if (started)
                        {
                            fireSuccesses++;
                            ignitedInitial.Add(cell);
                            CombustionUtility.ConsumeBloodFilthAt(cell, map, bloodFilthDefs);
                        }
                    }
                }
            }

            Log.Message("[Avir] Combustion cast at " + center + " - bloody cells in radius: " + bloodyCellsFound
                + " | fire attempted: " + fireAttempts + " | fire actually started: " + fireSuccesses
                + " | spreaderThingDef resolved: " + (this.Props.spreaderThingDef != null));

            if (this.Props.spreaderThingDef != null && ignitedInitial.Count > 0)
            {
                Thing spreader = ThingMaker.MakeThing(this.Props.spreaderThingDef);
                GenSpawn.Spawn(spreader, center, map);

                CompCombustionSpread spreadComp = spreader.TryGetComp<CompCombustionSpread>();
                spreadComp?.Initialize(ignitedInitial, bloodFilthDefs);
            }

            if (prometheumSoakedDef != null)
            {
                SoakNearbyPawns(center, map);
            }
        }

        private void SoakNearbyPawns(IntVec3 center, Map map)
        {
            HashSet<Pawn> nearby = new HashSet<Pawn>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, this.Props.soakRadius, true))
            {
                if (!cell.InBounds(map)) continue;
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn p) nearby.Add(p);
                }
            }

            foreach (Pawn pawn in nearby)
            {
                if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null) continue;

                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(prometheumSoakedDef);
                if (existing != null)
                {
                    existing.Severity = Mathf.Max(existing.Severity, this.Props.soakSeverity);
                }
                else
                {
                    Hediff hediff = HediffMaker.MakeHediff(prometheumSoakedDef, pawn);
                    hediff.Severity = this.Props.soakSeverity;
                    pawn.health.AddHediff(hediff);
                }
            }
        }

        private static bool CellHasBlood(IntVec3 cell, Map map)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (bloodFilthDefs.Contains(things[i].def)) return true;
            }
            return false;
        }
    }
}
