using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    public class HediffCompProperties_AvirRestlessness : HediffCompProperties
    {
        public int checkInterval = 250;       // same TickRare-ish cadence as HediffComp_ClawConditioning
        public float risePerCheck = 0.012f;    // away from own work: climbs to max in a bit under a day
        public float fallPerCheck = 0.020f;    // at own work: settles back down somewhat faster than it built

        public HediffCompProperties_AvirRestlessness()
        {
            compClass = typeof(HediffComp_AvirRestlessness);
        }
    }

    /// <summary>
    /// Lives on Avir_Restlessness, granted to a pawn the moment they gain Avir_HA_CompulsiveMaker
    /// (see Patch_AvirHighArtificierConditions.cs). Every checkInterval ticks, looks at whether
    /// the pawn is currently doing work in their own lane (AvirTirelessWork.IsOwnWork - the same
    /// Construction/Research/Art/Crafting check the tireless-work patch uses) and moves severity
    /// toward 0 if so, or up toward 1 if not. The matching mood is entirely handled by
    /// Avir_HA_RestlessnessMood, a ThoughtDef keyed to this hediff's own stages - this comp only
    /// ever touches severity, nothing else.
    ///
    /// Net effect: a Compulsive Maker left hauling, cleaning, or simply idle for long enough
    /// visibly sours, and settles back down once given something to build or study again. This
    /// is the actual behavior, not a stat number - the player has to keep feeding them their own
    /// work or watch the mood (and eventually the mental break odds) get worse for it.
    /// </summary>
    public class HediffComp_AvirRestlessness : HediffComp
    {
        public HediffCompProperties_AvirRestlessness Props => (HediffCompProperties_AvirRestlessness)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = parent.pawn;
            if (pawn == null || pawn.Dead) return;

            int interval = UnityEngine.Mathf.Max(1, Props.checkInterval);

            // STAGGERED PER PAWN, same fix as HediffComp_ClawConditioning - offsetting by
            // thingIDNumber instead of a global tick modulo spreads the work evenly across the
            // interval instead of every carrier checking on the same tick.
            if ((Find.TickManager.TicksGame + pawn.thingIDNumber) % interval != 0) return;

            Job job = pawn.CurJob;
            WorkTypeDef workType = job?.workGiverDef?.workType;

            if (AvirTirelessWork.IsOwnWork(workType))
            {
                severityAdjustment -= Props.fallPerCheck;
            }
            else
            {
                severityAdjustment += Props.risePerCheck;
            }
        }
    }
}
