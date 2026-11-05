using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Avir
{
    /// <summary>
    /// WITHOUT THE BEAK AN AVIR CANNOT EAT. A human without a jaw still gets food down; an Avir's beak is its
    /// whole mouth - lost, nothing is eaten or drunk (food, drink, drugs by mouth), by itself or from another's
    /// hand, and it starves unless the beak is given back.
    /// </summary>
    public static class AvirBeakless
    {
        private static BodyPartRecord beak;

        public static BodyPartRecord Beak =>
            beak ?? (beak = AvirBody.Body?.AllParts.FirstOrDefault(r => r.def.defName == "Jaw"));

        public static bool Is(Pawn p) =>
            p != null && AvirBody.IsAvirBody(p) && Beak != null && p.health?.hediffSet != null && p.health.hediffSet.PartIsMissing(Beak);
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.WillEat), new[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    public static class Patch_AvirBeaklessThing
    {
        public static void Postfix(Pawn p, ref bool __result)
        {
            if (__result && AvirBeakless.Is(p)) __result = false;
        }
    }

    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.WillEat), new[] { typeof(Pawn), typeof(ThingDef), typeof(Pawn), typeof(bool), typeof(bool) })]
    public static class Patch_AvirBeaklessDef
    {
        public static void Postfix(Pawn p, ref bool __result)
        {
            if (__result && AvirBeakless.Is(p)) __result = false;
        }
    }

    /// <summary>Whatever still reaches it (a drug by policy): it cannot be swallowed - one portion spilled, no effect.</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    public static class Patch_AvirBeaklessIngest
    {
        public static bool Prefix(Thing __instance, Pawn ingester, ref float __result)
        {
            if (!AvirBeakless.Is(ingester)) return true;
            __result = 0f;
            if (!__instance.Destroyed)
            {
                if (__instance.stackCount > 1) __instance.SplitOff(1).Destroy();
                else __instance.Destroy();
            }
            return false;
        }
    }

    /// <summary>The colony is told at once.</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Patch_AvirBeakLostMessage
    {
        public static void Postfix(Hediff hediff, Pawn ___pawn)
        {
            if (!(hediff is Hediff_MissingPart) || hediff.Part == null || hediff.Part != AvirBeakless.Beak) return;
            if (___pawn?.Faction != Faction.OfPlayer || ___pawn.Dead) return;
            Find.LetterStack.ReceiveLetter("Beak lost: " + ___pawn.LabelShortCap,
                ___pawn.LabelShortCap + " has lost the beak. An Avir's beak is its whole mouth: without it, " + ___pawn.LabelShort
                + " cannot eat or drink anything and will starve unless the beak is restored.",
                LetterDefOf.ThreatSmall, ___pawn);
        }
    }
}
