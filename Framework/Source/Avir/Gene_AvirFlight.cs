using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace FlightGene
{
    /// <summary>
    /// THE FLIGHT GENE, NOW WITH A PERMISSION ON IT.
    ///
    /// Flight_Gene had no class of its own and did not need one until the pawn started making
    /// decisions about when to fly. The permission lives here rather than in a side table because
    /// this is where the mod already keeps per-pawn switches - AvirArcaneHunger carries its two
    /// feeding flags exactly this way - so it saves, loads, and follows the pawn without any
    /// separate bookkeeping.
    ///
    /// ONE SWITCH, TWO POSITIONS, AND IT ONLY GOVERNS TAKEOFF.
    ///
    /// Landing is deliberately NOT under it. A pawn that has finished travelling and is standing at
    /// a workbench has no reason to be hovering, and that is true whether or not the player wanted
    /// automatic flight - so idle landing happens regardless. Rescue from inside stone is likewise
    /// exempt: gating THAT behind a switch would mean a pawn with the switch off stays in the rock
    /// forever.
    ///
    /// So the switch answers exactly one question: may this Avir decide by itself to leave the
    /// ground when a journey would be easier in the air?
    ///
    /// ---------------------------------------------------------------------------------------
    /// WHY GetGizmos IS NOT AN ITERATOR ANY MORE - DO NOT PUT THE yield BACK.
    ///
    /// Verse.Pawn.GetGizmos is itself an iterator, and the caller runs the whole chain through
    /// System.Linq.ToList. That means every gene's gizmos are pulled inside one enumeration, and
    /// a single exception thrown from any MoveNext aborts the ENTIRE list - the pawn loses draft,
    /// abilities and every other button at once, while still being a perfectly healthy colonist.
    /// A drafted pawn stays drafted and cannot be undrafted, because the undraft button is part of
    /// the list that never got built.
    ///
    /// C# forbids try/catch around a yield return, so an iterator cannot defend itself. This
    /// method therefore builds a plain List and returns it, with each independent section wrapped
    /// separately. A failure now costs at most one button instead of the whole interface.
    /// ---------------------------------------------------------------------------------------
    /// </summary>
    public class Gene_AvirFlight : Gene
    {
        /// <summary>
        /// Default ON. The whole point of the feature is to remove the constant manual toggling,
        /// so a pawn that arrives with the gene should already behave sensibly; a player who wants
        /// hand control switches it off once per pawn rather than being made to switch it on for
        /// every Avir they will ever recruit.
        /// </summary>
        public bool autoFlightAllowed = true;

        // Each failure reports itself once per session and then goes quiet, so a gizmo that throws
        // every frame does not bury the log. The two flags are separate because the whole point of
        // splitting the sections is to tell WHICH half is at fault.
        private static bool loggedBaseFailure;
        private static bool loggedToggleFailure;

        // Resolved on first successful draw. ContentFinder returns null rather than throwing when
        // the path is missing, and a Command with a null icon is drawn by vanilla without
        // complaint, so a miss here is survivable - but it is still worth saying out loud once.
        private static bool iconResolved;
        private static Texture2D toggleIcon;

        public override void PostAdd()
        {
            base.PostAdd();
            AvirFlightCarriers.Add(pawn);
            Avir.AvirWingParts.Ensure(pawn);   // the wings grow back onto the body
        }

        public override void PostRemove()
        {
            base.PostRemove();
            AvirFlightCarriers.Remove(pawn);
            Avir.AvirWingParts.Ensure(pawn);   // no gene, no wings
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref autoFlightAllowed, "avirAutoFlightAllowed", true);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            List<Gizmo> gizmos = new List<Gizmo>();

            // ---- SECTION 1: whatever vanilla's Gene base class wants to contribute ----
            // Isolated on its own because a throw from here is not this mod's logic failing, it is
            // vanilla or another mod's patch on Gene.GetGizmos, and the distinction matters when
            // reading the report.
            try
            {
                IEnumerable<Gizmo> baseGizmos = base.GetGizmos();
                if (baseGizmos != null)
                {
                    foreach (Gizmo g in baseGizmos)
                    {
                        if (g != null) gizmos.Add(g);
                    }
                }
            }
            catch (Exception e)
            {
                if (!loggedBaseFailure)
                {
                    loggedBaseFailure = true;
                    Log.Error("[Avir] Gene_AvirFlight: base.GetGizmos() threw on "
                        + DescribePawn() + ". Base gizmos dropped, pawn interface preserved. " + e);
                }
            }

            // The auto-flight switch is no longer a gizmo: it is a column in the Assign tab (AvirColumns.cs).

            return gizmos;
        }

        /// <summary>
        /// Deliberately paranoid: this runs inside a catch block that exists precisely because
        /// something nearby was null, so it must not become the second exception.
        /// </summary>
        private string DescribePawn()
        {
            try
            {
                if (pawn == null) return "a null pawn";
                return pawn.LabelShortCap + " (" + pawn.ThingID + ")";
            }
            catch
            {
                return "an unreadable pawn";
            }
        }

        /// <summary>
        /// Convenience lookup. Returns true when the pawn has no flight gene of this class at all,
        /// so callers that ask about a non-Avir get "no objection" rather than a special case -
        /// they will have already failed the gene test anyway.
        /// </summary>
        public static bool AutoAllowed(Pawn pawn)
        {
            if (pawn?.genes == null) return true;

            List<Gene> genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                Gene_AvirFlight g = genes[i] as Gene_AvirFlight;
                if (g != null && g.Active) return g.autoFlightAllowed;
            }

            return true;
        }
    }
}
