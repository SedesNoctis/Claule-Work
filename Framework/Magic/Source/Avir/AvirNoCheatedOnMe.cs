using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// STRAYING DOES NOT TURN AVIR MATES AGAINST EACH OTHER.
    ///
    /// Vanilla answers cheating with CheatedOnMe, an opinion thought. For an Avir pair what comes
    /// instead is mood - Avir_Infidelity and Avir_MateUnfaithful (Compat/RJW) - and the opinion
    /// between them stays where it was. So vanilla's thought is skipped whenever either side carries
    /// the pair-bond gene.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirNoCheatedOnMe
    {
        static AvirNoCheatedOnMe()
        {
            try
            {
                // Fix254: in RimWorld 1.6 the method lives on the romance attempt's worker, not on the love-partner
                // utility any more - looked for there first, then in its old place (so both versions are served).
                // It was not found at all before: the patch never applied, and bonded Avir got "cheated on me".
                MethodInfo target = AccessTools.Method(typeof(InteractionWorker_RomanceAttempt), "TryAddCheaterThought")
                    ?? AccessTools.Method(typeof(LovePartnerRelationUtility), "TryAddCheaterThought");
                if (target == null)
                {
                    Log.Warning("[Avir] TryAddCheaterThought not found; Avir mates may still get CheatedOnMe.");
                    return;
                }
                new Harmony("Tenebris.Avir.NoCheatedOnMe").Patch(target,
                    prefix: new HarmonyMethod(typeof(AvirNoCheatedOnMe), nameof(Prefix)));
            }
            catch (Exception e)
            {
                Log.Error("[Avir] CheatedOnMe suppression failed: " + e);
            }
        }

        public static bool Prefix(Pawn __0, Pawn __1)
        {
            try
            {
                if (AvirBondLibido.HasPairBondGene(__0) || AvirBondLibido.HasPairBondGene(__1)) return false;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] CheatedOnMe suppression failed: " + e, 0x4A52A1);
            }
            return true;
        }
    }
}
