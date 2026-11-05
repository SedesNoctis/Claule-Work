using System.Collections.Generic;
using Verse;

namespace Avir.Traits
{
    public class HediffCompProperties_CrowWatcherBonds : HediffCompProperties
    {
        public HediffCompProperties_CrowWatcherBonds()
        {
            this.compClass = typeof(HediffComp_CrowWatcherBonds);
        }
    }

    /// <summary>
    /// Ordered so index 0 is always the first-ever bond - that's where the Crow Watcher
    /// themselves reappears if they die with active bonds (see Patch_CrowDeath_Effects).
    /// </summary>
    public class HediffComp_CrowWatcherBonds : HediffComp
    {
        private List<Pawn> bondedCrows = new List<Pawn>();

        public IReadOnlyList<Pawn> BondedCrows => this.bondedCrows;
        public int Count => this.bondedCrows.Count;
        public Pawn FirstCrow => this.bondedCrows.Count > 0 ? this.bondedCrows[0] : null;

        public bool TryAddCrow(Pawn crow, int maxBonds)
        {
            if (crow == null || this.bondedCrows.Contains(crow)) return false;
            if (this.bondedCrows.Count >= maxBonds) return false;
            this.bondedCrows.Add(crow);
            return true;
        }

        public void RemoveCrow(Pawn crow)
        {
            this.bondedCrows.Remove(crow);

            // No more active bonds - this hediff has nothing left to track, remove it.
            if (this.bondedCrows.Count == 0 && this.parent?.pawn?.health != null)
            {
                this.parent.pawn.health.RemoveHediff(this.parent);
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Collections.Look(ref this.bondedCrows, "bondedCrows", LookMode.Reference);
            if (this.bondedCrows == null) this.bondedCrows = new List<Pawn>();
        }
    }
}
