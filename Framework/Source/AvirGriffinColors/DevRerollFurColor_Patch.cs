using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace AvirColors
{
    /// <summary>
    /// Dev-only gizmo (only shows with DebugSettings.ShowDevGizmos, same
    /// condition Anthrosonae uses for its own "DEV: Change fur" button) that
    /// force-invokes the private parameterless FurGene.SetFurColor(), i.e.
    /// exactly the method FurGene_SetFurColor_Patch prefixes. Lets you re-roll
    /// an existing pawn's color on demand instead of only ever seeing the
    /// gendered pool logic apply to brand-new pawns.
    /// </summary>
    [HarmonyPatch(typeof(Anthrosonae.FurGene), nameof(Anthrosonae.FurGene.GetGizmos))]
    public static class DevRerollFurColor_Patch
    {
        private static readonly MethodInfo SetFurColorNoArgMethod =
            AccessTools.Method(typeof(Anthrosonae.FurGene), "SetFurColor", new System.Type[] { });

        static void Postfix(Anthrosonae.FurGene __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!DebugSettings.ShowDevGizmos)
            {
                return;
            }

            var list = new List<Gizmo>(__result)
            {
                new Command_Action
                {
                    defaultLabel = "DEV: Reroll Avir fur (gendered)",
                    action = () => SetFurColorNoArgMethod.Invoke(__instance, null)
                }
            };
            __result = list;
        }
    }
}
