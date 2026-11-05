using Avir;
using Kharassian;
using Verse;

namespace AvirKharassianCompat
{
    /// <summary>
    /// An Avir is not held by chains. One the player enslaves anyway is UNTAMEABLE to the
    /// Kharassian slavery system (KharassianSlaveHooks): joins every rising, starts them four
    /// times as often, ignores the odds and every cooldown, and never breaks. Whether its rising
    /// is a fight or a flight is left to the usual rules - an Avir is fast enough simply to fly.
    /// Only an Instrument holds one.
    ///
    /// "An Avir" is AvirFaction.ShouldUseAvirContent: the Fearborn gene or an Avir_ pawn kind.
    ///
    /// That no trader SELLS an Avir lives in the Avir mod itself (Patch_AvirNeverSoldAsSlaves),
    /// so it holds with or without the Kharassians.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirUntameable
    {
        public static bool IsAvir(Pawn pawn)
        {
            return pawn != null && AvirFaction.ShouldUseAvirContent(pawn);
        }

        static AvirUntameable()
        {
            KharassianSlaveHooks.UntameableChecks.Add(IsAvir);
        }
    }
}
