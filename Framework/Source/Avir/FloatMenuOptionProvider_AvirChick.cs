using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>Right-click a chick with a grown Avir selected: take it on the back (or set it down if it rides this one).</summary>
    public class FloatMenuOptionProvider_AvirChick : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            try
            {
                Pawn actor = context?.FirstSelectedPawn;
                if (actor == null || clickedPawn == null || clickedPawn == actor) return null;
                if (!AvirChicks.IsGrownAvir(actor) || !AvirChicks.IsChick(clickedPawn)) return null;
                if (AvirChicks.CarrierOf.TryGetValue(clickedPawn, out Pawn carrier) && carrier == actor)
                    return new FloatMenuOption("Set " + clickedPawn.LabelShort + " down", () => AvirChicks.SetDownByHand(clickedPawn));
                string why = AvirChicks.CannotCarry(actor, clickedPawn);
                if (why != null) return new FloatMenuOption("Cannot take " + clickedPawn.LabelShort + " on the back: " + why, null);
                if (!actor.CanReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
                    return new FloatMenuOption("Cannot take " + clickedPawn.LabelShort + " on the back: no path", null);
                JobDef take = DefDatabase<JobDef>.GetNamedSilentFail("Avir_TakeChick");
                if (take == null) return null;
                return new FloatMenuOption("Take " + clickedPawn.LabelShort + " on the back", () =>
                    actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(take, clickedPawn), JobTag.Misc));
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] chick menu option failed: " + e, 0x4A5273);
                return null;
            }
        }
    }
}
