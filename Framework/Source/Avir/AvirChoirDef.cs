using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// ONE KIND OF CHOIR. The engine used to be a single hardcoded aura belonging to the
    /// Silversong/Goldensight amulets; everything that made it *that* aura specifically now
    /// lives in defs of this type, and GameComponent_AvirChoir no longer knows the word
    /// "Silversong".
    ///
    /// WHY A DEF AND NOT A SECOND COMPONENT. Two engines drift. The amulet aura and the hymns
    /// share the whole hard part - proximity rescan, membership churn, Fearborn filtering,
    /// per-singer upkeep accounting, enemy severity synced to live voice count, and surviving a
    /// save mid-song. Duplicating that for a work hymn would mean fixing every future bug twice.
    ///
    /// THE TWO PACING MODELS ARE BOTH EXPRESSIBLE HERE, which was the thing worth checking
    /// before committing to this shape:
    ///
    ///   Sustained (durationTicks 0)  - the amulet choir. Runs until cancelled or until the
    ///                                  caster's own reserve hits casterCollapse. No cooldown;
    ///                                  Arcane Hunger IS the limiter.
    ///   Timed (durationTicks > 0)    - the hymns. Fixed length, fixed cooldown on the
    ///                                  AbilityDef, no upkeep, no collapse. The reserve is
    ///                                  charged once at cast and then left alone.
    ///
    /// A field set to a negative number means "this rule does not apply to this choir" rather
    /// than "zero" - see casterCollapse/singerOptOut. Zero is a legitimate threshold (collapse
    /// at empty), so it cannot double as the off switch.
    /// </summary>
    public class AvirChoirDef : Def
    {
        /// <summary>Aura radius in cells, measured from the caster every rescan.</summary>
        public float radius = 35f;

        /// <summary>Marker hediff every singer carries while in range. MUST differ per choir def
        /// or two simultaneous choirs will fight over the same hediff - one removing what the
        /// other just added, every 60 ticks, forever.</summary>
        public HediffDef singerHediff;

        /// <summary>Optional. Applied to hostile non-Fearborn pawns in range.</summary>
        public HediffDef enemyHediff;

        /// <summary>When true, enemyHediff severity is kept live-synced to the current singer
        /// count, capped at maxEnemySeverity. Lets a hediff scale its own stages by how many
        /// voices are actually carrying the song - including how often it panics them.</summary>
        public bool scaleEnemySeverityByVoices = true;
        public int maxEnemySeverity = 7;

        /// <summary>Arcane Hunger charged once, at cast.</summary>
        public float castCost = 0.20f;

        /// <summary>Arcane Hunger charged to every singer each in-game hour. 0 disables upkeep
        /// entirely and no hourly tick is scheduled.</summary>
        public float upkeepPerHour = 0f;

        /// <summary>Caster reserve at or below which the whole choir collapses disruptively.
        /// Negative disables.</summary>
        public float casterCollapse = -1f;

        /// <summary>Non-caster singer reserve at or below which they quietly leave and are barred
        /// from rejoining this same choir. Negative disables.</summary>
        public float singerOptOut = -1f;

        /// <summary>Memory given to everyone still singing when a choir collapses disruptively.
        /// Null means a collapse is silent.</summary>
        public ThoughtDef collapseThought;

        /// <summary>0 = sustained until cancelled or collapsed. Otherwise the choir ends itself
        /// after this many ticks.</summary>
        public int durationTicks = 0;

        /// <summary>Whether re-casting while this choir is already running ends it instead of
        /// being rejected. Cancelling a timed hymn does NOT refund its cooldown - the cooldown
        /// lives on the AbilityDef and starts at cast, by RimWorld's own rule.</summary>
        public bool cancellable = true;

        /// <summary>Only ONE def should set this. StatPart_AvirChoirDodge and
        /// StatPart_AvirChoirSensitivity read a single choir per pawn through
        /// GameComponent_AvirChoir.TryGetChoirFor, so the lookup they use is populated only from
        /// choirs that declare this. Without the flag a pawn standing in both the amulet aura
        /// and a hymn would have one arbitrarily overwrite the other in that dictionary, and the
        /// dodge bonus would flicker as the rescans interleaved.</summary>
        public bool feedsStatParts = false;
    }
}
