using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Avir.Artifactorics
{
    [DefOf]
    public static class ArtifactMotionHediffDefOf
    {
        public static HediffDef Avir_ArtifactPhasing;
        public static HediffDef Avir_ArtifactUnseen;

        static ArtifactMotionHediffDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactMotionHediffDefOf)); }
    }

    /// <summary>
    /// A hediff that lasts a set number of ticks, given by the motion milestones.
    ///  - Phasing (Swiftness): VEF's phasing comp lets the pawn walk through walls. It never ends
    ///    inside a wall: the time is stretched until the pawn steps out; if it is stuck longer than
    ///    phasingRescueSeconds it is moved to the nearest free cell.
    ///  - Unseen: vanilla invisibility. Milestone 3 breaks it at the first attack, 7 and 9 at the
    ///    first kill (see Patch_ArtifactUnseenKill).
    /// </summary>
    public class Hediff_ArtifactTimed : HediffWithComps
    {
        public int ticksLeft;
        public int level;          // milestone that gave it (3, 7, 9)
        private int stretched;

        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public bool IsPhasing => def == ArtifactMotionHediffDefOf.Avir_ArtifactPhasing;

        public override bool ShouldRemove => base.ShouldRemove || ticksLeft <= 0;

        /// <summary>Unseen 9: the first blow from invisibility deals double damage.</summary>
        public bool ambushReady;
        private bool wasInWall;

        public bool IsUnseen => def == ArtifactMotionHediffDefOf.Avir_ArtifactUnseen;

        public override void Tick()
        {
            base.Tick();
            // Unseen 7+: in darkness the veil does not run out (an attack or kill still ends it)
            bool hold = IsUnseen && level >= 7 && pawn.Spawned && pawn.IsHashIntervalTick(1)
                        && pawn.Map.glowGrid.PsychGlowAt(pawn.Position) == PsychGlow.Dark;
            if (!hold) ticksLeft--;
            else if (level < 9 && pawn.stances?.curStance is Stance_Busy b && b.verb != null && b.verb.verbProps.violent)
                ticksLeft = 0;
            if (IsPhasing)
            {
                bool inWall = pawn.Spawned && !pawn.Position.Walkable(pawn.Map);
                if (wasInWall && !inWall && pawn.Spawned) ArtifactMotionExtras.ExitWall(pawn, level);
                wasInWall = inWall;
                if (ticksLeft <= 1 && inWall)
                {
                    ticksLeft = 2;
                    stretched++;
                    if (stretched > (S?.phasingRescueSeconds ?? 10f) * 60f) Rescue();
                }
            }
            else if (def == ArtifactMotionHediffDefOf.Avir_ArtifactUnseen && level < 7 && pawn.IsHashIntervalTick(10))
            {
                // Unseen 3: the first attack ends it
                if (pawn.stances?.curStance is Stance_Busy busy && busy.verb != null && busy.verb.verbProps.violent)
                    ticksLeft = 0;
            }
        }

        private void Rescue()
        {
            Map map = pawn.Map;
            if (CellFinder.TryFindRandomCellNear(pawn.Position, map, 8, c => c.Standable(map) && !c.Fogged(map), out IntVec3 cell))
            {
                pawn.Position = cell;
                pawn.Notify_Teleported(true, false);
            }
            ticksLeft = 0;
        }

        public override string LabelInBrackets => (ticksLeft / 60f).ToString("0") + " s";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksLeft, "ticksLeft");
            Scribe_Values.Look(ref level, "level");
            Scribe_Values.Look(ref stretched, "stretched");
            Scribe_Values.Look(ref ambushReady, "ambushReady");
        }
    }

    /// <summary>
    /// Stage 8c - Swiftness (walking through walls) and Unseen (invisibility) while moving.
    ///
    /// How it triggers: once a second, only while the pawn is walking, it rolls a chance
    /// (per milestone, x arm strength). On success the effect lasts its duration (per milestone,
    /// x arm strength, capped). After it ends, a window passes in which no new roll happens.
    /// Standing still never triggers anything. Swiftness has one short window on every level - its
    /// frequency comes from the chance; Unseen keeps the spec's windows 20 / 16 / 12 s.
    /// </summary>
    public class MapComponent_ArtifactMotion : MapComponent
    {
        private const int Interval = 60;
        private readonly Dictionary<int, int> swiftReady = new Dictionary<int, int>();
        private readonly Dictionary<int, int> unseenReady = new Dictionary<int, int>();

        private static readonly AccessTools.FieldRef<Pawn_PathFollower, PathEndMode> peMode =
            AccessTools.FieldRefAccess<Pawn_PathFollower, PathEndMode>("peMode");

        private static ArtifactoricsSettingsDef S => ArtifactoricsSettingsDef.Main;

        public MapComponent_ArtifactMotion(Map map) : base(map) { }

        private static int Idx(int lvl) => lvl >= 9 ? 2 : lvl >= 7 ? 1 : 0;

        private static float At(List<float> l, int i) => l.NullOrEmpty() ? 0f : l[Mathf.Min(i, l.Count - 1)];

        public override void MapComponentTick()
        {
            if (S == null) return;
            ArtifactMobility.ProcessPending();
            int now = Find.TickManager.TicksGame;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            if (now % 250 == 0)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p.abilities == null || p.apparel == null) continue;
                    if (ArtifactWearCache.Worn(p).Count > 0 || p.abilities.GetAbility(ArtifactMobilityDefOf.Avir_ArtifactBlink) != null)
                        ArtifactMobility.SyncAbility(p);
                }
            }
            if (now % Interval != 0) return;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.apparel == null || p.Downed || p.Dead || p.pather == null || !p.pather.Moving) continue;
                if (ArtifactWearCache.Worn(p).Count == 0) continue;
                if (!ArtifactCharge.CombatReady(p)) continue;   // only in a fight (drafted, fighting, or going for an enemy)

                int sw = ArtifactMilestones.Level(p, ArtifactArmDefOf.ArtifactArm_Swiftness, out float swStr);
                if (sw > 0) TrySwift(p, sw, swStr, now);

                int un = ArtifactMilestones.Level(p, ArtifactArmDefOf.ArtifactArm_Unseen, out float unStr);
                if (un > 0) TryUnseen(p, un, unStr, now);
            }
        }

        private void TrySwift(Pawn p, int lvl, float str, int now)
        {
            if (p.health.hediffSet.HasHediff(ArtifactMotionHediffDefOf.Avir_ArtifactPhasing)) return;
            if (swiftReady.TryGetValue(p.thingIDNumber, out int ready) && now < ready) return;
            int i = Idx(lvl);
            float chance = At(S.swiftnessChancePerSecond, i) * str;
            // Swiftness 7: more often in a fight
            if (lvl >= 7 && (p.Drafted || now - p.mindState.lastEngageTargetTick < 600)) chance *= S.swiftnessCombatChanceFactor;
            if (!Rand.Chance(chance)) return;
            int ticks = Mathf.RoundToInt(Mathf.Min(S.swiftnessMaxDurationSeconds, At(S.swiftnessDurationSeconds, i) * str) * 60f);
            if (ticks < 60) return;
            if (!ArtifactCharge.Spend(p, ArtifactArmDefOf.ArtifactArm_Swiftness, S.chargePhasing)) return;
            Give(p, ArtifactMotionHediffDefOf.Avir_ArtifactPhasing, ticks, lvl);
            swiftReady[p.thingIDNumber] = now + ticks + Mathf.RoundToInt(S.swiftnessWindowSeconds * 60f);
            Repath(p);
        }

        private void TryUnseen(Pawn p, int lvl, float str, int now)
        {
            if (p.health.hediffSet.HasHediff(ArtifactMotionHediffDefOf.Avir_ArtifactUnseen)) return;
            if (unseenReady.TryGetValue(p.thingIDNumber, out int ready) && now < ready) return;
            int i = Idx(lvl);
            if (!Rand.Chance(At(S.unseenChancePerSecond, i) * str)) return;
            int ticks = Mathf.RoundToInt(Mathf.Min(S.unseenMaxDurationSeconds, At(S.unseenDurationSeconds, i) * str) * 60f);
            if (ticks < 60) return;
            if (!ArtifactCharge.Spend(p, ArtifactArmDefOf.ArtifactArm_Unseen, S.chargeVeil)) return;
            Give(p, ArtifactMotionHediffDefOf.Avir_ArtifactUnseen, ticks, lvl);
            unseenReady[p.thingIDNumber] = now + ticks + Mathf.RoundToInt(At(S.unseenWindowSeconds, i) * 60f);
        }

        private static void Give(Pawn p, HediffDef def, int ticks, int lvl)
        {
            var h = (Hediff_ArtifactTimed)HediffMaker.MakeHediff(def, p);
            h.ticksLeft = ticks;
            h.level = lvl;
            h.ambushReady = def == ArtifactMotionHediffDefOf.Avir_ArtifactUnseen && lvl >= 9;
            p.health.AddHediff(h);
        }

        /// <summary>The current route was planned without walls in mind: plan it again so the
        /// effect is used on this walk, not only on the next one.</summary>
        private static void Repath(Pawn p)
        {
            try
            {
                if (!p.pather.Moving) return;
                LocalTargetInfo dest = p.pather.Destination;
                if (!dest.IsValid) return;
                p.pather.StartPath(dest, peMode(p.pather));
            }
            catch (System.Exception e)
            {
                Log.WarningOnce("[Avir] Artifactorics: re-path after phasing failed: " + e.Message, 0x4A5300);
            }
        }
    }

    /// <summary>Unseen 7 and 9: invisibility ends with the first kill.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_ArtifactUnseenKill
    {
        public static void Postfix(DamageInfo? __0)
        {
            if (!(__0?.Instigator is Pawn killer) || killer.health?.hediffSet == null) return;
            if (killer.health.hediffSet.GetFirstHediffOfDef(ArtifactMotionHediffDefOf.Avir_ArtifactUnseen) is Hediff_ArtifactTimed h)
            {
                if (h.level >= 9) h.ticksLeft += Mathf.RoundToInt((ArtifactoricsSettingsDef.Main?.unseenKillExtendSeconds ?? 6f) * 60f);
                else h.ticksLeft = 0;
            }
        }
    }

    [DefOf]
    public static class ArtifactMotionExtraDefOf
    {
        public static HediffDef Avir_ArtifactWallBurst;

        static ArtifactMotionExtraDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(ArtifactMotionExtraDefOf)); }
    }

    public static class ArtifactMotionExtras
    {
        /// <summary>Swiftness 3: stepping out of a wall gives a short burst of speed.
        /// Swiftness 9: enemies next to the exit are staggered.</summary>
        public static void ExitWall(Pawn p, int level)
        {
            ArtifactoricsSettingsDef s = ArtifactoricsSettingsDef.Main;
            if (s == null) return;
            HediffDef def = ArtifactMotionExtraDefOf.Avir_ArtifactWallBurst;
            int ticks = Mathf.RoundToInt(s.wallBurstSeconds * 60f);
            if (p.health.hediffSet.GetFirstHediffOfDef(def) is Hediff_ArtifactTimed old) old.ticksLeft = Mathf.Max(old.ticksLeft, ticks);
            else
            {
                var h = (Hediff_ArtifactTimed)HediffMaker.MakeHediff(def, p);
                h.ticksLeft = ticks;
                h.level = level;
                p.health.AddHediff(h);
            }
            if (level < 9) return;
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(p))
            {
                if (!c.InBounds(p.Map)) continue;
                Pawn e = c.GetFirstPawn(p.Map);
                if (e != null && e.HostileTo(p) && e.stances?.stunner != null)
                    e.stances.stunner.StunFor(Mathf.RoundToInt(s.wallExitStunSeconds * 60f), p, false, true);
            }
        }
    }

    /// <summary>Unseen 9 - ambush: the first blow from invisibility deals double damage. Runs
    /// before the artifact shields so the doubled hit is what they meet.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_ArtifactAmbush
    {
        [HarmonyPriority(Priority.High)]
        public static void Prefix(ref DamageInfo __0)
        {
            if (!(__0.Instigator is Pawn attacker) || attacker.health?.hediffSet == null) return;
            if (!(attacker.health.hediffSet.GetFirstHediffOfDef(ArtifactMotionHediffDefOf.Avir_ArtifactUnseen) is Hediff_ArtifactTimed h)) return;
            if (!h.ambushReady) return;
            h.ambushReady = false;
            __0.SetAmount(__0.Amount * (ArtifactoricsSettingsDef.Main?.ambushDamageFactor ?? 2f));
        }
    }
}
