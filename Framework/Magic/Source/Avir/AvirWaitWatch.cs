using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>Colonists left standing in a bare "Wait" (a wait no think node and no work giver asked for) while there is
    /// work to do (the author's report, Nov 2026: pawns without flight stand idle for long stretches). The watch notes who
    /// started each such wait (a short stack, for the log) and, if a colonist stands in one for more than ten seconds while
    /// the think tree would give them something to do, ends it so they go on - and logs the culprit once in a while.</summary>
    public static class AvirWaitWatch
    {
        private class Seen { public int since; public string origin; }
        private static readonly Dictionary<int, Seen> waits = new Dictionary<int, Seen>();
        private static readonly Dictionary<int, int> lastLog = new Dictionary<int, int>();

        public static bool Bare(Job j) => j != null && j.def == JobDefOf.Wait && j.jobGiver == null && j.workGiverDef == null && !j.playerForced;

        private static bool Concerned(Pawn p) => p != null && p.Spawned && p.IsColonist && !p.Drafted && !p.Downed && !p.InMentalState && !p.Dead;

        public static void Noted(Pawn p, Job j)
        {
            if (!Bare(j) || !Concerned(p)) return;
            // Fix254: the stack is taken only in developer mode - it is costly, and this runs on every bare wait
            // every colonist starts; the letting-go itself (Tick) needs only the time.
            if (!Prefs.DevMode) { waits[p.thingIDNumber] = new Seen { since = Find.TickManager.TicksGame, origin = "(not recorded: developer mode was off)" }; return; }
            string st = new System.Diagnostics.StackTrace(2, false).ToString();
            var lines = st.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.Contains("Harmony") && !l.Contains("DMD<")).Take(8);
            waits[p.thingIDNumber] = new Seen { since = Find.TickManager.TicksGame, origin = string.Join(" <- ", lines) };
        }

        public static string Origin(Pawn p) => waits.TryGetValue(p.thingIDNumber, out Seen s) ? s.origin : null;

        public static void Tick(int now)
        {
            if (now % 250 != 0) return;
            foreach (Map m in Find.Maps)
                foreach (Pawn p in m.mapPawns.FreeColonistsSpawned.ToList())
                {
                    if (!Concerned(p) || !Bare(p.CurJob)) { waits.Remove(p.thingIDNumber); continue; }
                    if (!waits.TryGetValue(p.thingIDNumber, out Seen s)) { waits[p.thingIDNumber] = s = new Seen { since = now, origin = "(started before the watch)" }; continue; }
                    if (now - s.since < 600) continue;
                    ThinkResult r;
                    try { r = p.thinker.MainThinkNodeRoot.TryIssueJobPackage(p, default(JobIssueParams)); } catch { continue; }
                    if (r.Job == null || r.Job.def == JobDefOf.Wait) continue;
                    if (!lastLog.TryGetValue(p.thingIDNumber, out int ll) || now - ll > GenDate.TicksPerDay)
                    {
                        lastLog[p.thingIDNumber] = now;
                        Log.Warning("[Avir idle] " + p.LabelShort + " stood in a bare Wait for " + (now - s.since) + " ticks while there was " + r.Job.def.defName
                            + " to do - let go. The wait was started by: " + s.origin);
                    }
                    waits.Remove(p.thingIDNumber);
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_AvirWaitWatch
    {
        public static void Prefix(Job newJob, Pawn ___pawn)
        {
            try { AvirWaitWatch.Noted(___pawn, newJob); } catch { }
        }
    }

    public class GameComponent_AvirWaitWatch : GameComponent
    {
        public GameComponent_AvirWaitWatch(Game g) { }
        public override void GameComponentTick() => AvirWaitWatch.Tick(Find.TickManager.TicksGame);
    }
}
