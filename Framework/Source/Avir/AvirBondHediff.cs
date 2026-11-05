using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// THE BOND AS A THING THAT EXISTS, rather than a set of rules with nothing to point at.
    ///
    /// Maximus was right that this was missing. Every effect was being inferred from the marriage
    /// relation by four separate workers, each rediscovering the relationship for itself, and there
    /// was nothing a player could look at to see that a pawn was bonded at all.
    ///
    /// The hediff is now the answer to "is this pawn bonded", carried by both partners, created and
    /// destroyed at exactly the moments the bond begins and ends. Nothing else changes: the rules
    /// still read the relation, because the relation is what the GAME understands and a hediff
    /// cannot survive a mod uninstall or a dev-mode edit that the relation would.
    ///
    /// WHERE IT IS CREATED AND DESTROYED, and why in these places rather than on a tick:
    ///   - marriage           the courtship succeeded; both get it
    ///   - resurrection       the reunion restores it alongside the marriage
    ///   - loss               severed, whether by death or by being left
    ///   - a sweep            the safety net, because relations can be changed by other mods, by
    ///                        dev tools, and by saves that predate this system
    /// </summary>
    public static class AvirBondHediff
    {
        public static void Grant(Pawn a, Pawn b)
        {
            GrantOne(a, b);
            GrantOne(b, a);
        }

        private static void GrantOne(Pawn pawn, Pawn mate)
        {
            try
            {
                if (pawn == null || pawn.Dead || pawn.health == null) return;

                // Only the species this belongs to. A human married to an Avir is married, not
                // bonded, and carries nothing.
                if (!AvirPairBond.HasBondGene(pawn)) return;

                HediffDef def = Def;
                if (def == null) return;

                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (existing != null)
                {
                    // Already bonded - but possibly pointing at nobody, if it came from a save made
                    // before the hediff carried a target at all. Repair rather than duplicate.
                    SetTarget(existing, mate);
                    return;
                }

                Hediff added = HediffMaker.MakeHediff(def, pawn);
                SetTarget(added, mate);
                pawn.health.AddHediff(added, BrainOf(pawn));
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] granting the bond hediff failed: " + e, 0x4A5294);
            }
        }

        /// <summary>
        /// Points the hediff at the mate. Written through the base class rather than our own so
        /// that a hediff created by dev mode - which makes a plain HediffWithTarget - is handled
        /// too.
        /// </summary>
        private static void SetTarget(Hediff h, Pawn mate)
        {
            HediffWithTarget targeted = h as HediffWithTarget;
            if (targeted != null) targeted.target = mate;
        }

        public static void Remove(Pawn pawn)
        {
            try
            {
                if (pawn?.health == null) return;

                HediffDef def = Def;
                if (def == null) return;

                Hediff h = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (h != null) pawn.health.RemoveHediff(h);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] removing the bond hediff failed: " + e, 0x4A5295);
            }
        }

        /// <summary>
        /// Brings the hediff into line with the relation, in either direction.
        ///
        /// THE RELATION IS THE TRUTH AND THE HEDIFF FOLLOWS IT. That ordering matters: a save made
        /// before this system existed has bonded couples with no hediff, another mod can hand out
        /// or dissolve a marriage without telling us, and dev tools do both. Deriving the hediff
        /// from the relation on a slow sweep means all of those correct themselves; deriving the
        /// relation from the hediff would mean the mod inventing marriages nobody asked for.
        /// </summary>
        public static void Reconcile(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return;

            bool shouldHave = AvirPairBond.HasBondGene(pawn)
                              && AvirPairBond.IsBonded(pawn);

            bool has = Def != null
                       && pawn.health?.hediffSet != null
                       && pawn.health.hediffSet.HasHediff(Def);

            if (shouldHave == has) return;

            if (shouldHave)
            {
                Pawn mate = LovePartnerRelationUtility.ExistingMostLikedLovePartner(pawn, false);
                GrantOne(pawn, mate);
            }
            else
            {
                Remove(pawn);
            }
        }

        /// <summary>
        /// VANILLA'S BodyPartDefOf DOES NOT DECLARE Brain.
        ///
        /// Same trap HediffComp_AvirClawTier already documents for Foot: BodyPartDefOf carries
        /// only the handful of parts the core code happens to reference, and Brain is not one of
        /// them. Looked up from the database by name instead, once, and cached - a body part def
        /// cannot change over a session.
        /// </summary>
        private static BodyPartDef brainDef;
        private static bool brainLooked;

        private static BodyPartDef BrainDef
        {
            get
            {
                if (!brainLooked)
                {
                    brainLooked = true;
                    brainDef = DefDatabase<BodyPartDef>.GetNamedSilentFail("Brain");
                }

                return brainDef;
            }
        }

        /// <summary>
        /// The brain record, or null for a body that has none. Looked up by def rather than
        /// remembered, because a pawn can lose and regain the part.
        /// </summary>
        public static BodyPartRecord BrainOf(Pawn pawn)
        {
            if (pawn?.RaceProps?.body == null) return null;

            BodyPartDef wanted = BrainDef;
            if (wanted == null) return null;

            List<BodyPartRecord> parts = pawn.RaceProps.body.AllParts;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].def == wanted) return parts[i];
            }

            return null;
        }

        private static HediffDef cached;
        private static bool looked;

        private static HediffDef Def
        {
            get
            {
                if (!looked)
                {
                    looked = true;
                    cached = DefDatabase<HediffDef>.GetNamedSilentFail("Avir_PairBondHediff");
                }

                return cached;
            }
        }
    }

    /// <summary>
    /// The slow sweep that keeps the hediff honest. Once a day is ample - nothing depends on it
    /// being immediate, because every moment the bond actually begins or ends already grants or
    /// removes it directly. This exists only for the cases nobody told us about.
    /// </summary>
    public class GameComponent_AvirBondReconcile : GameComponent
    {
        private const int Interval = 60000;

        public GameComponent_AvirBondReconcile(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0) return;

            try
            {
                List<Map> maps = Find.Maps;
                if (maps == null) return;

                for (int m = 0; m < maps.Count; m++)
                {
                    IReadOnlyList<Pawn> pawns = maps[m]?.mapPawns?.AllPawnsSpawned;
                    if (pawns == null) continue;

                    for (int i = 0; i < pawns.Count; i++)
                    {
                        Pawn p = pawns[i];
                        if (p == null || !AvirPairBond.HasBondGene(p)) continue;

                        AvirBondHediff.Reconcile(p);
                    }
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] reconcile sweep failed: " + e, 0x4A5296);
            }
        }
    }

    /// <summary>
    /// AN AVIR DOES NOT SEE HER MATE AS UGLY.
    ///
    /// Vanilla judges appearance socially through two situational thoughts - Disfigured at -15 and
    /// Ugly at -20 - and both apply to a spouse exactly as they apply to a stranger. An Avir whose
    /// mate loses a nose to a bullet would think less of him for it, permanently, which is a
    /// grotesque thing to say about a creature that chose once and cannot choose again.
    ///
    /// SUPPRESSED ONLY TOWARD THE MATE. She still finds a disfigured stranger hard to look at; the
    /// exemption is about who he is to her, not about Avir being indifferent to appearance. Making
    /// it species-wide would have been the easier patch and would have said something quite
    /// different.
    ///
    /// The pair bond is the qualifier, not the gene: the splinter-hearted, who has no bond, is not
    /// blind to anything.
    /// </summary>
    public static class Patch_AvirBondBlindToScars
    {
        private static readonly HashSet<string> AppearanceThoughts = new HashSet<string>
        {
            "Disfigured",
            "Ugly",
        };

        public static void Apply(Harmony harmony)
        {
            try
            {
                int bound = 0;

                foreach (string name in AppearanceThoughts)
                {
                    ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail(name);
                    if (def?.workerClass == null) continue;

                    MethodInfo target = AccessTools.Method(def.workerClass,
                        "CurrentSocialStateInternal", new[] { typeof(Pawn), typeof(Pawn) });

                    if (target == null) continue;

                    harmony.Patch(target, postfix: new HarmonyMethod(
                        typeof(Patch_AvirBondBlindToScars), nameof(Appearance_Postfix)));
                    bound++;
                }

                if (bound == 0)
                {
                    Log.Warning("[Avir bond] no appearance thought workers found; a bonded Avir "
                        + "will still think less of a scarred mate.");
                    return;
                }

                Log.Message("[Avir bond] bonded Avir are blind to a mate's scars ("
                    + bound + " worker(s)).");
            }
            catch (Exception e)
            {
                Log.Warning("[Avir bond] could not bind the scar exemption: " + e);
            }
        }

        /// <summary>
        /// THE FIRST PARAMETER IS CALLED pawn, NOT p.
        ///
        /// Harmony binds injected parameters BY NAME, and the name here was wrong, so the binding
        /// threw and the scar exemption never attached to anything:
        ///
        ///     Parameter "p" not found in method
        ///     ThoughtWorker_Disfigured::CurrentSocialStateInternal(Verse.Pawn pawn, Verse.Pawn other)
        ///
        /// The comment above this method used to assert that p and other matched ThoughtWorker's
        /// own signature. It did not, and the comment being confident about it is exactly how this
        /// survived - a wrong parameter name is invisible to the compiler and only ever shows up
        /// as one line in the log at startup.
        ///
        /// The body still calls it p internally; only the parameter name had to change.
        /// </summary>
        public static void Appearance_Postfix(Pawn pawn, Pawn other, ref ThoughtState __result)
        {
            if (!__result.Active) return;

            try
            {
                Pawn p = pawn;
                if (!AvirPairBond.HasBondGene(p)) return;

                Pawn mate = LovePartnerRelationUtility.ExistingMostLikedLovePartner(p, false);
                if (mate != other) return;

                __result = ThoughtState.Inactive;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir bond] scar exemption failed: " + e, 0x4A5297);
            }
        }
    }
}

namespace Avir
{
    /// <summary>
    /// The bond hediff, carrying the name of the one it points at.
    ///
    /// DERIVED FROM HediffWithTarget, WHICH IS WHERE THE `target` FIELD LIVES. That single
    /// inheritance buys two things Maximus asked for: the label reads "pair bond (Ro)" instead of
    /// a bare "pair bond", and dev mode offers the Choose Pawn dialog when adding it by hand,
    /// exactly as it does for PsychicBond.
    ///
    /// NOT Hediff_PsychicBond ITSELF, though that would have been one word shorter. That class
    /// overrides PostTickInterval with the distance logic - the Consciousness penalty for being on
    /// another map - and PostRemoved with vanilla's own bond-tearing. Inheriting it would have
    /// dragged back in the exact behaviour this whole system exists to replace.
    ///
    /// ShouldRemove is left to the base, which drops the hediff when the target is gone. That is
    /// correct here: the bond ends when the other one does, and what remains afterwards is
    /// Avir_BondSevered, which is a different thing and is granted separately.
    /// </summary>
    public class Hediff_AvirPairBond : HediffWithTarget
    {
        /// <summary>
        /// A PAIR BOND LIVES IN THE BRAIN AND NOWHERE ELSE.
        ///
        /// The character editor and dev mode let a hediff be attached to any body part, which for
        /// this one is simply wrong: a bond is not a condition of somebody's foot. Maximus found
        /// it hanging off arbitrary parts and asked for it to be brain-only.
        ///
        /// Corrected here rather than only at the grant site, because the grant site is not the
        /// only way this hediff arrives - the editor, dev tools and old saves all bypass it. This
        /// runs whenever the hediff is added, whoever added it, so there is one answer instead of
        /// a rule that holds only when the mod itself is doing the adding.
        /// </summary>
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);

            try
            {
                BodyPartRecord brain = AvirBondHediff.BrainOf(pawn);
                if (brain != null && Part != brain) Part = brain;
            }
            catch
            {
                // A pawn with no brain record is not a case worth crashing a bond over.
            }
        }

        public override string LabelBase
        {
            get
            {
                string name = target?.LabelShort;
                return name.NullOrEmpty()
                    ? base.LabelBase
                    : base.LabelBase + " (" + name + ")";
            }
        }
    }
}
