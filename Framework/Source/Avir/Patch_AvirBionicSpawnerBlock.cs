using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Avir
{
    /// <summary>
    /// INTEGRATION: Configurable Bionic Distribution (five.five.bionicspawner).
    ///
    /// That mod runs its own Harmony patch - BionicSpawner.Patch_GeneratePawn - on
    /// PawnGenerator.GeneratePawn, and bolts bionics onto whatever comes out.
    ///
    /// PREVENTION, NOT CURE. Stripping bionics after the fact was a bad idea and it produced a
    /// bad bug: an installed bionic leg has already destroyed the natural one, so removing it
    /// left hunters with no legs. Patch_AvirBionics now restores the limb properly, but the
    /// right answer is to never let the implant be installed at all.
    ///
    /// So this reaches into that mod's own patch and stops it dead for:
    ///   - every Avir pawnkind (the whole faction, all castes)
    ///   - the player's Free Nesting, which includes Avir colonists
    ///
    /// Everyone else in the world still gets their bionics. The mod keeps working exactly as
    /// configured; it simply cannot see the Avir.
    ///
    /// Reflection-only and self-disabling. If the mod is not installed, the type is not found,
    /// nothing is patched, and nothing errors.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AvirBionicSpawnerBlock
    {
        static AvirBionicSpawnerBlock()
        {
            try
            {
                Type target = AccessTools.TypeByName("BionicSpawner.Patch_GeneratePawn");
                if (target == null) return;   // mod not installed - nothing to do

                Harmony harmony = new Harmony("avir.bionicspawner.block");

                MethodInfo blocker = AccessTools.Method(
                    typeof(AvirBionicSpawnerBlock), nameof(SkipForAvir));

                int patched = 0;

                // Their patch class will have a Prefix and/or a Postfix. Whichever it uses, we
                // put a prefix in front of it that returns false for Avir pawns - Harmony then
                // skips the original body entirely.
                foreach (MethodInfo mi in AccessTools.GetDeclaredMethods(target))
                {
                    if (!mi.IsStatic) continue;

                    bool touchesAPawn = false;
                    foreach (ParameterInfo pi in mi.GetParameters())
                    {
                        if (pi.ParameterType == typeof(Pawn)) { touchesAPawn = true; break; }
                    }

                    if (!touchesAPawn) continue;

                    harmony.Patch(mi, prefix: new HarmonyMethod(blocker));
                    patched++;
                }

                if (patched > 0)
                {
                    Log.Message("[Avir] Configurable Bionic Distribution detected - blocked from "
                        + patched + " entry point(s). The Avir install their own archotech, thank you.");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[Avir] Could not block the bionic spawner (harmless): " + e.Message);
            }
        }

        /// <summary>
        /// Prefix on their patch. Returning false makes Harmony skip their method body, so the
        /// bionic is never installed - as opposed to being installed and then torn back out,
        /// which is what cost those hunters their legs.
        /// </summary>
        public static bool SkipForAvir(object[] __args)
        {
            try
            {
                if (__args == null) return true;

                foreach (object o in __args)
                {
                    Pawn pawn = o as Pawn;
                    if (pawn == null) continue;

                    // The whole faction - every caste, and the player's Avir with them.
                    if (AvirFaction.IsAvirPawnKind(pawn)) return false;
                    if (AvirFaction.IsPlayerAvir(pawn)) return false;
                }
            }
            catch
            {
                // If anything about their signature surprises us, let them run. Better a bionic
                // Avir than a broken generator.
            }

            return true;
        }
    }
}
