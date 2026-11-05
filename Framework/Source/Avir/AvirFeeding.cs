using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Avir
{
    /// <summary>
    /// A bird does not feed its chick for three hours on end - and it feeds it a hundred times a day.
    /// When an Avir feeds a baby (the vanilla feeding session is 5000 ticks, two hours), the feeding runs
    /// ten times as fast: a few minutes a meal. The same for an Avir hand-feeding a chick under three
    /// (Toddlers' little ones eat fast themselves; their parents now keep up).
    /// </summary>
    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.DriverTickInterval))]
    public static class Patch_AvirFastFeeding
    {
        public const int Speed = 10;

        public static bool Applies(JobDriver d)
        {
            if (!(d is JobDriver_FeedBaby) && !(d is JobDriver_FoodFeedPatient)) return false;
            Pawn feeder = d.pawn;
            if (feeder == null || !AvirBody.IsAvir(feeder)) return false;
            if (d is JobDriver_FoodFeedPatient && !(d.job?.targetB.Thing is Pawn p && AvirChicks.IsChick(p))) return false;
            return true;
        }

        public static void Prefix(JobDriver __instance, ref int delta)
        {
            if (Applies(__instance)) delta *= Speed;
        }
    }

    /// <summary>The same for feeding that counts every tick rather than every interval.</summary>
    [HarmonyPatch(typeof(JobDriver), nameof(JobDriver.DriverTick))]
    public static class Patch_AvirFastFeedingTick
    {
        private static bool inside;
        private static System.Func<JobDriver, Toil> curToilOf;
        private static Toil CurToilOf(JobDriver d) =>
            (curToilOf ?? (curToilOf = AccessTools.MethodDelegate<System.Func<JobDriver, Toil>>(AccessTools.PropertyGetter(typeof(JobDriver), "CurToil"))))(d);

        public static void Postfix(JobDriver __instance)
        {
            if (inside || !Patch_AvirFastFeeding.Applies(__instance)) return;
            inside = true;
            try
            {
                Toil toil = CurToilOf(__instance);
                for (int i = 1; i < Patch_AvirFastFeeding.Speed && !__instance.ended && CurToilOf(__instance) == toil && toil?.tickAction != null; i++)
                    toil.tickAction();
            }
            finally { inside = false; }
        }
    }
}
