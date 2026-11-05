using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// A memory thought tied to one specific worn garment. sourceApparel is used to (a) plug the
    /// garment's name into the stage description's "{0}" and (b) keep separate garments from
    /// merging into a single stacked line - two different awful shirts should read as two
    /// separate complaints, not "Awful clothing x2".
    /// </summary>
    public class Thought_PridefulApparel : Thought_Memory
    {
        public Apparel sourceApparel;

        public override string Description
        {
            get
            {
                string baseDescription = base.Description;
                if (sourceApparel == null || string.IsNullOrEmpty(baseDescription))
                {
                    return baseDescription;
                }
                return string.Format(baseDescription, sourceApparel.LabelCapNoCount);
            }
        }

        public override bool GroupsWith(Thought other)
        {
            Thought_PridefulApparel otherPrideful = other as Thought_PridefulApparel;
            return otherPrideful != null
                && otherPrideful.def == def
                && otherPrideful.CurStageIndex == CurStageIndex
                && otherPrideful.sourceApparel == sourceApparel;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref sourceApparel, "sourceApparel");
        }
    }
}
