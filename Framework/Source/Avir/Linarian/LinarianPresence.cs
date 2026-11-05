using Verse;

namespace Avir.Linarian
{
    /// <summary>The hidden Heralds on a map (the wild one and the Primal one): filled in by LinarianWild.cs.</summary>
    public static partial class LinarianPresence
    {
        public static void Tick(Map map, int now) => TickImpl(map, now);
        public static void OnRevealed(Pawn her, Thing by) => RevealedImpl(her, by);

        static partial void TickImplP(Map map, int now);
        private static void TickImpl(Map map, int now) { TickImplP(map, now); }
        static partial void RevealedImplP(Pawn her, Thing by);
        private static void RevealedImpl(Pawn her, Thing by) { RevealedImplP(her, by); }
    }
}
