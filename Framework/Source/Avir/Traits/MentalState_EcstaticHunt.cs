using Verse;
using Verse.AI;
using RimWorld;

namespace Avir.Traits
{
    /// <summary>
    /// A berserk-style mental state that will attack anything nearby EXCEPT pawns of its own
    /// faction (or faction-less allies, e.g. player-owned animals) - hostiles and neutral
    /// wildlife are both fair game, colony/faction-mates are not.
    ///
    /// Built on AttackTargetFinder.BestAttackTarget with a custom validator rather than
    /// subclassing MentalState_Berserk, since Berserk's own targeting hooks are internal/private
    /// in places that can shift between RimWorld versions. This re-acquires a target periodically
    /// instead of once, so it keeps hunting for the duration of the break rather than fixating on
    /// a single kill.
    ///
    /// NOTE: TryStartMentalState's exact parameter names/overload have moved around between
    /// RimWorld versions before - worth a quick compile-check against your installed version's
    /// decompiled Assembly-CSharp before trusting this blind.
    /// </summary>
    public class MentalState_EcstaticHunt : MentalState
    {
        private const int RetargetIntervalTicks = 240;
        private int nextRetargetTick;

        // RIMWORLD 1.6 SIGNATURE. This was MentalStateTick() with no parameters and it stopped
        // compiling:  "no suitable method found to override".
        //
        // 1.6 introduced variable tick rate (VTR) - systems can now run as slowly as 4Hz instead
        // of 60Hz, and every tick method takes a DELTA saying how many ticks have actually passed
        // since it last ran. Hauts' own code shows the same shape: TickInterval(int delta),
        // CompTickInterval(int delta). MentalState was changed the same way.
        //
        // The retarget schedule below is deliberately written against Find.TickManager.TicksGame
        // rather than accumulating delta, so it behaves identically whether the engine calls this
        // sixty times a second or four.
        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);

            if (this.pawn == null || this.pawn.Dead || !this.pawn.Spawned) return;

            if (Find.TickManager.TicksGame < this.nextRetargetTick) return;
            this.nextRetargetTick = Find.TickManager.TicksGame + RetargetIntervalTicks;

            Thing currentTarget = (this.pawn.CurJob != null && this.pawn.CurJob.def == JobDefOf.AttackMelee)
                ? this.pawn.CurJob.targetA.Thing
                : null;

            if (currentTarget != null && currentTarget is Pawn ct && !ct.Dead && ct.Spawned
                && ct.Faction != this.pawn.Faction)
            {
                // Already chasing a valid, still-hostile-to-us target - leave it be.
                return;
            }

            // The validator is Predicate<Thing>, not Predicate<IAttackTarget> - that is what
            // "cannot convert from 'method group' to 'System.Predicate<Verse.Thing>'" was saying.
            //
            // 'var' on the result is deliberate: BestAttackTarget hands back an IAttackTarget, and
            // every attack target in the game IS a Thing, so the 'as Thing' below gets us the Thing
            // without this file having to hard-code which of the two the method signature returns.
            var best = AttackTargetFinder.BestAttackTarget(
                this.pawn,
                TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat,
                validator: IsValidEcstaticHuntTarget);

            Thing target = best as Thing;

            if (target != null)
            {
                Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                job.maxNumMeleeAttacks = 1;
                job.expiryInterval = RetargetIntervalTicks;
                this.pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            }
        }

        private bool IsValidEcstaticHuntTarget(Thing t)
        {
            Pawn other = t as Pawn;
            if (other == null || other == this.pawn) return false;
            // Ignore allies: same faction as the caster, or a faction-less colony animal.
            if (other.Faction == this.pawn.Faction) return false;
            if (other.HostFaction != null && other.HostFaction == this.pawn.Faction) return false;
            return true;
        }
    }
}
