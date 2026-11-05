using RimWorld;
using Verse;

namespace Avir
{
    // The Avir's own switches, one column each in the Assign tab: no gizmos, the whole flock set at a glance.

    /// <summary>Automatic flight: takes off by itself when a journey is easier flown.</summary>
    public class PawnColumnWorker_AvirAutoFlight : PawnColumnWorker_Checkbox
    {
        private static FlightGene.Gene_AvirFlight Gene(Pawn p) => p?.genes?.GetFirstGeneOfType<FlightGene.Gene_AvirFlight>();
        protected override bool HasCheckbox(Pawn pawn) => Gene(pawn) != null;
        protected override bool GetValue(Pawn pawn) => Gene(pawn)?.autoFlightAllowed ?? false;
        protected override void SetValue(Pawn pawn, bool value, PawnTable table) { var g = Gene(pawn); if (g != null) g.autoFlightAllowed = value; }
        protected override string GetTip(Pawn pawn) => "Avir_AutoFlightToggleDesc".Translate();
    }

    /// <summary>Carries the family's chicks by itself: takes up the awake ones, puts the sleepy ones to bed.</summary>
    public class PawnColumnWorker_AvirCarryChicks : PawnColumnWorker_Checkbox
    {
        protected override bool HasCheckbox(Pawn pawn) => AvirChicks.IsGrownAvir(pawn);
        protected override bool GetValue(Pawn pawn) => !AvirChicks.AutoOff.Contains(pawn);
        protected override void SetValue(Pawn pawn, bool value, PawnTable table) { if (value) AvirChicks.AutoOff.Remove(pawn); else AvirChicks.AutoOff.Add(pawn); }
        protected override string GetTip(Pawn pawn) =>
            "On: when a chick of the family is awake, rested and fed, this Avir takes it on the back by itself, and carries it to its crib when it gets sleepy. "
            + "Not while drafted, asleep, under a direct order or under attack. A chick you set down is left alone for half a day.\n\nTake a chick by hand: select the Avir and right-click the chick.";
    }
}
