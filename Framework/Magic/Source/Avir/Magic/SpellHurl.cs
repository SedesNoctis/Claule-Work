using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Avir.Artifactorics;

namespace Avir.Magic
{
    // =====================================================================================
    // What a spell does to a body, seen and felt - no stand-ins:
    //   thrown  - the body flies back in a low arc; if a wall stops it, it slams into the wall;
    //   lifted  - the body is thrown up and hangs there (out of reach of any blow or shot), then falls: the fall hurts,
    //             bones may crack, a long fall can kill. Under a roof it strikes the ceiling first;
    //   felled  - knocked off their feet: a short throw, then they lie on the ground, unable to walk or fight, and get up
    //             (the knockdown of the amulets' battle telekinesis - one and the same);
    //   rooted  - the feet are held: they cannot take a step, but can still shoot and strike.
    // Big beasts are too heavy to lift: they are felled instead, and thrown less far.
    // =====================================================================================
    public enum HurlKind { Thrown, Lifted }

    public class SpellHurl : PawnFlyer
    {
        public HurlKind kind;
        public float power = 1f;
        public Pawn by;
        public bool slam;
        public int lie;

        private static bool Unmoved(Pawn p) => p == null || p.Dead || !p.Spawned || MagicFx.Rooted(p) || SpellWards.Stops(p, false);

        /// <summary>Thrown back from a point (a negative distance pulls toward it); lie &gt; 0 - and left lying.</summary>
        public static void Throw(Pawn p, IntVec3 from, int dist, Map map, Pawn by = null, float power = 1f, int lie = 0)
        {
            if (Unmoved(p) || map == null) return;
            float size = Mathf.Max(1f, p.BodySize);
            int d = Mathf.RoundToInt(Mathf.Abs(dist) / Mathf.Sqrt(size));
            Vector3 dir = (p.Position - from).ToVector3();
            if (dir.sqrMagnitude < 0.01f) dir = Rand.InsideUnitCircleVec3;
            dir.Normalize();
            if (dist < 0) dir = -dir;
            IntVec3 cur = p.Position;
            bool stopped = false;
            for (int i = 1; i <= d; i++)
            {
                IntVec3 n = (p.Position.ToVector3Shifted() + dir * i).ToIntVec3();
                if (!n.InBounds(map) || !n.Walkable(map) || n.GetEdifice(map) != null) { stopped = true; break; }
                if (n.GetFirstPawn(map) != null) continue;
                cur = n;
            }
            if (cur == p.Position)
            {
                // nowhere to fly: the blow lands where they stand
                if (stopped && d > 0) Slam(p, by, power);
                if (lie > 0) Fell(p, lie, by);
                return;
            }
            Launch(p, cur, HurlKind.Thrown, by, power, lie, stopped && d > 0);
        }

        /// <summary>Knocked off their feet: thrown a little and left lying for a while.</summary>
        public static void Fell(Pawn p, IntVec3 from, int dist, Map map, Pawn by, float power, int ticks)
        {
            if (Unmoved(p)) return;
            if (dist > 0) Throw(p, from, dist, map, by, power, ticks);
            else Fell(p, ticks, by);
        }

        /// <summary>Thrown up into the air; they come down where they were and the fall hurts.</summary>
        public static void Lift(Pawn p, Pawn by, float power)
        {
            if (Unmoved(p)) return;
            // the updraft is the spell's strength: the heavier the body with all it wears, the lower it goes up - and the
            // fall is that weight on the ground from that height, through any plate (Магия-правила.md, "Тело и стихии")
            float height = power / Mathf.Sqrt(SpellBody.Heavy(p));
            if (p.BodySize > 2.5f || height < 0.6f) { Topple(p, by, power); Fell(p, Mathf.RoundToInt(120f * power), by); return; }
            Launch(p, p.Position, HurlKind.Lifted, by, height, 0, false);
        }

        /// <summary>Too heavy to throw far or lift high: the body is toppled where it stands, and its own weight strikes
        /// the ground - through any plate, the heavier the harder.</summary>
        public static void Topple(Pawn p, Pawn by, float power)
        {
            if (p == null || p.Dead) return;
            var b = SpellBody.Of(p);
            float w = SpellBody.Heavy(p) * b.legsLand;
            float pen = ModsConfig.IsActive("CETeam.CombatExtended") ? 500f : 2f;
            for (int i = 0; i < 2 && !p.Dead; i++)
                p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Rand.Range(2f, 4f) * Mathf.Max(0.5f, power) * w, pen, -1f, by, Part(p, 0.1f)));
            if (p.Spawned) FleckMaker.ThrowDustPuffThick(p.DrawPos, p.Map, 1.6f, MagicFx.AirDust);
        }

        private static void Launch(Pawn p, IntVec3 to, HurlKind kind, Pawn by, float power, int lie, bool slam)
        {
            Map map = p.Map;
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(kind == HurlKind.Lifted ? "Avir_SpellLift" : "Avir_SpellThrow");
            if (def == null) return;
            IntVec3 from = p.Position;
            var f = (SpellHurl)MakeFlyer(def, p, to, null, null);
            if (f == null) return;
            f.kind = kind; f.by = by; f.power = power; f.lie = lie; f.slam = slam;
            GenSpawn.Spawn(f, from, map);
        }

        /// <summary>Left lying: the knockdown of battle telekinesis, longer for a light body, shorter for a heavy one.</summary>
        private static void Fell(Pawn p, int ticks, Pawn by)
        {
            if (p.Dead || ticks <= 0) return;
            AmuletBattleTelekinesis.LayDown(p, Mathf.Max(30, Mathf.RoundToInt(ticks * AmuletBattleTelekinesis.KnockdownFactor(p))));
        }

        /// <summary>Struck against a wall.</summary>
        private static void Slam(Pawn p, Pawn by, float power)
        {
            p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 5f * power, 0f, -1f, by, Part(p, 0.3f)));
            if (p.Spawned) FleckMaker.ThrowDustPuffThick(p.DrawPos, p.Map, 1.4f, Color.gray);
        }

        /// <summary>The fall: several blows - mostly the legs, then the body, the arms, now and then the head. Blunt
        /// blows crack bones as any blunt blow does; enough of them kill.</summary>
        private static void Fall(Pawn p, Pawn by, float power, bool ceiling)
        {
            if (ceiling && !p.Dead) p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 6f * power, 0f, -1f, by, Part(p, 0.6f)));
            // a fall is the body's own weight against the ground: plate does not soften it - the heavier the gear, the
            // harder the landing (the piano from the tenth floor); bionic legs take some of it, a peg or a stump none
            var b = SpellBody.Of(p);
            float w = SpellBody.Heavy(p) * b.legsLand;
            float pen = ModsConfig.IsActive("CETeam.CombatExtended") ? 500f : 2f;
            int hits = 2 + Mathf.RoundToInt(power);
            for (int i = 0; i < hits && !p.Dead; i++)
                p.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Rand.Range(4f, 8f) * power * w, pen, -1f, by, Part(p, 0.06f)));
        }

        private static BodyPartRecord Part(Pawn p, float headChance)
        {
            var parts = p.health.hediffSet.GetNotMissingParts().ToList();
            if (parts.Count == 0) return null;
            if (Rand.Chance(headChance)) { BodyPartRecord head = parts.FirstOrDefault(x => x.def == BodyPartDefOf.Head); if (head != null) return head; }
            float roll = Rand.Value;
            List<BodyPartRecord> pick = roll < 0.6f ? parts.Where(x => x.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore) || x.def.tags.Contains(BodyPartTagDefOf.MovingLimbSegment)).ToList()
                                      : roll < 0.85f ? parts.Where(x => x == p.RaceProps.body.corePart || x.parent == p.RaceProps.body.corePart && x.depth == BodyPartDepth.Outside && !x.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore)).ToList()
                                      : parts.Where(x => x.def.tags.Contains(BodyPartTagDefOf.ManipulationLimbCore) || x.def.tags.Contains(BodyPartTagDefOf.ManipulationLimbSegment)).ToList();
            return pick.Count > 0 ? pick.RandomElement() : null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref kind, "kind", HurlKind.Thrown);
            Scribe_Values.Look(ref power, "power", 1f);
            Scribe_References.Look(ref by, "by");
            Scribe_Values.Look(ref slam, "slam");
            Scribe_Values.Look(ref lie, "lie");
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (Spawned && kind == HurlKind.Lifted && this.IsHashIntervalTick(6, delta)) FleckMaker.ThrowDustPuffThick(DrawPos, Map, 0.8f, MagicFx.AirDust);
        }

        protected override void RespawnPawn()
        {
            Map map = Map; IntVec3 at = DestinationPos.ToIntVec3();
            Pawn p = FlyingPawn;
            bool ceiling = kind == HurlKind.Lifted && map != null && at.Roofed(map);
            base.RespawnPawn();
            if (p == null || p.Dead) return;
            if (map != null) FleckMaker.ThrowDustPuffThick(p.DrawPos, map, kind == HurlKind.Lifted ? 2f : 1.3f, Color.gray);
            if (kind == HurlKind.Lifted)
            {
                Fall(p, by, power, ceiling);
                if (!p.Dead) Fell(p, Mathf.RoundToInt(90f * power), by);
                return;
            }
            if (slam) Slam(p, by, power);
            if (p.Dead) return;
            if (lie > 0) Fell(p, lie, by);
            else p.stances?.stunner?.StunFor(30, by, false, false);
        }
    }

    /// <summary>Feet held by the ground: no step can be taken; the hands are free.</summary>
    public class Hediff_Rooted : HediffWithComps
    {
        public static readonly HashSet<Pawn> Held = new HashSet<Pawn>();

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            Held.Add(pawn);
            if (pawn.Spawned && pawn.pather != null && pawn.pather.Moving) { pawn.pather.StopDead(); pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced); }
        }
        public override void PostRemoved() { base.PostRemoved(); Held.Remove(pawn); }
        public override void ExposeData() { base.ExposeData(); if (Scribe.mode == LoadSaveMode.PostLoadInit && pawn != null) Held.Add(pawn); }

        public override void PostTick()
        {
            base.PostTick();
            if (pawn.Spawned && pawn.IsHashIntervalTick(45)) FleckMaker.ThrowDustPuffThick(pawn.Position.ToVector3Shifted() + new Vector3(Rand.Range(-0.3f, 0.3f), 0f, -0.3f), pawn.Map, 0.9f, MagicFx.EarthDust);
        }
    }

    /// <summary>Short of breath: they gasp - a pale puff at the mouth now and then.</summary>
    public class Hediff_Gasping : HediffWithComps
    {
        public override void PostTick()
        {
            base.PostTick();
            if (pawn.Spawned && pawn.IsHashIntervalTick(90)) FleckMaker.ThrowDustPuffThick(pawn.DrawPos + new Vector3(0f, 0f, 0.35f), pawn.Map, 0.6f, MagicFx.AirDust);
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    public static class Patch_RootedStays
    {
        public static bool Prefix(Pawn ___pawn)
        {
            if (Hediff_Rooted.Held.Count == 0 || ___pawn == null || !Hediff_Rooted.Held.Contains(___pawn)) return true;
            ___pawn.pather.StopDead();
            ___pawn.jobs?.curDriver?.Notify_PatherFailed();
            return false;
        }
    }
}
