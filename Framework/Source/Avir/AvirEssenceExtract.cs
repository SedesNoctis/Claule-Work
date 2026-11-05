using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    [DefOf]
    public static class AvirEssenceExtractDefOf
    {
        public static ThingDef Avir_EssenceExtract;
        public static HediffDef Avir_EssenceExtracted;
        public static ThoughtDef Avir_Cattle;
        public static RecipeDef Avir_ExtractEssence;
        public static PrisonerInteractionModeDef Avir_ExtractEssenceFarm;

        static AvirEssenceExtractDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirEssenceExtractDefOf));
        }
    }

    /// <summary>
    /// Drinking Essence Extract tops up Arcane Hunger the same way a hemogen pack tops up
    /// hemogen - a flat offset into the 0-1 gene resource, via Gene_ArcaneHunger.Feed.
    /// </summary>
    public class IngestionOutcomeDoer_GiveArcaneHunger : IngestionOutcomeDoer
    {
        public float hungerGain = 0.40f;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int numTaken)
        {
            Gene_ArcaneHunger.Feed(pawn, hungerGain);
        }
    }

    /// <summary>
    /// Extract Essence: a whole-pawn surgical operation (no body part, no anesthesia - see the
    /// RecipeDef). Bottles the draw as an Essence Extract item next to the patient, and leaves the
    /// patient with the Avir_EssenceExtracted hediff (36-day cooldown, zeroes Psychic sensitivity
    /// for the duration) plus the Avir_Cattle mood memory.
    ///
    /// AvailableOnNow blocks re-queuing the bill while the cooldown hediff is still active, so a
    /// "Do Forever" bill just naturally waits it out rather than stacking repeats.
    /// </summary>
    public class Recipe_Avir_ExtractEssence : Recipe_Surgery
    {
        public override bool AvailableOnNow(Thing thing, BodyPartRecord part = null)
        {
            if (!base.AvailableOnNow(thing, part)) return false;

            Pawn pawn = thing as Pawn;
            if (pawn == null || pawn.health == null) return false;

            return !pawn.health.hediffSet.HasHediff(AvirEssenceExtractDefOf.Avir_EssenceExtracted);
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (pawn == null || pawn.Dead) return;

            Hediff hediff = HediffMaker.MakeHediff(AvirEssenceExtractDefOf.Avir_EssenceExtracted, pawn);
            pawn.health.AddHediff(hediff);

            if (pawn.needs != null && pawn.needs.mood != null)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(AvirEssenceExtractDefOf.Avir_Cattle);
            }

            if (pawn.MapHeld != null)
            {
                Thing extract = ThingMaker.MakeThing(AvirEssenceExtractDefOf.Avir_EssenceExtract);
                extract.stackCount = 1;
                GenPlace.TryPlaceThing(extract, pawn.PositionHeld, pawn.MapHeld, ThingPlaceMode.Near);
            }

            if (billDoer != null)
            {
                Messages.Message(
                    billDoer.LabelShortCap + " extracted essence from " + pawn.LabelShortCap + ".",
                    pawn, MessageTypeDefOf.PositiveEvent, false);
            }
        }
    }
}
