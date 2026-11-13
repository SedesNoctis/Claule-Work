using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace FlightGene
{
    /// <summary>
    /// THE ONLY PLACE A PAWN IS TAKEN OUT OF SOMEWHERE IT MAY NOT BE.
    ///
    /// There were two of these, written weeks apart, both teleporting, with different search radii
    /// and different tick rates - Patch_AvirPhasingGate.EvictIfStranded and the sweep inside
    /// GameComponent_AvirFlightControl. Neither had ever run, so they had never fought each other.
    /// Fixing the gene cache would have woken both at once onto the same pawn.
    ///
    /// TWO OUTCOMES, DECIDED BY WHETHER THE PAWN CAN FLY ITSELF OUT.
    ///
    ///   conscious   -> flight is forced on and it flies out under its own power. No teleport, no
    ///                  injury. The player cannot call it off; the escape is re-issued every time
    ///                  this is called until the pawn is somewhere legal.
    ///
    ///   unconscious -> nobody is at the controls. It is placed at the nearest open sky and takes
    ///   or dead       bruises scaled by how far it had to be moved, which is how deep in the rock
    ///                  it was. Announced to the player.
    ///
    /// The split is by state, not by distance. Distance is random - the same situation injures or
    /// does not depending on where the hillside happens to end - and the player would read that as
    /// noise. "Unconscious inside stone means you fell" is a sentence; "further than 35 cells means
    /// you fell" is not.
    /// </summary>
    public static class AvirFlightRescue
    {
        /// <summary>How far to look for somewhere to put a fallen pawn down.</summary>
        private const float TeleportSearchRadius = 60f;

        /// <summary>How far to look for somewhere a flying pawn can land by itself.</summary>
        private const float FlySearchRadius = 40f;

        /// <summary>
        /// Distance at which fall damage reaches its ceiling. Maximus's original figure for the
        /// threshold at which a pawn should simply DIE - deliberately not lethal yet.
        ///
        /// Everything in this file is running for the first time; the rule that decides what counts
        /// as an illegal cell has never once executed in a live game. If it is even slightly wider
        /// than intended, a lethal version would kill colonists before there was anything to read
        /// in the log. Heavy bruises are reversible and say the same thing. Once play confirms this
        /// only ever fires where it should, this becomes the lethal threshold - one constant.
        /// </summary>
        private const float FullDamageDistance = 35f;

        private const float MinFallSeverity = 3f;
        private const float MaxFallSeverity = 11f;
        private const int MaxInjuredParts = 4;

        private static HediffDef bruiseDef;
        private static bool bruiseLookedUp;

        /// <summary>
        /// Bruise is resolved from the database rather than named off HediffDefOf. HediffDefOf in
        /// this game version does not carry every field one would expect - Burn is absent from it,
        /// which cost a compile round earlier in this mod - so nothing here trusts that class.
        /// </summary>
        private static HediffDef BruiseDef
        {
            get
            {
                if (!bruiseLookedUp)
                {
                    bruiseLookedUp = true;
                    bruiseDef = DefDatabase<HediffDef>.GetNamedSilentFail("Bruise");
                    if (bruiseDef == null)
                    {
                        Log.Warning("[Avir] Hediff 'Bruise' not found - fallen Avir will be moved "
                            + "without injury.");
                    }
                }

                return bruiseDef;
            }
        }

        private static bool heartbeatLogged;

        /// <summary>
        /// One line, the first time any periodic hook reaches this code with a flight-carrier in
        /// hand. FLIGHT_STATE.md recorded that the periodic hooks "never run" - the evidence was a
        /// log line that in fact sat behind three conditions including the broken gene cache, so
        /// silence proved nothing. This one sits at the top of the shared entry point, so if it
        /// never appears the conclusion is finally safe to draw.
        /// </summary>
        public static void NoteHookAlive(string hookName)
        {
            if (heartbeatLogged) return;
            heartbeatLogged = true;
            Log.Message("[Avir flight] periodic hook alive: " + hookName);
        }

        /// <summary>
        /// Called from every hook that might notice a stranded pawn. Cheap and idempotent: a pawn
        /// already flying itself out simply has its escape order refreshed.
        /// </summary>
        public static void CheckAndRescue(Pawn pawn)
        {
            try
            {
                if (pawn == null || !pawn.Spawned) return;

                Map map = pawn.Map;
                if (map == null) return;

                IntVec3 at = pawn.Position;
                if (!AvirFlightRules.CannotStandAt(map, at))
                {
                    stationarySince.Remove(pawn.thingIDNumber);
                    return;
                }

                // A pawn being CARRIED is its carrier's problem, not its own. Rescuing it here
                // would drop it out of the carrier's arms mid-corridor.
                if (pawn.ParentHolder is Pawn_CarryTracker) return;

                // PHASING OF ITS OWN (Fix259, the author): a wings carrier that walks through stone by a living
                // shadow, an amulet or another power is not stuck - that power answers for where it ends up.
                if (!pawn.Dead && Patch_AvirPhasingGate.PhasesOtherwise(pawn)) { stationarySince.Remove(pawn.thingIDNumber); return; }

                // MOVING MEANS PASSING THROUGH, AND THAT IS THE WHOLE POINT OF FLIGHT.
                //
                // This guard existed in the code I replaced and I failed to carry it over, which
                // produced exactly what Maximus saw: a healthy Avir halfway across a wall suddenly
                // abandoning its order and flying back out. It was not stuck. It was in transit,
                // and I was treating transit as an emergency.
                //
                // Only a pawn that has STOPPED somewhere it cannot stand is a problem.
                if (pawn.pather != null && pawn.pather.Moving)
                {
                    stationarySince.Remove(pawn.thingIDNumber);
                    return;
                }

                // AND EVEN A STOPPED PAWN GETS A MOMENT. A path recalculation, a job handover or a
                // single frame of standing still inside a wall is ordinary; acting on the first
                // observation would interrupt legitimate crossings that merely paused. Two seconds
                // of genuinely standing still in stone is not something that happens by accident.
                //
                // The dead and the downed skip the wait: they are not going to start moving.
                int now = Find.TickManager?.TicksGame ?? 0;
                bool helpless = pawn.Dead || pawn.Downed;

                if (!helpless)
                {
                    int since;
                    if (!stationarySince.TryGetValue(pawn.thingIDNumber, out since))
                    {
                        stationarySince[pawn.thingIDNumber] = now;
                        return;
                    }

                    if (now - since < StationaryGraceTicks) return;
                }

                stationarySince.Remove(pawn.thingIDNumber);

                // DOWNED IS THE WHOLE TEST, AND IT IS ENOUGH.
                //
                // I first wrote a separate capacity check for "can this pawn move at all", to
                // catch a legless Avir that would fail the escape and stay in the rock forever.
                // It is redundant: a pawn whose moving capacity has collapsed is downed by the
                // engine's own rules, so it is already on the other branch. Dropping it also
                // removes a call I had not verified against this game's assembly, which is the
                // habit that cost this mod several compile rounds.
                bool canActForItself = !helpless && pawn.jobs != null;

                if (canActForItself) FlyOut(pawn, map, at);
                else PutDownAndInjure(pawn, map, at);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Flight rescue failed: " + e, 0x4A5240);
            }
        }

        /// <summary>
        /// The conscious case. Flight is switched on whether or not the pawn wanted it, and a Goto
        /// is issued to the nearest place it can stand.
        ///
        /// WHY THE ORDER IS RE-ISSUED RATHER THAN MADE UNCANCELLABLE. Marking a job as something
        /// the player may not interrupt means naming a field on Job that I have not verified
        /// against this game's assembly, and this mod has already paid for several such guesses.
        /// Re-issuing achieves the same visible result through StartJob with InterruptForced, which
        /// is proven to work in this file's neighbour: whatever the player orders instead, the next
        /// call here - at most a few seconds later - takes control back. The pawn cannot be made to
        /// stay in the wall.
        /// </summary>
        private static void FlyOut(Pawn pawn, Map map, IntVec3 at)
        {
            if (!AvirFlightUtility.IsFlying(pawn))
            {
                // Forced, so rest level is not consulted. An exhausted Avir inside stone still has
                // to get out; the alternative is standing in rock until it starves.
                AvirFlightUtility.TakeOff(pawn, automatic: true);
            }

            IntVec3 target = AvirFlightRules.NearestFooting(map, at, FlySearchRadius);
            if (!target.IsValid)
            {
                // Nowhere to land within range. Leave it airborne rather than teleporting a
                // conscious pawn - it keeps whatever route it had, and the next call tries again
                // from wherever it has drifted to.
                return;
            }

            // RATE LIMITER, NOT A STATE CHECK.
            //
            // A pawn that is actually on its way never reaches this method at all - CheckAndRescue
            // returns at the "moving means passing through" guard. So the only pawn arriving here
            // repeatedly is one whose escape route could not be built. Re-issuing that every 60
            // ticks is harmless but pointless; this spaces it out.
            int now = Find.TickManager?.TicksGame ?? 0;
            int issuedAt;
            if (escapeIssuedTick.TryGetValue(pawn.thingIDNumber, out issuedAt)
                && now - issuedAt < EscapeGraceTicks)
            {
                return;
            }

            if (pawn.jobs == null) return;
            escapeIssuedTick[pawn.thingIDNumber] = now;

            NoteRepeatRescue(pawn, at);

            Job job = JobMaker.MakeJob(JobDefOf.Goto, target);
            job.locomotionUrgency = LocomotionUrgency.Sprint;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);

            if (announcedEscape.Add(pawn.thingIDNumber)
                && PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                Messages.Message("Avir_FlightForcedEscape".Translate(pawn.LabelShort),
                    pawn, MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        /// <summary>
        /// The unconscious case. There is nobody to fly, so the pawn is put down at open sky and
        /// pays for the drop.
        /// </summary>
        private static void PutDownAndInjure(Pawn pawn, Map map, IntVec3 at)
        {
            IntVec3 landing = AvirFlightRules.NearestOpenSky(map, at, TeleportSearchRadius);

            // No open sky within range - deep under a mountain. Fall back to anywhere legal rather
            // than leaving a body inside the rock forever.
            if (!landing.IsValid) landing = AvirFlightRules.NearestFooting(map, at, TeleportSearchRadius);
            if (!landing.IsValid) return;

            float distance = at.DistanceTo(landing);

            pawn.Position = landing;
            pawn.Notify_Teleported(false, true);
            pawn.pather?.StopDead();

            if (pawn.Dead) return;

            int hurt = ApplyFallInjuries(pawn, distance);
            if (hurt <= 0) return;

            if (PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                Messages.Message("Avir_FlightFellInStone".Translate(pawn.LabelShort),
                    pawn, MessageTypeDefOf.NegativeEvent, historical: false);
            }
        }

        /// <summary>
        /// Bruises on the limbs, scaled by how far the pawn had to be moved - which is a stand-in
        /// for how deep in the rock it was, and therefore for how far it fell.
        ///
        /// SEVERITY IS A FLAT NUMBER, NOT A FRACTION OF PART HEALTH. Reading a body part's maximum
        /// health means calling into HediffSet, and I have not verified the shape of that call in
        /// this version. A ceiling of 11 is well under the health of any limb on the human body
        /// these pawns use, so no part can be destroyed and nothing here can kill. That is the
        /// intended behaviour today in any case: heavy, reversible, and loud enough to notice.
        /// </summary>
        private static int ApplyFallInjuries(Pawn pawn, float distance)
        {
            HediffDef bruise = BruiseDef;
            if (bruise == null || pawn.health?.hediffSet == null) return 0;

            float t = Mathf01(distance / FullDamageDistance);
            float severity = MinFallSeverity + (MaxFallSeverity - MinFallSeverity) * t;

            List<BodyPartRecord> limbs = LimbsOf(pawn);
            if (limbs.Count == 0) return 0;

            int applied = 0;
            for (int i = 0; i < limbs.Count && applied < MaxInjuredParts; i++)
            {
                BodyPartRecord part = limbs[i];

                Hediff h = HediffMaker.MakeHediff(bruise, pawn, part);
                h.Severity = severity * Rand.Range(0.7f, 1f);
                pawn.health.AddHediff(h, part);
                applied++;
            }

            return applied;
        }

        private static float Mathf01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        /// <summary>
        /// Outside limbs, shuffled so the same two parts are not hit every single time.
        ///
        /// The Avir use the vanilla human body - there is no BodyDef of their own anywhere in the
        /// mod's Defs - so the vanilla part names apply. The name list is nevertheless backed by a
        /// depth-based fallback, because a future custom body would silently produce no injuries at
        /// all otherwise, and silence is exactly the failure mode that cost the last session.
        /// </summary>
        private static List<BodyPartRecord> LimbsOf(Pawn pawn)
        {
            List<BodyPartRecord> named = new List<BodyPartRecord>();
            List<BodyPartRecord> fallback = new List<BodyPartRecord>();

            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (part?.def == null) continue;
                if (part.depth != BodyPartDepth.Outside) continue;

                string n = part.def.defName;

                if (n == "Leg" || n == "Foot" || n == "Arm" || n == "Hand" || n == "Shoulder")
                {
                    named.Add(part);
                    continue;
                }

                // Anything outside that is clearly not a limb stays out of the fallback, so a
                // custom body cannot produce bruised eyeballs.
                if (n == "Head" || n == "Neck" || n == "Torso" || n == "Skull" || n == "Eye"
                    || n == "Ear" || n == "Nose" || n == "Jaw")
                {
                    continue;
                }

                fallback.Add(part);
            }

            List<BodyPartRecord> chosen = named.Count > 0 ? named : fallback;

            // Written out rather than calling the list-shuffle extension: that extension is used
            // nowhere else in this mod, so it is one more name taken on trust. Rand.Range is used
            // throughout and is certain.
            for (int i = chosen.Count - 1; i > 0; i--)
            {
                int j = Rand.Range(0, i + 1);
                BodyPartRecord tmp = chosen[i];
                chosen[i] = chosen[j];
                chosen[j] = tmp;
            }

            return chosen;
        }

        private static readonly HashSet<int> announcedEscape = new HashSet<int>();

        /// <summary>
        /// THE RESCUE CANNOT WIN AN ARGUMENT WITH A WORK GIVER, AND SHOULD SAY SO.
        ///
        /// Pulling a pawn out of a wall does nothing about the reason it went in. If the job that
        /// put it there is still on offer, the giver hands it straight back and the pawn flies
        /// back in - out, in, out, in. That loop is not fixable from this file: the rescue is the
        /// symptom, and killing it would only leave the pawn working inside the stone quietly
        /// instead of noisily.
        ///
        /// The cure is Patch_AvirFlightArrival, which stops the pawn stopping there in the first
        /// place. This method exists for the case where that patch does not cover some path into
        /// the wall - an interaction cell, a job driver that positions the pawn itself - and its
        /// entire job is to NAME the culprit instead of leaving Maximus to guess from a screenshot.
        ///
        /// Fires once per pawn per outbreak, on the third rescue inside a game hour. One rescue is
        /// an accident; three is a loop.
        /// </summary>
        private static void NoteRepeatRescue(Pawn pawn, IntVec3 at)
        {
            try
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                int id = pawn.thingIDNumber;

                RescueStreak streak;
                if (!streaks.TryGetValue(id, out streak) || now - streak.lastTick > StreakWindowTicks)
                {
                    streak = new RescueStreak();
                    streaks[id] = streak;
                }

                streak.lastTick = now;
                streak.count++;

                if (streak.count != StreakReportAt) return;

                Job job = pawn.CurJob;
                string what = job == null
                    ? "no job"
                    : job.def?.defName + " -> " + job.targetA.ToString();

                Log.Warning("[Avir arrival] " + pawn.LabelShort + " has been pulled out of "
                    + at + " " + StreakReportAt + " times in a row and keeps going back. Current job: "
                    + what + ". The arrival guard did not cover this route into the wall - this job "
                    + "is positioning the pawn itself, or reaching the cell some other way.");
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] repeat-rescue bookkeeping failed: " + e, 0x4A5265);
            }
        }

        private class RescueStreak
        {
            public int count;
            public int lastTick;
        }

        /// <summary>A game hour. Rescues further apart than this are separate incidents, not a
        /// loop.</summary>
        private const int StreakWindowTicks = 2500;

        private const int StreakReportAt = 3;

        private static readonly Dictionary<int, RescueStreak> streaks = new Dictionary<int, RescueStreak>();

        /// <summary>How long an issued escape is left alone before being re-asserted. Long enough
        /// for a pawn to actually cross a wall, short enough that countermanding it buys the player
        /// only a couple of seconds.</summary>
        private const int EscapeGraceTicks = 180;

        private static readonly Dictionary<int, int> escapeIssuedTick = new Dictionary<int, int>();

        /// <summary>How long a pawn must stand STILL inside an illegal cell before anything is
        /// done about it. Two seconds. Anything shorter interrupts ordinary crossings that paused
        /// for a repath.</summary>
        private const int StationaryGraceTicks = 120;

        private static readonly Dictionary<int, int> stationarySince = new Dictionary<int, int>();
    }
}
