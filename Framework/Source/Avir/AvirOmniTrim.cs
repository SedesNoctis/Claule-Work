using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Avir
{
    /// <summary>
    /// TRIMS THE OMNICRAFTER DOWN TO WHAT AVIR USES.
    ///
    /// Assemblies/FullyAutomaticOmniCrafter.dll is the full original mod, not a cut-down copy.
    /// RimWorld builds the Mod class of every DLL in an active mod's Assemblies folder, and that
    /// class (OmniCrafterMod) calls PatchAll in its constructor. So every patch of the original
    /// mod is live from the first frame, whether or not a single machine is ever built -
    /// including patches on line of sight, region traversal, cell light, cell and room
    /// temperature, hit checks and plant ticks.
    ///
    /// Avir only defines three machines on top of it (MUS_AvirOuterrealBuildings.xml): the
    /// OmniCrafter, the Reality Weaver and the Auto Detector. None of them needs the force-field
    /// dome, the phantom walls, the biosphere, the auto-surgeon or the interceptors.
    ///
    /// This removes every OmniCrafter patch EXCEPT the few kept below, and only the
    /// OmniCrafter's own patches: anything another mod placed on the same method is untouched.
    /// It runs twice - at startup and again when a game finishes loading - so a patch the
    /// OmniCrafter applies late is caught too. The second pass normally finds nothing.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirOmniTrim
    {
        public const string OmniHarmonyId = "Jeremie.Fully.Automatic.OmniCrafter";
        private const string TrimHarmonyId = "Tenebris.Avir.OmniTrim";

        /// <summary>
        /// Patches that stay. Uninstall and minify rules for the machines themselves, the area
        /// hook (kept in case the Reality Weaver reads roof/area changes through it), and the
        /// designation-category hook, which only runs while defs load.
        /// </summary>
        private static readonly HashSet<string> Keep = new HashSet<string>
        {
            "RimWorld.MinifyUtility.MakeMinified",
            "RimWorld.Designator_Uninstall.CanDesignateThing",
            "Verse.Area.Set",
            "Verse.DesignationCategoryDef.ResolveReferences",
        };

        private static bool reportedOnce;

        static AvirOmniTrim()
        {
            Trim("startup");
        }

        public static void Trim(string when)
        {
            try
            {
                Harmony trimmer = new Harmony(TrimHarmonyId);
                List<MethodBase> targets = Harmony.GetAllPatchedMethods().ToList();

                int removed = 0;
                int kept = 0;

                for (int i = 0; i < targets.Count; i++)
                {
                    MethodBase m = targets[i];
                    Patches info = Harmony.GetPatchInfo(m);
                    if (info == null || !info.Owners.Contains(OmniHarmonyId)) continue;

                    string key = (m.DeclaringType != null ? m.DeclaringType.FullName : "?") + "." + m.Name;
                    if (Keep.Contains(key))
                    {
                        kept++;
                        continue;
                    }

                    trimmer.Unpatch(m, HarmonyPatchType.All, OmniHarmonyId);
                    removed++;
                }

                if (removed > 0 || !reportedOnce)
                {
                    reportedOnce = true;
                    Log.Message("[Avir] OmniCrafter trim (" + when + "): removed patches from "
                        + removed + " methods, kept " + kept + ".");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] OmniCrafter trim failed, the original patches stay: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class Patch_AvirOmniTrim_FinalizeInit
    {
        private static bool done;

        public static void Postfix()
        {
            if (done) return;
            done = true;
            AvirOmniTrim.Trim("game load");
        }
    }
}
