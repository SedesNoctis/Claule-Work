using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using rjw;
using rjw.Modules.Attraction;
using Verse;

namespace AvirRJW
{
    /// <summary>
    /// AN AVIR DOES NOT STRAY.
    ///
    /// With RJW hookups on, an Avir with the pair-bond gene will have consensual sex with its own
    /// mate and nobody else - in either direction: it does not go looking, and nobody's hookup,
    /// quickie or whoring search picks it as a target. Unbonded Avir have no one, so no one.
    ///
    ///   PreflightChecks  - RJW's gate on every consensual partner search. Fails the pair if either
    ///                      side is a faithful Avir and the other is not its mate.
    ///   CanFoolAround    - RJW's "may this pawn cheat" rule. Always no for a faithful Avir.
    ///
    /// THE SPLINTER-HEARTED (Avir_ManyMated) IS THE EXCEPTION, as she is everywhere else: RJW's
    /// ordinary rules apply to her, so she can stray - and feels no guilt over it. Her mates still do.
    ///
    /// AN OUTSIDER MATE IS NOT HELD BY ANY OF THIS. A human with an Avir partner can stray; the
    /// Avir gets Avir_MateUnfaithful, the human gets nothing, and the Avir does not leave.
    ///
    /// AND IF IT HAPPENS ANYWAY - her, a player order, a mental break - ProcessSex catches it. The
    /// one who strayed gets Avir_Infidelity (-24, faithful Avir only) and every living mate gets Avir_MateUnfaithful
    /// (-18), wherever they are. Both are mood memories, not social thoughts: what it does to them
    /// is grief, not a falling-out, so opinion between them is untouched. Vanilla's CheatedOnMe is
    /// suppressed for Avir pairs in Avir.dll for the same reason.
    ///
    /// A RAPE VICTIM HAS NOT BETRAYED ANYONE and gets nothing. An Avir who is the rapist has.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirBondFidelity
    {
        private static GeneDef pairBond;
        private static TraitDef splinter;
        private static ThoughtDef infidelity;
        private static ThoughtDef mateUnfaithful;

        static AvirBondFidelity()
        {
            if (!ModsConfig.IsActive("rim.job.world")) return;
            try
            {
                pairBond = DefDatabase<GeneDef>.GetNamedSilentFail("Avir_PairBond");
                splinter = DefDatabase<TraitDef>.GetNamedSilentFail("Avir_ManyMated");
                infidelity = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_Infidelity");
                mateUnfaithful = DefDatabase<ThoughtDef>.GetNamedSilentFail("Avir_MateUnfaithful");
                if (pairBond == null) return;

                Harmony h = new Harmony("user.avirrjw.fidelity");
                Patch(h, AccessTools.Method(typeof(AttractionUtility), "PreflightChecks"), nameof(Preflight_Postfix));
                Patch(h, AccessTools.Method(typeof(AttractionUtility), "CanFoolAround", new[] { typeof(Pawn), typeof(bool) }), nameof(CanFoolAround_Postfix));
                Patch(h, AccessTools.Method(typeof(SexUtility), nameof(SexUtility.ProcessSex)), nameof(ProcessSex_Postfix));
            }
            catch (Exception e)
            {
                Log.Error("[AvirRJW] fidelity setup failed: " + e);
            }
        }

        private static void Patch(Harmony h, MethodInfo target, string postfix)
        {
            if (target == null)
            {
                Log.Warning("[AvirRJW] fidelity: hook for " + postfix + " not found.");
                return;
            }
            try
            {
                h.Patch(target, postfix: new HarmonyMethod(typeof(AvirBondFidelity), postfix));
            }
            catch (Exception e)
            {
                Log.Error("[AvirRJW] fidelity: could not patch " + target.Name + ": " + e);
            }
        }

        // ------------------------------------------------------------------ who

        public static bool HasGene(Pawn p)
        {
            return pairBond != null && p?.genes != null && p.genes.HasActiveGene(pairBond);
        }

        public static bool IsSplinter(Pawn p)
        {
            return splinter != null && p?.story?.traits != null && p.story.traits.HasTrait(splinter);
        }

        /// <summary>Held to the bond: the gene, without the one trait that breaks it.</summary>
        public static bool IsFaithful(Pawn p)
        {
            return HasGene(p) && !IsSplinter(p);
        }

        private static bool IsMate(Pawn a, Pawn b)
        {
            return a?.relations != null && b != null && LovePartnerRelationUtility.LovePartnerRelationExists(a, b);
        }

        // ------------------------------------------------------------------ prevention

        public static void Preflight_Postfix(ref AttractionRequest request, ref bool __result)
        {
            if (!__result || request.Purpose != AttractionPurpose.ForFucking) return;
            try
            {
                Pawn a = request.Pawn;
                Pawn b = request.Target;
                if (a == null || b == null) return;
                if ((IsFaithful(a) && !IsMate(a, b)) || (IsFaithful(b) && !IsMate(b, a)))
                {
                    __result = false;
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirRJW] fidelity preflight failed: " + e, 0x4A52C0);
            }
        }

        public static void CanFoolAround_Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result) return;
            try
            {
                if (IsFaithful(pawn)) __result = false;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirRJW] fidelity fool-around failed: " + e, 0x4A52C1);
            }
        }

        // ------------------------------------------------------------------ consequence

        public static void ProcessSex_Postfix(SexProps props)
        {
            if (props == null) return;
            try
            {
                Check(props.pawn, props.partner, props);
                Check(props.partner, props.pawn, props);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirRJW] fidelity consequence failed: " + e, 0x4A52C2);
            }
        }

        private static void Check(Pawn x, Pawn other, SexProps props)
        {
            if (x == null || other == null || x == other || x.Dead || other.Dead) return;
            if (other.RaceProps == null || !other.RaceProps.Humanlike) return;
            if (x.relations == null) return;

            // The victim did nothing. props.pawn is whoever this side of the act belongs to;
            // isReceiver flips between the two sides, so the initiator is the rapist.
            if (props.isRape)
            {
                Pawn rapist = props.isReceiver ? props.partner : props.pawn;
                if (x != rapist) return;
            }

            // Sex with one of their own mates is not straying, however many there are.
            if (IsMate(x, other)) return;

            // Who feels it: every mate of a straying Avir; only the Avir mates of a straying
            // outsider. A human whose human-or-outsider partner strays is not this mod's business.
            bool xAvir = HasGene(x);
            List<Pawn> mates = new List<Pawn>();
            List<DirectPawnRelation> rels = x.relations.DirectRelations;
            for (int i = 0; i < rels.Count; i++)
            {
                Pawn m = rels[i].otherPawn;
                if (m == null || m.Dead || m == other || mates.Contains(m)) continue;
                if (!LovePartnerRelationUtility.IsLovePartnerRelation(rels[i].def)) continue;
                if (xAvir || HasGene(m)) mates.Add(m);
            }
            if (mates.Count == 0) return;

            if (mateUnfaithful != null)
            {
                for (int i = 0; i < mates.Count; i++)
                {
                    mates[i].needs?.mood?.thoughts?.memories?.TryGainMemory(mateUnfaithful, x);
                }
            }

            // The guilt is only an Avir's. The splinter-hearted does not feel it, and neither does
            // an outsider who strays from an Avir.
            if (infidelity != null && IsFaithful(x))
            {
                x.needs?.mood?.thoughts?.memories?.TryGainMemory(infidelity);
            }
        }
    }
}
