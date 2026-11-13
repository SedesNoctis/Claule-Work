using System.Collections.Generic;
using System.Linq;
using Avir.Ritualistics;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Magic
{
    /// <summary>
    /// THE GROVE OF LIGHTS - Light meeting Life (the author, Fix264-265; working name). One meeting, whichever holds the
    /// other: a great circle of Light with a great circle of Life in it, or the other way round, neither sealed.
    ///
    /// Light is endless and makes itself; Life always pays. Together Life is paid in light - the old Alleanor, the
    /// shining forest, whose trees kept "thousands of new lights" under their branches.
    ///  1. Withering takes LIGHT, not life: the living are not touched; the store fills from the light round the point -
    ///     reflectors (they fade faster), shining water, lit lamps, the open sky by day - and where the light was drunk a
    ///     blind patch stays.
    ///  2. Restoration grows shining flowers round the ones it healed - flowers of the map's own climate (the north's, the
    ///     twilight forest's, two of the hot lands made for this). They live on and do not die of age: each is a living
    ///     reflector.
    ///  3. The meeting's reflectors are spirit lights (ochre, blue, amber, azure, ruby) in place of shards: they drift to
    ///     the caster's own people and mend whoever stands close.
    ///  4. The reflectors echo Life's acts too. An echo of Life out of a shining flower is paid by the flower - it withers;
    ///     out of any other reflector - from the store, as any giving.
    ///  5. A lost hold: the lights and the flowers break into wild overgrowth.
    ///  6. A work: a mage of Light and Life plants a shining flower where it is marked.
    /// </summary>
    public static class LightLife
    {
        public static readonly Color GroveColor = new Color(0.78f, 1f, 0.72f);
        private static readonly Color[] Spirits =
        {
            new Color(0.93f, 0.72f, 0.36f), new Color(0.55f, 0.75f, 1f), new Color(1f, 0.8f, 0.4f), new Color(0.45f, 0.8f, 0.95f), new Color(0.95f, 0.38f, 0.42f)
        };
        public static Color SpiritColor(Thing t) => Spirits[(t.thingIDNumber & 0x7fffffff) % Spirits.Length];

        public static ThingDef SpiritDef => DefDatabase<ThingDef>.GetNamedSilentFail("Avir_SpiritLight");
        public static ThingDef MarkDef => DefDatabase<ThingDef>.GetNamedSilentFail("Avir_LifeLightMark");

        /// <summary>Light and Life held one in the other, neither sealed.</summary>
        public static bool Meets(SpellCircle root)
        {
            if (root == null) return false;
            foreach (SpellCircle g in WeaveSpell.Greats(root))
            {
                if (g.element == null || WeaveSpell.SealedGreat(g)) continue;
                bool light = LightMagic.Is(g.element), life = LifeMagic.Is(g.element);
                if (!light && !life) continue;
                if (g.inner.Any(k => k.Great && k.element != null && !WeaveSpell.SealedGreat(k) && (light ? LifeMagic.Is(k.element) : LightMagic.Is(k.element)))) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ 1. Withering drinks light

        public static float DrinkLight(Pawn caster, IntVec3 at, float r, float power, Map map)
        {
            if (map == null || !at.InBounds(map)) return 0f;
            float got = 0f;
            int now = Find.TickManager.TicksGame;
            foreach (Building_LightReflector rf in Building_LightReflector.All(map).Where(x => x.Position.DistanceTo(at) <= r + 0.5f && !x.Still && !x.IsPlantMark && !x.IsWalker).ToList())
            {
                got += rf.IsWater ? 2f : rf.Work ? 4f : 3f;
                if (rf.dies > 0) rf.dies = now + Mathf.Max(60, (rf.dies - now) / 2);   // drunk: it fades faster
                LightMagic.RayLine(map, rf.DrawPos, caster.DrawPos, false, 14);
            }
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(at, map, r + 0.5f, true).Where(t => t is Building).ToList())
            {
                CompGlower gl = t.TryGetComp<CompGlower>();
                if (gl != null && gl.Glows) got += 1f;
            }
            float sky = map.skyManager.CurSkyGlow;
            if (sky > 0.3f)
                got += 0.04f * sky * GenRadial.RadialCellsAround(at, r, true).Count(c => c.InBounds(map) && !c.Roofed(map));
            got *= Mathf.Clamp(power, 0.4f, 2f);
            MapComponent_LightZones.Darken(map, at, r + 0.5f, Mathf.RoundToInt(GenDate.TicksPerHour * Mathf.Clamp(power, 0.3f, 2f)));
            if (got > 0f) for (int i = 0; i < 4; i++) LightMagic.Glint(map, at.ToVector3Shifted() + Rand.InsideUnitCircleVec3 * r * 0.6f, 0.7f, false);
            return got;
        }

        // ------------------------------------------------------------------ 2. shining flowers

        private static readonly string[] Cold = { "Avir_PlantSnowbalm", "Avir_PlantRimebloom", "Avir_PlantMoonshard", "Avir_PlantStarbell" };
        private static readonly string[] Mild = { "Avir_PlantDuskbloom", "Avir_PlantDuskbell", "Avir_PlantDuskShrub" };
        private static readonly string[] Hot = { "Avir_PlantEmberlace", "Avir_PlantSandlantern" };

        /// <summary>A shining flower of this map's climate (its yearly warmth).</summary>
        public static ThingDef Flower(Map map)
        {
            float t = map?.TileInfo != null ? map.TileInfo.temperature : 12f;
            string[] set = t < 2f ? Cold : t > 24f ? Hot : Mild;
            var defs = set.Select(n => DefDatabase<ThingDef>.GetNamedSilentFail(n)).Where(d => d != null).ToList();
            if (defs.Count == 0) defs = Mild.Concat(Cold).Select(n => DefDatabase<ThingDef>.GetNamedSilentFail(n)).Where(d => d != null).ToList();
            return defs.Count == 0 ? null : defs.RandomElement();
        }

        public static Plant Plant(Pawn caster, Map map, IntVec3 c)
        {
            if (map == null || !c.InBounds(map) || !c.Standable(map) || c.GetEdifice(map) != null || c.GetFirstItem(map) != null) return null;
            if (map.terrainGrid.TerrainAt(c).fertility <= 0f || c.GetThingList(map).Any(t => t is Building_LightReflector)) return null;
            Plant old = c.GetPlant(map);
            if (old != null && old.def.plant.IsTree) return null;
            ThingDef def = Flower(map);
            if (def == null) return null;
            old?.Destroy(DestroyMode.Vanish);
            var p = (Plant)GenSpawn.Spawn(def, c, map);
            p.Growth = Rand.Range(0.75f, 1f);
            if (MarkDef != null)
            {
                var m = (Building_LightReflector)ThingMaker.MakeThing(MarkDef);
                m.caster = caster; m.bound = p; m.dies = -1;
                GenSpawn.Spawn(m, c, map, WipeMode.Vanish);
                if (caster?.Faction != null) m.SetFaction(caster.Faction);
            }
            MapComponent_SpellSprites.PutTinted(map, "Life/Petals", GroveColor, c.ToVector3Shifted(), 1.2f, 40);
            return p;
        }

        /// <summary>Restoration in the meeting: around the healed, shining flowers spring.</summary>
        public static void Bloom(Pawn caster, List<Pawn> healed, IntVec3 at, float r, float power, Map map)
        {
            if (map == null || LightMagic.Echoing) return;
            int n = Mathf.Clamp(Mathf.RoundToInt(2f + 2f * power), 2, 7);
            var spots = (healed.Count > 0 ? healed.Select(p => p.Position) : new[] { at })
                .SelectMany(c => GenRadial.RadialCellsAround(c, 2.4f, false)).Where(c => c.InBounds(map)).Distinct().InRandomOrder().ToList();
            foreach (IntVec3 c in spots) { if (n <= 0) break; if (Plant(caster, map, c) != null) n--; }
        }

        /// <summary>The flower a living reflector stands in keeps its youth: it does not die of age.</summary>
        public static bool Keep(Building_LightReflector m)
        {
            if (!(m.bound is Plant p) || !p.Spawned || p.Map != m.Map || p.Position != m.Position) { m.Destroy(DestroyMode.Vanish); return false; }
            int span = Mathf.RoundToInt(p.def.plant.LifespanTicks);
            if (span > 0 && p.Age > span / 2) Traverse.Create(p).Field("ageInt").SetValue(span / 4);
            return true;
        }

        // ------------------------------------------------------------------ 3. spirit lights

        /// <summary>A spirit light drifts toward the caster's people near it and mends whoever stands close.</summary>
        public static void Drift(Building_LightReflector s)
        {
            Map map = s.Map;
            if (map == null || s.caster == null) return;
            Pawn friend = map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && LightZenith.Friend(p, s.caster) && p.Position.DistanceTo(s.Position) <= 7f)
                .OrderBy(p => p.Position.DistanceToSquared(s.Position)).FirstOrDefault();
            IntVec3 to = s.Position;
            if (friend != null && friend.Position.DistanceTo(s.Position) > 1.5f)
            {
                IntVec3 step = s.Position + new IntVec3(System.Math.Sign(friend.Position.x - s.Position.x), 0, System.Math.Sign(friend.Position.z - s.Position.z));
                if (step.InBounds(map) && step.Walkable(map) && step.GetEdifice(map) == null) to = step;
            }
            else if (friend == null && Rand.Chance(0.3f))
            {
                IntVec3 step = s.Position + GenAdj.AdjacentCells.RandomElement();
                if (step.InBounds(map) && step.Walkable(map) && step.GetEdifice(map) == null) to = step;
            }
            if (to != s.Position) s.Position = to;
            foreach (Pawn p in GenRadial.RadialDistinctThingsAround(s.Position, map, 1.6f, true).OfType<Pawn>().Where(p => !p.Dead && LightZenith.Friend(p, s.caster)).ToList())
                LifeMagic.HealInjuries(p, 1.2f);
        }

        private static Material spiritMat;
        public static void DrawSpirit(Building_LightReflector s, Vector3 at)
        {
            if (spiritMat == null) spiritMat = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Light/Reflector_Night", ShaderDatabase.MoteGlow);
            float t = Time.realtimeSinceStartup * 1.7f + s.thingIDNumber * 0.41f;
            float size = 0.8f + 0.12f * Mathf.Sin(t);
            var block = new MaterialPropertyBlock(); block.SetColor(ShaderPropertyIDs.Color, SpiritColor(s));
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at + new Vector3(0.2f * Mathf.Sin(t * 0.6f), 0f, 0.25f + 0.15f * Mathf.Sin(t * 0.9f)), Quaternion.identity, new Vector3(size, 1f, size)), spiritMat, 0, null, 0, block);
        }

        // ------------------------------------------------------------------ 4. echoes of Life

        /// <summary>An echo of a circle of Life out of a reflector: out of a shining flower it is paid by the flower (it
        /// withers); out of any other - from the store.</summary>
        public static void EchoLife(Pawn caster, SpellCircle root, SpellCircle c, IntVec3 from, IntVec3 to, float power, Map map)
        {
            Building_LightReflector flower = from.InBounds(map) ? from.GetThingList(map).OfType<Building_LightReflector>().FirstOrDefault(r => r.IsPlantMark) : null;
            if (flower != null)
            {
                Plant p = flower.bound as Plant;
                LifeMagic.FreeGift = true;
                try { LifeMagic.CastCircle(caster, root, c, to, power, map); }
                finally { LifeMagic.FreeGift = false; }
                MapComponent_SpellSprites.PutTinted(map, "Life/Petals", GroveColor, from.ToVector3Shifted(), 1.3f, 40);
                flower.Destroy(DestroyMode.Vanish);
                if (p != null && p.Spawned) p.Kill();
                return;
            }
            LifeMagic.CastCircle(caster, root, c, to, power, map);
        }

        // ------------------------------------------------------------------ 5. the lost hold

        public static void Overgrow(Map map, IEnumerable<IntVec3> where)
        {
            ThingDef grass = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_TallGrass") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Grass");
            foreach (IntVec3 o in where.ToList())
                foreach (IntVec3 c in GenRadial.RadialCellsAround(o, 2.5f, true).Where(c => c.InBounds(map)))
                {
                    Plant pl = c.GetPlant(map);
                    if (pl != null) pl.Growth = 1f;
                    else if (grass != null && Rand.Chance(0.6f) && c.GetEdifice(map) == null && map.terrainGrid.TerrainAt(c).fertility > 0f && c.GetFirstItem(map) == null)
                    { Plant np = (Plant)GenSpawn.Spawn(grass, c, map); np.Growth = Rand.Range(0.6f, 1f); }
                    foreach (Pawn p in c.GetThingList(map).OfType<Pawn>().ToList()) MagicFx.Give(p, DefDatabase<HediffDef>.GetNamedSilentFail("Avir_Entangled"), 300);
                }
        }

        public static bool Able(Hediff_ArcaneCore c) => c != null && MagicWork.Knows(c, RuneKind.Light) && MagicWork.Knows(c, RuneKind.Life);
        public static Hediff_ArcaneCore Able(Pawn p) { Hediff_ArcaneCore c = MagicWork.Able(p, RuneKind.Light, LightWorks.Arcana); return Able(c) ? c : null; }
    }

    public class Designator_LifeLightGarden : Designator_Arcane
    {
        protected override DesignationDef Designation => DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_LifeLightGarden");
        public Designator_LifeLightGarden()
        {
            defaultLabel = "Shining flower (Light and Life)";
            defaultDesc = "Mark fertile ground for a mage of Light and Life to plant a shining flower of this land's climate: it lights the ground at night, lives on without growing old, and is a living reflector for every spell of Light.";
            icon = ArcaneIcons.For("Avir_LifeLightGarden", "UI/Designators/Harvest");
        }
        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map) || Designation == null || Map.designationManager.DesignationAt(c, Designation) != null) return false;
            if (!c.Standable(Map) || c.GetEdifice(Map) != null || Map.terrainGrid.TerrainAt(c).fertility <= 0f) return "A flower needs fertile open ground.";
            if (c.GetThingList(Map).Any(t => t is Building_LightReflector)) return "Something of light stands here already.";
            return true;
        }
    }

    public static class LightLifeDev
    {
        [DebugAction("Magic", "Light+Life: shining flower here", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Flower() { Map m = Find.CurrentMap; if (m != null) LightLife.Plant(m.mapPawns.FreeColonistsSpawned.FirstOrDefault(), m, Verse.UI.MouseCell()); }

        [DebugAction("Magic", "Light+Life: spirit light here", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Spirit()
        {
            Map m = Find.CurrentMap; IntVec3 c = Verse.UI.MouseCell();
            if (m != null && LightLife.SpiritDef != null) Building_LightReflector.RaiseAt(m.mapPawns.FreeColonistsSpawned.FirstOrDefault(), m, c, LightLife.SpiritDef, Building_LightReflector.Life);
        }

        [DebugAction("Magic", "Light+Life: drink light here", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Drink()
        {
            Map m = Find.CurrentMap; Pawn p = m?.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            if (p != null) Messages.Message("Light drunk: " + LightLife.DrinkLight(p, Verse.UI.MouseCell(), 3f, 1f, m).ToString("0.#"), MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
