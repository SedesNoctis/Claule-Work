using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Avir
{
    /// <summary>
    /// APEX PRIDE, BARE. Rewritten from a memory into a situational thought, which is the whole
    /// fix.
    ///
    /// WHY THE OLD ONE NEVER APPEARED. Avir_PridefulNudity was a MEMORY, granted and removed by
    /// Harmony patches on Notify_ApparelAdded / Notify_ApparelRemoved and re-checked by a periodic
    /// sweep. A memory only exists if a hook fires; miss the hook once - a pawn stripped by
    /// something that does not route through those notifies, a pawn that has simply never worn
    /// anything since the feature was added - and there is nothing on screen and nothing to
    /// explain why.
    ///
    /// This is exactly the asymmetry Maximus spotted: Arlia registers her partner being bare,
    /// because THAT thought was written as a situational worker and the game recomputes it
    /// constantly. He does not register his own, because his was a memory waiting on a hook.
    ///
    /// Vanilla does not do it my way. ThoughtWorker_NudistNude is situational, and the nudist trait
    /// - the closest thing in the game to this - has worked flawlessly for a decade on that basis.
    /// Copying that is not imitation, it is admitting the design was already solved.
    ///
    /// AnyClothing, NOT WornApparel.Count. Read off Pawn_ApparelTracker: AnyClothing excludes
    /// utility items, so a knife belt or a shield belt no longer counts as being dressed. That was
    /// a second, independent bug hiding behind the first.
    /// </summary>
    public class ThoughtWorker_AvirPridefulBare : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p?.apparel == null) return ThoughtState.Inactive;
            if (!PridefulApparelUtility.IsPrideful(p)) return ThoughtState.Inactive;

            return p.apparel.AnyClothing ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(0);
        }
    }

    /// <summary>
    /// KIN-DRAWN: their partner is uncovered where it counts, but not bare.
    ///
    /// THE MIDDLE STATE Maximus asked for. Avir_KindrawnBared fires only when the partner wears
    /// nothing whatsoever, which is rare in a colony that issues clothes. Far more common - and
    /// more interesting - is a partner in a shirt and nothing below it.
    ///
    /// PsychologicallyNude is vanilla's own name for that condition: groin uncovered, and chest too
    /// for a female pawn. Taken from Pawn_ApparelTracker rather than inventing a body-part-group
    /// test, so this thought means precisely what every other nudity rule in the game means and
    /// will keep meaning it if Ludeon changes the definition.
    ///
    /// MUTUALLY EXCLUSIVE WITH THE FULL VERSION by construction: this requires AnyClothing to be
    /// true, so a completely bare partner produces the +12 and this one stays silent. Stacking them
    /// would pay twice for one state.
    /// </summary>
    public class ThoughtWorker_AvirKindrawnUncovered : ThoughtWorker
    {
        private const float Radius = 10f;

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || !p.Spawned || p.Map == null) return ThoughtState.Inactive;
            if (!AvirRomanceKind.IsKindrawn(p)) return ThoughtState.Inactive;

            Pawn partner = LovePartnerRelationUtility.ExistingMostLikedLovePartner(p, false);
            if (partner == null || !partner.Spawned || partner.Map != p.Map) return ThoughtState.Inactive;
            if (!AvirFaction.HasFearborn(partner)) return ThoughtState.Inactive;
            if (partner.apparel == null) return ThoughtState.Inactive;

            // Wearing nothing at all is the other thought's business.
            if (!partner.apparel.AnyClothing) return ThoughtState.Inactive;

            if (!partner.apparel.PsychologicallyNude) return ThoughtState.Inactive;

            if ((partner.Position - p.Position).LengthHorizontalSquared > Radius * Radius)
            {
                return ThoughtState.Inactive;
            }

            return ThoughtState.ActiveAtStage(0);
        }
    }

    /// <summary>
    /// APEX PRIDE, DRESSED. One thought for the whole outfit.
    ///
    /// WHAT THIS REPLACES. Avir_PridefulApparelQuality was a MEMORY, one per garment, granted and
    /// removed by apparel hooks and a periodic resync. Two things were wrong with it and only one
    /// was the architecture:
    ///
    ///   - Same fragility as the nudity memory. If a hook missed, the mood was simply absent, with
    ///     nothing on screen to distinguish that from the feature not existing. The nudity case
    ///     proved that failure is not hypothetical.
    ///   - Per-garment is the wrong unit anyway. A creature this vain does not hold seven separate
    ///     opinions about seven items; it has one opinion about how it looks.
    ///
    /// Situational, so the game recomputes it and it cannot be missed.
    ///
    /// THE AVERAGE, NOT THE WORST - with one exception. Averaging lets a good outfit carry an
    /// ordinary shirt, which is how anyone actually judges an ensemble. But tainted or tattered is
    /// not a low grade, it is a disgrace: a legendary coat off a corpse still reads as stage 7, and
    /// one ruined garment drags the whole outfit there regardless of what else is on. That
    /// asymmetry is deliberate and it is the same rule the old per-garment version used.
    ///
    /// Clothing only. Utility items carry no quality worth an opinion, and counting them would let
    /// a shield belt dilute the average of an otherwise legendary outfit.
    /// </summary>
    public class ThoughtWorker_AvirApparelQuality : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p?.apparel == null) return ThoughtState.Inactive;
            if (!PridefulApparelUtility.IsPrideful(p)) return ThoughtState.Inactive;

            List<Apparel> worn = p.apparel.WornApparel;
            if (worn == null) return ThoughtState.Inactive;

            int total = 0;
            int counted = 0;

            for (int i = 0; i < worn.Count; i++)
            {
                Apparel a = worn[i];
                if (a?.def?.apparel == null) continue;

                List<BodyPartGroupDef> covers = a.def.apparel.bodyPartGroups;
                if (covers == null || covers.Count == 0) continue;

                int stage = PridefulApparelUtility.GetStageIndex(a);

                // One disgrace ruins the outfit. No averaging, no appeal.
                if (stage == PridefulApparelStage.TaintedOrTattered)
                {
                    return ThoughtState.ActiveAtStage(PridefulApparelStage.TaintedOrTattered);
                }

                total += stage;
                counted++;
            }

            // Wearing nothing that covers anything. ThoughtWorker_AvirPridefulBare has this.
            if (counted == 0) return ThoughtState.Inactive;

            int average = Mathf.RoundToInt(total / (float)counted);
            average = Mathf.Clamp(average, PridefulApparelStage.Awful, PridefulApparelStage.Legendary);

            return ThoughtState.ActiveAtStage(average);
        }
    }
}
