using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// The doctor-performed equivalent of JobGiver_AvirDrainEssence: since Extract Essence is a
    /// real surgery bill (not an ability job), there's no think-tree node to hook. Instead this
    /// mirrors how vanilla's "Hemogen farm" prisoner option works - a periodic sweep that queues
    /// the operation on any prisoner set to Avir_ExtractEssenceFarm, as long as they aren't still
    /// on cooldown from the last extraction and don't already have one queued. Once queued, the
    /// normal doctor/bill work pipeline takes it from there - same as any other operation.
    /// </summary>
    public class GameComponent_AvirExtractEssenceFarm : GameComponent
    {
        private const int Interval = 2500; // one in-game hour, offset from the other Avir sweeps

        public GameComponent_AvirExtractEssenceFarm(Game game) { }

        public override void GameComponentTick()
        {
            if ((Find.TickManager.TicksGame + 625) % Interval != 0) return;

            List<Map> maps = Find.Maps;
            if (maps == null) return;

            for (int m = 0; m < maps.Count; m++)
            {
                List<Pawn> prisoners = maps[m].mapPawns.PrisonersOfColonySpawned;
                for (int i = 0; i < prisoners.Count; i++)
                {
                    TryQueue(prisoners[i]);
                }
            }
        }

        private static void TryQueue(Pawn prisoner)
        {
            if (prisoner == null || prisoner.Dead || prisoner.guest == null) return;
            if (prisoner.health == null || prisoner.BillStack == null) return;

            // Checkbox, not radio button: ExclusiveInteractionMode never holds a non-exclusive
            // option, so the direct comparison this used to make would be false forever.
            if (!AvirPrisonerInteraction.IsSetTo(prisoner,
                    AvirEssenceExtractDefOf.Avir_ExtractEssenceFarm)) return;

            // Still on cooldown from the last extraction.
            if (prisoner.health.hediffSet.HasHediff(AvirEssenceExtractDefOf.Avir_EssenceExtracted)) return;

            // Already queued - don't stack duplicates.
            List<Bill> bills = prisoner.BillStack.Bills;
            for (int i = 0; i < bills.Count; i++)
            {
                if (bills[i].recipe == AvirEssenceExtractDefOf.Avir_ExtractEssence) return;
            }

            Bill_Medical bill = new Bill_Medical(AvirEssenceExtractDefOf.Avir_ExtractEssence, null);
            prisoner.BillStack.AddBill(bill);
        }
    }
}
