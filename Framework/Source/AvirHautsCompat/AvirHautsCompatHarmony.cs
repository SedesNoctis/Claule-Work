using System;
using HarmonyLib;
using Verse;

namespace AvirHautsCompat
{
    [StaticConstructorOnStartup]
    public static class AvirHautsCompatHarmony
    {
        static AvirHautsCompatHarmony()
        {
            Harmony harmony = new Harmony("yourname.avir.hautscompat");
            harmony.PatchAll();

            // Not attribute-based: targets a CE type this assembly doesn't reference at compile
            // time, so it has to be installed manually, and only if CE is actually there.
            Patch_CinderVolley_ProjectileExplosion.TryInstall(harmony);

            // Same string-based AccessTools lookup as Avir.AvirHarmony uses for
            // PsychicNaturePatches.CooldownPostfix on this exact method - reused here rather
            // than an attribute, since that file already established this is the safe way to
            // hook this particular method in this codebase.
            TryPatchPostfix(harmony, "Verse.VerbProperties:AdjustedCooldownTicks",
                nameof(ArchstructrorRestoredPatches.CooldownPostfix),
                "archstructror-restored-cooldown");
        }

        private static void TryPatchPostfix(Harmony harmony, string targetMethod, string postfixName, string label)
        {
            try
            {
                var method = AccessTools.Method(targetMethod);
                if (method == null)
                {
                    Log.Warning("[AvirHautsCompat] Could not find " + targetMethod + "; " + label + " effect is inactive.");
                    return;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(typeof(ArchstructrorRestoredPatches), postfixName));
            }
            catch (Exception e)
            {
                Log.Warning("[AvirHautsCompat] Failed to patch " + targetMethod + " for " + label + ": " + e);
            }
        }
    }
}
