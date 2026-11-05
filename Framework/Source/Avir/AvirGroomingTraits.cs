using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// WHAT EACH PLUMAGE TRAIT DOES ABOUT GROOMING.
    ///
    /// These four traits already existed and already said all of this in their descriptions -
    /// feather-proud spends visible hours on every barb, draggled cannot be bothered and never has
    /// been, thin-plumed got what was left over, featherbroken had something happen in the nest
    /// that never let go. Until now none of it touched anything: they were flavour text with a
    /// couple of stat lines.
    ///
    /// Grooming is the system those descriptions were waiting for, so each trait is wired to the
    /// part of it the description already implies. Nothing new is invented and nothing is renamed.
    ///
    /// KEPT IN ONE TABLE ON PURPOSE. Six traits touching five different points of the grooming
    /// system is exactly the shape that turns into six disagreeing rules scattered across three
    /// files. Every trait's effect is declared here and read from here.
    ///
    /// AND THE TABLE IS WHY Avir_Preener WAS MISSED. It was written long before the grooming
    /// system existed, it carries stat lines so it never looked inert in an audit, and its
    /// description - "a truly unreasonable share of the day on their own plumage" - describes this
    /// system exactly. It sat one file away from four traits doing the same job and was not
    /// connected to any of it. If another plumage trait turns up later, it belongs here.
    /// </summary>
    public static class AvirGroomingTraits
    {
        /// <summary>
        /// The three unkempt stages, as fractions of the coat's capacity, in ONE place.
        ///
        /// ThoughtWorker_AvirUnkempt reads them to decide the mood, and
        /// JobGiver_AvirGroomingUrgent reads Stage0 to decide when an Avir may put down its work
        /// and deal with the itch. Those two must agree exactly: the rule is "you interrupt work
        /// at the moment it starts costing you", and two separate numbers would drift into either
        /// a pawn who suffers without acting or one who downs tools before anything is wrong.
        /// </summary>
        public const float UnkemptStage0 = 0.60f;
        public const float UnkemptStage1 = 0.75f;
        public const float UnkemptStage2 = 0.90f;

        // Resolved lazily from the def database rather than through DefOf, so a stripped-down
        // install missing one of these traits does not throw at startup.
        private static bool resolved;
        private static TraitDef proud, draggled, thinplumed, broken, rough, preener;

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;

            proud = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Featherproud");
            draggled = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Draggled");
            thinplumed = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Thinplumed");
            broken = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Featherbroken");
            rough = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Roughhanded");
            preener = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Preener");
        }

        private static bool Has(Pawn pawn, TraitDef def)
        {
            if (def == null || pawn?.story?.traits == null) return false;
            return pawn.story.traits.HasTrait(def);
        }

        /// <summary>
        /// FEATHER-PROUD: twice as long, every time.
        ///
        /// "Every barb lies exactly where she put it, and she will happily tell anyone who asks -
        /// or doesn't - exactly how long that took. The time it takes shows too." The trait already
        /// carries a WorkSpeedGlobal penalty for the hours it eats; this is where those hours
        /// actually go.
        ///
        /// Applies to preening a partner as well as herself, at Maximus's request: she is no more
        /// willing to rush someone she loves than to rush her own coat. Taken as the MAXIMUM across
        /// the pair, so one fussy Avir sets the pace for both.
        /// </summary>
        public static float SpeedFactor(Pawn a, Pawn b = null)
        {
            Resolve();

            float f = 1f;
            if (Has(a, proud)) f = 2f;
            if (b != null && Has(b, proud)) f = 2f;

            // ROUGHHANDED IS NOT AN IMPROVEMENT, IT IS A SHORTCUT. Half the time, and the partner
            // knows it - see IsRoughGroomer, which downgrades the memory they come away with.
            //
            // Checked on the ACTOR only, unlike feather-proud: how long a session takes is set by
            // whoever's claws are doing the work. And feather-proud wins outright if either of
            // them has it, because someone determined to do it properly will not be hurried.
            if (f <= 1f && Has(a, rough)) f = 0.5f;

            // PREENER: twice as long, same as feather-proud, for an entirely different reason.
            //
            // Feather-proud takes twice as long because the result has to be perfect. A preener
            // takes twice as long because they are not really trying to finish - the description
            // says "a truly unreasonable share of the day", and this is where that share goes.
            //
            // Checked on both sides like feather-proud, and it does not stack with it: whichever
            // applies, the answer is 2x, never 4x. An hour of two Avir fussing is a scene; four
            // hours is a pawn removed from the colony for half a day.
            if (Has(a, preener) || (b != null && Has(b, preener))) f = 2f;

            // A'SHACSAIGN ARE QUICKER, BUT ONLY AMONG THEMSELVES.
            //
            // Bare wings mean there is far less coat to work through, so an A'Shacsaign over her
            // own plumage - or over another A'Shacsaign - finishes in well under half the time.
            //
            // A MIXED PAIR GETS NO DISCOUNT, and that is the whole point of testing both sides.
            // Grooming here is not only harvesting: the session is affection first and product
            // second. An A'Shacsaign preening a feathered partner still has the full coat in front
            // of her, and a feathered Avir preening an A'Shacsaign will spend the saved time on
            // her anyway rather than walk off early. Either way the pair sits as long as any other.
            //
            // Multiplies with the trait factors rather than overriding them, so a feather-proud
            // A'Shacsaign is still slower than her plain-tempered kin - just not slower than a
            // feather-proud Avir with a full coat.
            if (IsBare(a) && (b == null || IsBare(b))) f *= 0.4f;

            return f;
        }

        /// <summary>
        /// True when this pawn has no feathered wings to work through - the A'Shacsaign subrace.
        /// Kept as a named one-liner so the speed rule above reads as intent rather than as a
        /// reach into another patch class.
        /// </summary>
        private static bool IsBare(Pawn pawn)
        {
            return Patch_AvirAShacsaignSpawn.IsSubrace(pawn);
        }

        /// <summary>
        /// DRAGGLED: never bothered by a coat full of loose fur.
        ///
        /// "Cannot be bothered, has never been bothered, and does not intend to start being
        /// bothered." Giving this pawn a mood penalty for an unpreened coat would contradict the
        /// only thing the trait says about itself. She still sheds, still can be groomed, still
        /// gets the memory for it if a mate insists - she simply does not mind either way.
        /// </summary>
        public static bool FeelsNeglect(Pawn pawn)
        {
            Resolve();
            return !Has(pawn, draggled);
        }

        /// <summary>
        /// FEATHERBROKEN: nobody else's hands in the plumage.
        ///
        /// "Something happened in the nest that she has never spoken of. Whatever it was, it never
        /// entirely let go." She is not a valid target for anyone else's grooming - she preens
        /// alone or not at all - which costs her the halved time and the mood memory, and is meant
        /// to. She may still groom OTHERS; the trait is about being touched, not about touching.
        /// </summary>
        public static bool AcceptsGroomingFromOthers(Pawn pawn)
        {
            Resolve();
            return !Has(pawn, broken);
        }

        /// <summary>
        /// PREENER: starts far sooner than anyone else would bother.
        ///
        /// The other half of "constantly, at the faintest excuse". Multiplies the threshold at
        /// which a coat is worth grooming, so a preener sits down to it at roughly a third of the
        /// loose fur everyone else waits for - about once a day rather than once every three.
        ///
        /// Combined with the doubled duration above, this is a pawn who really does spend an
        /// unreasonable share of the day on their plumage, which is what the trait has claimed
        /// since the day it was written and never once delivered.
        /// </summary>
        public static float GroomThresholdFactor(Pawn pawn)
        {
            Resolve();
            return Has(pawn, preener) ? 0.35f : 1f;
        }

        /// <summary>
        /// ROUGHHANDED: fast claws, and the partner feels every one of them.
        ///
        /// "Claws that can open a ribcage cleanly can also ruin a fine seam without even trying."
        /// The seam version of that is in the trait's stat lines; this is the version that matters
        /// socially. They get through a coat in half the time and their mate comes away with a
        /// much smaller memory of having been cared for, because they were not, particularly.
        /// </summary>
        public static bool IsRoughGroomer(Pawn pawn)
        {
            Resolve();
            return Has(pawn, rough);
        }

        /// <summary>
        /// THIN-PLUMED: less coat, so less of everything that comes off it.
        ///
        /// "A clutch-mate got the good down. She got what was left over." Scales production, which
        /// means she litters less, fills up slower and reaches the unkempt thresholds later. The
        /// existing cold penalty on the trait stays the headline; this is the quiet consequence.
        /// </summary>
        public static float CoatFactor(Pawn pawn)
        {
            Resolve();
            return Has(pawn, thinplumed) ? 0.6f : 1f;
        }
    }

    /// <summary>
    /// An itch nobody has dealt with.
    ///
    /// SCALED BY THE COAT, NOT BY A CLOCK. A separate "days since last groomed" timer would be a
    /// second source of truth about the same thing, and the two would drift - a pawn groomed at a
    /// full pool and a pawn groomed at an empty one are not equally overdue. The pool already
    /// measures exactly what the mood is about, and it is already saved.
    ///
    /// Thresholds are fractions of poolCapacity, so retuning feathersPerDay or poolCapacity in XML
    /// moves the mood with them instead of silently desynchronising from it.
    /// </summary>
    public class ThoughtWorker_AvirUnkempt : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || !p.RaceProps.Humanlike) return ThoughtState.Inactive;
            if (!AvirGroomingTraits.FeelsNeglect(p)) return ThoughtState.Inactive;

            HediffComp_FurShed coat = AvirGrooming.CoatOf(p);
            if (coat == null) return ThoughtState.Inactive;

            float f = coat.LooseFraction;

            // EVERY STAGE SITS AFTER THE POINT WHERE GROOMING BECOMES POSSIBLE, and that ordering
            // is the whole design of this thought.
            //
            // The coat reaches minLooseToGroom (0.50 of capacity) on day three. The first stage
            // does not fire until 0.60, most of a day later. So an Avir who is groomed on schedule
            // never sees any of this: the mood is not a clock counting down to the next session,
            // it is the penalty for MISSING one.
            //
            // Get these below the grooming threshold and the mood becomes unavoidable - the pawn
            // would be miserable about something the game had not yet allowed anyone to fix.
            //
            // At the shipped rates: groomable day 3, mild about half a day late, worse a day and a
            // half late, bad two and a half days late.
            if (f >= AvirGroomingTraits.UnkemptStage2) return ThoughtState.ActiveAtStage(2);
            if (f >= AvirGroomingTraits.UnkemptStage1) return ThoughtState.ActiveAtStage(1);
            if (f >= AvirGroomingTraits.UnkemptStage0) return ThoughtState.ActiveAtStage(0);

            return ThoughtState.Inactive;
        }
    }
}
