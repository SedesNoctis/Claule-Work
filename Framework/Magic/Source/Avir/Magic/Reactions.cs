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
    // =====================================================================================
    // STATES AND REACTIONS. What the elements leave behind is not only their own: a cell may be wet (a pool, held water,
    // a river, a mire), molten (magma), mired, dusty, full of steam or of marsh gas. Whatever spell comes next meets what
    // lies there, whoever cast either of them:
    //   heat on standing water     - it boils: a cloud of scalding steam;
    //   heat or fire on a mire     - the marsh gas in it catches: a blast, and the gas round it goes up after;
    //   lightning on wet ground    - it runs through the water: everyone standing in it, as far as the water goes;
    //   water on magma             - it sets at once into black glass that cuts whoever walks it, with a burst of steam;
    //   heat on a cloud of dust    - the sand fuses into glass shards.
    // The spells say only what they bring (fire, heat, water, earth, air, lightning, dust); this is where they meet.
    // =====================================================================================
    [Flags]
    public enum Brings { None = 0, Fire = 1, Water = 2, Earth = 4, Air = 8, Lightning = 16, Heat = 32, Dust = 64 }

    public static class Reactions
    {
        public static Brings Of(RuneKind el)
        {
            switch (el)
            {
                case RuneKind.Fire: return Brings.Fire | Brings.Heat;
                case RuneKind.Water: return Brings.Water;
                case RuneKind.Earth: return Brings.Earth;
                case RuneKind.Air: return Brings.Air;
                default: return Brings.None;
            }
        }

        public static Brings Of(ComboKind k)
        {
            switch (k)
            {
                case ComboKind.Steam: return Brings.Heat | Brings.Water;
                case ComboKind.Storm: return Brings.Water | Brings.Air | Brings.Lightning;
                case ComboKind.Magma: return Brings.Fire | Brings.Heat | Brings.Earth;
                case ComboKind.Haze: return Brings.Heat | Brings.Air;
                case ComboKind.Bog: return Brings.Water | Brings.Earth;
                case ComboKind.Dust: return Brings.Earth | Brings.Air | Brings.Dust;
                default: return Brings.None;
            }
        }

        /// <summary>Standing water on a cell: a pool, water held by a mage, a mire - or the land's own water.</summary>
        public static bool Wet(IntVec3 c, Map map)
        {
            if (!c.InBounds(map)) return false;
            TerrainDef t = c.GetTerrain(map);
            if (t != null && (t.IsWater || t.defName.IndexOf("Marsh", StringComparison.OrdinalIgnoreCase) >= 0 || t.defName.IndexOf("Mud", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            return Pools(c, map).Any();
        }

        /// <summary>The water lying on a cell that can be taken away (pools, held water, mire).</summary>
        public static IEnumerable<Thing> Pools(IntVec3 c, Map map) =>
            c.GetThingList(map).Where(t => t.def == ThingDefOf.Filth_Water || t.def.defName == "Avir_SpellPuddle" || t is HeldWater || t is MirePatch);

        /// <summary>What a spell brought to these cells meets what lies on them.</summary>
        public static void Touch(Brings b, IEnumerable<IntVec3> cells, Map map, Pawn caster, float power)
        {
            if (b == Brings.None || map == null) return;
            bool hot = (b & (Brings.Heat | Brings.Fire)) != 0, water = (b & Brings.Water) != 0;
            foreach (IntVec3 c in cells.Where(x => x.InBounds(map)).Distinct().ToList())
            {
                var things = c.GetThingList(map);
                if (hot)
                {
                    // a mire's gas catches
                    if (things.Any(t => t is MirePatch)) Gas_MarshGas.Release(c, map, 1, true);
                    foreach (Gas_MarshGas g in things.OfType<Gas_MarshGas>().ToList()) g.Ignite(Rand.Range(4, 12));
                    // standing water boils
                    var pools = Pools(c, map).Where(t => !(t is MirePatch)).ToList();
                    if (pools.Count > 0)
                    {
                        foreach (Thing t in pools) t.Destroy();
                        Gas_ScaldingSteam.Release(c, map, 2);
                    }
                    // dust fuses into glass
                    if (MapComponent_ComboFields.DustAt(map, c) && Rand.Chance(0.5f)) ShardGround.Scatter(c, map, ComboDefOf.Avir_Glass);
                }
                if (water)
                {
                    // magma quenched: black glass and a burst of steam
                    foreach (MagmaPool m in things.OfType<MagmaPool>().ToList())
                    {
                        m.Destroy();
                        ShardGround.Scatter(c, map, ComboDefOf.Avir_Obsidian);
                        Gas_ScaldingSteam.Release(c, map, 1);
                    }
                }
            }
        }

        /// <summary>Lightning that reaches wet ground runs through all the water that touches it, and strikes everyone
        /// standing in it (the mage's own side spared).</summary>
        public static void Conduct(IntVec3 from, Map map, Pawn caster, float power)
        {
            if (map == null || !Wet(from, map)) return;
            var seen = new HashSet<IntVec3> { from };
            var open = new Queue<IntVec3>(); open.Enqueue(from);
            while (open.Count > 0 && seen.Count < 80)
            {
                IntVec3 c = open.Dequeue();
                foreach (IntVec3 n in GenAdj.CellsAdjacent8Way(new TargetInfo(c, map)))
                    if (!seen.Contains(n) && Wet(n, map)) { seen.Add(n); open.Enqueue(n); }
            }
            foreach (IntVec3 c in seen)
            {
                if (Rand.Chance(0.15f)) FleckMaker.ThrowLightningGlow(c.ToVector3Shifted(), map, 0.7f);
                foreach (Pawn p in c.GetThingList(map).OfType<Pawn>().ToList())
                {
                    if (p.Dead || p == caster || (caster != null && !p.HostileTo(caster))) continue;
                    p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 5f * power, 0f, -1f, caster));
                    if (!p.Dead) MagicFx.Stun(p, 45, caster);
                }
            }
        }

        /// <summary>How much standing water lies round a point: water spells draw on it (up to +40%).</summary>
        public static float WaterAround(IntVec3 at, Map map, float radius = 4f)
        {
            int n = 0;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, radius, true)) if (Wet(c, map)) n++;
            return Mathf.Min(0.4f, 0.04f * n);
        }
    }

    // -------------------------------------------------------------------------------------------- gases

    /// <summary>
    /// A cloud on one cell that creeps on its own. Built on Verse.Gas because the game's gas grid has only four kinds and
    /// no room for a fifth. The way of it - a creep budget each child inherits one less of, cardinal spreading so a closed
    /// door holds, a once-a-second look for lungs - is taken from the Kharassian clouds (Gas_KharassianCloud).
    /// </summary>
    public abstract class Gas_AvirCloud : Gas
    {
        protected virtual int CheckIntervalTicks => 60;
        protected virtual int SpreadIntervalTicks => 120;
        protected virtual float SpreadChance => 0.5f;
        protected virtual int DefaultSpreadBudget => 2;
        public int spreadBudget = -1;
        private int ticksToSpread, ticksToCheck;

        protected Gas_AvirCloud() { ticksToSpread = Rand.Range(0, SpreadIntervalTicks); ticksToCheck = Rand.Range(0, CheckIntervalTicks); }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (spreadBudget < 0) spreadBudget = DefaultSpreadBudget;
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (Destroyed || !Spawned) return;
            Spread(delta);
            if ((ticksToCheck -= delta) > 0) return;
            ticksToCheck = CheckIntervalTicks;
            OnCheck();
            foreach (Pawn p in Position.GetThingList(Map).OfType<Pawn>().ToList()) if (!p.Dead) AffectPawn(p);
        }

        protected virtual void OnCheck() { }
        protected abstract void AffectPawn(Pawn p);

        private void Spread(int delta)
        {
            if (spreadBudget <= 0 || (ticksToSpread -= delta) > 0) return;
            ticksToSpread = SpreadIntervalTicks;
            if (!Rand.Chance(SpreadChance)) return;
            foreach (IntVec3 o in GenAdj.CardinalDirections.InRandomOrder())
            {
                IntVec3 c = Position + o;
                if (!c.InBounds(Map) || c.Filled(Map) || !c.Walkable(Map) || c.GetFirstThing(Map, def) != null) continue;
                if (GenSpawn.Spawn(def, c, Map) is Gas_AvirCloud g) g.spreadBudget = spreadBudget - 1;
                spreadBudget--;
                return;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref spreadBudget, "avirCloudBudget", 0);
        }
    }

    /// <summary>Scalding steam: whoever breathes it is scalded, friend or foe; it hides them a little too.</summary>
    public class Gas_ScaldingSteam : Gas_AvirCloud
    {
        protected override int DefaultSpreadBudget => 2;

        public static void Release(IntVec3 c, Map map, int budget)
        {
            if (ComboDefOf.Avir_GasSteam == null || !c.InBounds(map) || c.Filled(map)) return;
            if (GenSpawn.Spawn(ComboDefOf.Avir_GasSteam, c, map) is Gas_ScaldingSteam g) g.spreadBudget = budget;
            GasUtility.AddGas(c, map, GasType.BlindSmoke, 1f);
        }

        protected override void AffectPawn(Pawn p)
        {
            if (p.RaceProps.IsMechanoid) return;
            p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 2f, 0f, -1f));
        }
    }

    /// <summary>Marsh gas: harmless until fire, a burning body or a blast reaches it - then it goes off, and every cloud
    /// of it the blast reaches after it (the Kharassian dunewake, done again for the bog).</summary>
    public class Gas_MarshGas : Gas_AvirCloud
    {
        protected override int DefaultSpreadBudget => 1;
        protected override int SpreadIntervalTicks => 200;
        private int igniteAt = -1;
        public bool Primed => igniteAt >= 0;

        public static void Release(IntVec3 c, Map map, int budget, bool lit)
        {
            if (ComboDefOf.Avir_GasMarsh == null || !c.InBounds(map) || c.Filled(map)) return;
            var g = c.GetFirstThing(map, ComboDefOf.Avir_GasMarsh) as Gas_MarshGas ?? GenSpawn.Spawn(ComboDefOf.Avir_GasMarsh, c, map) as Gas_MarshGas;
            if (g == null) return;
            g.spreadBudget = Mathf.Max(g.spreadBudget, budget);
            if (lit) g.Ignite(Rand.Range(4, 14));
        }

        public void Ignite(int delay)
        {
            int at = Find.TickManager.TicksGame + Mathf.Max(1, delay);
            if (igniteAt < 0 || at < igniteAt) igniteAt = at;
        }

        protected override void AffectPawn(Pawn p) { if (!Primed && p.IsBurning()) Ignite(Rand.Range(3, 10)); }

        protected override void OnCheck()
        {
            if (!Primed && Position.GetThingList(Map).Any(t => t is Fire || t is MagmaPool)) Ignite(Rand.Range(5, 15));
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (!Spawned || igniteAt < 0 || Find.TickManager.TicksGame < igniteAt) return;
            Map map = Map; IntVec3 c = Position;
            Destroy();
            GenExplosion.DoExplosion(c, map, 1.9f, DamageDefOf.Bomb, null, 16, chanceToStartFire: 0.4f);
        }

        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref igniteAt, "igniteAt", -1); }
    }

    /// <summary>Every blast that reaches marsh gas sets it off a few ticks later - the chain through a bog.</summary>
    [HarmonyPatch(typeof(Explosion), "AffectCell")]
    public static class Patch_MarshGasChain
    {
        public static void Postfix(Explosion __instance, IntVec3 c)
        {
            Map map = __instance?.Map;
            if (map == null || !c.InBounds(map)) return;
            foreach (Gas_MarshGas g in c.GetThingList(map).OfType<Gas_MarshGas>()) if (!g.Primed) g.Ignite(Rand.Range(8, 22));
        }
    }

    // -------------------------------------------------------------------------------------------- sharp ground

    /// <summary>Ground covered in shards of glass - black from quenched magma, pale from fused sand. Whoever walks on it
    /// is cut about the feet and goes carefully; it lasts for days.</summary>
    public class ShardGround : ThingWithComps
    {
        public int left = 60000 * 3;

        public static void Scatter(IntVec3 c, Map map, ThingDef def)
        {
            if (def == null || !c.InBounds(map) || !c.Standable(map) || c.GetThingList(map).Any(t => t is ShardGround)) return;
            GenSpawn.Spawn(def, c, map);
            FleckMaker.ThrowMicroSparks(c.ToVector3Shifted(), map);
        }

        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref left, "left", 180000); }

        protected override void Tick()
        {
            base.Tick();
            if (--left <= 0) { Destroy(); return; }
            if (!this.IsHashIntervalTick(60)) return;
            foreach (Pawn p in Position.GetThingList(Map).OfType<Pawn>().ToList())
            {
                if (p.Dead || p.Downed || !(p.pather?.Moving ?? false) || (p.def.race?.Humanlike == true && Rand.Chance(0.4f))) continue;
                BodyPartRecord foot = p.health.hediffSet.GetNotMissingParts().Where(x => x.def.tags.Contains(BodyPartTagDefOf.MovingLimbDigit) || x.def.tags.Contains(BodyPartTagDefOf.MovingLimbSegment)).RandomElementWithFallback();
                p.TakeDamage(new DamageInfo(DamageDefOf.Cut, 3f, 0.2f, -1f, null, foot));
            }
        }
    }
}
