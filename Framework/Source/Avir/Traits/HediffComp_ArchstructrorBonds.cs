using System.Collections.Generic;
using Verse;

namespace Avir.Traits
{
    public class HediffCompProperties_ArchstructrorBonds : HediffCompProperties
    {
        public HediffCompProperties_ArchstructrorBonds()
        {
            this.compClass = typeof(HediffComp_ArchstructrorBonds);
        }
    }

    /// <summary>
    /// Lives on the Architect. Same shape as HediffComp_CrowWatcherBonds, cap 7 instead of 3
    /// per the spec ("1 mark for each mechanoid... up to 7").
    /// </summary>
    public class HediffComp_ArchstructrorBonds : HediffComp
    {
        private List<Pawn> bondedMechanoids = new List<Pawn>();

        public IReadOnlyList<Pawn> BondedMechanoids => this.bondedMechanoids;
        public int Count => this.bondedMechanoids.Count;

        public bool TryAddMechanoid(Pawn mechanoid, int maxBonds)
        {
            if (mechanoid == null || this.bondedMechanoids.Contains(mechanoid)) return false;
            if (this.bondedMechanoids.Count >= maxBonds) return false;
            this.bondedMechanoids.Add(mechanoid);
            return true;
        }

        public void RemoveMechanoid(Pawn mechanoid)
        {
            if (mechanoid != null) this.bondedMechanoids.Remove(mechanoid);

            // No more active bonds - nothing left to track, remove it.
            if (this.bondedMechanoids.Count == 0 && this.parent?.pawn?.health != null)
            {
                this.parent.pawn.health.RemoveHediff(this.parent);
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Collections.Look(ref this.bondedMechanoids, "bondedMechanoids", LookMode.Reference);
            if (this.bondedMechanoids == null) this.bondedMechanoids = new List<Pawn>();
        }
    }
}
