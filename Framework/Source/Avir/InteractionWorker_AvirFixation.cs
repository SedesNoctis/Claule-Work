using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// The other half of the "real behavior, not just stats" ask: a genuinely new social
    /// interaction, selectable only for a pawn carrying any High Artificer trait (checked via
    /// the same TraitCategoryExtension tag the restriction system already uses - no hardcoded
    /// trait list to keep in sync), where the initiator corners someone to talk about their own
    /// obsession. The recipient's reaction is not fixed - it depends on who they are:
    ///
    ///   - Another High Artificer (kindred obsession)        -> big positive memory
    ///   - Someone Abrasive or a Psychopath (has no patience for it) -> negative, "weirded out"
    ///   - Everyone else                                     -> small, genuinely polite interest
    ///
    /// The initiator gets a small memory of their own too - being heard, even briefly, is its
    /// own reward for this personality type.
    /// </summary>
    public class InteractionWorker_AvirFixation : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (initiator?.story?.traits == null || recipient == null) return 0f;
            if (recipient.Dead || recipient.Downed || !recipient.RaceProps.Humanlike) return 0f;
            if (!HasHighArtificierTrait(initiator)) return 0f;

            return 0.32f; // comparable weight to vanilla's own chitchat-tier interactions
        }

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = null;

            try
            {
                GrantRecipientMemory(initiator, recipient);

                ThoughtDef spoken = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_HA_FixationSpokenMood");
                if (spoken != null) initiator.needs?.mood?.thoughts?.memories?.TryGainMemory(spoken);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Fixation interaction failed: " + e, 0x4A51E7);
            }
        }

        private static void GrantRecipientMemory(Pawn initiator, Pawn recipient)
        {
            string thoughtDefName;

            if (HasHighArtificierTrait(recipient))
            {
                thoughtDefName = "Avir_HA_FixationKindred";
            }
            else if (HasLowPatienceForIt(recipient))
            {
                thoughtDefName = "Avir_HA_FixationWeirded";
            }
            else
            {
                thoughtDefName = "Avir_HA_FixationPolite";
            }

            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(thoughtDefName);
            if (thought != null)
            {
                recipient.needs?.mood?.thoughts?.memories?.TryGainMemory(thought, initiator);
            }
        }

        private static bool HasHighArtificierTrait(Pawn pawn)
        {
            if (pawn?.story?.traits?.allTraits == null) return false;

            foreach (Trait t in pawn.story.traits.allTraits)
            {
                if (t.def.GetModExtension<TraitCategoryExtension>() != null) return true;
            }

            return false;
        }

        private static bool HasLowPatienceForIt(Pawn pawn)
        {
            if (pawn?.story?.traits == null) return false;

            TraitDef abrasive = DefDatabase<TraitDef>.GetNamedSilentFail("Abrasive");
            TraitDef psychopath = DefDatabase<TraitDef>.GetNamedSilentFail("Psychopath");

            return (abrasive != null && pawn.story.traits.HasTrait(abrasive))
                || (psychopath != null && pawn.story.traits.HasTrait(psychopath));
        }
    }
}
