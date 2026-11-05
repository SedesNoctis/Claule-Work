using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// "PREEN X" IN THE RIGHT-CLICK MENU.
    ///
    /// WRITTEN AGAINST THE REAL API, FINALLY. 1.6 replaced the old
    /// FloatMenuMakerMap.AddHumanlikeOrders hook with provider classes, and
    /// FloatMenuMakerMap.Init builds its own list of every subclass - so this needs no Harmony
    /// patch at all, it simply has to exist.
    ///
    /// The signatures below are read out of Assembly-CSharp's metadata, not remembered: the base
    /// declares Drafted, Undrafted and Multiselect abstract, CanSelfTarget virtual, and
    /// GetSingleOptionFor(Pawn, FloatMenuContext) as the pawn-targeted entry point. That last one
    /// is the whole reason this file replaces the gizmo as the primary way to order grooming -
    /// Maximus asked for it on the pawn you click, not on the pawn you have selected.
    ///
    /// ORDERED, SO IT IGNORES LEISURE. Automatic grooming is recreation, which means an Avir will
    /// not walk off a construction job to preen a mate. That restraint is right for a pastime and
    /// wrong for an instruction. Everything else still applies: featherbroken refuses to be touched
    /// by anyone, and a coat with nothing in it produces no option at all rather than a greyed-out
    /// one - the option's absence is a real answer to "is there anything to preen here".
    /// </summary>
    public class FloatMenuOptionProvider_AvirPreen : FloatMenuOptionProvider
    {
        // Drafted pawns are being positioned by the player; grooming is not a combat order.
        protected override bool Drafted => false;

        protected override bool Undrafted => true;

        // One groomer at a time. A pile of Avir all preening the same pawn is not a thing.
        protected override bool Multiselect => false;

        // "Preen yourself" is a legitimate order, so the clicked pawn may be the selected one.
        protected override bool CanSelfTarget => true;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            try
            {
                Pawn actor = context?.FirstSelectedPawn;
                if (actor == null || clickedPawn == null) return null;
                if (!AvirFaction.HasFearborn(actor)) return null;
                if (!AvirGrooming.CanGroom(actor)) return null;

                if (clickedPawn == actor)
                {
                    if (!AvirGrooming.CanGroomSelf(actor)) return null;

                    return new FloatMenuOption("Preen own coat", delegate
                    {
                        Job job = JobMaker.MakeJob(AvirGroomingDefOf.Avir_GroomSelfJob, actor);
                        actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    });
                }

                if (!AvirGrooming.IsOrderableTarget(actor, clickedPawn)) return null;

                if (!actor.CanReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
                {
                    return new FloatMenuOption("Cannot preen " + clickedPawn.LabelShort
                        + " (no path)", null);
                }

                return new FloatMenuOption("Preen " + clickedPawn.LabelShort, delegate
                {
                    Job job = JobMaker.MakeJob(AvirGroomingDefOf.Avir_GroomOtherJob, clickedPawn);
                    actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                });
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir grooming] preen menu option failed: " + e, 0x4A5272);
                return null;
            }
        }
    }
}
