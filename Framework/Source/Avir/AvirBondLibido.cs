using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// AVIR LIBIDO FOLLOWS THE BOND.
    ///
    /// An unbonded Avir has no desire at all. Vanilla already guarantees that on its own: lovin
    /// needs a partner in the same bed, and an Avir only ever has a partner once bonded. So the only
    /// vanilla work left is the other half - once bonded, it comes on strongly, and more strongly in
    /// females. That is a multiplier on the pawn's side of the lovin interval.
    ///
    /// The "no need at all" half lives in AvirRJW (Compat/RJW), because the Sex need is RJW's.
    ///
    /// The splinter-hearted (Avir_ManyMated) is outside all of this: her libido and her Sex need
    /// are the ordinary ones, as for anyone else.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirBondLibido
    {
        static AvirBondLibido()
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(LovePartnerRelationUtility), "LovinMtbSinglePawnFactor");
                if (target == null)
                {
                    Log.Warning("[Avir] LovinMtbSinglePawnFactor not found; bonded Avir keep ordinary lovin frequency.");
                    return;
                }
                new Harmony("Tenebris.Avir.BondLibido").Patch(target,
                    postfix: new HarmonyMethod(typeof(AvirBondLibido), nameof(LovinFactorPostfix)));
            }
            catch (Exception e)
            {
                Log.Error("[Avir] bond libido patch failed: " + e);
            }
        }

        public static bool HasPairBondGene(Pawn pawn)
        {
            return pawn?.genes != null
                && AvirPairBondDefOf.Avir_PairBond != null
                && pawn.genes.HasActiveGene(AvirPairBondDefOf.Avir_PairBond);
        }

        /// <summary>
        /// The body is awake: the gene, and a living mate. The splinter-hearted is left out - her
        /// libido is the ordinary one, bonded or not.
        /// </summary>
        public static bool IsAwakened(Pawn pawn)
        {
            return AvirPairBond.HasBondGene(pawn) && AvirPairBond.IsBonded(pawn);
        }

        /// <summary>Lower result = more frequent lovin, so the factor divides.</summary>
        public static void LovinFactorPostfix(Pawn __0, ref float __result)
        {
            try
            {
                Pawn pawn = __0;
                if (IsAwakened(pawn))
                {
                    __result /= AvirPairBond.BondedLibidoFactor(pawn);
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] bond libido failed: " + e, 0x4A52A0);
            }
        }
    }
}
