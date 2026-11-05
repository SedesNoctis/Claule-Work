using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// Psylink for Matriarchs and Priestesses: a random level of 7 to 18.
    ///
    /// This previously went through reflection on Pawn.ChangePsylinkLevel and clearly never
    /// resolved - the pawns spawned with no psylink at all, which meant VPE had nothing to
    /// hang psycasts on and they had no abilities. So it now uses the real API directly:
    /// Hediff_Psylink and its ChangeLevel(). Both live in the base RimWorld assembly, so this
    /// compiles with or without Royalty content installed.
    ///
    /// The PATHS themselves (VPE_Nightstalker, VPE_Protector) still come from VPE's own
    /// PawnKindAbilityExtension_Psycasts modExtension in the pawnkind XML - that part is
    /// VPE's supported mechanism and needs no code. This just makes sure there is a psylink
    /// for it to attach to.
    /// </summary>
    public static class AvirPsycasts
    {
        private const int MinPsylink = 7;
        private const int MaxPsylink = 18;

        public static bool IsPsycasterKind(Pawn pawn)
        {
            if (pawn == null || pawn.kindDef == null) return false;
            return pawn.kindDef.defName == "Avir_Matriarch"
                || pawn.kindDef.defName == "Avir_Priestess";
        }

        public static void RollPsylink(Pawn pawn)
        {
            if (pawn == null || pawn.health == null) return;

            try
            {
                int target = Rand.RangeInclusive(MinPsylink, MaxPsylink);

                HediffDef psylinkDef = HediffDefOf.PsychicAmplifier;
                if (psylinkDef == null) return;

                Hediff_Psylink link =
                    pawn.health.hediffSet.GetFirstHediffOfDef(psylinkDef) as Hediff_Psylink;

                if (link == null)
                {
                    BodyPartRecord brain = pawn.health.hediffSet.GetBrain();

                    link = (Hediff_Psylink)HediffMaker.MakeHediff(psylinkDef, pawn, brain);
                    pawn.health.AddHediff(link, brain);
                }

                int delta = target - link.level;
                if (delta > 0)
                {
                    // sendLetter: false - a raid of psycasting matriarchs should not spam letters
                    link.ChangeLevel(delta, false);
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Could not grant psylink: " + e, 0x4A51C2);
            }
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_AvirPsycasts
    {
        public static void Postfix(Pawn __result)
        {
            try
            {
                if (!AvirPsycasts.IsPsycasterKind(__result)) return;
                AvirPsycasts.RollPsylink(__result);
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Psylink assignment failed: " + e, 0x4A51C3);
            }
        }
    }
}
