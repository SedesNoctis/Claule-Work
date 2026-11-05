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
    // Transmutation in battle, the aura's trail, stone water and the secret paths (Магия-правила.md, the author's decisions).
    // =====================================================================================

    /// <summary>Ground changed for a while: what it was, what it is, until when.</summary>
    public class MapComponent_TempTerrain : MapComponent
    {
        public Dictionary<IntVec3, TerrainDef> from = new Dictionary<IntVec3, TerrainDef>(), to = new Dictionary<IntVec3, TerrainDef>();
        public Dictionary<IntVec3, int> until = new Dictionary<IntVec3, int>();
        public MapComponent_TempTerrain(Map map) : base(map) { }
        public static MapComponent_TempTerrain Of(Map m) => m?.GetComponent<MapComponent_TempTerrain>();

        public void Set(IntVec3 c, TerrainDef def, int ticks)
        {
            if (def == null || !c.InBounds(map) || c.GetEdifice(map) != null) return;
            TerrainDef was = map.terrainGrid.TerrainAt(c);
            if (was == def) { if (until.ContainsKey(c)) until[c] = Mathf.Max(until[c], Find.TickManager.TicksGame + ticks); return; }
            if (was.IsWater && !def.IsWater) return;   // the trail does not fill in water
            if (!from.ContainsKey(c)) from[c] = was;
            to[c] = def;
            until[c] = Find.TickManager.TicksGame + ticks;
            map.terrainGrid.SetTerrain(c, def);
        }

        public override void MapComponentTick()
        {
            if (until.Count == 0 || Find.TickManager.TicksGame % 250 != 0) return;
            int now = Find.TickManager.TicksGame;
            foreach (IntVec3 c in until.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
            {
                if (to.TryGetValue(c, out TerrainDef t) && map.terrainGrid.TerrainAt(c) == t && from.TryGetValue(c, out TerrainDef f)) map.terrainGrid.SetTerrain(c, f);
                until.Remove(c); from.Remove(c); to.Remove(c);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref from, "from", LookMode.Value, LookMode.Def);
            Scribe_Collections.Look(ref to, "to", LookMode.Value, LookMode.Def);
            Scribe_Collections.Look(ref until, "until", LookMode.Value, LookMode.Value);
            if (from == null) from = new Dictionary<IntVec3, TerrainDef>();
            if (to == null) to = new Dictionary<IntVec3, TerrainDef>();
            if (until == null) until = new Dictionary<IntVec3, int>();
        }
    }

    /// <summary>Stone water (the author): water the bog has made bear building - it stays water to look at and to walk
    /// through, it only holds up walls and floors like ground. For good.</summary>
    public class MapComponent_StoneWater : MapComponent
    {
        public HashSet<IntVec3> cells = new HashSet<IntVec3>();
        public MapComponent_StoneWater(Map map) : base(map) { }
        public static MapComponent_StoneWater Of(Map m) => m?.GetComponent<MapComponent_StoneWater>();
        public static bool Holds(Map m, IntVec3 c) => Of(m)?.cells.Contains(c) == true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref cells, "cells", LookMode.Value);
            if (cells == null) cells = new HashSet<IntVec3>();
        }
    }

    [HarmonyPatch(typeof(GenGrid), nameof(GenGrid.SupportsStructureType))]
    public static class Patch_StoneWaterBears
    {
        public static void Postfix(IntVec3 c, Map map, TerrainAffordanceDef surfaceType, ref bool __result)
        {
            if (__result || map == null || surfaceType == null) return;
            if (surfaceType != TerrainAffordanceDefOf.Light && surfaceType != TerrainAffordanceDefOf.Medium && surfaceType != TerrainAffordanceDefOf.Heavy) return;
            if (MapComponent_StoneWater.Holds(map, c)) __result = true;
        }
    }

    /// <summary>The ground changed by transmutation in battle and by the aura's trail: what each element makes of it.</summary>
    public static class SpellBattleGround
    {
        public const int ReleaseTicks = 5000, AuraTicks = 1800;

        /// <summary>A Release with plain transmutation changes the ground under the foes for a while; an Aura with it leaves a
        /// short trail round the caster (magma behind its caster, mud round a water caster...). Under Selective Protection only
        /// the ground under the foes changes (and right round them).</summary>
        public static void Change(Pawn caster, Map map, IntVec3 at, float radius, float power, RuneDef el, ComboKind k, bool aura)
        {
            if (map == null) return;
            int ticks = aura ? AuraTicks : ReleaseTicks;
            IEnumerable<IntVec3> area = GenRadial.RadialCellsAround(at, Mathf.Min(radius, GenRadial.MaxRadialPatternRadius - 1f), true).Where(c => c.InBounds(map));
            if (aura) area = area.Where(c => c != caster.Position && !c.AdjacentTo8Way(caster.Position));
            if (SpellSculpt.Active)
            {
                var under = new HashSet<IntVec3>();
                foreach (Pawn p in area.SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Distinct().Where(p => SpellSculpt.Struck(p, caster)))
                    foreach (IntVec3 c in GenAdj.CellsOccupiedBy(p).Concat(GenAdj.CellsAdjacent8Way(p))) if (c.InBounds(map)) under.Add(c);
                area = area.Where(under.Contains);
            }
            List<IntVec3> cells = area.ToList();
            var temp = MapComponent_TempTerrain.Of(map);
            RuneKind[] ks = SpellTransmute.Kinds(el, k);
            TerrainDef mud = DefDatabase<TerrainDef>.GetNamedSilentFail("Mud"), gravel = DefDatabase<TerrainDef>.GetNamedSilentFail("Gravel"), sand = DefDatabase<TerrainDef>.GetNamedSilentFail("Sand");
            foreach (IntVec3 c in cells)
            {
                switch (k)
                {
                    case ComboKind.Magma: if (Rand.Chance(aura ? 0.35f : 0.5f)) MagmaPool.Pour(c, map, caster, power, aura ? 600 : 1500, false); continue;
                    case ComboKind.Bog: if (Rand.Chance(0.6f)) MirePatch.Spread(c, map, caster, power, aura ? 900 : 2500); continue;
                    case ComboKind.Steam: if (Rand.Chance(0.3f)) Gas_ScaldingSteam.Release(c, map, 1); continue;
                    case ComboKind.Dust: temp?.Set(c, sand, ticks); continue;
                    case ComboKind.Haze: if (Rand.Chance(0.3f)) ShardGround.Scatter(c, map, ComboDefOf.Avir_Glass); continue;
                }
                if (ks.Contains(RuneKind.Water)) temp?.Set(c, mud, ticks);
                else if (ks.Contains(RuneKind.Earth)) temp?.Set(c, gravel, ticks);
                if (ks.Contains(RuneKind.Fire) && Rand.Chance(0.25f)) ShardGround.Scatter(c, map, ComboDefOf.Avir_Glass);
            }
            if (ks.Contains(RuneKind.Air))
                foreach (Pawn p in cells.SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Distinct().ToList())
                    if (MagicFx.Foe(p, caster) && p.RaceProps.IsFlesh) MagicFx.Give(p, EarthAirDefOf.Avir_Breathless, Mathf.RoundToInt(600f * power));
            if (ks.Contains(RuneKind.Earth) && !aura)
                foreach (Pawn p in cells.SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Distinct().ToList())
                    if (MagicFx.Foe(p, caster)) MagicFx.Fell(p, at, 0, map, caster, power, 60);
        }
    }

    /// <summary>The fields the secret paths leave: moss, charged ground, mist.</summary>
    public class MapComponent_PathFields : MapComponent
    {
        public class Field { public ComboKind k; public HashSet<IntVec3> cells; public int until; public Pawn owner; public float power; }
        public readonly List<Field> fields = new List<Field>();
        public MapComponent_PathFields(Map map) : base(map) { }
        public static MapComponent_PathFields Of(Map m) => m?.GetComponent<MapComponent_PathFields>();

        public static bool InMist(Map m, IntVec3 c) => Of(m)?.fields.Any(f => f.k == ComboKind.Steam && f.cells.Contains(c)) == true;

        public override void MapComponentTick()
        {
            if (fields.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            fields.RemoveAll(f => f.until <= now);
            if (now % 120 != 0) return;
            foreach (Field f in fields)
            {
                foreach (IntVec3 c in f.cells)
                {
                    if (!c.InBounds(map)) continue;
                    List<Thing> here = c.GetThingList(map);
                    switch (f.k)
                    {
                        case ComboKind.Bog:   // moss: its own mend, foes sink, fire does not take
                            foreach (Thing t in here.Where(t => t is Fire).ToList()) t.Destroy();
                            foreach (Pawn p in here.OfType<Pawn>().ToList())
                            {
                                if (f.owner != null && !p.HostileTo(f.owner)) SpellHeal.Mend(p, 1.2f * f.power);
                                else MagicFx.Give(p, ComboDefOf.Avir_Mired, 150);
                            }
                            break;
                        case ComboKind.Storm:   // current: the wet ground strikes whoever steps on it
                            if (!Reactions.Wet(c, map)) break;
                            foreach (Pawn p in here.OfType<Pawn>().ToList())
                            {
                                if (f.owner != null && !MagicFx.Foe(p, f.owner)) continue;
                                if (p.RaceProps.IsMechanoid) p.TakeDamage(new DamageInfo(DamageDefOf.EMP, 8f * f.power, 0f, -1f, f.owner));
                                else { p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 3f * f.power, 0f, -1f, f.owner)); MagicFx.Stun(p, 60, f.owner); }
                                FleckMaker.ThrowMicroSparks(p.DrawPos, map);
                            }
                            break;
                        case ComboKind.Steam:   // mist: it hangs there
                            if (Rand.Chance(0.12f)) FleckMaker.ThrowSmoke(c.ToVector3Shifted(), map, 1.6f);
                            break;
                    }
                }
            }
        }
    }

    /// <summary>The secret paths (the author's decisions): Moss (bog), Hot Metal (magma), Current (storm), Glass (haze),
    /// Sand (dust), Mist (steam). Each is a meeting turned onto one thing - mostly the ground it covers.</summary>
    public static class SpellPaths
    {
        public const string MossName = "Moss", HotMetalName = "Hot Metal", CurrentName = "Current", GlassName = "Glass", SandName = "Sand", MistName = "Mist";
        public static string Name(ComboKind k) => k == ComboKind.Bog ? MossName : k == ComboKind.Magma ? HotMetalName : k == ComboKind.Storm ? CurrentName
            : k == ComboKind.Haze ? GlassName : k == ComboKind.Dust ? SandName : k == ComboKind.Steam ? MistName : null;

        public static string Key(ComboKind k) => "Path:" + k;

        public static void Cast(Pawn caster, Map map, IntVec3 at, float radius, float power, ComboKind k)
        {
            if (map == null || Name(k) == null) return;
            SpellLore.Learn(Key(k));
            List<IntVec3> cells = GenRadial.RadialCellsAround(at, Mathf.Min(radius, GenRadial.MaxRadialPatternRadius - 1f), true).Where(c => c.InBounds(map)).ToList();
            List<Pawn> pawns = cells.SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Distinct().ToList();
            var fields = MapComponent_PathFields.Of(map);
            int day = GenDate.TicksPerDay;
            switch (k)
            {
                case ComboKind.Bog:
                    {
                        var temp = MapComponent_TempTerrain.Of(map);
                        foreach (IntVec3 c in cells) temp?.Set(c, ArcaneWorks.ArcaneSoilRich, Mathf.RoundToInt(day * power));
                        fields?.fields.Add(new MapComponent_PathFields.Field { k = k, cells = new HashSet<IntVec3>(cells), until = Find.TickManager.TicksGame + Mathf.RoundToInt(day * power), owner = caster, power = power });
                        break;
                    }
                case ComboKind.Magma:
                    foreach (Pawn p in pawns.Where(p => MagicFx.Foe(p, caster)))
                    {
                        if (p.RaceProps.IsMechanoid) { p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 12f * power, 0f, -1f, caster)); MagicFx.Stun(p, 120, caster); continue; }
                        ThingWithComps w = p.equipment?.Primary;
                        if (w != null && Metal(w))
                        {
                            p.equipment.TryDropEquipment(w, out _, p.Position);
                            p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 5f * power, 0f, -1f, caster, p.health.hediffSet.GetNotMissingParts().FirstOrDefault(x => x.def.tags.Contains(BodyPartTagDefOf.ManipulationLimbDigit))));
                            MoteMaker.ThrowText(p.DrawPos, map, "Red-hot!", 2.5f);
                        }
                        foreach (Apparel a in p.apparel?.WornApparel.Where(Metal).ToList() ?? new List<Apparel>())
                            p.TakeDamage(new DamageInfo(DamageDefOf.Burn, 3f * power, 0f, -1f, caster));
                    }
                    break;
                case ComboKind.Storm:
                    fields?.fields.Add(new MapComponent_PathFields.Field { k = k, cells = new HashSet<IntVec3>(cells), until = Find.TickManager.TicksGame + Mathf.RoundToInt(day * 0.25f * power), owner = caster, power = power });
                    // the caster carries the current: electric benches work under their hands, with no power line
                    MagicFx.Give(caster, PathDefOf.Avir_Current, Mathf.RoundToInt(GenDate.TicksPerHour * 12f * power));
                    break;
                case ComboKind.Haze:
                    foreach (IntVec3 c in cells) if (Rand.Chance(0.6f)) ShardGround.Scatter(c, map, ComboDefOf.Avir_Glass);
                    break;
                case ComboKind.Dust:
                    {
                        var temp = MapComponent_TempTerrain.Of(map);
                        TerrainDef sand = DefDatabase<TerrainDef>.GetNamedSilentFail("Sand");
                        foreach (IntVec3 c in cells) temp?.Set(c, sand, Mathf.RoundToInt(day * power));
                        foreach (Building b in cells.Select(c => c.GetEdifice(map)).Where(b => b != null && b.def.useHitPoints).Distinct().ToList())
                            b.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 12f * power, 0f, -1f, caster));
                        break;
                    }
                case ComboKind.Steam:
                    fields?.fields.Add(new MapComponent_PathFields.Field { k = k, cells = new HashSet<IntVec3>(cells), until = Find.TickManager.TicksGame + Mathf.RoundToInt(GenDate.TicksPerHour * 12f * power), owner = caster, power = power });
                    foreach (IntVec3 c in cells) foreach (Thing t in c.GetThingList(map).Where(t => t is Fire).ToList()) t.Destroy();
                    break;
            }
            foreach (IntVec3 c in cells) if (Rand.Chance(0.3f)) FleckMaker.ThrowDustPuffThick(c.ToVector3Shifted(), map, 0.8f, Combos.ColorOf(k));
        }

        public static bool Metal(Thing t) => t?.Stuff?.stuffProps?.categories?.Contains(StuffCategoryDefOf.Metallic) == true
            || (t?.Stuff == null && t?.def.costList?.Any(c => c.thingDef?.stuffProps?.categories?.Contains(StuffCategoryDefOf.Metallic) == true) == true);
    }

    [DefOf]
    public static class PathDefOf
    {
        public static HediffDef Avir_Current;
        static PathDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(PathDefOf)); }
    }

    /// <summary>Current in the body: the bench a carrier works at is powered by them, whatever the line says.</summary>
    public static class CurrentPower
    {
        private static readonly Dictionary<Thing, int> powered = new Dictionary<Thing, int>();
        public static bool Powered(Thing t) => t != null && powered.TryGetValue(t, out int until) && until > Find.TickManager.TicksGame;

        public static void Note(Pawn p)
        {
            Thing t = p.CurJob?.targetA.Thing;
            if (t is Building && t.TryGetComp<CompPowerTrader>() != null) powered[t] = Find.TickManager.TicksGame + 150;
        }

        /// <summary>The pawn whose bill work is being weighed just now: a carrier of current may take up a bench with no power.</summary>
        [System.ThreadStatic] public static Pawn Weighing;
        public static bool Carries(Pawn p) => p?.health?.hediffSet != null && PathDefOf.Avir_Current != null && p.health.hediffSet.HasHediff(PathDefOf.Avir_Current);
    }

    public class HediffComp_Current : HediffComp
    {
        public override void CompPostTick(ref float severityAdjustment)
        {
            if (parent.pawn.IsHashIntervalTick(60)) CurrentPower.Note(parent.pawn);
        }
    }

    public class HediffCompProperties_Current : HediffCompProperties
    {
        public HediffCompProperties_Current() { compClass = typeof(HediffComp_Current); }
    }

    [HarmonyPatch(typeof(CompPowerTrader), "get_PowerOn")]
    public static class Patch_CurrentPowersBench
    {
        public static void Postfix(CompPowerTrader __instance, ref bool __result)
        {
            if (__result || __instance?.parent == null) return;
            if (CurrentPower.Powered(__instance.parent) || CurrentPower.Carries(CurrentPower.Weighing)) __result = true;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_CurrentWeighsBench
    {
        public static void Prefix(Pawn pawn) => CurrentPower.Weighing = pawn;
        public static void Postfix() => CurrentPower.Weighing = null;
    }

    /// <summary>Mist (the author): inside it a shot reaches less far, and the further it goes the worse it hits.</summary>
    public static class Mist
    {
        public const float Reach = 10f;
        public static bool Touches(Thing shooter, LocalTargetInfo target) =>
            shooter?.Map != null && (MapComponent_PathFields.InMist(shooter.Map, shooter.Position) || (target.IsValid && MapComponent_PathFields.InMist(shooter.Map, target.Cell)));
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.CanHitTargetFrom))]
    public static class Patch_MistReach
    {
        public static void Postfix(Verb __instance, IntVec3 root, LocalTargetInfo targ, ref bool __result)
        {
            if (!__result || __instance?.caster?.Map == null || __instance.verbProps.IsMeleeAttack) return;
            if (Mist.Touches(__instance.caster, targ) && root.DistanceTo(targ.Cell) > Mist.Reach) __result = false;
        }
    }

    [HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
    public static class Patch_MistAim
    {
        private static readonly System.Reflection.FieldInfo dist = AccessTools.Field(typeof(ShotReport), "factorFromShooterAndDist");
        public static void Postfix(Thing caster, LocalTargetInfo target, ref ShotReport __result)
        {
            if (caster?.Map == null || !Mist.Touches(caster, target)) return;
            float d = caster.Position.DistanceTo(target.Cell);
            if (dist == null) return;
            object boxed = __result;
            dist.SetValue(boxed, (float)dist.GetValue(boxed) * Mathf.Clamp(1f - d / (Mist.Reach + 2f), 0.1f, 1f));
            __result = (ShotReport)boxed;
        }
    }
}
