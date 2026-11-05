using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Throttled provider of "steps" (PsychicSensitivity / 0.15) for the Psychic Nature gene.
    ///
    /// StatPart_PsychicNature sits on ~14 stats, several of them hot (move speed, dodge, armour,
    /// carry, learning...), so this lookup runs on a huge number of stat reads. Two things keep
    /// it cheap:
    ///
    ///   1. NON-CARRIERS BAIL BEFORE ANY WORK. Animals have no gene tracker at all
    ///      (pawn.genes == null), so all wildlife returns instantly. Humanlikes that lack the
    ///      gene return after a single HasActiveGene test. Neither ever allocates a cache Box.
    ///
    ///   2. CARRIERS PAY THE EXPENSIVE PART RARELY. GetStatValue(PsychicSensitivity) - the only
    ///      costly step - is computed at most once per RefreshTicks per pawn and reused in
    ///      between. Everything downstream (the light check, the arithmetic) stays live and cheap.
    ///
    /// This replaces the old design where every read did TWO gene scans (HasGene, then Steps ->
    /// HasGene again) plus a cache traversal. TryGetSteps now does ONE gene scan and, for carriers
    /// only, one cache lookup.
    ///
    /// The cache uses a ConditionalWeakTable, so dead pawns are collected automatically - no
    /// bookkeeping, no leaks. A re-entrancy guard prevents a stack overflow in the rare case that
    /// computing PsychicSensitivity ends up asking for a stat that reads this cache.
    /// </summary>
    public static class PsychicNatureCache
    {
        // How long a computed reading is reused before recomputing. Psychic sensitivity for an
        // Avir only moves on slow inputs - apparel swaps, the arcane-hunger multiplier draining
        // across a day, a brain injury - so paying for GetStatValue(PsychicSensitivity) more than
        // a couple of times a game day is wasted work. 30000 ticks = half an in-game day (~12h);
        // raise toward 60000 (a full day) for even more headroom. The only visible cost of a
        // longer interval is that a freshly-equipped psychic item takes up to this long to show
        // in the derived stats (move speed, dodge, armour...). Lower it for snappier response.
        public const int RefreshTicks = 30000;

        private class Box
        {
            public int tick = int.MinValue;
            public float steps;
            public bool refreshing;
        }

        private static readonly ConditionalWeakTable<Pawn, Box> table = new ConditionalWeakTable<Pawn, Box>();

        // Allocation-free carrier test. Animals have no gene tracker and bail on the null check
        // before HasActiveGene is ever reached.
        private static bool CarriesGene(Pawn pawn)
        {
            return pawn != null
                   && pawn.genes != null
                   && pawn.genes.HasActiveGene(AvirDefOf.Avir_PsychicNature);
        }

        /// <summary>
        /// The single hot-path entry point. Returns false (and steps 0) for any pawn that does not
        /// carry the Psychic Nature gene, after nothing more than a gene test - and without ever
        /// allocating a cache Box for them. For carriers it returns true and the cached step
        /// count, recomputing the expensive PsychicSensitivity read at most once per RefreshTicks.
        /// </summary>
        public static bool TryGetSteps(Pawn pawn, out float steps)
        {
            steps = 0f;

            // Non-carriers (all animals, all non-Avir humanlikes) stop here. No Box is created.
            if (!CarriesGene(pawn))
            {
                return false;
            }

            Box box = table.GetValue(pawn, _ => new Box());
            int now = (Find.TickManager != null) ? Find.TickManager.TicksGame : 0;

            bool stale = box.tick == int.MinValue || (now - box.tick) >= RefreshTicks;
            if (stale && !box.refreshing)
            {
                box.refreshing = true;
                try
                {
                    box.steps = pawn.GetStatValue(StatDefOf.PsychicSensitivity) / StatPart_PsychicNature.StepSize;
                    box.tick = now;
                }
                finally
                {
                    box.refreshing = false;
                }
            }

            steps = box.steps;

            // Choir's passive: doubles the OUTPUT of every step for as long as the pawn is
            // actually in an active Choir, while the step SIZE itself (0.15) never changes -
            // mathematically identical to doubling perStep in StatPart_PsychicNature directly,
            // since every mode there (additive/factor/base-scaled) is linear in steps. Checked
            // fresh every call, not folded into the box above: Choir membership can change on a
            // ~1-second cadence (the aura's own scan interval), far faster than this cache's own
            // 30000-tick refresh, so a pawn stepping into or out of the choir needs to see the
            // doubling apply or drop immediately rather than waiting on the next scheduled
            // sensitivity re-read.
            if (GameComponent_AvirChoir.TryGetChoirFor(pawn, out _))
            {
                steps *= 2f;
            }

            return true;
        }

        // ---- Back-compat shims for existing call sites -------------------------------------

        public static bool HasGene(Pawn pawn)
        {
            return TryGetSteps(pawn, out _);
        }

        /// <summary>Cached steps of Psychic sensitivity for a carrier. Returns 0 for non-carriers.</summary>
        public static float Steps(Pawn pawn)
        {
            TryGetSteps(pawn, out float s);
            return s;
        }
    }
}
