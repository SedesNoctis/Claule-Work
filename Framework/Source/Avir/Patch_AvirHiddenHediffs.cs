using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Avir
{
    /// <summary>
    /// HIDES THE PERMANENT WHOLE-BODY AVIR HEDIFFS FROM THE HEALTH TAB.
    ///
    /// An Avir carries a standing set of hediffs that are simply WHAT AN AVIR IS - feathered fur,
    /// echolocation, keen senses, and the two whole-body conditioning trackers for the claws and
    /// the beak. None of them is a wound, none of them is news, and none of them will ever change
    /// in a way the player needs to read. They just sit there, five lines deep, on every single
    /// Avir, burying the things that actually matter - the bleeding, the infection, the broken rib.
    ///
    /// So they are hidden. They still EXIST and still WORK exactly as before - their comps tick,
    /// their stat offsets apply, the conditioning still climbs. This changes nothing but the UI.
    ///
    /// WHAT IS DELIBERATELY STILL SHOWN:
    ///
    ///   Avir_EssentiumClaws, Avir_EssentiumBeak_1/2/3 - the ACTUAL PARTS, sitting on the hands and
    ///   the jaw. Those are body parts a player needs to see: they can be damaged, they are what
    ///   the pawn fights with, and their tier is meaningful. Only the whole-body CONDITIONING
    ///   trackers are hidden, which is what "on the whole body, not implants" means.
    ///
    /// It patches Hediff.Visible rather than adding an XML field, because there is no XML field for
    /// this - visibility is a property on the hediff, not data on the def.
    ///
    /// TO SEE THEM ANYWAY: turn on Dev Mode. Hidden hediffs are shown again, so nothing is ever
    /// truly lost to debugging.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_AvirHiddenHediffs
    {
        /// <summary>
        /// Patch EVERY declared Visible getter in the chain, not just Hediff's.
        ///
        /// Visible is VIRTUAL, and HediffWithComps overrides it (it has to - a comp can hide its
        /// own hediff). All five of the hediffs below are HediffWithComps. So patching only
        /// Hediff.Visible would never fire for them: virtual dispatch goes straight to the override
        /// and the base getter is never called. Patch the override too and it works either way -
        /// and if a future RimWorld drops one of these overrides, the loop simply skips it instead
        /// of throwing at startup.
        /// </summary>
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types = { typeof(Hediff), typeof(HediffWithComps) };

            foreach (Type t in types)
            {
                PropertyInfo prop = AccessTools.DeclaredProperty(t, "Visible");
                MethodInfo getter = prop != null ? prop.GetGetMethod(true) : null;

                if (getter != null) yield return getter;
            }
        }

        /// <summary>
        /// The whole-body "this is what an Avir is" hediffs. Permanent, unchanging, and pure noise
        /// in the health tab.
        ///
        /// NOT in this list, on purpose: Avir_EssentiumClaws and Avir_EssentiumBeak_1/2/3. Those are
        /// real parts on real body parts and stay visible.
        /// </summary>
        private static readonly HashSet<string> Hidden = new HashSet<string>
        {
            "Avir_FeatheredFurShed",          // feathered fur
            "Avir_WingFeathersShed",          // a harpy's wing feathers
            "Avir_BatlikeHearing",            // echolocation
            "KeenSensesStance",               // keen senses
            "Avir_EssentiumClawConditioning", // whole-body claw tracker (NOT the claws themselves)
            "Avir_EssentiumBeakConditioning", // whole-body beak tracker (NOT the beak itself)
            "Avir_BlueberryCraving",          // Capricious trait's 3-day timer, not meant to be seen
        };

        public static void Postfix(Hediff __instance, ref bool __result)
        {
            try
            {
                if (!__result) return;              // already hidden - nothing to do
                if (Prefs.DevMode) return;          // dev mode shows everything

                if (__instance == null || __instance.def == null || __instance.def.defName == null) return;

                if (Hidden.Contains(__instance.def.defName)) __result = false;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[Avir] Hidden-hediff check failed: " + e, 0x4A5210);
            }
        }
    }
}
