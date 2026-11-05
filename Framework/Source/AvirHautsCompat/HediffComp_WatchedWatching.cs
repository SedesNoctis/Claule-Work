using Verse;

namespace AvirHautsCompat
{
    public class HediffCompProperties_WatchedOver : HediffCompProperties
    {
        public HediffCompProperties_WatchedOver()
        {
            this.compClass = typeof(HediffComp_WatchedOver);
        }
    }

    /// <summary>On the bonded human. Holds who's watching them and who cast the bond.</summary>
    public class HediffComp_WatchedOver : HediffComp
    {
        public Pawn crow;
        public Pawn crowWatcher;

        // Same risk category as CompDescriptionExtra/Salvation's countdown: if this fails to
        // compile (CS0115), delete this property override - nothing else depends on it.
        public override string CompTipStringExtra =>
            this.crow != null ? "Watched over by: " + this.crow.LabelShortCap : null;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_References.Look(ref this.crow, "watchingCrow");
            Scribe_References.Look(ref this.crowWatcher, "crowWatcher");
        }
    }

    public class HediffCompProperties_WatchingOver : HediffCompProperties
    {
        public HediffCompProperties_WatchingOver()
        {
            this.compClass = typeof(HediffComp_WatchingOver);
        }
    }

    /// <summary>On the crow. Holds who it's watching and who cast the bond.</summary>
    public class HediffComp_WatchingOver : HediffComp
    {
        public Pawn watchedPawn;
        public Pawn crowWatcher;

        public override string CompTipStringExtra =>
            this.watchedPawn != null ? "Watching over: " + this.watchedPawn.LabelShortCap : null;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_References.Look(ref this.watchedPawn, "watchedPawn");
            Scribe_References.Look(ref this.crowWatcher, "crowWatcher");
        }
    }
}
