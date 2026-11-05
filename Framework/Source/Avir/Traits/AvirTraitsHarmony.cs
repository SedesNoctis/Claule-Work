using System;
using HarmonyLib;
using Verse;

namespace Avir.Traits
{
    [StaticConstructorOnStartup]
    public static class AvirTraitsHarmony
    {
        static AvirTraitsHarmony()
        {
            // The [HarmonyPatch] classes of the trait powers are applied with the rest of the mod
            // (AvirHarmony.PatchEachClass); only the by-name hook is installed here.
            Harmony harmony = new Harmony("yourname.avir.traitpowers");

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
                    Log.Warning("[Avir] Could not find " + targetMethod + "; " + label + " effect is inactive.");
                    return;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(typeof(ArchstructrorRestoredPatches), postfixName));
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Failed to patch " + targetMethod + " for " + label + ": " + e);
            }
        }
    }
}
