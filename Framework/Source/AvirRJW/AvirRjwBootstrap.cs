using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AvirRJW
{
    /// <summary>
    /// Applies Harmony patches at startup, but ONLY if RimJobWorld is actually active. The mod
    /// already declares RJW as a dependency, so this is belt-and-suspenders: the RJW-referencing
    /// patch registration lives in a separate method that is never called (never JIT-compiled)
    /// when RJW is absent, so a missing RJW can't throw here.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirRjwBootstrap
    {
        static AvirRjwBootstrap()
        {
            if (!ModsConfig.IsActive("rim.job.world"))
            {
                Log.Message("[AvirRJW] RimJobWorld not active; canine/no-breasts genes are inert.");
                return;
            }

            ApplyPatches();
        }

        private static void ApplyPatches()
        {
            // Class by class instead of PatchAll: one patch whose target changed in RJW (a renamed
            // parameter, a new overload) used to take every later patch down with it, silently.
            // Every outcome is logged, so the log always says what AvirRJW did at startup.
            Harmony harmony = new Harmony("user.avirrjw");
            int ok = 0, failed = 0;
            foreach (Type t in typeof(AvirRjwBootstrap).Assembly.GetTypes())
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                try
                {
                    harmony.CreateClassProcessor(t).Patch();
                    ok++;
                    Log.Message("[AvirRJW] patched " + t.Name);
                }
                catch (Exception e)
                {
                    failed++;
                    Log.Error("[AvirRJW] patch " + t.Name + " FAILED: " + e);
                }
            }
            Log.Message("[AvirRJW] startup: " + ok + " patch classes applied, " + failed + " failed");
        }
    }
}
