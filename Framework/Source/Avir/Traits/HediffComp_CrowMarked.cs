using Verse;

namespace Avir.Traits
{
    public class HediffCompProperties_CrowMarked : HediffCompProperties
    {
        public HediffCompProperties_CrowMarked()
        {
            this.compClass = typeof(HediffComp_CrowMarked);
        }
    }

    public class HediffComp_CrowMarked : HediffComp
    {
        public Pawn markedCrow;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_References.Look(ref this.markedCrow, "markedCrow");
        }
    }
}
