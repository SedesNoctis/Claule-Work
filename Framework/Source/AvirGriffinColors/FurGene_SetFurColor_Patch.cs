using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace AvirColors
{
    /// <summary>
    /// Anthrosonae's FurGene.SetFurColor() (parameterless overload) does a
    /// straight weighted-random pick across the gene's entire allowedFurColors
    /// list with no concept of pawn gender. This prefix replaces that pick:
    /// it filters the list down to presets valid for the pawn's gender
    /// (GriffinFurColorDef.allowedGender == Gender.None means "any gender"),
    /// then reproduces the same weighted-random selection on the filtered
    /// subset, and hands the result to the private SetFurColor(FurColorDef)
    /// overload via reflection (which does the actual color assignment).
    ///
    /// Only GriffinFurColorDef entries can be gender-restricted - plain
    /// Anthrosonae.FurColorDef entries (if any end up in the list) are always
    /// treated as available to any gender.
    /// </summary>
    [HarmonyPatch(typeof(Anthrosonae.FurGene))]
    [HarmonyPatch("SetFurColor", MethodType.Normal)]
    [HarmonyPatch(new Type[] { })]
    public static class FurGene_SetFurColor_Patch
    {
        private static readonly MethodInfo SetFurColorWithArgMethod =
            AccessTools.Method(typeof(Anthrosonae.FurGene), "SetFurColor",
                new[] { typeof(Anthrosonae.FurColorDef) });

        static bool Prefix(Anthrosonae.FurGene __instance)
        {
            var extension = __instance.def.GetModExtension<Anthrosonae.FurColors>();
            if (extension?.allowedFurColors == null || extension.allowedFurColors.Count == 0)
            {
                return true; // let the original method run/no-op as it normally would
            }

            var pawn = __instance.pawn;
            Gender gender = pawn?.gender ?? Gender.None;

            List<Anthrosonae.FurColorDef> pool = extension.allowedFurColors
                .Where(c => !(c is GriffinFurColorDef g) || g.allowedGender == Gender.None || g.allowedGender == gender)
                .ToList();

            // Safety net: if filtering leaves nothing (e.g. a list that's ALL
            // female-only and this pawn isn't female), fall back to the full
            // list rather than leaving the pawn with no fur color at all.
            if (pool.Count == 0)
            {
                pool = extension.allowedFurColors;
            }

            if (pool.TryRandomElementByWeight(x => x.selectionWeight, out var result))
            {
                Log.Message($"[AvirGriffinColors] pawn={pawn?.LabelShort}, gender={gender}, pool={pool.Count}/{extension.allowedFurColors.Count}, picked={result.defName}");
                SetFurColorWithArgMethod.Invoke(__instance, new object[] { result });
            }
            else
            {
                Log.Warning($"[AvirGriffinColors] pawn={pawn?.LabelShort}: TryRandomElementByWeight failed on a pool of {pool.Count}");
            }

            return false; // skip the original ungendered pick
        }
    }
}
