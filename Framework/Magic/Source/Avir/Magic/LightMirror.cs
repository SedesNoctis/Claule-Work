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
    /// LIGHT THROUGH REFLECTIONS, AND THE MIRROR (the author, Fix261-262).
    ///
    /// Every mage of Light may strike a point it does not see, if a reflector of the node - a shard, a crystal, shining
    /// water - stands within the spell's reach of the mage and sees the point within the spell's reach of itself. The spell
    /// then comes out of the reflector at full strength; nothing comes out of the mage.
    ///
    /// The Mirror (a great circle of Light that took a small circle of Water first) is the mage who works ONLY through
    /// water: any shining water on the map is a way out, however far from the mage; from the mage itself - only with a flask
    /// of shining water carried (one flask a cast). Release in the Water circle lays new water where it strikes, a Beam a pool
    /// at its end; every water the Mirror's light touches shines for sixteen hours. A work keeps still shining water - it
    /// never fades, lights the ground like a lamp and fills flasks.
    /// </summary>
    public static class LightMirror
    {
        public const int Life = GenDate.TicksPerHour * 16;
        public const float ThrowRange = 14f;
        public static readonly Color MirrorColor = new Color(0.62f, 0.88f, 1f);

        public static ThingDef GlowDef => DefDatabase<ThingDef>.GetNamedSilentFail("Avir_GlowWater");
        public static ThingDef StillDef => DefDatabase<ThingDef>.GetNamedSilentFail("Avir_GlowWaterStill");
        public static ThingDef FlaskDef => DefDatabase<ThingDef>.GetNamedSilentFail("Avir_GlowFlask");

        // ------------------------------------------------------------------ the water

        /// <summary>Water lies here: water ground, or a pool on it (the game's puddle, a spell's, held water, mire).</summary>
        public static bool Water(IntVec3 c, Map map) => map != null && c.InBounds(map) && (c.GetTerrain(map)?.IsWater == true || Reactions.Pools(c, map).Any());

        public static Building_LightReflector At(IntVec3 c, Map map) =>
            map == null || !c.InBounds(map) ? null : c.GetThingList(map).OfType<Building_LightReflector>().FirstOrDefault(r => r.IsWater);

        /// <summary>The water on this cell starts to shine (or shines on, renewed). Nothing where there is no water.</summary>
        public static Building_LightReflector Light(Pawn caster, Map map, IntVec3 c, bool still = false)
        {
            if (!Water(c, map)) return null;
            Building_LightReflector old = At(c, map);
            if (old != null)
            {
                if (still && !old.Still) old.Destroy(DestroyMode.Vanish);
                else { if (!old.Still) old.dies = Find.TickManager.TicksGame + Life; return old; }
            }
            ThingDef def = still ? StillDef : GlowDef;
            if (def == null) return null;
            var r = (Building_LightReflector)ThingMaker.MakeThing(def);
            r.caster = caster;
            r.dies = still ? -1 : Find.TickManager.TicksGame + Life;
            GenSpawn.Spawn(r, c, map, WipeMode.Vanish);
            if (caster?.Faction != null) r.SetFaction(caster.Faction);
            if (Rand.Chance(0.4f)) FleckMaker.Static(c, map, FleckDefOf.WaterRipple, 1.2f);
            return r;
        }

        /// <summary>A pool laid where there was none (not on water ground - that is water already), and set shining.</summary>
        public static void Pool(Pawn caster, Map map, IntVec3 c)
        {
            if (map == null || !c.InBounds(map)) return;
            if (!Water(c, map) && c.Standable(map))
            {
                ThingDef pool = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_SpellPuddle") ?? ThingDefOf.Filth_Water;
                if (!FilthMaker.TryMakeFilth(c, map, pool, 3)) GenSpawn.Spawn(pool, c, map);
                FleckMaker.Static(c, map, FleckDefOf.WaterSplash, 1f);
            }
            Light(caster, map, c);
        }

        /// <summary>What a circle of Light does in a Mirror: every water its light reaches shines; the Mirror's own Water
        /// (the first circle of Light of the spell only - not on every circle, not from echoes) lays new water: Release where
        /// it strikes, a Beam at its end.</summary>
        public static void OnCircle(Pawn caster, SpellCircle root, SpellCircle g, IntVec3 from, IntVec3 at, float power, Map map, float radius)
        {
            if (map == null || !at.InBounds(map)) return;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, radius + 0.5f, true))
                if (c.InBounds(map) && GenSight.LineOfSight(at, c, map, true)) Light(caster, map, c);
            if (LightMagic.Echoing || root == null) return;
            SpellCircle box = g.Great ? g : LightMagic.BoxOf(root, g);
            if (LightMagic.Links(root).FirstOrDefault() != g) return;
            SpellCircle water = LightMagic.Absorbed(box);
            if (water == null) return;
            var runes = water.OwnRunes.ToList();
            int breadth = runes.Count(x => x.kind == RuneKind.Breadth);
            if (runes.Any(x => x.kind == RuneKind.Release))
                foreach (IntVec3 c in GenRadial.RadialCellsAround(at, 1.5f + breadth, true))
                    if (c.InBounds(map) && GenSight.LineOfSight(at, c, map, true)) Pool(caster, map, c);
            if (runes.Any(x => x.kind == RuneKind.Beam))
                foreach (IntVec3 c in GenRadial.RadialCellsAround(at, 0.9f + 0.5f * breadth, true)) Pool(caster, map, c);
        }

        // ------------------------------------------------------------------ casting through reflections

        public static int Flasks(Pawn p) => p?.inventory?.innerContainer == null || FlaskDef == null ? 0 : p.inventory.innerContainer.TotalStackCountOfDef(FlaskDef);

        public static void UseFlask(Pawn p)
        {
            if (Flasks(p) <= 0) return;
            Thing f = p.inventory.innerContainer.FirstOrDefault(t => t.def == FlaskDef);
            if (f == null) return;
            if (f.stackCount > 1) f.SplitOff(1).Destroy(); else f.Destroy();
        }

        /// <summary>Where a spell with Light in it comes out when aimed at `at`: from the mage (relay invalid), from a reflector
        /// (relay - its cell), or nowhere (false, with why). A spell with no Light: the plain rule - in reach and seen.</summary>
        public static bool Aim(Pawn mage, RitualSchema s, IntVec3 at, out IntVec3 relay, out string why)
        {
            relay = IntVec3.Invalid; why = null;
            Map map = mage?.Map;
            if (map == null || s == null || !at.InBounds(map)) { why = "Nowhere to strike."; return false; }
            float range = Spells.Range(s);
            bool inReach = at.DistanceTo(mage.Position) <= range, seen = Spells.Sees(s, mage.Position, at, map);
            SpellCircle root = s.weave;
            if (root == null || !LightMagic.Any(root))
            {
                if (!inReach) why = "Too far for this spell.";
                else if (!seen) why = "The mage cannot see there.";
                return why == null;
            }
            bool mirror = LightMagic.AnyMirror(root);
            if (inReach && seen && (!mirror || Flasks(mage) > 0)) return true;
            IntVec3 me = mage.Position;
            Building_LightReflector best = null; float bd = float.MaxValue;
            foreach (Building_LightReflector r in Building_LightReflector.All(map))
            {
                if (mirror ? !r.IsWater : r.Position.DistanceTo(me) > range) continue;
                float d = r.Position.DistanceTo(at);
                if (d > range || d >= bd || !GenSight.LineOfSight(r.Position, at, map, true)) continue;
                best = r; bd = d;
            }
            if (best != null) { relay = best.Position; return true; }
            why = mirror ? (Flasks(mage) > 0 || !inReach || !seen
                              ? "No shining water sees there within the spell's reach."
                              : "A mage of the Mirror casts only through shining water - none sees there, and no flask of it is carried.")
                         : !inReach ? "Too far, and no reflector within reach of the mage sees there."
                         : "The mage cannot see there, and no reflector within reach sees it.";
            return false;
        }

        /// <summary>The light leaving the reflector it is cast through.</summary>
        public static void Relay(Map map, IntVec3 from, IntVec3 at)
        {
            LightMagic.Glint(map, from.ToVector3Shifted(), 1.5f, true);
            LightMagic.RayLine(map, from.ToVector3Shifted(), at.ToVector3Shifted(), true);
        }

        // ------------------------------------------------------------------ the look of shining water

        private static Material dayMat, nightMat;

        public static void DrawWater(Building_LightReflector r, Vector3 at)
        {
            if (dayMat == null)
            {
                dayMat = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Light/GlowWater", ShaderDatabase.Transparent, new Color(1f, 0.97f, 0.86f, 0.75f));
                nightMat = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Light/GlowWater", ShaderDatabase.Transparent, new Color(0.7f, 0.85f, 1f, 0.9f));
            }
            float t = Time.realtimeSinceStartup * 1.3f + r.thingIDNumber * 0.37f;
            float s = (r.Still ? 1.35f : 1.15f) + 0.07f * Mathf.Sin(t);
            at.y = AltitudeLayer.Filth.AltitudeFor() + 0.01f;
            Quaternion turn = Quaternion.AngleAxis((r.thingIDNumber * 47) % 360 + 6f * Mathf.Sin(t * 0.5f), Vector3.up);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, turn, new Vector3(s, 1f, s)), LightMagic.Night(r.Map) ? nightMat : dayMat, 0);
        }
    }

    // ====================================================================== the flask

    /// <summary>A flask of shining water: thrown, it lays shining water where it lands.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_GlowFlaskThrow
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            Pawn p = __instance;
            if (!AvirMagic.On || p == null || !p.Spawned || p.Faction != Faction.OfPlayer || p.Downed) yield break;
            int n = LightMirror.Flasks(p);
            if (n <= 0) yield break;
            yield return new Command_Action
            {
                defaultLabel = "Throw shining water (" + n + ")",
                defaultDesc = "Throw a flask of shining water: where it lands, a pool of it lies shining for sixteen hours - a way out for a mage of the Mirror, and a reflector for every mage of Light. Up to " + (int)LightMirror.ThrowRange + " cells, where the thrower can see.",
                icon = ContentFinder<Texture2D>.Get("Things/Item/Avir/Magic/GlowFlask", false) ?? BaseContent.BadTex,
                action = () => Find.Targeter.BeginTargeting(new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false },
                    t => Throw(p, t.Cell), null,
                    t => t.IsValid && t.Cell.InBounds(p.Map) && t.Cell.DistanceTo(p.Position) <= LightMirror.ThrowRange && GenSight.LineOfSight(p.Position, t.Cell, p.Map, true),
                    p, null, null, true, null, t => GenDraw.DrawRadiusRing(p.Position, LightMirror.ThrowRange))
            };
        }

        public static void Throw(Pawn p, IntVec3 c)
        {
            if (p?.Map == null || LightMirror.Flasks(p) <= 0) return;
            Map map = p.Map;
            LightMirror.UseFlask(p);
            LightMagic.RayLine(map, p.DrawPos, c.ToVector3Shifted(), false, 14);
            foreach (IntVec3 x in GenRadial.RadialCellsAround(c, 1.5f, true))
                if (x.InBounds(map) && GenSight.LineOfSight(c, x, map, true)) LightMirror.Pool(p, map, x);
            LightMagic.Glint(map, c.ToVector3Shifted(), 1.6f, false);
        }
    }

    // ====================================================================== the works of the Mirror

    public static class MirrorWorks
    {
        public static bool Mirror(Hediff_ArcaneCore c) => c != null && MagicWork.Knows(c, RuneKind.Light) && MagicWork.Knows(c, RuneKind.Water);
        public static Hediff_ArcaneCore Able(Pawn p) { Hediff_ArcaneCore c = MagicWork.Able(p, RuneKind.Light, LightWorks.Arcana); return Mirror(c) ? c : null; }
        public const int FlasksPerWork = 3;
    }

    public class Designator_GlowWaterStill : Designator_Arcane
    {
        protected override DesignationDef Designation => DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_GlowWaterStill");
        public Designator_GlowWaterStill()
        {
            defaultLabel = "Still shining water (Mirror)";
            defaultDesc = "Mark water - shallow ground water or a pool - for a mage of the Mirror (Light and Water) to make it shine for good: it never fades, lights the ground round it like a lamp at all hours with no power, and flasks of shining water are filled at it. A reflector and a way out for the Mirror too, answering every spell of Light at a steady share.";
            icon = ArcaneIcons.For("Avir_GlowWaterStill", "UI/Designators/SmoothSurface");
        }
        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map) || Designation == null || Map.designationManager.DesignationAt(c, Designation) != null) return false;
            if (!LightMirror.Water(c, Map)) return "Only water can be made to shine.";
            if (LightMirror.At(c, Map)?.Still == true) return "It shines already.";
            return true;
        }
    }

    public class Designator_GlowFlask : Designator_Arcane
    {
        protected override DesignationDef Designation => DefDatabase<DesignationDef>.GetNamedSilentFail("Avir_GlowFlask");
        public Designator_GlowFlask()
        {
            defaultLabel = "Fill shining flasks (Mirror)";
            defaultDesc = "Mark still shining water for a mage of the Mirror to fill " + MirrorWorks.FlasksPerWork + " flasks of it. A mage of the Mirror carrying one may cast from itself (one flask a cast); anyone may throw one to lay shining water where it lands.";
            icon = ArcaneIcons.For("Avir_GlowFlask", "UI/Designators/SmoothSurface");
        }
        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map) || c.Fogged(Map) || Designation == null || Map.designationManager.DesignationAt(c, Designation) != null) return false;
            if (LightMirror.At(c, Map)?.Still != true) return "Flasks are filled at still shining water.";
            return true;
        }
    }

    // ====================================================================== dev

    public static class MirrorDev
    {
        [DebugAction("Magic", "Light: shining water here (Mirror)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Shine()
        {
            Map map = Find.CurrentMap; IntVec3 c = Verse.UI.MouseCell();
            if (map == null || !c.InBounds(map)) return;
            Pawn by = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            foreach (IntVec3 x in GenRadial.RadialCellsAround(c, 1.5f, true)) LightMirror.Pool(by, map, x);
        }

        [DebugAction("Magic", "Light: still shining water here", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Still()
        {
            Map map = Find.CurrentMap; IntVec3 c = Verse.UI.MouseCell();
            if (map == null || !c.InBounds(map)) return;
            LightMirror.Pool(null, map, c);
            LightMirror.Light(map.mapPawns.FreeColonistsSpawned.FirstOrDefault(), map, c, true);
        }

        [DebugAction("Magic", "Light: give 3 shining flasks", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Flasks(Pawn p)
        {
            if (p?.inventory == null || LightMirror.FlaskDef == null) return;
            Thing f = ThingMaker.MakeThing(LightMirror.FlaskDef); f.stackCount = 3;
            p.inventory.innerContainer.TryAdd(f);
        }

        [DebugAction("Magic", "Light: list reflections (relays)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void List()
        {
            Map map = Find.CurrentMap; if (map == null) return;
            var all = Building_LightReflector.All(map).ToList();
            Log.Message("[Avir] reflectors: " + all.Count + " (shining water " + all.Count(r => r.IsWater) + ", still " + all.Count(r => r.Still) + ", crystals " + all.Count(r => r.IsCrystal) + ")");
        }
    }
}
