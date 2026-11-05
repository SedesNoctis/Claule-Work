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
                MethodInfo target = AccessTools.Method(typeof(LovePartnerRelationUtility), "TryAddCheaterThought");
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
