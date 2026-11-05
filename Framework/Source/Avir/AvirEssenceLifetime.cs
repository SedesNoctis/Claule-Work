using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Avir
{
    [DefOf]
    public static class AvirEssenceLifetimeDefOf
    {
        public static HediffDef Avir_EssenceLifetimeTracker;

        static AvirEssenceLifetimeDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(AvirEssenceLifetimeDefOf));
        }
    }

    /// <summary>
    /// Purely internal bookkeeping hediff. Never shown in the health tab (Visible always false).
    /// Severity IS the counter - "gross arcane essence ever drawn," uncapped. Fed by:
    ///   - Essence Siphon (the raw draw, before Glutton/falloff reduce what's actually kept)
    ///   - Essence Reaver (the raw draw off a killing blow)
    ///   - Faction/caste baseline rolled once at generation for non-player Avir (see AvirEssenceBaseline)
    /// Deliberately NOT fed by Drain Essence - draining a victim doesn't grant the caster anything
    /// permanent, so it isn't "consumption" in the sense this tracker cares about.
    /// </summary>
    public class Hediff_EssenceTracker : HediffWithComps
    {
        public override bool Visible => false;

        public void AddAmount(float amount)
        {
            if (amount <= 0f) return;
            Severity += amount;
        }
    }

    public static class AvirEssenceLifetime
    {
        public static void Track(Pawn pawn, float rawAmount, string source = null)
        {
            if (pawn == null || pawn.health == null || rawAmount <= 0f) return;

            Hediff_EssenceTracker tracker = pawn.health.hediffSet
                .GetFirstHediffOfDef(AvirEssenceLifetimeDefOf.Avir_EssenceLifetimeTracker) as Hediff_EssenceTracker;

            if (tracker == null)
            {
                tracker = (Hediff_EssenceTracker)HediffMaker.MakeHediff(
                    AvirEssenceLifetimeDefOf.Avir_EssenceLifetimeTracker, pawn);
                pawn.health.AddHediff(tracker);
            }

            tracker.AddAmount(rawAmount);
        }

        /// <summary>Gross lifetime essence drawn by this pawn. 0 if never tracked.</summary>
        public static float Get(Pawn pawn)
        {
            if (pawn == null || pawn.health == null) return 0f;

            Hediff_EssenceTracker tracker = pawn.health.hediffSet
                .GetFirstHediffOfDef(AvirEssenceLifetimeDefOf.Avir_EssenceLifetimeTracker) as Hediff_EssenceTracker;

            return tracker == null ? 0f : tracker.Severity;
        }
    }

    /// <summary>
    /// Per-pawnkind starting Psychic sensitivity bonus for non-player Avir, representing essence
    /// they'd already taken before the player ever met them. Rolled once, at gene PostAdd, and only
    /// for pawns not on the player's faction - starting colonists and player-made Avir always get 0.
    ///
    /// Ranges are hand-tuned PER PAWNKIND (not per broad caste) because two pawns of the same
    /// "caste" can still differ a lot in how much they've fed - a green Young Hunter and a seasoned
    /// Predator both come from the Talon line, but should not roll the same range.
    ///
    /// "flavor" is cosmetic only (siphon vs reaver vs blend) - it doesn't change the math, just
    /// which of the two abilities the bonus is narratively attributed to for description/debug text.
    /// </summary>
    public static class AvirEssenceBaseline
    {
        private struct Range
        {
            public readonly float min;
            public readonly float max;
            public readonly string flavor;

            public Range(float min, float max, string flavor)
            {
                this.min = min;
                this.max = max;
                this.flavor = flavor;
            }
        }

        // ---- Talon line (Free/High Nesting) - war caste, reaver-flavored ----
        // ---- Artificer line - maker caste, siphon-flavored ----
        // ---- Guild line - the binding caste, blended ----
        // ---- Legion (AShialis compat, VMemes) - militant faction, harsher across the board ----
        private static readonly Dictionary<string, Range> Table = new Dictionary<string, Range>
        {
            { "Avir_YoungHunter", new Range(0.05f, 0.15f, "reaver") },
            { "Avir_Huntress",    new Range(0.10f, 0.25f, "reaver") },
            { "Avir_Warrior",     new Range(0.10f, 0.30f, "reaver") },
            { "Avir_Predator",    new Range(0.25f, 0.50f, "reaver") },
            { "Avir_Strider",     new Range(0.30f, 0.50f, "blend") },

            { "Avir_Artificer",   new Range(0.15f, 0.35f, "siphon") },
            { "Avir_Architect",   new Range(0.25f, 0.45f, "siphon") },
            { "Avir_Artist",      new Range(0.10f, 0.25f, "siphon") },

            { "Avir_GuildMaster", new Range(0.20f, 0.40f, "blend") },
            { "Avir_Matriarch",   new Range(0.35f, 0.90f, "blend") },
            { "Avir_Priestess",   new Range(0.25f, 0.90f, "blend") },

            { "Avir_Legionnaire", new Range(0.25f, 0.45f, "reaver") },
            { "Avir_Centurion",   new Range(0.15f, 0.35f, "reaver") },
            { "Avir_Inquisitor",  new Range(0.20f, 0.40f, "siphon") },
            { "Avir_Highlord",    new Range(0.40f, 0.65f, "blend") },

            // Avir_Hatchling and Avir_Colonist deliberately absent -> always 0.
        };

        /// <summary>Rolls (and does not repeat) a starting bonus for this pawn, or 0 if ineligible.</summary>
        public static float RollFor(Pawn pawn)
        {
            if (pawn == null) return 0f;
            if (pawn.Faction == Faction.OfPlayer) return 0f;
            if (pawn.kindDef == null) return 0f;

            Range r;
            if (!Table.TryGetValue(pawn.kindDef.defName, out r)) return 0f;

            return Rand.Range(r.min, r.max);
        }
    }
}
