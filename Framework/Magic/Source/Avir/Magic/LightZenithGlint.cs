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
    /// THE ZENITH - Light meeting Fire (the author, Fix261-263). Light and Fire together are energy: the strongest quick
    /// healing, strength given, seals. The Fire circle that went into the Light says what is done at the point:
    ///  - Restoration: friends there are healed hard and at once (the bleeding first);
    ///  - Growth: strength - tiredness lifts, the body moves and works quicker, for hours;
    ///  - Hold: seals - each Hold one seal on every friend there; a seal flares and heals when its bearer is hurt, each
    ///    seal once;
    ///  - Purge: fire and white flame put out on friends;
    ///  - Release, Beam: WHITE FLAME on foes - at the point, or along the line. It clings to anything, machines too (as
    ///    prometheum does in Combat Extended), flickers and dazzles, marks the burning one (easier to strike), and the
    ///    burning one is a WALKING REFLECTOR: Light's echoes come out of it wherever it goes, in the common count. Water
    ///    does not put it out: whoever walks into a pool or a river burning with it makes that water shine.
    /// The circles of Light themselves strike as plain Light.
    /// </summary>
    public static class LightZenith
    {
        public static readonly Color ZenithColor = new Color(1f, 0.97f, 0.82f);
        public static HediffDef WhiteFlame => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_WhiteFlame");
        public static HediffDef Seal => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_ZenithSeal");
        public static HediffDef Vigor => DefDatabase<HediffDef>.GetNamedSilentFail("Avir_ZenithVigor");
        public const int MaxSeals = 6;

        private static IEnumerable<Pawn> At(Map map, IntVec3 at, float r) =>
            GenRadial.RadialCellsAround(at, r, true).Where(c => c.InBounds(map) && GenSight.LineOfSight(at, c, map, true))
                .SelectMany(c => c.GetThingList(map).OfType<Pawn>()).Where(p => !p.Dead).Distinct().ToList();

        public static bool Friend(Pawn p, Pawn caster) => caster != null && p != null && (p == caster || !p.HostileTo(caster));

        public static void OnCircle(Pawn caster, SpellCircle root, SpellCircle g, IntVec3 from, IntVec3 at, float power, Map map, float radius)
        {
            if (map == null || !at.InBounds(map) || root == null) return;
            SpellCircle box = g.Great ? g : LightMagic.BoxOf(root, g);
            if (LightMagic.Links(root).FirstOrDefault() != g) return;   // once a spell, on its first circle of Light (echoes too, weaker)
            SpellCircle fire = LightMagic.Absorbed(box);
            if (fire == null) return;
            var runes = fire.OwnRunes.ToList();
            int breadth = runes.Count(x => x.kind == RuneKind.Breadth);
            float r = Mathf.Max(radius, 1.5f + breadth);
            power *= 1f + 0.3f * runes.Count(x => x.kind == RuneKind.Amplify);
            var here = At(map, at, r);
            var friends = here.Where(p => Friend(p, caster)).ToList();
            if (runes.Any(x => x.kind == RuneKind.Restore))
                foreach (Pawn p in friends) { LifeMagic.HealInjuries(p, 24f * power); MapComponent_SpellSprites.PutTinted(map, "Light/Flare_Night", ZenithColor, p.DrawPos, 1.4f, 30); }
            if (runes.Any(x => x.kind == RuneKind.Growth) && Vigor != null)
                foreach (Pawn p in friends)
                {
                    MagicFx.Give(p, Vigor, Mathf.RoundToInt(GenDate.TicksPerHour * Mathf.Clamp(3f * power, 1f, 8f)));
                    if (p.needs?.rest != null) p.needs.rest.CurLevel = Mathf.Min(1f, p.needs.rest.CurLevel + 0.25f * power);
                }
            int holds = runes.Count(x => x.kind == RuneKind.Hold);
            if (holds > 0 && Seal != null && !LightMagic.Echoing)
                foreach (Pawn p in friends)
                {
                    Hediff s = p.health.hediffSet.GetFirstHediffOfDef(Seal) ?? p.health.AddHediff(Seal);
                    s.Severity = Mathf.Min(MaxSeals, Mathf.Floor(s.Severity < 1f ? 0f : s.Severity) + holds);
                    s.TryGetComp<HediffComp_Disappears>()?.ResetElapsedTicks();
                }
            if (runes.Any(x => x.kind == RuneKind.Purge))
                foreach (Pawn p in friends)
                {
                    p.GetAttachment(ThingDefOf.Fire)?.Destroy();
                    Hediff w = WhiteFlame != null ? p.health.hediffSet.GetFirstHediffOfDef(WhiteFlame) : null;
                    if (w != null) p.health.RemoveHediff(w);
                }
            if (runes.Any(x => x.kind == RuneKind.Release))
                foreach (Pawn p in here.Where(p => p.HostileTo(caster))) Ignite(p, caster, power);
            if (runes.Any(x => x.kind == RuneKind.Beam))
                foreach (IntVec3 c in GenSight.BresenhamCellsBetween(from, at).Where(c => c.InBounds(map)))
                    foreach (Pawn p in c.GetThingList(map).OfType<Pawn>().Where(p => !p.Dead && p.HostileTo(caster)).ToList()) Ignite(p, caster, power);
        }

        /// <summary>White flame on a body: it clings whatever the body is made of.</summary>
        public static void Ignite(Pawn p, Pawn by, float power)
        {
            if (WhiteFlame == null || p == null || p.Dead || SpellWards.Stops(p, true)) return;
            Hediff h = p.health.hediffSet.GetFirstHediffOfDef(WhiteFlame);
            if (h == null) { h = p.health.AddHediff(WhiteFlame); h.Severity = Mathf.Clamp(0.5f * power, 0.2f, 1f); }
            else { h.Severity = Mathf.Min(1f, h.Severity + 0.3f * power); h.TryGetComp<HediffComp_Disappears>()?.ResetElapsedTicks(); }
            if (h.TryGetComp<HediffComp_WhiteFlame>() is HediffComp_WhiteFlame f) { f.by = by ?? f.by; f.EnsureMark(); }
        }

        /// <summary>The walking reflector keeps to its burning body; false - it went out.</summary>
        public static bool Follow(Building_LightReflector r)
        {
            Pawn p = r.follows;
            if (p == null || p.Dead || !p.Spawned || p.Map != r.Map || WhiteFlame == null || !p.health.hediffSet.HasHediff(WhiteFlame))
            { r.Destroy(DestroyMode.Vanish); return false; }
            if (p.Position != r.Position)
            {
                r.Position = p.Position;
                if (LightMirror.Water(p.Position, p.Map)) LightMirror.Light(r.caster, p.Map, p.Position);   // water does not put it out: it shines
            }
            return true;
        }
    }

    /// <summary>White flame's own life on a body: it sears and dazzles in flickers, it lights the ground, and it carries a
    /// walking reflector with it.</summary>
    public class HediffCompProperties_WhiteFlame : HediffCompProperties
    {
        public HediffCompProperties_WhiteFlame() { compClass = typeof(HediffComp_WhiteFlame); }
    }

    public class HediffComp_WhiteFlame : HediffComp
    {
        public Pawn by;
        public Building_LightReflector mark;

        public void EnsureMark()
        {
            Pawn p = Pawn;
            if (p?.Map == null || (mark != null && mark.Spawned)) return;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Avir_WhiteFlameMark");
            if (def == null) return;
            mark = (Building_LightReflector)ThingMaker.MakeThing(def);
            mark.caster = by; mark.follows = p; mark.dies = -1;
            GenSpawn.Spawn(mark, p.Position, p.Map, WipeMode.Vanish);
            if (by?.Faction != null) mark.SetFaction(by.Faction);
            LightGlow g = LightGlow.At(p.Map, p.Position, false, 1200);
            if (g != null) g.follows = p;
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn p = Pawn;
            if (p == null || !p.Spawned || !p.IsHashIntervalTick(45)) return;
            EnsureMark();
            float s = parent.Severity;
            p.TakeDamage(LightMagic.SearOn(1.2f + 2f * s, by));
            if (Rand.Chance(0.5f)) LightMagic.DazzlePawn(p, 0.06f + 0.1f * s);
            MapComponent_SpellSprites.PutTinted(p.Map, "Light/Flare_Night", LightZenith.ZenithColor, p.DrawPos + new Vector3(Rand.Range(-0.2f, 0.2f), 0f, Rand.Range(0f, 0.3f)), 0.9f + 0.4f * s, 20);
        }

        public override void CompPostPostRemoved()
        {
            if (mark != null && mark.Spawned) mark.Destroy(DestroyMode.Vanish);
            mark = null;
        }

        public override void CompExposeData()
        {
            Scribe_References.Look(ref by, "by");
            Scribe_References.Look(ref mark, "mark");
        }
    }

    /// <summary>A seal of the Zenith: each one flares once and heals, when its bearer is hurt.</summary>
    public class HediffCompProperties_ZenithSeal : HediffCompProperties
    {
        public HediffCompProperties_ZenithSeal() { compClass = typeof(HediffComp_ZenithSeal); }
    }

    public class HediffComp_ZenithSeal : HediffComp
    {
        public override string CompLabelInBracketsExtra => "x" + Mathf.FloorToInt(parent.Severity);

        public override void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            Pawn p = Pawn;
            if (p == null || p.Dead || totalDamageDealt < 2f || parent.Severity < 1f) return;
            LifeMagic.HealInjuries(p, 14f + totalDamageDealt);
            if (p.Spawned)
            {
                MapComponent_SpellSprites.PutTinted(p.Map, "Light/Flare_Night", LightZenith.ZenithColor, p.DrawPos, 1.8f, 35);
                LightMagic.Glint(p.Map, p.DrawPos, 1.2f, true);
            }
            parent.Severity -= 1f;
            if (parent.Severity < 1f) p.health.RemoveHediff(parent);
        }
    }

    /// <summary>
    /// THE GLINT - Light meeting Air (the author, Fix261-263). Every cast leaves a mirage where it struck and one more, as a
    /// reflector stands; a mirage is a reflector (its echo loses little) and a way: the mage, drafted, steps out of the
    /// mirage nearest where it is ordered to go, and, struck close, slips into the mirage farthest from its foes - no Step
    /// needed. The mage strikes from any of its mirages as from a reflector, however far. Eight at most - the oldest goes.
    /// The Air circle that went into the Light: Release - a flash on the wind at the point; Beam - along the line; foes are
    /// thrown back, fall and are dazzled.
    /// </summary>
    public static class LightGlint
    {
        public static readonly Color GlintColor = new Color(0.86f, 0.9f, 1f);
        public const int Max = 8;
        public static int Live;
        private static readonly Dictionary<int, int> ready = new Dictionary<int, int>();

        public static ThingDef MirageDef => DefDatabase<ThingDef>.GetNamedSilentFail("Avir_GlintMirage");

        public static List<Building_LightReflector> Mirages(Pawn p) =>
            Live <= 0 || p?.Map == null ? new List<Building_LightReflector>() : Building_LightReflector.All(p.Map).Where(r => r.IsMirage && r.caster == p).ToList();

        public static void Raise(Pawn caster, Map map, IntVec3 aim, float dist)
        {
            ThingDef def = MirageDef;
            if (def == null || caster == null) return;
            IntVec3 near = CellFinder.StandableCellNear(aim, map, 2f, c => !c.GetThingList(map).Any(t => t is Building || t.def.category == ThingCategory.Item));
            if (near.IsValid) Building_LightReflector.RaiseAt(caster, map, near, def, Building_LightReflector.Life);
            Building_LightReflector.Raise(caster, map, aim, dist, def);
            var mine = Building_LightReflector.All(map).Where(r => r.IsMirage && r.caster == caster).OrderBy(r => r.born).ToList();
            for (int i = 0; i < mine.Count - Max; i++) { LightMagic.Glint(map, mine[i].DrawPos, 1f, false); mine[i].Destroy(DestroyMode.Vanish); }
        }

        public static void OnCircle(Pawn caster, SpellCircle root, SpellCircle g, IntVec3 from, IntVec3 at, float power, Map map, float radius)
        {
            if (map == null || !at.InBounds(map) || root == null) return;
            SpellCircle box = g.Great ? g : LightMagic.BoxOf(root, g);
            if (LightMagic.Links(root).FirstOrDefault() != g) return;
            SpellCircle air = LightMagic.Absorbed(box);
            if (air == null) return;
            var runes = air.OwnRunes.ToList();
            int breadth = runes.Count(x => x.kind == RuneKind.Breadth);
            power *= 1f + 0.3f * runes.Count(x => x.kind == RuneKind.Amplify);
            var hit = new HashSet<Pawn>();
            if (runes.Any(x => x.kind == RuneKind.Release))
                foreach (IntVec3 c in GenRadial.RadialCellsAround(at, 1.5f + breadth, true).Where(c => c.InBounds(map) && GenSight.LineOfSight(at, c, map, true)))
                    foreach (Pawn p in c.GetThingList(map).OfType<Pawn>()) hit.Add(p);
            if (runes.Any(x => x.kind == RuneKind.Beam))
                foreach (IntVec3 c in GenSight.BresenhamCellsBetween(from, at).Where(c => c.InBounds(map)))
                    foreach (Pawn p in c.GetThingList(map).OfType<Pawn>()) hit.Add(p);
            if (hit.Count == 0) return;
            LightMagic.RayLine(map, from.ToVector3Shifted(), at.ToVector3Shifted(), true);
            foreach (Pawn p in hit.Where(p => !p.Dead && p != caster && p.HostileTo(caster)).ToList())
            {
                IntVec3 push = runes.Any(x => x.kind == RuneKind.Release) ? at : from;   // a flash at the point throws out of it; a beam - away from where it came
                if (p.Position == push) push = from;
                for (int i = 0; i < 3; i++) FleckMaker.ThrowDustPuffThick(p.DrawPos, map, 1f, new Color(1f, 1f, 1f, 0.5f));
                SpellHurl.Throw(p, push, 2 + breadth, map, caster, power, Mathf.RoundToInt(150f * power));
                LightMagic.DazzlePawn(p, 0.35f * power);
            }
        }

        // ------------------------------------------------------------------ the ways

        private static bool Ready(Pawn p, int cooldown)
        {
            int now = Find.TickManager.TicksGame;
            if (ready.TryGetValue(p.thingIDNumber, out int t) && now < t) return false;
            ready[p.thingIDNumber] = now + cooldown;
            return true;
        }

        public static void Slip(Pawn p, Building_LightReflector m, bool endJob)
        {
            Map map = p.Map;
            IntVec3 to = m.Position.Standable(map) && m.Position.GetFirstPawn(map) == null ? m.Position : CellFinder.StandableCellNear(m.Position, map, 2f);
            if (!to.IsValid) return;
            LightMagic.Glint(map, p.DrawPos, 1.4f, true);
            MapComponent_SpellSprites.PutTinted(map, "Light/Flare_Night", GlintColor, p.DrawPos, 1.6f, 25);
            p.Position = to;
            p.Notify_Teleported(endJob, true);
            LightMagic.Glint(map, to.ToVector3Shifted(), 1.6f, true);
            LightMagic.RayLine(map, m.DrawPos, to.ToVector3Shifted(), true, 12);
        }

        /// <summary>Drafted and sent somewhere: out of the mirage nearest the goal, if that is a good way shorter.</summary>
        public static void OnPath(Pawn p, LocalTargetInfo dest)
        {
            if (Live <= 0 || p == null || !p.Spawned || !p.Drafted || p.Downed || !dest.IsValid) return;
            IntVec3 goal = dest.Cell;
            float now = p.Position.DistanceTo(goal);
            if (now < 10f) return;
            Building_LightReflector best = Mirages(p).Where(m => m.Position.DistanceTo(goal) <= now - 8f).OrderBy(m => m.Position.DistanceTo(goal)).FirstOrDefault();
            if (best == null || !Ready(p, 120)) return;
            Slip(p, best, false);
        }

        /// <summary>Struck close: into the mirage farthest from the foes.</summary>
        public static void OnStruck(Pawn p, DamageInfo d)
        {
            if (Live <= 0 || p == null || !p.Spawned || p.Dead || p.Downed) return;
            if (!(d.Instigator is Pawn foe) || !foe.Spawned || foe.Map != p.Map || foe.Position.DistanceTo(p.Position) > 2.2f || !foe.HostileTo(p)) return;
            var mine = Mirages(p);
            if (mine.Count == 0) return;
            var foes = p.Map.mapPawns.AllPawnsSpawned.Where(q => !q.Dead && !q.Downed && q.HostileTo(p) && q.Position.DistanceTo(p.Position) < 40f).ToList();
            Building_LightReflector best = mine.OrderByDescending(m => foes.Count == 0 ? 0f : foes.Min(q => q.Position.DistanceTo(m.Position))).First();
            if (foes.Count > 0 && foes.Min(q => q.Position.DistanceTo(best.Position)) < 4f) return;
            if (!Ready(p, 300)) return;
            Slip(p, best, true);
        }

        private static Material mat;
        public static void DrawMirage(Building_LightReflector r, Vector3 at)
        {
            if (mat == null) mat = MaterialPool.MatFrom("Things/Mote/Avir/Magic/Light/Reflector_Night", ShaderDatabase.MoteGlow, new Color(0.85f, 0.9f, 1f, 0.7f));
            float t = Time.realtimeSinceStartup * 2.1f + r.thingIDNumber * 0.53f;
            float s = 1.3f + 0.1f * Mathf.Sin(t);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at + new Vector3(0f, 0f, 0.05f * Mathf.Sin(t * 0.7f)), Quaternion.identity, new Vector3(s * 0.8f, 1f, s * 1.2f)), mat, 0);
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    public static class Patch_GlintPath
    {
        public static void Prefix(Pawn ___pawn, LocalTargetInfo dest)
        {
            if (LightGlint.Live <= 0) return;
            try { LightGlint.OnPath(___pawn, dest); } catch (System.Exception e) { Log.WarningOnce("[Avir] glint path: " + e.Message, 0x4A5263); }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_GlintStruck
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo)
        {
            if (LightGlint.Live <= 0) return;
            try { LightGlint.OnStruck(__instance, dinfo); } catch (System.Exception e) { Log.WarningOnce("[Avir] glint struck: " + e.Message, 0x4A5264); }
        }
    }

    public static class ZenithGlintDev
    {
        [DebugAction("Magic", "Light: white flame on pawn (Zenith)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Flame(Pawn p) => LightZenith.Ignite(p, Find.CurrentMap?.mapPawns.FreeColonistsSpawned.FirstOrDefault(), 1f);

        [DebugAction("Magic", "Light: 3 Zenith seals on pawn", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Seals(Pawn p)
        {
            if (LightZenith.Seal == null) return;
            Hediff s = p.health.hediffSet.GetFirstHediffOfDef(LightZenith.Seal) ?? p.health.AddHediff(LightZenith.Seal);
            s.Severity = 3f;
        }

        [DebugAction("Magic", "Light: mirage of selected pawn here (Glint)", actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void Mirage()
        {
            Pawn p = Find.Selector.SingleSelectedThing as Pawn; Map map = Find.CurrentMap; IntVec3 c = Verse.UI.MouseCell();
            if (p == null || map == null || LightGlint.MirageDef == null) { Messages.Message("Select one pawn first.", MessageTypeDefOf.RejectInput, false); return; }
            Building_LightReflector.RaiseAt(p, map, c, LightGlint.MirageDef, Building_LightReflector.Life);
        }
    }
}
