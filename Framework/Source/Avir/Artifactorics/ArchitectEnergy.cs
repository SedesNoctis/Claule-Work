using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Avir.Ritualistics;
using Avir.Spire;

namespace Avir.Artifactorics
{
    // =====================================================================================
    // THE ENERGY ARCHITECT - a pilot of the stream.
    //   3  - the pilot: the spheres' actives are paid from the stream's energy keepers and the spire, anywhere on the
    //        map, before the spheres' own charge (SpherePaths.Pay). Energy dome: a dome that stops shots from outside.
    //   6  - flame strike: golden fire with a golden amulet, silver with a silver one, a plain blast with neither.
    //   9  - silver lines: stream wires within the spheres' reach are laid at once and without silver; every keeper on
    //        the map holds a quarter more.
    //   12 - pilot of the spire: while the architect is on its map the spire makes half as much energy again, its
    //        barrier is a third stronger and a third cheaper to hold, and it flies two tiles further.
    // =====================================================================================
    public static class SphereEnergy
    {
        public const float DomeCost = 0.15f;
        public const int DomeCooldown = GenDate.TicksPerHour * 6;
        public const int DomeTicks = GenDate.TicksPerDay;
        public const float FlameCost = 0.1f;
        public const int FlameCooldown = GenDate.TicksPerHour;
        public const float Range = 30f;

        private static CompSpireCradle Cradle(Map map)
        {
            ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_SpireCradle");
            return d == null ? null : map.listerThings.ThingsOfDef(d).Select(t => t.TryGetComp<CompSpireCradle>()).FirstOrDefault(c => c != null);
        }

        public static float Available(Map map)
        {
            float s = CompEnergyKeeper.All.Where(k => k.parent.Spawned && k.parent.Map == map).Sum(k => k.stored);
            CompSpireCradle cr = Cradle(map);
            return s + (cr?.Stored ?? 0f);
        }

        public static void Take(Map map, float amount)
        {
            float left = amount;
            foreach (CompEnergyKeeper k in CompEnergyKeeper.All.Where(k => k.parent.Spawned && k.parent.Map == map).OrderByDescending(k => k.stored).ToList())
            {
                if (left <= 0.01f) return;
                left -= k.Take(left);
            }
            if (left > 0.01f) Cradle(map)?.Take(left);
        }

        // ---------------------------------------------------------------- the dome

        public static Command DomeCommand(Pawn p, CompArchitectSpheres c)
        {
            var cmd = new Command_Target
            {
                defaultLabel = "Energy dome",
                defaultDesc = "Raise a dome at a point that stops the shots flying in from outside. It holds a day, or until it has taken all it can.\n\nCosts "
                              + DomeCost.ToStringPercent() + " of a full artifact (from the stream's keepers and the spire first).",
                icon = SphereIcons.Named("Energy_Dome") ?? ContentFinder<Texture2D>.Get("UI/Ritualistics/Runes/Ink/Rune_Barrier", false) ?? SphereActives.Icon(c),
                targetingParams = new TargetingParameters
                {
                    canTargetLocations = true, canTargetPawns = true, canTargetBuildings = true, canTargetItems = false,
                    validator = ti => ti.Cell.IsValid && ti.Cell.InBounds(p.Map) && (ti.Cell - p.Position).LengthHorizontal <= Range
                },
                action = ti =>
                {
                    if (!SpherePaths.Pay(p, c, DomeCost)) return;
                    c.Cooldown("dome", DomeCooldown);
                    float pw = SpherePaths.Power(p, SpherePaths.Energy);
                    MapComponent_SphereDomes.Of(p.Map)?.Raise(new SphereDome { center = ti.Cell, radius = 5.9f, hp = 400f * pw, until = Find.TickManager.TicksGame + DomeTicks });
                    RitualFx.Ripple(ti.Cell.ToVector3Shifted(), p.Map, 6f);
                }
            };
            SphereActives.Gate(cmd, p, c, "dome", DomeCost);
            return cmd;
        }

        // ---------------------------------------------------------------- flame strike

        public static Command FlameCommand(Pawn p, CompArchitectSpheres c)
        {
            string el = SpherePaths.Element(p);
            var cmd = new Command_Target
            {
                defaultLabel = el == "Gold" ? "Golden flame" : el == "Silver" ? "Silver flame" : "Energy strike",
                defaultDesc = SpherePaths.Powers(SpherePaths.Energy)[1][1] + "\n\nCosts " + FlameCost.ToStringPercent() + " of a full artifact (from the stream's keepers and the spire first).",
                icon = SphereIcons.Named(el == "Gold" ? "Flame_Gold" : el == "Silver" ? "Flame_Silver" : "Flame_Plain") ?? ContentFinder<Texture2D>.Get("UI/Ritualistics/Runes/Ink/Rune_Release", false) ?? SphereActives.Icon(c),
                targetingParams = new TargetingParameters
                {
                    canTargetLocations = true, canTargetPawns = true, canTargetBuildings = true, canTargetItems = false,
                    validator = ti => ti.Cell.IsValid && ti.Cell.InBounds(p.Map) && (ti.Cell - p.Position).LengthHorizontal <= Range
                },
                action = ti =>
                {
                    if (!SpherePaths.Pay(p, c, FlameCost)) return;
                    c.Cooldown("flame", FlameCooldown);
                    Strike(p, ti.Cell, el);
                }
            };
            SphereActives.Gate(cmd, p, c, "flame", FlameCost);
            return cmd;
        }

        private static void Strike(Pawn p, IntVec3 at, string el)
        {
            Map map = p.Map;
            float pw = SpherePaths.Power(p, SpherePaths.Energy);
            const float r = 3.9f;
            if (el == null)
            {
                GenExplosion.DoExplosion(at, map, r, DamageDefOf.Bomb, p, Mathf.RoundToInt(25f * pw), ignoredThings: new List<Thing> { p });
                StriderBowFx.Spawn("Shockwave", at.ToVector3Shifted(), map, new Color(1f, 0.9f, 0.7f), r * 2f);
                return;
            }
            StriderBowFx.Spawn("FireFall", at.ToVector3Shifted() + new Vector3(0f, 0f, 1f), map, el == "Gold" ? new Color(1f, 0.82f, 0.35f) : new Color(0.85f, 0.9f, 1f), r);
            ThingDef fire = el == "Gold" ? EssentiumFireDefOf.Avir_GoldFire : EssentiumFireDefOf.Avir_SilverFire;
            Color col = el == "Gold" ? new Color(1f, 0.82f, 0.35f) : new Color(0.85f, 0.9f, 1f);
            foreach (Pawn q in GenRadial.RadialDistinctThingsAround(at, map, r, true).OfType<Pawn>().Where(q => !q.Dead && q.HostileTo(p)).ToList())
                q.TakeDamage(new DamageInfo(DamageDefOf.Flame, 18f * pw, ArtifactCE.Pen(0.3f, 4f), -1f, p));
            foreach (IntVec3 c in GenRadial.RadialCellsAround(at, r, true))
                if (c.InBounds(map) && Rand.Chance(0.35f)) EssentiumFire.StartIn(fire, c, map, 0.5f, p);
            RitualFx.Blast(at.ToVector3Shifted(), map, r / 2.5f);
            for (int i = 0; i < 6; i++) RitualFx.Mote(at.ToVector3Shifted() + Rand.InsideUnitCircleVec3 * r, map, Rand.Range(0.8f, 1.6f), col);
        }

        // ---------------------------------------------------------------- silver lines

        public static void SilverLines(Pawn p, Map map)
        {
            ThingDef wire = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_StreamWire");
            if (wire == null) return;
            float reach = ArtifactoricsSettingsDef.Main?.sphereReach ?? 12f;
            foreach (Thing t in GenRadial.RadialDistinctThingsAround(p.Position, map, reach, true).ToList())
            {
                if (t.Faction != p.Faction || t.Destroyed) continue;
                if (t is Blueprint_Build bp && bp.def.entityDefToBuild == wire)
                {
                    IntVec3 c = bp.Position;
                    Rot4 rot = bp.Rotation;
                    bp.Destroy();
                    Thing w = ThingMaker.MakeThing(wire);
                    w.SetFaction(p.Faction);
                    GenSpawn.Spawn(w, c, map, rot);
                    FleckMaker.ThrowMicroSparks(c.ToVector3Shifted(), map);
                }
                else if (t is Frame f && f.def.entityDefToBuild == wire) f.CompleteConstruction(p);
            }
        }

        // ---------------------------------------------------------------- who is on the map (cached: stats ask often)

        public static bool Lines(Map map) => SpherePaths.OnMapCached(map, SpherePaths.Energy, 9);
        public static bool Pilot(Map map) => SpherePaths.OnMapCached(map, SpherePaths.Energy, 12);
    }

    // ---------------------------------------------------------------- domes

    public class SphereDome : IExposable
    {
        public IntVec3 center;
        public float radius;
        public float hp;
        public int until;
        [Unsaved] public BarrierDome dome;

        public void ExposeData()
        {
            Scribe_Values.Look(ref center, "center");
            Scribe_Values.Look(ref radius, "radius");
            Scribe_Values.Look(ref hp, "hp");
            Scribe_Values.Look(ref until, "until");
        }
    }

    public class MapComponent_SphereDomes : MapComponent
    {
        public List<SphereDome> domes = new List<SphereDome>();

        public MapComponent_SphereDomes(Map map) : base(map) { }

        public static MapComponent_SphereDomes Of(Map map) => map?.GetComponent<MapComponent_SphereDomes>();

        public void Raise(SphereDome d)
        {
            if (domes.Count >= 3) Drop(domes[0]);
            domes.Add(d);
        }

        private void Drop(SphereDome d)
        {
            if (d.dome != null) BarrierDome.All.Remove(d.dome);
            domes.Remove(d);
        }

        public override void MapComponentTick()
        {
            if (domes.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            foreach (SphereDome d in domes.ToList())
            {
                if (now >= d.until || d.hp <= 0f) { Drop(d); continue; }
                if (d.dome == null || !BarrierDome.All.Contains(d.dome))
                {
                    SphereDome dd = d;
                    d.dome = new BarrierDome { map = map, center = d.center, radius = d.radius, hit = proj => { dd.hp -= Mathf.Max(1f, proj.DamageAmount); return true; } };
                    BarrierDome.All.Add(d.dome);
                }
            }
        }

        public override void MapComponentUpdate()
        {
            if (domes.Count == 0 || Find.CurrentMap != map) return;
            foreach (SphereDome d in domes)
            {
                Vector3 c = d.center.ToVector3Shifted(); c.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                float a = 0.18f + 0.04f * Mathf.Sin(Find.TickManager.TicksGame / 80f);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(c, Quaternion.identity, new Vector3(d.radius * 2f, 1f, d.radius * 2f)),
                    MaterialPool.MatFrom(FlowTex.Dome ?? BaseContent.WhiteTex, ShaderDatabase.Transparent, new Color(1f, 0.85f, 0.55f, a)), 0);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref domes, "sphereDomes", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (domes == null) domes = new List<SphereDome>();
                domes.RemoveAll(x => x == null);
            }
        }
    }

    // ---------------------------------------------------------------- silver lines and the pilot of the spire

    [HarmonyPatch(typeof(CompEnergyKeeper), nameof(CompEnergyKeeper.Capacity), MethodType.Getter)]
    public static class Patch_SphereKeeperCapacity
    {
        public static void Postfix(CompEnergyKeeper __instance, ref float __result)
        {
            if (__instance.parent.Spawned && SphereEnergy.Lines(__instance.parent.Map)) __result *= 1.25f;
        }
    }

    [HarmonyPatch(typeof(CompSpireCradle), nameof(CompSpireCradle.Generation), MethodType.Getter)]
    public static class Patch_SpirePilotGeneration
    {
        public static void Postfix(CompSpireCradle __instance, ref float __result)
        {
            if (__instance.parent.Spawned && SphereEnergy.Pilot(__instance.parent.Map)) __result *= 1.5f;
        }
    }

    [HarmonyPatch(typeof(CompSpireCradle), nameof(CompSpireCradle.Barrier), MethodType.Getter)]
    public static class Patch_SpirePilotBarrier
    {
        public static void Postfix(CompSpireCradle __instance, ref float __result)
        {
            if (__instance.parent.Spawned && SphereEnergy.Pilot(__instance.parent.Map)) __result *= 1.33f;
        }
    }

    [HarmonyPatch(typeof(CompSpireCradle), nameof(CompSpireCradle.BarrierUpkeep), MethodType.Getter)]
    public static class Patch_SpirePilotUpkeep
    {
        public static void Postfix(CompSpireCradle __instance, ref float __result)
        {
            if (__instance.parent.Spawned && SphereEnergy.Pilot(__instance.parent.Map)) __result *= 0.67f;
        }
    }

    [HarmonyPatch(typeof(CompSpireCradle), nameof(CompSpireCradle.Range), MethodType.Getter)]
    public static class Patch_SpirePilotRange
    {
        public static void Postfix(CompSpireCradle __instance, ref int __result)
        {
            if (__instance.parent.Spawned && SphereEnergy.Pilot(__instance.parent.Map)) __result += 2;
        }
    }
}
