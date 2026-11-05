using System;
using System.Reflection;
using HarmonyLib;
using rjw;
using Verse;

namespace AvirRJWNeedFix
{
    /// <summary>
    /// RJW's xxx.need_sex now returns the enum xxx.SexNeed. AvirRJW.dll still patched it with an
    /// int __result, so Harmony refused the patch ("Cannot assign method return type rjw.xxx+SexNeed
    /// to __result type System.Int32") and a pair-bonded Avir whose bond is dormant was no longer
    /// read as satisfied. AvirRJW has no source in the mod, so this small assembly fixes it from
    /// the outside, once defs are loaded but before the static constructors run:
    ///   1. AvirRJW's own need_sex hook is skipped, so it no longer logs an error;
    ///   2. the same rule is patched in with the right type: dormant bond -> SexNeed.Satisfied.
    /// </summary>
    public class AvirRJWNeedFixMod : Mod
    {
        public AvirRJWNeedFixMod(ModContentPack content) : base(content)
        {
            // NOT here directly: patching rjw.xxx runs its static constructor, and in the mod
            // constructor no defs are loaded yet - RJW's own static def fields would all come out
            // empty ("Failed to find ... There are 0 defs of this type loaded"). Queued instead:
            // this runs once defs are loaded, but still before [StaticConstructorOnStartup]
            // classes (AvirRJW's bootstrap among them), so the old hook is still skipped.
            LongEventHandler.ExecuteWhenFinished(Apply);
        }

        private static void Apply()
        {
            try
            {
                var harmony = new Harmony("tenebris.avirrjw.needsexfix");
                MethodInfo avirPatch = AccessTools.Method("AvirRJW.AvirBondSexNeed:Patch");
                if (avirPatch != null)
                    harmony.Patch(avirPatch, prefix: new HarmonyMethod(typeof(AvirRJWNeedFixMod), nameof(SkipOldNeedSexHook)));
                MethodInfo needSex = AccessTools.Method(typeof(xxx), nameof(xxx.need_sex));
                if (needSex != null)
                    harmony.Patch(needSex, postfix: new HarmonyMethod(typeof(AvirRJWNeedFixMod), nameof(NeedSexPostfix)));
                else
                    Log.Warning("[AvirRJW] need_sex fix: rjw.xxx.need_sex not found.");
            }
            catch (Exception e)
            {
                Log.Error("[AvirRJW] need_sex fix failed: " + e);
            }
        }

        /// <summary>AvirBondSexNeed.Patch(harmony, target, postfixName): skip only the broken one.</summary>
        public static bool SkipOldNeedSexHook(string __2)
        {
            return __2 != "NeedSex_Postfix";
        }

        public static void NeedSexPostfix(Pawn pawn, ref xxx.SexNeed __result)
        {
            if (__result == xxx.SexNeed.Satisfied || pawn == null) return;
            try
            {
                if (AvirRJW.AvirBondSexNeed.IsDormant(pawn)) __result = xxx.SexNeed.Satisfied;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[AvirRJW] need_sex check failed: " + e, 4870834);
            }
        }
    }
}
