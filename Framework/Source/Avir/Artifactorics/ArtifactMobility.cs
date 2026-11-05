using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Avir.Artifactorics
{
    /// <summary>How a move looks: a body's leap, the artifact's quick blink, or the stone's real skip.</summary>
    public enum MoveFx { Leap, Blink, Teleport }

    [DefOf]
    public static class ArtifactMobilityDefOf
    {
        public static HediffDef Avir_ArtifactBlinkFocus;
        public static AbilityDef Avir_ArtifactBlink;

        static ArtifactMobilityDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactMobilityDefOf)); }
    }

    /// <summary>
    /// Stage 8d - Mobility.
    ///   3  blink: each cell walked has a small chance (x arm strength) to jump up to 4 cells ahead
    ///      along the pawn's own route; 10 s rest between blinks. The next shot within 2 s after a
    ///      blink is steadier (blink focus).
    ///   7  blinks reach 9 cells and leave an afterimage: the enemy loses the pawn for 2 s
    ///      (short invisibility).
    ///   9  blinks reach 12 cells; a manual Blink ability (12 cells, 30 s); and Dissolve: a heavy
    ///      hit throws the pawn a few cells away and hides it for 2 s (30 s rest).
    /// The jump is instant, so ranged fire cannot touch the pawn mid-jump.
    /// Blinks are rolled when a cell is entered but carried out on the next map tick, outside the
    /// path follower's own code.
    /// </summary>
    public static class ArtifactMobility
    {
        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        private static readonly Dictionary<int, int> blinkReady = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> dissolveReady = new Dictionary<int, int>();
        internal static readonly List<Pawn> pendingBlink = new List<Pawn>();
        internal static readonly List<(Pawn pawn, IntVec3 from)> pendingDissolve = new List<(Pawn, IntVec3)>();

        private static readonly AccessTools.FieldRef<Pawn_PathFollower, PathEndMode> peMode =
            AccessTools.FieldRefAccess<Pawn_PathFollower, PathEndMode>("peMode");

        private static int Idx(int lvl) => lvl >= 9 ? 2 : lvl >= 7 ? 1 : 0;

        private static int RangeFor(int lvl)
        {
            List<int> l = S.blinkRangeCells;
            return l.NullOrEmpty() ? 4 : l[Mathf.Min(Idx(lvl), l.Count - 1)];
        }

        /// <summary>Called for every cell a pawn enters.</summary>
        public static void OnCellEntered(Pawn p)
        {
            if (S == null || p?.Map == null || p.Downed || p.apparel == null) return;
            if (ArtifactWearCache.Worn(p).Count == 0) return;
            if (!ArtifactCharge.CombatReady(p)) return;   // blinking along the walk is for a fight, not for hauling
            int lvl = ArtifactMilestones.Level(p, ArtifactArmDefOf.ArtifactArm_Mobility, out float str);
            if (lvl <= 0) return;
            int now = Find.TickManager.TicksGame;
            if (blinkReady.TryGetValue(p.thingIDNumber, out int ready) && now < ready) return;
            if (!Rand.Chance(S.blinkChancePerCell * str)) return;
            if (!pendingBlink.Contains(p)) pendingBlink.Add(p);
        }

        /// <summary>Runs the queued blinks (from MapComponent_ArtifactMotion, every tick).</summary>
        public static void ProcessPending()
        {
            if (pendingBlink.Count > 0)
            {
                var list = new List<Pawn>(pendingBlink);
                pendingBlink.Clear();
                foreach (Pawn p in list) TryBlinkAlongPath(p);
            }
            if (pendingDissolve.Count > 0)
            {
                var list = new List<(Pawn, IntVec3)>(pendingDissolve);
                pendingDissolve.Clear();
                foreach ((Pawn p, IntVec3 from) in list) Dissolve(p, from);
            }
        }

        private static void TryBlinkAlongPath(Pawn p)
        {
            if (!p.Spawned || p.Downed || p.pather == null || !p.pather.Moving) return;
            PawnPath path = p.pather.curPath;
            if (path == null || !path.Found || path.NodesLeftCount < 3) return;
            int lvl = ArtifactMilestones.Level(p, ArtifactArmDefOf.ArtifactArm_Mobility);
            if (lvl <= 0) return;
            Map map = p.Map;
            int range = RangeFor(lvl);

            // the farthest node of the route within range that is a safe place to land
            IntVec3 target = IntVec3.Invalid;
            int ahead = Mathf.Min(path.NodesLeftCount - 1, 40);
            for (int i = 1; i <= ahead; i++)
            {
                IntVec3 c = path.Peek(i);
                if ((c - p.Position).LengthHorizontal > range + 0.01f) break;
                if (SafeLanding(c, map, p)) target = c;
            }
            if (!target.IsValid || target.DistanceTo(p.Position) < 2f) return;

            if (!ArtifactCharge.Spend(p, ArtifactArmDefOf.ArtifactArm_Mobility, S.chargeBlink)) return;
            LocalTargetInfo dest = p.pather.Destination;
            PathEndMode pe = peMode(p.pather);
            Jump(p, target, MoveFx.Blink);
            blinkReady[p.thingIDNumber] = Find.TickManager.TicksGame + Mathf.RoundToInt(S.blinkCooldownSeconds * 60f);

            GiveTimed(p, ArtifactMobilityDefOf.Avir_ArtifactBlinkFocus, Mathf.RoundToInt(S.blinkFocusSeconds * 60f), lvl);
            if (lvl >= 7) GiveTimed(p, ArtifactMotionHediffDefOf.Avir_ArtifactUnseen, Mathf.RoundToInt(S.blinkAfterimageSeconds * 60f), 7);

            if (dest.IsValid && p.CurJob != null) p.pather.StartPath(dest, pe);
        }

        public static bool SafeLanding(IntVec3 c, Map map, Pawn p)
        {
            return c.InBounds(map) && c.Standable(map) && !c.Fogged(map) && c.GetFirstPawn(map) == null
                   && !c.ContainsStaticFire(map) && c.GetDangerFor(p, map) != Danger.Deadly;
        }

        /// <summary>The jump itself: effects at both ends, then the pawn is moved. A leap is a body thrown through the air
        /// (dust where it pushes off and lands, a trail between); a blink is the artifact's quick step (a small soundless
        /// flicker - it happens often); a teleport is the stone's real skip, with the psycast's flash and sound.</summary>
        public static void Jump(Pawn p, IntVec3 target, MoveFx fx = MoveFx.Teleport)
        {
            Map map = p.Map;
            Vector3 from = p.TrueCenter(), to = target.ToVector3Shifted();
            switch (fx)
            {
                case MoveFx.Leap:
                    FleckMaker.ThrowDustPuffThick(from, map, 1.4f, new Color(0.75f, 0.7f, 0.62f, 0.8f));
                    Vector3 d = to - from;
                    int n = Mathf.Clamp(Mathf.RoundToInt(d.MagnitudeHorizontal()), 2, 10);
                    for (int i = 1; i < n; i++) FleckMaker.ThrowDustPuff(from + d * (i / (float)n), map, 0.5f);
                    break;
                case MoveFx.Blink:
                    FleckMaker.Static(from, map, FleckDefOf.PsycastSkipFlashEntry, 0.35f);
                    break;
                default:
                    FleckMaker.Static(from, map, FleckDefOf.PsycastSkipFlashEntry, 1f);
                    SoundDefOf.Psycast_Skip_Entry.PlayOneShot(new TargetInfo(p.Position, map));
                    break;
            }
            p.Position = target;
            p.Notify_Teleported(false, true);
            switch (fx)
            {
                case MoveFx.Leap:
                    FleckMaker.ThrowDustPuffThick(to, map, 1.8f, new Color(0.75f, 0.7f, 0.62f, 0.85f));
                    break;
                case MoveFx.Blink:
                    FleckMaker.Static(to, map, FleckDefOf.PsycastSkipInnerExit, 0.4f);
                    break;
                default:
                    FleckMaker.Static(to, map, FleckDefOf.PsycastSkipInnerExit, 1f);
                    SoundDefOf.Psycast_Skip_Exit.PlayOneShot(new TargetInfo(target, map));
                    break;
            }
        }

        private static void GiveTimed(Pawn p, HediffDef def, int ticks, int lvl)
        {
            if (def == null || ticks <= 0) return;
            if (p.health.hediffSet.GetFirstHediffOfDef(def) is Hediff_ArtifactTimed old)
            {
                old.ticksLeft = Mathf.Max(old.ticksLeft, ticks);
                return;
            }
            var h = (Hediff_ArtifactTimed)HediffMaker.MakeHediff(def, p);
            h.ticksLeft = ticks;
            h.level = lvl;
            p.health.AddHediff(h);
        }

        // ------------------------------------------------------------------ dissolve (9)

        public static void OnDamaged(Pawn p, DamageInfo dinfo, float dealt)
        {
            if (S == null || p?.Map == null || p.Dead || p.apparel == null) return;
            if (dealt < S.dissolveDamageThreshold) return;
            if (ArtifactWearCache.Worn(p).Count == 0) return;
            if (ArtifactMilestones.Level(p, ArtifactArmDefOf.ArtifactArm_Mobility) < 9) return;
            int now = Find.TickManager.TicksGame;
            if (dissolveReady.TryGetValue(p.thingIDNumber, out int ready) && now < ready) return;
            dissolveReady[p.thingIDNumber] = now + Mathf.RoundToInt(S.dissolveCooldownSeconds * 60f);
            IntVec3 from = dinfo.Instigator?.Spawned == true ? dinfo.Instigator.Position : p.Position;
            pendingDissolve.Add((p, from));
        }

        private static void Dissolve(Pawn p, IntVec3 from)
        {
            if (!p.Spawned || p.Dead) return;
            Map map = p.Map;
            IntVec3 best = IntVec3.Invalid;
            float bestScore = -1f;
            int dist = Mathf.RoundToInt(S.dissolveDistanceCells);
            foreach (IntVec3 c in GenRadial.RadialCellsAround(p.Position, dist, true))
            {
                if (!SafeLanding(c, map, p)) continue;
                float d = c.DistanceTo(p.Position);
                if (d < dist * 0.5f) continue;
                float score = c.DistanceTo(from) + Rand.Value;   // away from the attacker
                if (score > bestScore) { bestScore = score; best = c; }
            }
            if (!best.IsValid) return;
            if (!ArtifactCharge.Spend(p, ArtifactArmDefOf.ArtifactArm_Mobility, S.chargeDissolve)) return;
            Jump(p, best, MoveFx.Blink);
            if (p.Drafted || p.CurJob == null) p.jobs?.StopAll();
            GiveTimed(p, ArtifactMotionHediffDefOf.Avir_ArtifactUnseen, Mathf.RoundToInt(S.dissolveUnseenSeconds * 60f), 7);
        }

        // ------------------------------------------------------------------ manual blink ability (9)

        public static void SyncAbility(Pawn p)
        {
            if (p.abilities == null || ArtifactMobilityDefOf.Avir_ArtifactBlink == null) return;
            bool should = ArtifactMilestones.Level(p, ArtifactArmDefOf.ArtifactArm_Mobility) >= 9;
            bool has = p.abilities.GetAbility(ArtifactMobilityDefOf.Avir_ArtifactBlink) != null;
            if (should && !has) p.abilities.GainAbility(ArtifactMobilityDefOf.Avir_ArtifactBlink);
            else if (!should && has) p.abilities.RemoveAbility(ArtifactMobilityDefOf.Avir_ArtifactBlink);
        }
    }

    /// <summary>Rolls a blink for every cell a pawn enters.</summary>
    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    public static class Patch_ArtifactBlinkRoll
    {
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, Pawn> patherPawn =
            AccessTools.FieldRefAccess<Pawn_PathFollower, Pawn>("pawn");

        public static void Postfix(Pawn_PathFollower __instance)
        {
            Pawn p = patherPawn(__instance);
            if (p == null) return;
            ArtifactMobility.OnCellEntered(p);
        }
    }

    /// <summary>Heavy hits trigger Dissolve (Mobility 9).</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_ArtifactDissolve
    {
        public static void Postfix(Pawn __instance, DamageInfo __0, float __1)
        {
            ArtifactMobility.OnDamaged(__instance, __0, __1);
        }
    }

    /// <summary>Manual blink: a jump to a chosen free cell within range.</summary>
    public class CompProperties_ArtifactBlink : CompProperties_AbilityEffect
    {
        public CompProperties_ArtifactBlink() { compClass = typeof(CompAbilityEffect_ArtifactBlink); }
    }

    public class CompAbilityEffect_ArtifactBlink : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn p = parent.pawn;
            if (p?.Map == null || !target.IsValid) return false;
            if (!ArtifactMobility.SafeLanding(target.Cell, p.Map, p))
            {
                if (throwMessages) Messages.Message("Cannot blink there.", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            return base.Valid(target, throwMessages);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn p = parent.pawn;
            if (p?.Map == null || !ArtifactMobility.SafeLanding(target.Cell, p.Map, p)) return;
            if (!ArtifactCharge.Spend(p, ArtifactArmDefOf.ArtifactArm_Mobility,
                    ArtifactoricsSettingsDef.Main?.chargeBlink ?? 2f, true))
            {
                Messages.Message("The artifact is out of charge.", p, MessageTypeDefOf.RejectInput, false);
                return;
            }
            ArtifactMobility.Jump(p, target.Cell, MoveFx.Blink);
            p.jobs?.StopAll();
        }
    }
}
