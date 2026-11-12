using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Avir.Magic;

// HOTFIX: a caster (sorcerer / wizard) never sits down to meditate.
//
// TO REMOVE: delete this one file and rebuild Avir.dll. Nothing else was touched - no Defs, no saves, no other .cs.
// TO SWITCH OFF WITHOUT DELETING: set Enabled below to false.
//
// What it does (and only this):
//   1. Postfix on JobGiver_Meditate.TryGiveJob - the vanilla node that fires on the "Meditate" schedule, and on
//      "Anything" for a psylinked pawn under her psyfocus target. If vanilla came back EMPTY-HANDED for an arcane
//      caster although CanMeditateNow() is true (so nothing forbids her to meditate), the hotfix finds her a plain
//      safe cell nearby itself and hands her an ordinary Meditate job there.
//   2. The same for JoyGiver_ArcaneMeditate (the mod's own free-time meditation), only while the Arcana is below
//      its mark - exactly when that giver wants to fire.
//   3. When it has to step in, or when it cannot, ONE line goes to the log per pawn per three game hours with every
//      vanilla gate listed, so the real cause is readable instead of guessed.
//   4. A debug action (Magic category) prints the same line for a pawn on demand.
//
// What it deliberately does NOT do: it does not override CanMeditateNow(). That is vanilla's safety gate (starving,
// exhausted, bleeding, needs a doctor, asleep) and the Meditate job itself re-checks it every tick, so forcing a
// job past it would only make the job fail again at once. If the log line shows that gate as the reason, that is
// where the real bug is.

namespace Avir.Hotfix
{
    public static class CasterMeditation
    {
        public const bool Enabled = true;

        private const float SearchRadius = 28f;       // how far from her a fallback cell may be
        private const int MaxReachChecks = 40;        // path checks per search (the cheap tests run first)
        private const int RetryAfterFail = 250;       // ticks before a failed search is repeated for the same pawn
        private const int LogEvery = GenDate.TicksPerHour * 3;

        private static readonly Dictionary<int, int> nextTry = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> lastLog = new Dictionary<int, int>();

        /// <summary>An arcane caster of the player's colony who could meditate at all right now (null otherwise).</summary>
        private static Hediff_ArcaneCore Caster(Pawn p)
        {
            if (!Enabled || p == null || !AvirMagic.On) return null;
            if (p.Faction != Faction.OfPlayer || p.IsPrisoner || !p.Spawned || p.Map == null) return null;
            if (p.Dead || p.Downed || p.Drafted || p.InMentalState) return null;
            return Mages.Core(p);
        }

        /// <summary>Called when vanilla (or the mod's own giver) produced no meditation job for a caster.</summary>
        public static Job Rescue(Pawn pawn, bool forJoy)
        {
            Hediff_ArcaneCore core = Caster(pawn);
            if (core == null) return null;
            // free-time giver: it hands out nothing once the Arcana is full to its mark - that is not a failure
            if (forJoy && core.arcana >= core.target * core.Capacity - 1f) return null;

            int now = Find.TickManager.TicksGame;
            if (nextTry.TryGetValue(pawn.thingIDNumber, out int due) && now < due) return null;

            string gates = Gates(pawn, out bool vanillaAllows);
            Job job = vanillaAllows ? Fallback(pawn, forJoy) : null;
            if (job == null) nextTry[pawn.thingIDNumber] = now + RetryAfterFail;

            if (!lastLog.TryGetValue(pawn.thingIDNumber, out int logged) || now - logged >= LogEvery)
            {
                lastLog[pawn.thingIDNumber] = now;
                Log.Message("[Avir hotfix] " + pawn.LabelShort + ": vanilla gave no meditation job; "
                    + (job != null ? "sent to " + job.targetA.Cell + " instead" : vanillaAllows ? "no safe reachable cell found nearby" : "CanMeditateNow() is false - the gate below says why")
                    + " | " + gates);
            }
            return job;
        }

        /// <summary>A plain Meditate job at the nearest safe, reachable, allowed cell - the same kind of job vanilla builds for a fallback spot.</summary>
        private static Job Fallback(Pawn p, bool forJoy)
        {
            Map map = p.Map;
            int reach = 0;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(p.Position, SearchRadius, true))
            {
                if (!c.InBounds(map) || !c.Standable(map) || c.GetDoor(map) != null || c.IsForbidden(p)) continue;
                // the Meditate toil fails the moment this is false, so a cell that fails it is no use
                if (!MeditationUtility.SafeEnvironmentalConditions(p, c, map)) continue;
                if (++reach > MaxReachChecks) break;
                if (!p.CanReserveAndReach(c, PathEndMode.OnCell, Danger.Some)) continue;

                LocalTargetInfo focus = LocalTargetInfo.Invalid;
                try
                {
                    LocalTargetInfo f = MeditationUtility.BestFocusAt(c, p);
                    if (f.IsValid && f.Thing != null && f.Thing.GetStatValueForPawn(StatDefOf.MeditationFocusStrength, p) > 0f) focus = f;
                }
                catch (Exception) { /* no focus is fine: vanilla meditates on a bare cell too */ }

                Job job = JobMaker.MakeJob(JobDefOf.Meditate, c, LocalTargetInfo.Invalid, focus);
                job.ignoreJoyTimeAssignment = !forJoy;
                return job;
            }
            return null;
        }

        /// <summary>Every vanilla gate on meditation, as one line.</summary>
        public static string Gates(Pawn p, out bool canMeditateNow)
        {
            var sb = new StringBuilder();
            canMeditateNow = false;
            try
            {
                canMeditateNow = MeditationUtility.CanMeditateNow(p);
                sb.Append("CanMeditateNow=").Append(canMeditateNow);
                sb.Append(", schedule=").Append(p.timetable?.CurrentAssignment?.defName ?? "none");
                Need_Rest rest = p.needs?.rest;
                sb.Append(", rest=").Append(rest == null ? "n/a" : rest.CurCategory + " (" + rest.CurLevelPercentage.ToString("0.00") + ")");
                Need_Food food = p.needs?.food;
                sb.Append(", starving=").Append(food != null && food.Starving);
                sb.Append(", downed=").Append(p.Downed).Append(", awake=").Append(p.Awake());
                sb.Append(", bleed=").Append(p.health.hediffSet.BleedRateTotal.ToString("0.###"));
                sb.Append(", needsTendUrgent=").Append(HealthAIUtility.ShouldSeekMedicalRestUrgent(p));
                sb.Append(", seeksMedicalRest=").Append(HealthAIUtility.ShouldSeekMedicalRest(p));
                sb.Append(", dangerHere=").Append(p.Position.GetDangerFor(p, p.Map));
                sb.Append(", psylink=").Append(p.HasPsylink);
                Pawn_PsychicEntropyTracker pe = p.psychicEntropy;
                if (pe != null) sb.Append(", psyfocus=").Append(pe.CurrentPsyfocus.ToString("0.00")).Append("/target ").Append(pe.TargetPsyfocus.ToString("0.00"))
                                  .Append(", psySens=").Append(pe.PsychicSensitivity.ToString("0.00"));
                int spots = 0, usable = 0;
                foreach (Building b in p.Map.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.MeditationSpot))
                {
                    spots++;
                    if (MeditationUtility.IsValidMeditationBuildingForPawn(b, p)) usable++;
                }
                sb.Append(", meditationSpots=").Append(usable).Append("/").Append(spots).Append(" usable");
                Hediff_ArcaneCore c = Mages.Core(p);
                if (c != null) sb.Append(", arcana=").Append(c.arcana.ToString("0")).Append("/").Append(c.Capacity.ToString("0")).Append(" (mark ").Append(c.target.ToString("0.00")).Append(")");
            }
            catch (Exception e)
            {
                sb.Append(" [gate report failed: ").Append(e.GetType().Name).Append("]");
            }
            return sb.ToString();
        }

        [LudeonTK.DebugAction("Magic", "Hotfix: why won't she meditate (log)", actionType = LudeonTK.DebugActionType.ToolMapForPawns, allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void Why(Pawn p)
        {
            string g = Gates(p, out bool ok);
            Job j = ok && p.Spawned && p.Map != null ? Fallback(p, false) : null;
            string verdict = j != null ? "a fallback cell exists: " + j.targetA.Cell : ok ? "NO safe reachable cell within " + SearchRadius + " cells" : "vanilla forbids meditation right now (see gates)";
            Log.Message("[Avir hotfix] " + p.LabelShort + ": " + verdict + " | " + g);
            Messages.Message(p.LabelShort + ": " + verdict, MessageTypeDefOf.NeutralEvent, false);
        }
    }

    /// <summary>The vanilla node that sends a pawn to meditate on the "Meditate" schedule (and on "Anything" under her psyfocus target).</summary>
    [HarmonyPatch(typeof(JobGiver_Meditate), "TryGiveJob")]
    public static class Patch_CasterMeditate_JobGiver
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result != null) return;
            try { __result = CasterMeditation.Rescue(pawn, false); }
            catch (Exception e) { Log.WarningOnce("[Avir hotfix] caster meditation rescue failed: " + e, 0x4A51F1); }
        }
    }

    /// <summary>The mod's own free-time meditation for casters.</summary>
    [HarmonyPatch(typeof(JoyGiver_ArcaneMeditate), nameof(JoyGiver_ArcaneMeditate.TryGiveJob))]
    public static class Patch_CasterMeditate_JoyGiver
    {
        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result != null) return;
            try { __result = CasterMeditation.Rescue(pawn, true); }
            catch (Exception e) { Log.WarningOnce("[Avir hotfix] caster joy-meditation rescue failed: " + e, 0x4A51F2); }
        }
    }
}
