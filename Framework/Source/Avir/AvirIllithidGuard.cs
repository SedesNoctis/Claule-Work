using Verse;

namespace Avir
{
    /// <summary>
    /// AN ILLITHID HAS NOTHING TO TAKE.
    ///
    /// Whatever an illithid runs on, it is not the thing Avir drink. Their own feeding works the
    /// other way round - they eat minds - and the two systems must not become a loop where each
    /// species farms the other.
    ///
    /// Resolved BY NAME, every call, with no DefOf and no assembly reference. The illithid mod is
    /// optional: without it the lookup returns null, IsIllithid is always false, and every guard
    /// below is a no-op. Avir does not gain a dependency for this.
    ///
    /// The def is cached after the first successful lookup - GetNamedSilentFail walks a dictionary
    /// and this sits inside VictimValue, which the essence reaver calls on every melee kill.
    /// </summary>
    public static class AvirIllithidGuard
    {
        private const string MindflayerGeneName = "Illithid_Mindflayer";

        private static GeneDef cached;
        private static bool looked;

        private static GeneDef MindflayerGene
        {
            get
            {
                if (!looked)
                {
                    looked = true;
                    cached = DefDatabase<GeneDef>.GetNamedSilentFail(MindflayerGeneName);
                }
                return cached;
            }
        }

        /// <summary>
        /// True when this pawn carries the mindflayer gene. Checked on the ACTIVE gene rather than
        /// on the xenotype, so an illithid whose gene was implanted or stripped is judged by what
        /// it currently is, not by what it was labelled.
        /// </summary>
        public static bool IsIllithid(Pawn pawn)
        {
            GeneDef def = MindflayerGene;
            if (def == null || pawn == null || pawn.genes == null) return false;
            return pawn.genes.HasActiveGene(def);
        }
    }
}
