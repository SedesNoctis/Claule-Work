using HarmonyLib;
using rjw;
using Verse;

namespace AvirRJW
{
    // Intercept RJW's adders. Humanoid Alien Races makes RJW treat the Avir as a non-human race, so its adders fall
    // back on GENERIC parts. For a framed MALE we assign canine penis / bare chest / human anus; for a framed FEMALE
    // (or a calm harpy) the human vagina, anus and small breasts - and skip RJW's own, so nothing stacks.
    // Everyone else is left to RJW.

    [HarmonyPatch(typeof(SexPartAdder), nameof(SexPartAdder.add_genitals))]
    public static class Patch_add_genitals
    {
        public static bool Prefix(Pawn pawn, Gender gender)
        {
            if (RjwPartEnforcer.IsFramedMale(pawn))
            {
                Log.Message($"[AvirRJW] add_genitals prefix {pawn.LabelShort} male -> canine");
                RjwPartEnforcer.EnforceMaleGenitals(pawn);
                return false;
            }
            // a female gets the human vagina (a futa's second, male set - gender == Male - is RJW's business)
            if (RjwPartEnforcer.IsHumanFemale(pawn) && gender != Gender.Male)
            {
                Log.Message($"[AvirRJW] add_genitals prefix {pawn.LabelShort} female -> human vagina");
                RjwPartEnforcer.GiveHuman(pawn, Genital_Helper.get_genitalsBPR(pawn), Genital_Helper.generic_vagina, Genital_Helper.average_vagina, true);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(SexPartAdder), nameof(SexPartAdder.add_breasts))]
    public static class Patch_add_breasts
    {
        public static bool Prefix(Pawn pawn)
        {
            if (RjwPartEnforcer.IsFramedMale(pawn))
            {
                Log.Message($"[AvirRJW] add_breasts prefix {pawn.LabelShort} male -> bare chest");
                RjwPartEnforcer.EnforceMaleChest(pawn);
                return false;
            }
            if (RjwPartEnforcer.IsHumanFemale(pawn))
            {
                Log.Message($"[AvirRJW] add_breasts prefix {pawn.LabelShort} female -> small human breasts");
                RjwPartEnforcer.GiveHuman(pawn, Genital_Helper.get_breastsBPR(pawn), Genital_Helper.generic_breasts, Genital_Helper.average_breasts, true);
                RjwPartEnforcer.ShrinkHumanBreasts(pawn);
                return false;
            }
            return true;
        }
    }

    // The anus: RJW's generic one for males AND females -> the ordinary human anus.
    [HarmonyPatch(typeof(SexPartAdder), nameof(SexPartAdder.add_anus))]
    public static class Patch_add_anus
    {
        public static bool Prefix(Pawn pawn)
        {
            if (!RjwPartEnforcer.IsFramedMale(pawn) && !RjwPartEnforcer.IsHumanFemale(pawn)) return true;
            Log.Message($"[AvirRJW] add_anus prefix {pawn.LabelShort} -> human anus");
            RjwPartEnforcer.GiveHuman(pawn, Genital_Helper.get_anusBPR(pawn), Genital_Helper.generic_anus, Genital_Helper.average_anus, true);
            return false;
        }
    }

    // Safety net: reconcile again after a full sexualization pass (idempotent).
    [HarmonyPatch(typeof(Sexualizer), nameof(Sexualizer.sexualize_pawn))]
    public static class Patch_Sexualizer_sexualize_pawn
    {
        public static void Postfix(Pawn pawn)
        {
            if (pawn == null) return;
            if (RjwPartEnforcer.HasFrame(pawn) || RjwPartEnforcer.IsCalmHarpy(pawn))
            {
                Log.Message($"[AvirRJW] sexualize_pawn postfix {pawn.LabelShort} gender={pawn.gender}");
                RjwPartEnforcer.EnforceAvir(pawn);
            }
        }
    }
}
