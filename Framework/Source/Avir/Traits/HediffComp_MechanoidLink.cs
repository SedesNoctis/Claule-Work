using Verse;

namespace Avir.Traits
{
    public class HediffCompProperties_MechanoidLink : HediffCompProperties
    {
        public HediffCompProperties_MechanoidLink()
        {
            this.compClass = typeof(HediffComp_MechanoidLink);
        }
    }

    /// <summary>On the bonded mechanoid. Holds who it's bound to and who cast the bond.</summary>
    public class HediffComp_MechanoidLink : HediffComp
    {
        public Pawn victim;
        public Pawn architect;

        public override string CompTipStringExtra =>
            this.victim != null ? "Soulmark-bound to: " + this.victim.LabelShortCap : null;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_References.Look(ref this.victim, "linkedVictim");
            Scribe_References.Look(ref this.architect, "linkArchitect");
        }
    }
}
