using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// A starving Avir walks to the nearest stored Essence Extract and drinks it, restoring
    /// arcane hunger. Modelled on vanilla's JobGiver_GetHemogen so it behaves the way a
    /// sanguophage player already expects.
    ///
    /// Two things had to change on the item itself for this to be possible at all:
    ///
    ///   - preferability was DesperateOnly, which meant the only pawn who would ever pick it up
    ///     was one starving for FOOD, who would then waste it. It is now NeverForNutrition, the
    ///     same value hemogen packs use: nobody eats it as a meal, but an explicit Ingest job
    ///     like this one still works.
    ///   - nothing in the mod ever looked for it. That is this class.
    ///
    /// Gated behind Gene_ArcaneHunger.extractDrinkingAllowed, which defaults to FALSE. Extract is
    /// expensive and doubles as a building ingredient, so an Avir must be given permission before
    /// it starts drinking the stockpile.
    /// </summary>
    public class JobGiver_AvirGetEssenceExtract : ThinkNode_JobGiver
    {
        /// How far an Avir will walk for a bottle. Generous - it is inside its own base.
        private const float MaxSearchDist = 80f;

        protected override Job TryGiveJob(Pawn pawn)
        {
            Gene_ArcaneHunger hunger = ArcaneHungerCache.For(pawn);
            if (hunger == null) return null;
            if (!hunger.extractDrinkingAllowed) return null;
            if (hunger.Value >= hunger.FeedBelow) return null;

            if (pawn.Map == null || pawn.Drafted) return null;
            if (!pawn.IsColonist && !pawn.IsSlaveOfColony) return null;
            if (AvirEssenceExtractDefOf.Avir_EssenceExtract == null) return null;

            Thing bottle = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(AvirEssenceExtractDefOf.Avir_EssenceExtract),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                MaxSearchDist,
                (Thing t) => !t.IsForbidden(pawn) && pawn.CanReserve(t));

            if (bottle == null) return null;

            Job job = JobMaker.MakeJob(JobDefOf.Ingest, bottle);
            job.count = 1;
            return job;
        }
    }
}
