using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// WHO AN AVIR CAN WANT.
    ///
    /// Three tiers, and the whole point is that the middle one is the default:
    ///
    ///   KIN-DRAWN  - will not look at anything that is not Avir. Zero. Not "unlikely".
    ///   (baseline) - any Fearborn. A quarter of the usual chance with an outsider. Possible,
    ///                rare, and quietly humiliating on both sides if you think about it.
    ///   OUTKIND    - the baseline does not apply. Ordinary odds with anyone.
    ///
    /// That shape says something the stat lines cannot: an Avir who ends up with a human did not
    /// merely get lucky, they went against the grain of the species. And an Avir who is kin-drawn
    /// is not choosy - they are incapable of the thought.
    ///
    /// SYMMETRIC ON PURPOSE. The rule is evaluated for the PAIR, not for whoever happened to speak
    /// first. A human courting a kin-drawn Avir is refused as firmly as the reverse; otherwise the
    /// whole thing could be walked around by letting the other party initiate.
    /// </summary>
    public static class AvirRomanceKind
    {
        private static bool resolved;
        private static TraitDef kindrawn, outkind;

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;

            kindrawn = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Kindrawn");
            outkind = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_Outkind");
        }

        public static bool Has(Pawn pawn, TraitDef def)
        {
            if (def == null || pawn?.story?.traits == null) return false;
            return pawn.story.traits.HasTrait(def);
        }

        public static bool IsKindrawn(Pawn pawn)
        {
            Resolve();
            return Has(pawn, kindrawn);
        }

        public static bool IsOutkind(Pawn pawn)
        {
            Resolve();
            return Has(pawn, outkind);
        }

        /// <summary>
        /// Opposite sex, same kind. The one relationship this species is built around, and the
        /// thing kin-drawn Avir cannot stop noticing.
        ///
        /// Gender.None is excluded rather than treated as a match, so nothing here fires on a pawn
        /// with no gender at all.
        /// </summary>
        public static bool IsOppositeSexKin(Pawn p, Pawn other)
        {
            if (p == null || other == null || p == other) return false;
            if (!AvirFaction.HasFearborn(p) || !AvirFaction.HasFearborn(other)) return false;
            if (p.gender == Gender.None || other.gender == Gender.None) return false;

            return p.gender != other.gender;
        }

        /// <summary>
        /// How the two traits move the courtship odds for a Fearborn / non-Fearborn pair, or -1
        /// when neither trait applies and the gene's own figure should stand unaltered.
        ///
        /// THE BASELINE IS NOT HERE ANY MORE. It lives on the Fearborn gene as
        /// missingGeneRomanceChanceFactor, a vanilla field - the same one Furskin uses - which the
        /// game applies itself and prints in the gene tooltip. The Harmony patch that used to do
        /// that job was written before anyone looked at Furskin's tooltip, and it was doing by
        /// hand what the data format already did better and more visibly.
        ///
        /// What is left here is only the two exceptions, and the outkind case READS the gene's
        /// number rather than repeating it. Change 0.05 in the XML and outkind stays exactly
        /// normal; hardcode 20f here instead and the two drift apart the first time the balance
        /// moves.
        /// </summary>
        public static float TraitOverride(Pawn a, Pawn b)
        {
            if (a == null || b == null) return -1f;

            bool aKin = AvirFaction.HasFearborn(a);
            bool bKin = AvirFaction.HasFearborn(b);

            // Same side of the line, either side. The gene rule does not apply and neither do we.
            if (aKin == bKin) return -1f;

            Pawn avir = aKin ? a : b;

            // Drawn to outsiders: undo the species penalty exactly, whatever it currently is.
            if (IsOutkind(avir))
            {
                float baseline = FearbornRomanceFactor();
                if (baseline <= 0f) return -1f;
                return 1f / baseline;
            }

            // Kin-drawn: not a reduced chance. No chance.
            if (IsKindrawn(avir)) return 0f;

            return -1f;
        }

        private static bool factorRead;
        private static float cachedFactor = 0.05f;

        /// <summary>
        /// The Fearborn gene's own missingGeneRomanceChanceFactor, read once.
        ///
        /// Read by reflection because the field is recent enough that a direct reference would tie
        /// the whole assembly to it; if it is ever renamed this degrades to the shipped default and
        /// says so, rather than refusing to load.
        /// </summary>
        public static float FearbornRomanceFactor()
        {
            if (factorRead) return cachedFactor;
            factorRead = true;

            try
            {
                GeneDef fearborn = DefDatabase<GeneDef>.GetNamedSilentFail("Fearborn");
                if (fearborn == null) return cachedFactor;

                FieldInfo f = AccessTools.Field(typeof(GeneDef), "missingGeneRomanceChanceFactor");
                if (f == null || f.FieldType != typeof(float))
                {
                    Log.Warning("[Avir romance] GeneDef.missingGeneRomanceChanceFactor not found; "
                        + "outkind will be balanced against the shipped default of "
                        + cachedFactor.ToString("0.00") + " instead of the live value.");
                    return cachedFactor;
                }

                cachedFactor = (float)f.GetValue(fearborn);
            }
            catch (Exception e)
            {
                Log.Warning("[Avir romance] could not read the Fearborn romance factor: " + e);
            }

            return cachedFactor;
        }
    }

    /// <summary>
    /// Applies the rule where courtships are chosen.
    ///
    /// RandomSelectionWeight is the right hook rather than SuccessChance: a weight of zero means
    /// the interaction is never picked in the first place, so a kin-drawn Avir does not spend the
    /// game repeatedly propositioning a human and being turned down. Nothing happens at all, which
    /// is what "incapable of the thought" should look like from outside.
    ///
    /// The class name is confirmed from Core's own Interactions_Romance.xml, not recalled; the
    /// method is bound by shape so a signature change degrades to a log line rather than a crash.
    /// </summary>
    public static class Patch_AvirRomanceKind
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                Type worker = AccessTools.TypeByName("RimWorld.InteractionWorker_RomanceAttempt")
                              ?? AccessTools.TypeByName("InteractionWorker_RomanceAttempt");

                if (worker == null)
                {
                    Log.Warning("[Avir romance] InteractionWorker_RomanceAttempt not found; the "
                        + "cross-kind courtship rules are inactive.");
                    return;
                }

                MethodInfo target = AccessTools.Method(worker, "RandomSelectionWeight",
                    new[] { typeof(Pawn), typeof(Pawn) });

                if (target == null)
                {
                    Log.Warning("[Avir romance] RandomSelectionWeight(Pawn, Pawn) not found on "
                        + worker.Name + "; the cross-kind courtship rules are inactive.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(Patch_AvirRomanceKind), nameof(Weight_Postfix)));

                Log.Message("[Avir romance] cross-kind courtship rules bound to "
                    + worker.Name + ".RandomSelectionWeight.");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir romance] failed to apply: " + e);
            }
        }

        public static void Weight_Postfix(Pawn initiator, Pawn recipient, ref float __result)
        {
            if (__result <= 0f) return;

            try
            {
                float factor = AvirRomanceKind.TraitOverride(initiator, recipient);
                if (factor < 0f) return;

                __result *= factor;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir romance] weight postfix failed: " + e, 0x4A5269);
            }
        }
    }

    /// <summary>
    /// KIN-DRAWN: the mood lift of being among their own.
    ///
    /// ONE WORKER, TWO DEFS. The thought is split into _Male and _Female versions in XML so the
    /// pronouns can be right - the target is opposite-sex by definition, so the thinker's own
    /// gender settles the target's. The <gender> field on ThoughtDef does that filtering, and
    /// nothing about it belongs in here: both defs point at this same worker and it never needs to
    /// know which one asked.
    ///
    /// Counts Avir of the opposite sex within sight. Not partners - anyone. That is deliberate and
    /// it is the uncomfortable part of the trait: this is not love, it is an appetite for the shape
    /// of their own species, and it does not much care whose body is wearing it.
    /// </summary>
    public class ThoughtWorker_AvirKindrawnPresence : ThoughtWorker
    {
        private const float Radius = 12f;

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || !p.Spawned || p.Map == null) return ThoughtState.Inactive;
            if (!AvirRomanceKind.IsKindrawn(p)) return ThoughtState.Inactive;

            // IReadOnlyList, NOT List. Declared as List here originally and it cost a compile:
            // 1.6 hands this out read-only. Indexing and Count work identically, so nothing else
            // in the loop below changes.
            IReadOnlyList<Pawn> pawns = p.Map.mapPawns?.AllPawnsSpawned;
            if (pawns == null) return ThoughtState.Inactive;

            float radiusSq = Radius * Radius;
            int count = 0;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (other == p || other.Dead) continue;
                if (!AvirRomanceKind.IsOppositeSexKin(p, other)) continue;
                if ((other.Position - p.Position).LengthHorizontalSquared > radiusSq) continue;

                count++;
                if (count >= 3) return ThoughtState.ActiveAtStage(2);
            }

            if (count >= 2) return ThoughtState.ActiveAtStage(1);
            if (count >= 1) return ThoughtState.ActiveAtStage(0);

            return ThoughtState.Inactive;
        }
    }

    /// <summary>
    /// KIN-DRAWN: their partner, uncovered, in front of them.
    ///
    /// This is the trait at full volume, and it only fires for the pawn's actual love partner -
    /// the presence thought above already covers everyone else, and this one would be something
    /// quite different if it did not.
    ///
    /// Requires the partner to be BOTH Avir and unclothed, and near enough to see. It stacks with
    /// the presence thought on purpose: the partner is also one of the bodies being counted there.
    /// </summary>
    public class ThoughtWorker_AvirKindrawnBared : ThoughtWorker
    {
        private const float Radius = 10f;

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || !p.Spawned || p.Map == null) return ThoughtState.Inactive;
            if (!AvirRomanceKind.IsKindrawn(p)) return ThoughtState.Inactive;

            Pawn partner = LovePartnerRelationUtility.ExistingMostLikedLovePartner(p, false);
            if (partner == null || !partner.Spawned || partner.Map != p.Map) return ThoughtState.Inactive;

            if (!AvirFaction.HasFearborn(partner)) return ThoughtState.Inactive;
            if (partner.apparel == null || partner.apparel.WornApparel.Count > 0) return ThoughtState.Inactive;

            if ((partner.Position - p.Position).LengthHorizontalSquared > Radius * Radius)
            {
                return ThoughtState.Inactive;
            }

            return ThoughtState.ActiveAtStage(0);
        }
    }

    /// <summary>
    /// KIN-DRAWN: thinks better of every Avir of the opposite sex, on sight, for no reason beyond
    /// what they are. The servility Maximus described, expressed as an opinion offset rather than
    /// as a mood - it changes how they treat those pawns, not merely how they feel.
    /// </summary>
    public class ThoughtWorker_Social_AvirKindrawn : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (!AvirRomanceKind.IsKindrawn(p)) return ThoughtState.Inactive;
            if (!AvirRomanceKind.IsOppositeSexKin(p, other)) return ThoughtState.Inactive;

            return ThoughtState.ActiveAtStage(0);
        }
    }

    /// <summary>
    /// WHAT AN AVIR THINKS OF ANYTHING THAT IS NOT ONE.
    ///
    /// Not dislike, and the wording of the thought matters as much as the number: a species that
    /// files every other creature under "prey" does not resent them. It condescends. The −10 is
    /// the weight of being permanently, politely, not quite taken seriously.
    ///
    /// Every Avir carries this. The single exception is outkind, who was born without whatever
    /// does the filing - which is the entire content of that trait and the reason it is worth
    /// having.
    /// </summary>
    public class ThoughtWorker_Social_AvirOutsider : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (p == null || other == null || p == other) return ThoughtState.Inactive;
            if (!AvirFaction.HasFearborn(p)) return ThoughtState.Inactive;

            // Born without the instinct. The one Avir who looks at a human and sees a person.
            if (AvirRomanceKind.IsOutkind(p)) return ThoughtState.Inactive;

            // One of us. Nothing to condescend to.
            if (AvirFaction.HasFearborn(other)) return ThoughtState.Inactive;

            return ThoughtState.ActiveAtStage(0);
        }
    }
}
